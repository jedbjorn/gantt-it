using System.Globalization;
using GanttIt.Core.Dates;
using GanttIt.Core.Model;

namespace GanttIt.Core.Editing;

/// <summary>The one-line facts a home-screen card shows about a chart (§2.1).</summary>
public static class ChartSummary
{
    /// <summary>
    /// Earliest and latest date of any section (start or end, so an invalid
    /// section still counts). False for a chart with no sections.
    /// </summary>
    public static bool TryGetSpan(Chart chart, out DateOnly first, out DateOnly last)
    {
        first = DateOnly.MaxValue;
        last = DateOnly.MinValue;
        foreach (var s in chart.Sections)
        {
            var lo = s.Start < s.End ? s.Start : s.End;
            var hi = s.Start < s.End ? s.End : s.Start;
            if (lo < first) first = lo;
            if (hi > last) last = hi;
        }
        return chart.Sections.Count > 0;
    }

    /// <summary>
    /// "2026-10-01 – 2027-01-10" in <paramref name="dateFormat"/>, falling back
    /// to ISO when the pattern is invalid; "No sections" for an empty chart.
    /// </summary>
    public static string DescribeSpan(Chart chart, string? dateFormat)
    {
        if (!TryGetSpan(chart, out var first, out var last)) return "No sections";
        var pattern = DateFormats.IsValidPattern(dateFormat, out _) ? dateFormat! : DateFormats.Iso;
        return $"{DateFormats.Format(first, pattern)} – {DateFormats.Format(last, pattern)}";
    }

    /// <summary>"1 section" / "5 sections".</summary>
    public static string DescribeSections(int count) =>
        count == 1 ? "1 section" : $"{count.ToString(CultureInfo.InvariantCulture)} sections";

    /// <summary>"Not placed" / "On 1 sheet" / "On 3 sheets".</summary>
    public static string DescribePlacement(int sheetCount) => sheetCount switch
    {
        <= 0 => "Not placed",
        1 => "On 1 sheet",
        _ => $"On {sheetCount.ToString(CultureInfo.InvariantCulture)} sheets",
    };
}
