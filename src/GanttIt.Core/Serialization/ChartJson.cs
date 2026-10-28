using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;

namespace GanttIt.Core.Serialization;

/// <summary>
/// The chart JSON stored in the <c>GanttIt.Chart</c> entity's <c>ChartJson</c> field (§3, §7.2).
/// </summary>
public static class ChartJson
{
    /// <summary>
    /// Serializes a chart. Throws <see cref="InvalidOperationException"/> for a
    /// chart with a newer SchemaVersion, which must never be rewritten in this
    /// older format.
    /// </summary>
    public static string Serialize(Chart chart) =>
        JsonStore.Serialize(chart, chart.SchemaVersion, SchemaVersions.Chart, "chart");

    /// <summary>
    /// Reads a chart. Never throws: malformed or inconsistent JSON returns
    /// <see cref="LoadStatus.Invalid"/> with the reason; a newer SchemaVersion
    /// returns <see cref="LoadStatus.NewerVersion"/> (read-only).
    /// </summary>
    public static LoadResult<Chart> Deserialize(string? json) =>
        JsonStore.Deserialize<Chart>(json, SchemaVersions.Chart, Problem);

    /// <summary>Structural consistency the editor and layout rely on.</summary>
    private static string? Problem(Chart chart)
    {
        if (chart.Name is null) return "The chart has no name.";
        if (chart.Title is null) return "The chart has no title field.";
        if (chart.Sections is null) return "The chart has no section list.";
        if (chart.Links is null) return "The chart has no link list.";
        if (!double.IsFinite(chart.TimelineWidth) || !double.IsFinite(chart.NameMaxWidth))
            return "The chart has a non-finite width.";
        if (!Enum.IsDefined(chart.Timescale)) return "The chart has an unknown timescale.";
        if ((chart.Columns & ~ChartColumns.All) != 0) return "The chart has unknown columns.";

        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < chart.Sections.Count; i++)
        {
            var s = chart.Sections[i];
            if (s is null) return "The chart has an empty section entry.";
            if (s.Name is null) return $"Section {i + 1} has no name.";
            if (!SupportedDates.Contains(s.Start) || !SupportedDates.Contains(s.End))
                return $"Section {i + 1} has a date outside {SupportedDates.Min.Year}–{SupportedDates.Max.Year}.";
            if (!index.TryAdd(s.Id, i)) return $"Section id {s.Id} appears twice.";
        }

        var seen = new HashSet<(Guid, Guid)>();
        foreach (var link in chart.Links)
        {
            if (link is null) return "The chart has an empty link entry.";
            if (!index.TryGetValue(link.FromSectionId, out var from) || !index.TryGetValue(link.ToSectionId, out var to))
                return "A link refers to a section that does not exist.";
            if (to != from + 1) return "A link joins sections that are not neighbours.";
            if (!seen.Add((link.FromSectionId, link.ToSectionId))) return "A link appears twice.";
            // Every Scheduler path keeps To.Start = From.End + 1 + LagDays, so a
            // mismatch means outside edits; trusting it would shift the chain.
            if (link.LagDays != Scheduler.CurrentLag(chart.Sections[from], chart.Sections[to]))
                return "A link's lag does not match its sections' dates.";
        }
        return null;
    }
}
