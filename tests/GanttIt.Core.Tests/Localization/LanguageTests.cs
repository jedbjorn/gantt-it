using GanttIt.Core.Dates;
using GanttIt.Core.Editing;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Core.Serialization;
using GanttIt.Core.Timeline;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Localization;

/// <summary>A24: Revit language mapping, chart text tables, month names in dates, and the chart-language setting.</summary>
public class LanguageTests
{
    // ---- UI language from Revit's LanguageType ------------------------------------------

    [Theory]
    [InlineData("French", Language.French)]
    [InlineData("German", Language.German)]
    [InlineData("Spanish", Language.Spanish)]
    [InlineData("English_USA", Language.English)]
    [InlineData("English_GB", Language.English)]
    [InlineData("Unknown", Language.English)]
    [InlineData("Italian", Language.English)]
    [InlineData("Dutch", Language.English)]
    [InlineData("Chinese_Simplified", Language.English)]
    [InlineData("Chinese_Traditional", Language.English)]
    [InlineData("Japanese", Language.English)]
    [InlineData("Korean", Language.English)]
    [InlineData("Russian", Language.English)]
    [InlineData("Czech", Language.English)]
    [InlineData("Polish", Language.English)]
    [InlineData("Hungarian", Language.English)]
    [InlineData("Brazilian_Portuguese", Language.English)]
    [InlineData("French_Canada", Language.French)]     // a future variant still maps by its language
    [InlineData("Spanish_Mexico", Language.Spanish)]
    [InlineData("german", Language.German)]
    [InlineData("", Language.English)]
    [InlineData(null, Language.English)]
    public void RevitLanguage_MapsFrenchGermanSpanish_EverythingElseToEnglish(string? languageType, Language expected)
    {
        Assert.Equal(expected, Languages.FromRevitLanguage(languageType));
    }

    [Theory]
    [InlineData("en", Language.English)]
    [InlineData("fr", Language.French)]
    [InlineData("ES", Language.Spanish)]
    [InlineData(" de ", Language.German)]
    public void LanguageCodes_Parse(string code, Language expected)
    {
        Assert.True(Languages.TryParseCode(code, out var language));
        Assert.Equal(expected, language);
        Assert.Equal(code.Trim().ToLowerInvariant(), Languages.Code(language));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("it")]
    [InlineData("fra")]
    public void UnknownLanguageCodes_AreRefused(string? code)
    {
        Assert.False(Languages.TryParseCode(code, out var language));
        Assert.Equal(Language.English, language);
    }

    [Fact]
    public void Settings_ListEachLanguageByItsOwnName()
    {
        Assert.Equal(["English", "Français", "Español", "Deutsch"], Languages.All.Select(Languages.NativeName));
    }

