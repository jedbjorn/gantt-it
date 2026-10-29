using System.Globalization;

namespace GanttIt.Core.Localization;

/// <summary>
/// Every word the generator draws on a chart, in one chart language (spec §2.6,
/// decision #22): column headers, month and day names, the timescale label forms
/// and the default chart and section names. Hard-coded tables rather than
/// <see cref="CultureInfo"/> data, so a chart is identical on every Windows version
/// (NLS or ICU) and on the Linux CI (ICU or invariant mode).
/// </summary>
/// <remarks>
/// Label forms per language (the timescale rules of §6.2 hold for each: one form
/// per tier, a month never shorter than its abbreviation, top-tier labels keep the
/// year, thinning instead of shortening):
/// <list type="table">
/// <item><term>English</term><description>October 2026 · Oct 2026 · Oct '26; January · Jan; 19 Oct; Q4; 2026 · '26</description></item>
/// <item><term>French</term><description>octobre 2026 · oct. 2026 · oct. 26; janvier · janv.; 19 oct.; T4; 2026</description></item>
/// <item><term>Spanish</term><description>octubre 2026 · oct 2026 · oct 26; enero · ene; 19 oct; T4; 2026</description></item>
/// <item><term>German</term><description>Oktober 2026 · Okt 2026 · Okt '26; Januar · Jan; 19. Okt; Q4; 2026 · '26</description></item>
/// </list>
/// French and Spanish write a year in full: an apostrophe year (<c>'26</c>) is an
/// anglicism there, so a crowded year tier thins instead.
/// </remarks>
public sealed class ChartText
{
    private readonly string[] _months;
    private readonly string[] _abbreviations;
    private readonly Func<string, string, string> _dayMonth;
    private readonly Func<string, string, string> _monthShortYear;
    private readonly string _quarterPrefix;
    private readonly string _chartName;
    private readonly string _sectionName;

    private ChartText(
        Language language,
        string nameHeader, string startHeader, string endHeader, string durationHeader,
        string[] months, string[] abbreviations, string[] days, string[] dayAbbreviations,
        Func<string, string, string> dayMonth, Func<string, string, string> monthShortYear,
        bool shortYears, string quarterPrefix, string chartName, string sectionName)
    {
        Language = language;
        NameHeader = nameHeader;
        StartHeader = startHeader;
        EndHeader = endHeader;
        DurationHeader = durationHeader;
        _months = months;
        _abbreviations = abbreviations;
        _dayMonth = dayMonth;
        _monthShortYear = monthShortYear;
        HasShortYears = shortYears;
        _quarterPrefix = quarterPrefix;
        _chartName = chartName;
        _sectionName = sectionName;
        DateFormat = BuildDateFormat(months, abbreviations, days, dayAbbreviations);
    }

    public Language Language { get; }

    /// <summary>Header of the name column, e.g. "Section".</summary>
    public string NameHeader { get; }

    public string StartHeader { get; }

    public string EndHeader { get; }

    /// <summary>Header of the duration column, short where the word is long ("Dur").</summary>
    public string DurationHeader { get; }

    /// <summary>
    /// Formatting data for chart dates: the invariant patterns and digits, with this
    /// language's month and day names, so <c>MMM</c>/<c>MMMM</c>/<c>ddd</c>/<c>dddd</c>
    /// in a custom date pattern name the month or day in this language.
    /// </summary>
    public DateTimeFormatInfo DateFormat { get; }

    /// <summary>True when the language abbreviates a year as <c>'26</c> (English, German).</summary>
    public bool HasShortYears { get; }

    /// <summary>Full month name, <paramref name="month"/> 1–12.</summary>
    public string MonthName(int month) => _months[month - 1];

    /// <summary>The month's abbreviation, the shortest a month label ever gets (3+ letters).</summary>
    public string MonthAbbreviation(int month) => _abbreviations[month - 1];

    /// <summary>A day and month, e.g. "19 Oct", "19 oct.", "19. Okt".</summary>
    public string DayMonth(int day, int month) => _dayMonth(day.ToString(CultureInfo.InvariantCulture), MonthAbbreviation(month));

