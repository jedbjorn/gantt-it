using GanttIt.Core.Model;
using GanttIt.Core.Resources;
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
        if (chart.Name is null) return CoreStrings.Chart_NoName;
        if (chart.Title is null) return CoreStrings.Chart_NoTitle;
        if (chart.Sections is null) return CoreStrings.Chart_NoSections;
        if (chart.Links is null) return CoreStrings.Chart_NoLinks;
        if (!double.IsFinite(chart.TimelineWidth) || !double.IsFinite(chart.NameMaxWidth))
            return CoreStrings.Chart_NonFiniteWidth;
        if (!Enum.IsDefined(chart.Timescale)) return CoreStrings.Chart_UnknownTimescale;
        if ((chart.Columns & ~ChartColumns.All) != 0) return CoreStrings.Chart_UnknownColumns;

        var index = new Dictionary<Guid, int>();
        for (var i = 0; i < chart.Sections.Count; i++)
        {
            var s = chart.Sections[i];
            if (s is null) return CoreStrings.Chart_EmptySection;
            if (s.Name is null) return CoreStrings.Chart_SectionNoName(i + 1);
            if (!SupportedDates.Contains(s.Start) || !SupportedDates.Contains(s.End))
                return CoreStrings.Chart_SectionDate(i + 1, SupportedDates.Min.Year, SupportedDates.Max.Year);
            if (!index.TryAdd(s.Id, i)) return CoreStrings.Chart_DuplicateId(s.Id);
        }

        var seen = new HashSet<(Guid, Guid)>();
        foreach (var link in chart.Links)
        {
            if (link is null) return CoreStrings.Chart_EmptyLink;
            if (!index.TryGetValue(link.FromSectionId, out var from) || !index.TryGetValue(link.ToSectionId, out var to))
                return CoreStrings.Chart_LinkMissing;
            if (to != from + 1) return CoreStrings.Chart_LinkNotNeighbours;
            if (!seen.Add((link.FromSectionId, link.ToSectionId))) return CoreStrings.Chart_LinkTwice;
            // Every Scheduler path keeps To.Start = From.End + 1 + LagDays, so a
            // mismatch means outside edits; trusting it would shift the chain.
            if (link.LagDays != Scheduler.CurrentLag(chart.Sections[from], chart.Sections[to]))
                return CoreStrings.Issue_LinkLag;
        }
        return null;
    }
}
