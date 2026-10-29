using GanttIt.Core.Layout;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Core.Tests.Support;
using GanttIt.Core.Timeline;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Layout;

/// <summary>A24: the chart language drives every word on the chart, and the §6.2 timescale rules hold in each language.</summary>
public partial class LayoutEngineTests
{
    public static TheoryData<Language> AllLanguages() => [.. Languages.All];

    private static ProjectSettings SettingsIn(Language language, string? dateFormat = null)
    {
        var settings = Settings();
        settings.ChartLanguage = language;
        if (dateFormat is not null) settings.DateFormat = dateFormat;
        return settings;
    }

    private static ChartLayout RunIn(Chart chart, Language language, string? dateFormat = null) =>
        LayoutEngine.Layout(chart, SettingsIn(language, dateFormat), new FakeMeasurer());

    [Theory]
    [InlineData(Language.English, "Section", "Start", "End", "Dur")]
    [InlineData(Language.French, "Section", "Début", "Fin", "Durée")]
    [InlineData(Language.Spanish, "Sección", "Inicio", "Fin", "Dur.")]
    [InlineData(Language.German, "Abschnitt", "Beginn", "Ende", "Dauer")]
    public void ColumnHeaders_FollowTheChartLanguage(Language language, string name, string start, string end, string duration)
    {
        var cells = LayoutEngine.BuildCells(TwoSections(), SettingsIn(language));

        Assert.Equal([name, start, end, duration], cells.Headers.Select(h => h.Text));
    }

    [Theory]
    [InlineData(Language.English, "Oct", "Nov 2026")]
    [InlineData(Language.French, "oct.", "nov. 2026")]
    [InlineData(Language.Spanish, "oct", "nov 2026")]
    [InlineData(Language.German, "Okt", "Nov 2026")]
    public void BodyDates_NameMonthsInTheChartLanguage(Language language, string octAbbreviation, string novemberLabel)
    {
        var cells = LayoutEngine.BuildCells(TwoSections(), SettingsIn(language, "d MMM yyyy"));

        Assert.Equal($"1 {octAbbreviation} 2026", cells.Rows[0][1].Text);
        Assert.Contains(cells.Tiers[0].Candidates.SelectMany(c => c), r => r.Text == novemberLabel);
    }

    [Theory]
    [InlineData(Language.English, "Q1")]
    [InlineData(Language.French, "T1")]
    [InlineData(Language.Spanish, "T1")]
    [InlineData(Language.German, "Q1")]
    public void QuarterLabels_FollowTheChartLanguage(Language language, string q1)
    {
        var chart = Spanning(10, D(2020, 1, 1), D(2029, 12, 31));
        chart.TimelineWidth = Mm(90);

        var layout = RunIn(chart, language);

        Assert.Equal(new TierPair(TimeUnit.Year, TimeUnit.Quarter), layout.Grid.Axis!.Tiers);
        Assert.All(TierLabels(layout, 1), l => Assert.Equal(q1, l));
        AssertHeaderNamesItsPeriods(chart, layout, $"{language} quarters", SettingsIn(language));
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguage_EdgeCases_EveryTierCellNamesItsPeriod(Language language)
    {
        foreach (var row in EdgeCases())
        {
            var (timescale, first, last) = ((Timescale)row[0], (DateOnly)row[1], (DateOnly)row[2]);
            foreach (var timelineMm in new[] { 40.0, 60, 120, 180, 300 })
            {
                var chart = EdgeChart(timescale, first, last, timelineMm);
                AssertHeaderNamesItsPeriods(chart, RunIn(chart, language),
                    $"{language} {timescale} {first}..{last} at {timelineMm} mm", SettingsIn(language));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguage_ReviewProbes_NoCellHidesMoreThanASliver(Language language)
    {
        foreach (var row in ReviewProbes())
        {
            var (timescale, first, last, timelineMm) = ((Timescale)row[0], (DateOnly)row[1], (DateOnly)row[2], (double)row[3]);
            var chart = EdgeChart(timescale, first, last, timelineMm);
            AssertHeaderNamesItsPeriods(chart, RunIn(chart, language),
                $"{language} {timescale} {first}..{last} at {timelineMm} mm", SettingsIn(language));
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryLanguage_MonthLabelsThin_NeverDropBelowTheAbbreviation(Language language)
    {
        var text = ChartText.For(language);
        foreach (var width in new[] { 10.0, 20, 40, 80, 150, 240 })
        {
            var layout = RunIn(OneYear(width), language);
            Assert.Equal(new TierPair(TimeUnit.Year, TimeUnit.Month), layout.Grid.Axis!.Tiers);
            foreach (var label in TierLabels(layout, 1))
            {
                var month = Enumerable.Range(1, 12).Single(m => label == text.MonthName(m) || label == text.MonthAbbreviation(m));
                Assert.True(label.Length >= text.MonthAbbreviation(month).Length, $"{language} at {width} mm: '{label}'");
            }
            foreach (var t in layout.Grid.Texts)
                Assert.True(t.Size.Width + 2 * Pad <= t.Cell.Width + Eps, $"{language}: '{t.Request.Text}' overflows at {width} mm");
        }
    }

    [Fact]
    public void FrenchAndSpanish_YearTierThinsInsteadOfShorteningToAnApostropheYear()
    {
        // At 55 mm English shortens the edge years to "'26" (MultiYear test); French keeps
        // four-digit years and labels fewer of them.
        foreach (var language in new[] { Language.French, Language.Spanish })
        {
            var chart = EdgeChart(Timescale.Months, D(2026, 3, 2), D(2029, 7, 27), 55);
            var layout = RunIn(chart, language);

            Assert.All(TierLabels(layout, 0), l => Assert.Matches(@"^\d{4}$", l));
            AssertHeaderNamesItsPeriods(chart, layout, $"{language} 55 mm", SettingsIn(language));
        }
    }

    [Fact]
    public void Tier1Weeks_WithAMonthOnlyPattern_FallBackToDayAndMonth_InEveryLanguage()
    {
        // MM/yyyy names no day: every week of November would read "11/2026".
        var chart = EdgeChart(Timescale.Days, D(2026, 11, 2), D(2026, 11, 24), 180);
        foreach (var language in Languages.All)
        {
            var layout = RunIn(chart, language, "MM/yyyy");
            var labels = TierLabels(layout, 0);

            Assert.Equal(labels.Count, labels.Distinct().Count());
            Assert.Equal(ChartText.For(language).DayMonth(2, 11), labels[0]);
            AssertHeaderNamesItsPeriods(chart, layout, $"{language} MM/yyyy", SettingsIn(language, "MM/yyyy"));
        }
    }
}
