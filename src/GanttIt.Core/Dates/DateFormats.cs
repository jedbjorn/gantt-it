using System.Globalization;

namespace GanttIt.Core.Dates;

/// <summary>
/// Date formatting for the date columns and timescale labels (§2.3).
/// Always culture-invariant, so output never depends on the machine's locale.
/// </summary>
public static class DateFormats
{
    public const string Iso = "yyyy-MM-dd";
    public const string UsSlash = "MM/dd/yyyy";
    public const string EuSlash = "dd/MM/yyyy";
    public const string EuDot = "dd.MM.yyyy";

    /// <summary>The preset patterns offered in Settings, in display order.</summary>
    public static readonly IReadOnlyList<string> Presets = [Iso, UsSlash, EuSlash, EuDot];

    private static readonly DateOnly Probe = new(2026, 12, 31);

    /// <summary>Formats <paramref name="date"/> with <paramref name="pattern"/>, culture-invariant.</summary>
    public static string Format(DateOnly date, string pattern) =>
        date.ToString(pattern, CultureInfo.InvariantCulture);

    /// <summary>
    /// Checks a custom pattern: it must format without error, produce
    /// non-blank single-line text and contain no time-of-day fields.
    /// </summary>
    public static bool IsValidPattern(string? pattern, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "The date format is blank.";
            return false;
        }

        string sample;
        try
        {
            sample = Format(Probe, pattern);
        }
        catch (FormatException ex)
        {
            error = $"'{pattern}' is not a valid date format: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sample) || sample.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            error = $"'{pattern}' does not produce a single line of text.";
            return false;
        }

        // DateOnly.ToString rejects time fields (h, H, m, s, f, t, z) itself,
        // so reaching here means the pattern is date-only.
        return true;
    }

    /// <summary>Full invariant month name, e.g. "January".</summary>
    public static string MonthName(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    /// <summary>Abbreviated invariant month name, e.g. "Jan".</summary>
    public static string MonthAbbreviation(int month) =>
        CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month);
}
