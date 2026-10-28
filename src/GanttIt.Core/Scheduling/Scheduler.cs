using GanttIt.Core.Model;

namespace GanttIt.Core.Scheduling;

/// <summary>
/// Something the editor must show inline because the user's action changed
/// more than they directly edited (§5.1: "never a silent change").
/// </summary>
public sealed record ScheduleNotice(string Message, IReadOnlyList<Link> RemovedLinks);

/// <summary>
/// The scheduling rules (§5). Calendar days, end dates inclusive. Every
/// operation mutates the chart in place and is deterministic. Links only join
/// neighbours in list order, so propagation follows the list and has no cycles.
/// </summary>
public static class Scheduler
{
    /// <summary>Days a new section lasts (§2.2).</summary>
    public const int NewSectionDays = 7;

    /// <summary>
    /// Start edit (§5.2): the section keeps its duration and its whole chain,
    /// earlier and later linked sections alike, shifts by the same number of days.
    /// </summary>
    public static void SetStart(Chart chart, Guid sectionId, DateOnly start)
    {
        var index = RequireIndex(chart, sectionId);
        var delta = start.DayNumber - chart.Sections[index].Start.DayNumber;
        if (delta == 0) return;

        var (first, last) = ChainBounds(chart, index);
        for (var i = first; i <= last; i++)
        {
            var s = chart.Sections[i];
            s.Start = s.Start.AddDays(delta);
            s.End = s.End.AddDays(delta);
        }
    }

    /// <summary>
    /// End edit (§5.2): the section's duration changes; later linked sections
    /// shift (each keeping its duration); earlier ones don't move.
    /// An end before the start is accepted and left for validation to report.
    /// </summary>
    public static void SetEnd(Chart chart, Guid sectionId, DateOnly end)
    {
        var index = RequireIndex(chart, sectionId);
        chart.Sections[index].End = end;
        PropagateForward(chart, index);
    }

    /// <summary>Duration edit (§5.2): moves the end, then behaves like an End edit.</summary>
    public static void SetDuration(Chart chart, Guid sectionId, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        var section = chart.Sections[RequireIndex(chart, sectionId)];
        SetEnd(chart, sectionId, section.Start.AddDays(days - 1));
    }

    /// <summary>True when the section is linked to the one after it.</summary>
    public static bool IsLinkedToFollowing(Chart chart, Guid sectionId) =>
        chart.LinkAfter(RequireIndex(chart, sectionId)) is not null;

    /// <summary>True when the section is linked to the one before it.</summary>
    public static bool IsLinkedToPrevious(Chart chart, Guid sectionId) =>
        chart.LinkBefore(RequireIndex(chart, sectionId)) is not null;

    /// <summary>The "Relates to following" checkbox. Same link as the next row's "previous".</summary>
    public static void SetLinkToFollowing(Chart chart, Guid sectionId, bool linked) =>
        SetLink(chart, RequireIndex(chart, sectionId), linked);

    /// <summary>The "Relates to previous" checkbox. Same link as the previous row's "following".</summary>
    public static void SetLinkToPrevious(Chart chart, Guid sectionId, bool linked) =>
        SetLink(chart, RequireIndex(chart, sectionId) - 1, linked);

    /// <summary>
    /// Ticks or unticks the link between the section at <paramref name="fromIndex"/>
    /// and the next one. Ticking records the current gap as the lag; unticking
    /// changes no dates (§5.1). Both are idempotent.
    /// </summary>
    public static void SetLink(Chart chart, int fromIndex, bool linked)
    {
        if (fromIndex < 0 || fromIndex >= chart.Sections.Count - 1)
            throw new ArgumentOutOfRangeException(nameof(fromIndex), "A link needs a following section.");

        var existing = chart.LinkAfter(fromIndex);
        if (linked && existing is null)
        {
            var from = chart.Sections[fromIndex];
            var to = chart.Sections[fromIndex + 1];
            chart.Links.Add(new Link(from.Id, to.Id, CurrentLag(from, to)));
        }
        else if (!linked && existing is not null)
        {
            chart.Links.Remove(existing);
        }
    }

    /// <summary>The lag that the current dates imply: <c>To.Start − From.End − 1</c>.</summary>
    public static int CurrentLag(Section from, Section to) => to.Start.DayNumber - from.End.DayNumber - 1;

