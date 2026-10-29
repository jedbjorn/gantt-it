using System.Globalization;
using GanttIt.Core.Dates;

namespace GanttIt.Core.Timeline;

/// <summary>
/// Timescale label formats, longest first, and the thinning steps (§6.2). The
/// layout engine picks one format per tier: the longest that fits every
/// labelled cell, e.g. <c>January</c> or <c>Jan</c> for every month, never a mix.
/// No format is ambiguous on its own: a month is at least its 3-letter
/// abbreviation and a top-tier date always names its month, so where those
/// don't fit the tier labels every k-th tick instead. All text is culture-invariant.
/// </summary>
public static class TimescaleLabels
{
    /// <summary>
    /// Candidates for the cell of <paramref name="unit"/> that starts on
    /// <paramref name="cellStart"/>, one per format, in the same order for every
    /// cell of a tier. Tier-1 labels carry the context that the tier above
    /// would otherwise give (a month above weeks names its year).
    /// <paramref name="dateFormat"/> is the chart's date pattern, used for full week dates.
    /// </summary>
    public static IReadOnlyList<string> Candidates(TimeUnit unit, bool isTier1, DateOnly cellStart, string dateFormat)
    {
        var day = cellStart.Day.ToString(CultureInfo.InvariantCulture);
        var month = DateFormats.MonthName(cellStart.Month);
        var abbr = DateFormats.MonthAbbreviation(cellStart.Month);
        var year = cellStart.Year.ToString(CultureInfo.InvariantCulture);
        var shortYear = "'" + (cellStart.Year % 100).ToString("00", CultureInfo.InvariantCulture);

        return unit switch
        {
            TimeUnit.Day => [day],
            // A week is named by its first day on the axis: its Monday, unless
            // the cell is clipped at the axis start. The label sits at that edge.
            // A bare day number would name no month here, so there is no third format.
            TimeUnit.Week when isTier1 => [DateFormats.Format(cellStart, dateFormat), $"{day} {abbr}"],
            TimeUnit.Week => [day],
            TimeUnit.Month when isTier1 => [$"{month} {year}", $"{abbr} {year}", $"{abbr} {shortYear}"],
            TimeUnit.Month => [month, abbr],
            TimeUnit.Quarter => [$"Q{(cellStart.Month - 1) / 3 + 1}"],
            TimeUnit.Year => [year, shortYear],
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
    }

    /// <summary>
    /// Group sizes k for thinning a tier's labels, smallest first: label every
    /// tick, then every k-th. Months go 2, 3, 6, 12 so groups line up with
    /// quarters and half-years, then keep doubling (every 2nd, 4th… year);
    /// every other unit doubles from 1. The sequence ends before k overflows.
    /// </summary>
    public static IEnumerable<int> ThinningSteps(TimeUnit unit)
    {
        if (unit == TimeUnit.Month)
        {
            yield return 1;
            yield return 2;
            yield return 3;
            yield return 6;
            for (var k = 12; k > 0; k *= 2) yield return k;
        }
        else
        {
            for (var k = 1; k > 0; k *= 2) yield return k;
        }
    }

    /// <summary>
    /// Position of the cell starting on <paramref name="cellStart"/> for
    /// thinning: a group of k starts at every cell whose slot is a multiple of k
    /// (and at the first cell). Months, quarters and years count from year 0,
    /// so groups are calendar-aligned: every 2nd month starts in Jan, Mar…,
    /// every 3rd in Jan, Apr, Jul, Oct, every 2nd quarter is Q1/Q3, every 2nd
    /// year an even year. Days and weeks count from <paramref name="axisStart"/>.
    /// Consecutive cells have consecutive slots.
    /// </summary>
    public static int ThinningSlot(TimeUnit unit, DateOnly cellStart, DateOnly axisStart) => unit switch
    {
        TimeUnit.Day => cellStart.DayNumber - axisStart.DayNumber,
        TimeUnit.Week => (TimescaleRules.UnitStart(unit, cellStart).DayNumber
                          - TimescaleRules.UnitStart(unit, axisStart).DayNumber) / 7,
        TimeUnit.Month => cellStart.Year * 12 + cellStart.Month - 1,
        TimeUnit.Quarter => cellStart.Year * 4 + (cellStart.Month - 1) / 3,
        TimeUnit.Year => cellStart.Year,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>
    /// The full period (inclusive dates, not clipped to the axis) of the
    /// k-unit thinning group that holds the cell starting on <paramref name="cellStart"/>.
    /// A period reaching past the calendar's range (only for a huge k) is cut
    /// at <see cref="DateOnly.MinValue"/> / <see cref="DateOnly.MaxValue"/>.
    /// </summary>
    public static (DateOnly Start, DateOnly End) ThinningBlock(TimeUnit unit, DateOnly cellStart, int k, DateOnly axisStart)
    {
        long slot = ThinningSlot(unit, cellStart, axisStart) / k * k;
        switch (unit)
        {
            case TimeUnit.Day:
            case TimeUnit.Week:
                var days = unit == TimeUnit.Day ? 1L : 7L;
                var origin = unit == TimeUnit.Day ? axisStart : TimescaleRules.UnitStart(unit, axisStart);
                var first = origin.DayNumber + slot * days;
                var last = Math.Min(first + k * days - 1, DateOnly.MaxValue.DayNumber);
                return (DateOnly.FromDayNumber((int)first), DateOnly.FromDayNumber((int)last));
            default:
                // Months since 1 January of year 0.
                var months = unit switch
                {
                    TimeUnit.Month => 1L,
                    TimeUnit.Quarter => 3L,
                    TimeUnit.Year => 12L,
                    _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
                };
                var start = slot * months;
                var next = start + k * months;
                return (
                    start < 12 ? DateOnly.MinValue : MonthStart(start),
                    next >= 10000 * 12 ? DateOnly.MaxValue : MonthStart(next).AddDays(-1));
        }

        static DateOnly MonthStart(long monthIndex) => new((int)(monthIndex / 12), (int)(monthIndex % 12) + 1, 1);
    }
}
