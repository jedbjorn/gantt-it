using GanttIt.Core.Dates;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Core.Resources;

namespace GanttIt.Core.Editing;

/// <summary>The one-line facts a home-screen card shows about a chart (§2.1), in the UI language.</summary>
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
    /// "2026-10-01 – 2027-01-10" in <paramref name="dateFormat"/> (month names in the UI
    /// language), falling back to ISO when the pattern is invalid; "No sections" for an empty chart.
    /// </summary>
    public static string DescribeSpan(Chart chart, string? dateFormat)
    {
        if (!TryGetSpan(chart, out var first, out var last)) return CoreStrings.Summary_NoSections;
        var pattern = DateFormats.IsValidPattern(dateFormat, out _) ? dateFormat! : DateFormats.Iso;
        var language = UiLanguage.Current;
        return $"{DateFormats.Format(first, pattern, language)} – {DateFormats.Format(last, pattern, language)}";
    }

    /// <summary>"1 section" / "5 sections".</summary>
    public static string DescribeSections(int count) =>
        count == 1 ? CoreStrings.Summary_SectionsOne : CoreStrings.Summary_SectionsMany(count);

    /// <summary>"Not placed" / "On 1 sheet" / "On 3 sheets".</summary>
    public static string DescribePlacement(int sheetCount) => sheetCount switch
    {
        <= 0 => CoreStrings.Summary_NotPlaced,
        1 => CoreStrings.Summary_OnOneSheet,
        _ => CoreStrings.Summary_OnSheets(sheetCount),
    };
}
