using GanttIt.Core.Model;

namespace GanttIt.Core.Timeline;

/// <summary>An inclusive run of days covered by one header cell.</summary>
public readonly record struct TimeCell(DateOnly Start, DateOnly End)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

/// <summary>
/// The chart's time axis: the section span padded out to whole tier-2 units
/// (§5.3), with the resolved tiers.
/// </summary>
public sealed record TimeAxis(DateOnly Start, DateOnly End, TierPair Tiers)
{
    /// <summary>Days on the axis, inclusive.</summary>
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>
    /// Builds the axis for the span <paramref name="first"/>..<paramref name="last"/>:
    /// tiers per <paramref name="timescale"/>, then the start moved back and the
    /// end moved forward to whole tier-2 units.
    /// </summary>
    public static TimeAxis Create(Timescale timescale, DateOnly first, DateOnly last)
    {
        if (last < first) (first, last) = (last, first);
        var tiers = TimescaleRules.Resolve(timescale, first, last);
        return new TimeAxis(
            TimescaleRules.UnitStart(tiers.Tier2, first),
            TimescaleRules.UnitEnd(tiers.Tier2, last),
            tiers);
    }

    /// <summary>
    /// The axis for a chart: from the earliest date to the latest date of any
    /// section (start or end, so a section whose end is before its start still
    /// fits). Null when the chart has no sections.
    /// </summary>
    public static TimeAxis? ForChart(Chart chart)
    {
        if (chart.Sections.Count == 0) return null;
        var first = DateOnly.MaxValue;
        var last = DateOnly.MinValue;
        foreach (var s in chart.Sections)
        {
            var lo = s.Start < s.End ? s.Start : s.End;
            var hi = s.Start < s.End ? s.End : s.Start;
            if (lo < first) first = lo;
            if (hi > last) last = hi;
        }
        return Create(chart.Timescale, first, last);
    }

    /// <summary>Day offset of <paramref name="date"/> from the axis start.</summary>
    public int OffsetOf(DateOnly date) => date.DayNumber - Start.DayNumber;

    /// <summary>
    /// The cells of <paramref name="unit"/> across the axis, in order. Cells at
    /// either end are clipped to the axis, so a tier-1 unit that is not aligned
    /// with tier 2 (a month above weeks) can be partial.
    /// </summary>
    public IReadOnlyList<TimeCell> Cells(TimeUnit unit)
    {
        var cells = new List<TimeCell>();
        for (var start = Start; start <= End;)
        {
            var next = TimescaleRules.NextUnitStart(unit, start);
            var end = next.AddDays(-1);
            cells.Add(new TimeCell(start, end < End ? end : End));
            start = next;
        }
        return cells;
    }
}