    // ---- chart text tables ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Layout.LayoutEngineTests.AllLanguages), MemberType = typeof(Layout.LayoutEngineTests))]
    public void MonthTables_HaveTwelveDistinctNamesAndAbbreviations(Language language)
    {
        var text = ChartText.For(language);
        var names = Enumerable.Range(1, 12).Select(text.MonthName).ToList();
        var abbreviations = Enumerable.Range(1, 12).Select(text.MonthAbbreviation).ToList();

        Assert.Equal(12, names.Distinct().Count());
        Assert.Equal(12, abbreviations.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(abbreviations, a => Assert.True(a.TrimEnd('.').Length >= 3, a));
        for (var m = 0; m < 12; m++)
            Assert.True(abbreviations[m].Length <= names[m].Length, $"{names[m]} / {abbreviations[m]}");
    }

    [Theory]
    [InlineData(Language.English, "October 2026", "Oct 2026", "Oct '26", "19 Oct", "2026|'26")]
    [InlineData(Language.French, "octobre 2026", "oct. 2026", "oct. 26", "19 oct.", "2026")]
    [InlineData(Language.Spanish, "octubre 2026", "oct 2026", "oct 26", "19 oct", "2026")]
    [InlineData(Language.German, "Oktober 2026", "Okt 2026", "Okt '26", "19. Okt", "2026|'26")]
    public void TimescaleLabelForms_PerLanguage(Language language, string full, string abbreviated, string shortYear, string week, string years)
    {
        Assert.Equal([full, abbreviated, shortYear], TimescaleLabels.Candidates(TimeUnit.Month, true, D(2026, 10, 1), "yyyy-MM-dd", language));
        Assert.Equal(["2026-10-19", week], TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 10, 19), "yyyy-MM-dd", language));
        Assert.Equal(years.Split('|'), TimescaleLabels.Candidates(TimeUnit.Year, true, D(2026, 1, 1), "yyyy-MM-dd", language));
        Assert.Equal(["19"], TimescaleLabels.Candidates(TimeUnit.Week, false, D(2026, 10, 19), "yyyy-MM-dd", language));
    }

    [Theory]
    [MemberData(nameof(Layout.LayoutEngineTests.AllLanguages), MemberType = typeof(Layout.LayoutEngineTests))]
    public void TopTierMonthLabels_KeepTheYear_LowerTierNeverBelowTheAbbreviation(Language language)
    {
        var text = ChartText.For(language);
        for (var month = 1; month <= 12; month++)
        {
            var start = D(2026, month, 1);
            var abbreviation = text.MonthAbbreviation(month);
            Assert.All(TimescaleLabels.Candidates(TimeUnit.Month, false, start, "yyyy-MM-dd", language),
                l => Assert.True(l.Length >= abbreviation.Length && (l == abbreviation || l == text.MonthName(month)), l));
            Assert.All(TimescaleLabels.Candidates(TimeUnit.Month, true, start, "yyyy-MM-dd", language),
                l => Assert.Matches(@"^\p{L}[\p{L}.]{2,} ('?\d\d|\d{4})$", l));
        }
    }

    [Theory]
    [InlineData("MM/yyyy")]
    [InlineData("MMM yyyy")]
    [InlineData("MMMM")]
    [InlineData("dd")]
    [InlineData("ddd d")]
    public void Tier1Weeks_WithAPatternThatCantTellWeeksApart_FallBackToDayAndMonth(string pattern)
    {
        // 1 and 8 Feb 2026 (or 1 Feb and 1 Mar) would read the same.
        Assert.Equal(["2 Nov"], TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 11, 2), pattern));
        Assert.Equal(["2 nov."], TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 11, 2), pattern, Language.French));
    }

    [Theory]
    [InlineData(Language.English, "Gantt Chart 1", "Gantt Chart 2", "Section 3")]
    [InlineData(Language.French, "Diagramme de Gantt 1", "Diagramme de Gantt 2", "Section 3")]
    [InlineData(Language.Spanish, "Diagrama de Gantt 1", "Diagrama de Gantt 2", "Sección 3")]
    [InlineData(Language.German, "Gantt-Diagramm 1", "Gantt-Diagramm 2", "Abschnitt 3")]
    public void DefaultNames_FollowTheChartLanguage(Language language, string first, string second, string section3)
    {
        Assert.Equal(first, EditorInput.UniqueChartName([], language));
        Assert.Equal(second, EditorInput.UniqueChartName([first.ToUpperInvariant()], language));
        Assert.Equal(section3, ChartText.For(language).DefaultSectionName(3));
    }

    // ---- month names in date patterns (§2.6 item 6) ---------------------------------------

    [Theory]
    [InlineData(Language.English, "31 December 2026", "Thu 31 Dec 2026")]
    [InlineData(Language.French, "31 décembre 2026", "jeu. 31 déc. 2026")]
    [InlineData(Language.Spanish, "31 diciembre 2026", "jue 31 dic 2026")]
    [InlineData(Language.German, "31 Dezember 2026", "Do 31 Dez 2026")]
    public void CustomPatterns_NameMonthsAndDaysInTheLanguage_DigitsStayInvariant(Language language, string longForm, string shortForm)
    {
        var date = D(2026, 12, 31);
        Assert.Equal(longForm, DateFormats.Format(date, "d MMMM yyyy", language));
        Assert.Equal(shortForm, DateFormats.Format(date, "ddd d MMM yyyy", language));
        Assert.Equal("2026-12-31", DateFormats.Format(date, DateFormats.Iso, language));
        Assert.Equal("31.12.2026", DateFormats.Format(date, DateFormats.EuDot, language));
    }

    [Theory]
    [MemberData(nameof(Layout.LayoutEngineTests.AllLanguages), MemberType = typeof(Layout.LayoutEngineTests))]
    public void MonthNamePatterns_ReadBackInTheLanguage(Language language)
    {
        foreach (var pattern in new[] { "d MMM yyyy", "dd MMMM yyyy", "dddd d MMMM yyyy" })
        {
            Assert.Equal(pattern, DateFormats.EntryPattern(pattern, language));
            for (var month = 1; month <= 12; month++)
            {
                var date = D(2027, month, 9);
                var text = DateFormats.Format(date, pattern, language);
                Assert.True(DateFormats.TryParse(text, pattern, out var back, language), $"{language} '{text}'");
                Assert.Equal(date, back);
                Assert.True(DateFormats.TryParse(text.ToUpperInvariant(), pattern, out back, language), $"{language} '{text}' upper case");
                Assert.Equal(date, back);
            }
        }
    }

    [Fact]
    public void MonthNames_FromAnotherLanguage_DoNotParse()
    {
        Assert.False(DateFormats.TryParse("9 janv. 2027", "d MMM yyyy", out _, Language.English));
        Assert.True(DateFormats.TryParse("9 janv. 2027", "d MMM yyyy", out var date, Language.French));
        Assert.Equal(D(2027, 1, 9), date);
    }

    // ---- the chart-language setting (§2.6 item 3) -------------------------------------------

    [Fact]
    public void Settings_RoundTripTheChartLanguage()
    {
        var settings = new ProjectSettings { ChartLanguage = Language.German };

        var json = SettingsJson.Serialize(settings);
        var back = SettingsJson.Deserialize(json);

        Assert.Contains("\"chartLanguage\":\"German\"", json);
        Assert.True(back.IsEditable);
        Assert.Equal(Language.German, back.Value!.ChartLanguage);
        Assert.Equal(json, SettingsJson.Serialize(back.Value));
        Assert.Equal(Language.German, settings.Clone().ChartLanguage);
    }

    [Fact]
    public void Settings_WithoutTheField_ReadAsEnglish_TheLanguageTheyWereDrawnIn()
    {
        var json = SettingsJson.Serialize(new ProjectSettings { ChartLanguage = Language.French }).Replace("\"chartLanguage\":\"French\",", "");
        Assert.DoesNotContain("chartLanguage", json);

        var back = SettingsJson.Deserialize(json);

        Assert.True(back.IsEditable);
        Assert.Equal(Language.English, back.Value!.ChartLanguage);
        Assert.Equal(1, back.Value.SchemaVersion);
    }

    [Theory]
    [InlineData("\"Klingon\"")]
    [InlineData("9")]
    public void Settings_WithAnUnknownChartLanguage_AreInvalid(string value)
    {
        var json = SettingsJson.Serialize(new ProjectSettings()).Replace("\"chartLanguage\":\"English\"", $"\"chartLanguage\":{value}");

        Assert.Equal(LoadStatus.Invalid, SettingsJson.Deserialize(json).Status);
    }

    [Fact]
    public void NewerSettings_WithAChartLanguage_StayReadOnly()
    {
        var json = SettingsJson.Serialize(new ProjectSettings { ChartLanguage = Language.Spanish })
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":2");

        var back = SettingsJson.Deserialize(json);

        Assert.Equal(LoadStatus.NewerVersion, back.Status);
        Assert.Equal(Language.Spanish, back.Value!.ChartLanguage);
    }
}
