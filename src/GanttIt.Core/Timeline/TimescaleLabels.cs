using System.Globalization;
using GanttIt.Core.Dates;
using GanttIt.Core.Localization;

namespace GanttIt.Core.Timeline;

/// <summary>
/// Timescale label formats, longest first, and the thinning steps (§6.2). The
/// layout engine picks one format per tier: the longest that fits every
/// labelled cell, e.g. <c>January</c> or <c>Jan</c> for every month, never a mix.
/// No format is ambiguous on its own: a month is at least its 3-letter
/// abbreviation and a top-tier date always names its month, so where those
/// don't fit the tier labels every k-th tick instead. The words come from the
/// chart language's tables (<see cref="ChartText"/>), never from the machine's culture.
/// </summary>
public static class TimescaleLabels
{
    /// <summary>
    /// Candidates for the cell of <paramref name="unit"/> that starts on
    /// <paramref name="cellStart"/>, one per format, in the same order for every
    /// cell of a tier. Tier-1 labels carry the context that the tier above
    /// would otherwise give (a month above weeks names its year).
    /// <paramref name="dateFormat"/> is the chart's date pattern, used for full week dates;
    /// <paramref name="language"/> is the chart language (§2.6).
    /// </summary>
    public static IReadOnlyList<string> Candidates(
        TimeUnit unit, bool isTier1, DateOnly cellStart, string dateFormat, Language language = Language.English)
    {
        var text = ChartText.For(language);
        var day = cellStart.Day.ToString(CultureInfo.InvariantCulture);
        var month = cellStart.Month;
        var year = cellStart.Year;

        return unit switch
        {
            TimeUnit.Day => [day],
            // A week is named by its first day on the axis: its Monday, unless
            // the cell is clipped at the axis start. The label sits at that edge.
            // A bare day number would name no month here, so there is no third format,
            // and a chart date pattern that can't tell two weeks apart (e.g. "dd" or
            // "MMM yyyy") is not offered.
            TimeUnit.Week when isTier1 => NamesWeek(dateFormat, language)
                ? [DateFormats.Format(cellStart, dateFormat, language), text.DayMonth(cellStart.Day, month)]
                : [text.DayMonth(cellStart.Day, month)],
            TimeUnit.Week => [day],
            TimeUnit.Month when isTier1 =>
                [text.MonthYear(month, year, abbreviated: false), text.MonthYear(month, year, abbreviated: true), text.MonthShortYear(month, year)],
            TimeUnit.Month => [text.MonthName(month), text.MonthAbbreviation(month)],
            TimeUnit.Quarter => [text.Quarter((month - 1) / 3 + 1)],
            TimeUnit.Year => text.HasShortYears ? [ChartText.Year(year), ChartText.ShortYear(year)] : [ChartText.Year(year)],
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
    }

    /// <summary>
    /// True when <paramref name="pattern"/> can name a week by its first day: it shows
    /// the month (1 Feb and 1 Mar 2026 share their year, day and weekday, so only a
    /// month field tells them apart) and the day (1 and 8 Feb 2026 share their year,
    /// month and weekday, so only a day field tells them apart). <c>MM/yyyy</c> or
    /// <c>MMM yyyy</c> would give every week of a month the same label.
    /// </summary>
    private static bool NamesWeek(string pattern, Language language)
    {
        var first = DateFormats.Format(new DateOnly(2026, 2, 1), pattern, language);
        return first != DateFormats.Format(new DateOnly(2026, 3, 1), pattern, language)
               && first != DateFormats.Format(new DateOnly(2026, 2, 8), pattern, language);
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
    /// Length of <paramref name="start"/>..<paramref name="endExclusive"/> in the
    /// measure a tier of <paramref name="unit"/> compares edge slivers in (§6.2):
    /// days for day and week tiers, months for month, quarter and year tiers,
    /// so every quarter is exactly a quarter of a year whatever its day count.
    /// A month cut short counts the share of its days that it covers.
    /// </summary>
    public static double SpanLength(TimeUnit unit, DateOnly start, DateOnly endExclusive) =>
        unit is TimeUnit.Day or TimeUnit.Week
            ? endExclusive.DayNumber - start.DayNumber
            : MonthPosition(endExclusive) - MonthPosition(start);

    /// <summary>Length of one <paramref name="unit"/> in the <see cref="SpanLength"/> measure.</summary>
    public static int UnitLength(TimeUnit unit) => unit switch
    {
        TimeUnit.Day => 1,
        TimeUnit.Week => 7,
        TimeUnit.Month => 1,
        TimeUnit.Quarter => 3,
        TimeUnit.Year => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>Months since year 0 up to <paramref name="date"/>; whole on the 1st, so whole months subtract exactly.</summary>
    private static double MonthPosition(DateOnly date) =>
        date.Year * 12 + date.Month - 1 + (date.Day - 1) / (double)DateTime.DaysInMonth(date.Year, date.Month);
}