    /// <summary>
    /// Moves a section in the list (drag reorder). Its links on both sides are
    /// removed (§5.1). A link that joined the two sections it now sits between
    /// is removed too, since they are no longer neighbours. Dates don't change.
    /// Returns a notice when any link was removed, otherwise null.
    /// </summary>
    public static ScheduleNotice? Move(Chart chart, int fromIndex, int toIndex)
    {
        var count = chart.Sections.Count;
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, count);
        if (fromIndex == toIndex) return null;

        var moved = chart.Sections[fromIndex];
        chart.Sections.RemoveAt(fromIndex);
        chart.Sections.Insert(toIndex, moved);

        var removed = RemoveNonNeighbourLinks(chart);
        return removed.Count == 0
            ? null
            : new ScheduleNotice(
                $"Moving \"{moved.Name}\" removed {Plural(removed.Count, "link")}. Re-tick them if the sections still depend on each other.",
                removed);
    }

    /// <summary>
    /// Appends a section (§2.2): it starts the day after the previous section's
    /// end (or on <paramref name="startIfEmpty"/> in an empty chart), lasts
    /// <see cref="NewSectionDays"/> days and takes the next palette color.
    /// </summary>
    public static Section AddSection(
        Chart chart, IReadOnlyList<Rgb> palette, DateOnly startIfEmpty, string? name = null, Guid? id = null)
    {
        var start = chart.Sections.Count == 0 ? startIfEmpty : chart.Sections[^1].End.AddDays(1);
        var section = new Section
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? $"Section {chart.Sections.Count + 1}",
            Start = start,
            End = start.AddDays(NewSectionDays - 1),
            Color = Palette.NextColor(palette, chart.Sections),
        };
        chart.Sections.Add(section);
        return section;
    }

    /// <summary>
    /// Deletes a section and its links. Its former neighbours become
    /// neighbours but are not linked. Returns a notice when links were removed.
    /// </summary>
    public static ScheduleNotice? RemoveSection(Chart chart, Guid sectionId)
    {
        var index = RequireIndex(chart, sectionId);
        var name = chart.Sections[index].Name;
        chart.Sections.RemoveAt(index);
        var removed = RemoveNonNeighbourLinks(chart);
        return removed.Count == 0
            ? null
            : new ScheduleNotice($"Deleting \"{name}\" removed {Plural(removed.Count, "link")}.", removed);
    }

    /// <summary>
    /// Re-applies every link in list order, so each chain satisfies
    /// <c>To.Start = From.End + 1 + LagDays</c>. Each later section keeps its
    /// duration. Use after loading a chart whose dates may have been hand-edited.
    /// </summary>
    public static void EnforceLinks(Chart chart)
    {
        for (var i = 0; i < chart.Sections.Count - 1; i++)
            ApplyLink(chart, i);
    }

    private static void PropagateForward(Chart chart, int index)
    {
        for (var i = index; i < chart.Sections.Count - 1 && ApplyLink(chart, i); i++)
        {
        }
    }

    /// <summary>Applies the link after <paramref name="index"/>, if any. Returns whether one existed.</summary>
    private static bool ApplyLink(Chart chart, int index)
    {
        if (chart.LinkAfter(index) is not { } link) return false;
        var from = chart.Sections[index];
        var to = chart.Sections[index + 1];
        var duration = to.Duration;
        to.Start = from.End.AddDays(1 + link.LagDays);
        to.End = to.Start.AddDays(duration - 1);
        return true;
    }

    /// <summary>First and last index of the linked chain containing <paramref name="index"/>.</summary>
    private static (int First, int Last) ChainBounds(Chart chart, int index)
    {
        var first = index;
        while (chart.LinkBefore(first) is not null) first--;
        var last = index;
        while (chart.LinkAfter(last) is not null) last++;
        return (first, last);
    }

    private static List<Link> RemoveNonNeighbourLinks(Chart chart)
    {
        var position = new Dictionary<Guid, int>(chart.Sections.Count);
        for (var i = 0; i < chart.Sections.Count; i++) position[chart.Sections[i].Id] = i;

        var removed = chart.Links
            .Where(l => !position.TryGetValue(l.FromSectionId, out var f)
                        || !position.TryGetValue(l.ToSectionId, out var t)
                        || t != f + 1)
            .ToList();
        foreach (var link in removed) chart.Links.Remove(link);
        return removed;
    }

    private static int RequireIndex(Chart chart, Guid sectionId)
    {
        var index = chart.IndexOf(sectionId);
        return index >= 0 ? index : throw new ArgumentException($"No section with id {sectionId}.", nameof(sectionId));
    }

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
