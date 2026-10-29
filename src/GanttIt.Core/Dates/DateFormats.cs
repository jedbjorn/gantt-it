using System.Globalization;
using GanttIt.Core.Localization;
using GanttIt.Core.Resources;

namespace GanttIt.Core.Dates;

/// <summary>
/// Date formatting for the date columns and timescale labels (§2.3).
/// Patterns are culture-invariant, so output never depends on the machine's locale;
/// only month and day names (<c>MMM</c>, <c>MMMM</c>, <c>ddd</c>, <c>dddd</c>) follow
/// a <see cref="Language"/>: the chart language for chart output, the UI language in
/// the editor's fields (§2.6). English (the default) is the invariant names.
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

    /// <summary>
    /// Formats <paramref name="date"/> with <paramref name="pattern"/>: invariant digits
    /// and separators, month and day names in <paramref name="language"/>.
    /// </summary>
    public static string Format(DateOnly date, string pattern, Language language = Language.English) =>
        date.ToString(pattern, ChartText.For(language).DateFormat);

    /// <summary>
    /// Checks a custom pattern: it must format without error, produce
    /// non-blank single-line text and contain no time-of-day fields.
    /// </summary>
    public static bool IsValidPattern(string? pattern, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = CoreStrings.DateFormat_Blank;
            return false;
        }

        string sample;
        try
        {
            sample = Format(Probe, pattern);
        }
        catch (FormatException ex)
        {
            error = CoreStrings.DateFormat_Invalid(pattern, ex.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(sample) || sample.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            error = CoreStrings.DateFormat_NotSingleLine(pattern);
            return false;
        }

        // DateOnly.ToString rejects time fields (h, H, m, s, f, t, z) itself,
        // so reaching here means the pattern is date-only.
        return true;
    }

    /// <summary>
    /// The pattern the editor's date fields show and accept for the configured
    /// <paramref name="pattern"/>: the pattern itself when a date formatted with it
    /// can be read back unambiguously, otherwise <see cref="Iso"/>. A pattern without
    /// a day, month or year field (e.g. <c>MMM yyyy</c>) can't identify a date, so the
    /// fields fall back to ISO instead of guessing the missing part. The same goes for a
    /// two-digit year (<c>dd/MM/yy</c>): it can't tell 1926 from 2026 within the supported
    /// range, which the probes cover end to end. Month and day names are read in
    /// <paramref name="language"/>.
    /// </summary>
    public static string EntryPattern(string? pattern, Language language = Language.English)
    {
        if (!IsValidPattern(pattern, out _)) return Iso;
        foreach (var probe in RoundTripProbes)
        {
            string text;
            try
            {
                text = Format(probe, pattern!, language);
            }
            catch (FormatException)
            {
                return Iso;
            }
            if (!TryParseExact(text, pattern!, language, out var back) || back != probe) return Iso;
        }
        return pattern!;
    }

    /// <summary>
    /// Reads a typed date written with <paramref name="pattern"/> (as returned by
    /// <see cref="EntryPattern"/>), culture-invariant. The day, month and year must
    /// be in the pattern's order, so <c>03/04/2026</c> is 3 April with <c>dd/MM/yyyy</c>
    /// and 4 March with <c>MM/dd/yyyy</c>, never the machine locale's reading. Leading
    /// zeros may be left out (<c>2026-1-5</c> for <c>yyyy-MM-dd</c>); surrounding
    /// spaces are ignored. Month and day names are read in <paramref name="language"/>
    /// (any case).
    /// </summary>
    public static bool TryParse(string? text, string pattern, out DateOnly date, Language language = Language.English)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(pattern)) return false;
        var s = text.Trim();
        if (TryParseExact(s, pattern, language, out date)) return true;
        var relaxed = WithoutLeadingZeros(pattern);
        return relaxed != pattern && TryParseExact(s, relaxed, language, out date);
    }

    /// <summary>
    /// The UI message for typed text that <see cref="TryParse"/> rejected, naming the
    /// pattern with an example in <paramref name="language"/> (shown in the field's
    /// tooltip and the editor's issue list).
    /// </summary>
    public static string TypedDateError(string text, string pattern, Language language = Language.English) =>
        CoreStrings.DateFormat_TypedError(text.Trim(), pattern, Format(ErrorSample, pattern, language));

    private static readonly DateOnly ErrorSample = new(2026, 12, 31);

    /// <summary>
    /// <paramref name="pattern"/> with each two-letter day (<c>dd</c>) and month
    /// (<c>MM</c>) field shortened to one letter, which parses one or two digits.
    /// Quoted and escaped literals are copied unchanged. A pattern with two fields
    /// side by side (<c>ddMMyyyy</c>) is returned unchanged: without a separator,
    /// one-or-two-digit fields could split the digits in more than one way.
    /// </summary>
    public static string WithoutLeadingZeros(string pattern)
    {
        var sb = new System.Text.StringBuilder(pattern.Length);
        var i = 0;
        var afterField = false;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                afterField = false;
                var end = pattern.IndexOf(c, i + 1);
                end = end < 0 ? pattern.Length - 1 : end;
                sb.Append(pattern, i, end - i + 1);
                i = end + 1;
                continue;
            }
            if (c == '\\')
            {
                afterField = false;
                sb.Append(pattern, i, Math.Min(2, pattern.Length - i));
                i += 2;
                continue;
            }
            var run = 1;
            while (i + run < pattern.Length && pattern[i + run] == c) run++;
            var isField = char.IsAsciiLetter(c);
            if (isField && afterField) return pattern;
            afterField = isField;
            if (run == 2 && c is 'd' or 'M') sb.Append(c);
            else sb.Append(c, run);
            i += run;
        }
        var result = sb.ToString();
        // A one-letter custom pattern would be read as a standard format ("d" = short date).
        return result.Length == 1 ? "%" + result : result;
    }

    /// <summary>Dates a usable entry pattern must read back exactly, including both ends of <see cref="Model.SupportedDates"/>.</summary>
    private static readonly DateOnly[] RoundTripProbes =
        [Model.SupportedDates.Min, new(2026, 12, 31), new(2027, 1, 2), new(2031, 7, 9), Model.SupportedDates.Max];

    private static bool TryParseExact(string text, string pattern, Language language, out DateOnly date)
    {
        try
        {
            return DateOnly.TryParseExact(text, pattern, ChartText.For(language).DateFormat, DateTimeStyles.AllowWhiteSpaces, out date);
        }
        catch (FormatException)
        {
            date = default;
            return false;
        }
    }

    /// <summary>Full month name in <paramref name="language"/>, e.g. "January", "janvier".</summary>
    public static string MonthName(int month, Language language = Language.English) =>
        ChartText.For(language).MonthName(month);

    /// <summary>Abbreviated month name in <paramref name="language"/>, e.g. "Jan", "janv.".</summary>
    public static string MonthAbbreviation(int month, Language language = Language.English) =>
        ChartText.For(language).MonthAbbreviation(month);
}