    /// <summary>Month and full year, e.g. "October 2026" (<paramref name="abbreviated"/>: "Oct 2026").</summary>
    public string MonthYear(int month, int year, bool abbreviated) =>
        $"{(abbreviated ? MonthAbbreviation(month) : MonthName(month))} {year.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Abbreviated month and two-digit year, e.g. "Oct '26", "oct. 26".</summary>
    public string MonthShortYear(int month, int year) =>
        _monthShortYear(MonthAbbreviation(month), (year % 100).ToString("00", CultureInfo.InvariantCulture));

    /// <summary>The year in full, e.g. "2026".</summary>
    public static string Year(int year) => year.ToString(CultureInfo.InvariantCulture);

    /// <summary>The two-digit year, e.g. "'26"; only offered where <see cref="HasShortYears"/>.</summary>
    public static string ShortYear(int year) => "'" + (year % 100).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>A quarter, <paramref name="quarter"/> 1–4: "Q1" (English, German) or "T1" (French, Spanish: trimestre).</summary>
    public string Quarter(int quarter) => _quarterPrefix + quarter.ToString(CultureInfo.InvariantCulture);

    /// <summary>The default name of the <paramref name="n"/>-th new chart, e.g. "Gantt Chart 1".</summary>
    public string DefaultChartName(int n) => string.Format(CultureInfo.InvariantCulture, _chartName, n);

    /// <summary>The default name of the <paramref name="n"/>-th section, e.g. "Section 3".</summary>
    public string DefaultSectionName(int n) => string.Format(CultureInfo.InvariantCulture, _sectionName, n);

    public static ChartText English { get; } = new(
        Language.English, "Section", "Start", "End", "Dur",
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
        (d, m) => $"{d} {m}", (m, yy) => $"{m} '{yy}", shortYears: true, "Q", "Gantt Chart {0}", "Section {0}");

    public static ChartText French { get; } = new(
        Language.French, "Section", "Début", "Fin", "Durée",
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"],
        ["janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc."],
        ["dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi"],
        ["dim.", "lun.", "mar.", "mer.", "jeu.", "ven.", "sam."],
        (d, m) => $"{d} {m}", (m, yy) => $"{m} {yy}", shortYears: false, "T", "Diagramme de Gantt {0}", "Section {0}");

    public static ChartText Spanish { get; } = new(
        Language.Spanish, "Sección", "Inicio", "Fin", "Dur.",
        ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"],
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic"],
        ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"],
        ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"],
        (d, m) => $"{d} {m}", (m, yy) => $"{m} {yy}", shortYears: false, "T", "Diagrama de Gantt {0}", "Sección {0}");

    public static ChartText German { get; } = new(
        Language.German, "Abschnitt", "Beginn", "Ende", "Dauer",
        ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"],
        ["Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"],
        ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"],
        ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"],
        (d, m) => $"{d}. {m}", (m, yy) => $"{m} '{yy}", shortYears: true, "Q", "Gantt-Diagramm {0}", "Abschnitt {0}");

    /// <summary>The tables for <paramref name="language"/>; English for an undefined value.</summary>
    public static ChartText For(Language language) => language switch
    {
        Language.French => French,
        Language.Spanish => Spanish,
        Language.German => German,
        _ => English,
    };

    private static DateTimeFormatInfo BuildDateFormat(string[] months, string[] abbreviations, string[] days, string[] dayAbbreviations)
    {
        var info = (DateTimeFormatInfo)CultureInfo.InvariantCulture.DateTimeFormat.Clone();
        string[] Thirteen(string[] twelve) => [.. twelve, ""];
        info.MonthNames = Thirteen(months);
        info.MonthGenitiveNames = Thirteen(months);
        info.AbbreviatedMonthNames = Thirteen(abbreviations);
        info.AbbreviatedMonthGenitiveNames = Thirteen(abbreviations);
        info.DayNames = days;
        info.AbbreviatedDayNames = dayAbbreviations;
        info.ShortestDayNames = dayAbbreviations;
        return DateTimeFormatInfo.ReadOnly(info);
    }
}
