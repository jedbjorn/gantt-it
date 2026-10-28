using System.Globalization;
using GanttIt.Core.Dates;

namespace GanttIt.Core.Timeline;

/// <summary>
/// Timescale label candidates, longest first (§6.2). The layout engine uses the
/// longest candidate that fits its cell, e.g. <c>January → Jan → J</c> and
/// <c>2026 → '26</c>. All text is culture-invariant.
/// </summary>
public static class TimescaleLabels
{
    /// <summary>
    /// Candidates for the cell of <paramref name="unit"/> that starts on
    /// <paramref name="cellStart"/>. Tier-1 labels carry the context that the
    /// tier above would otherwise give (a month above weeks names its year).
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
            TimeUnit.Week when isTier1 => [DateFormats.Format(cellStart, dateFormat), $"{day} {abbr}", day],
            TimeUnit.Week => [day],
            TimeUnit.Month when isTier1 => [$"{month} {year}", $"{abbr} {year}", $"{abbr} {shortYear}", abbr, month[..1]],
            TimeUnit.Month => [month, abbr, month[..1]],
            TimeUnit.Quarter => [$"Q{(cellStart.Month - 1) / 3 + 1}"],
            TimeUnit.Year => [year, shortYear],
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
        };
    }
}
