using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Tests.Support;
using GanttIt.Core.Timeline;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Layout;

/// <summary>A3: the grid solver rules (§6.2) and clean-line emission (§6.3), with a fake measurer.</summary>
public class LayoutEngineTests
{
    private const double Eps = 1e-12;
    private static readonly double Pad = ProjectSettings.DefaultCellPadding;

    private static double Mm(double mm) => Units.FromMillimeters(mm);

    private static ChartLayout Run(Chart chart, FakeMeasurer? measurer = null) =>
        LayoutEngine.Layout(chart, Settings(), measurer ?? new FakeMeasurer());

    private static Chart TwoSections()
    {
        var chart = Chart.Create("Schedule", Id(1));
        chart.Sections.Add(Section(1, D(2026, 10, 1), D(2026, 11, 15), "Design"));
        chart.Sections.Add(Section(2, D(2026, 11, 16), D(2027, 1, 10), "Permitting"));
        return chart;
    }

    private static Chart OneYear(double timelineMm)
    {
        var chart = Chart.Create("Year", Id(1));
        chart.TimelineWidth = Mm(timelineMm);
        chart.Sections.Add(Section(1, D(2026, 1, 1), D(2026, 12, 31)));
        return chart;
    }

    private static List<PlacedText> TierTexts(ChartLayout layout, int tier)
    {
        var t = layout.Grid.Tiers[tier];
        return layout.Grid.Texts
            .Where(p => p.Cell.Top == t.Top && p.Cell.Bottom == t.Bottom && p.Cell.Left >= layout.Grid.TimelineLeft)
            .ToList();
    }

    private static List<string> TierLabels(ChartLayout layout, int tier) =>
        TierTexts(layout, tier).Select(p => p.Request.Text).ToList();

    // ---- Columns ------------------------------------------------------------------

    [Fact]
    public void Columns_AreTheWidestMeasuredText_HeaderIncluded_PlusTwicePadding()
    {
        var grid = Run(TwoSections()).Grid;

        double Width(int c) => grid.ColumnEdges[c + 1] - grid.ColumnEdges[c];
        Assert.Equal([TableColumn.Name, TableColumn.Start, TableColumn.End, TableColumn.Duration], grid.Columns);
        Assert.Equal(10 * FakeMeasurer.BodyCharWidth + 2 * Pad, Width(0), Eps);   // "Permitting"
        Assert.Equal(10 * FakeMeasurer.BodyCharWidth + 2 * Pad, Width(1), Eps);   // "2026-10-01"
        Assert.Equal(10 * FakeMeasurer.BodyCharWidth + 2 * Pad, Width(2), Eps);
        Assert.Equal(3 * FakeMeasurer.HeaderCharWidth + 2 * Pad, Width(3), Eps);  // header "Dur" beats "46"
        Assert.Equal(grid.TimelineLeft + Chart.DefaultTimelineWidth, grid.Width, Eps);
    }

    [Fact]
    public void Columns_FollowTheChartFlags_NameAlwaysShown()
    {
        var chart = TwoSections();
        chart.Columns = ChartColumns.Duration;

        var grid = Run(chart).Grid;

        Assert.Equal([TableColumn.Name, TableColumn.Duration], grid.Columns);
    }

    // ---- Name wrapping and row growth -------------------------------------------------

    [Fact]
    public void NameColumn_IsCappedAtNameMaxWidth_LongNamesWrapAndAreMeasuredAgain()
    {
        var chart = TwoSections();
        const string longName = "Structural steel fabrication and erection for the main tower";
        chart.Sections[1].Name = longName;
        var measurer = new FakeMeasurer();

        var layout = Run(chart, measurer);
        var grid = layout.Grid;

        Assert.Equal(chart.NameMaxWidth, grid.ColumnEdges[1] - grid.ColumnEdges[0], Eps);
        var placed = grid.Texts.Single(t => t.Request.Text == longName);
        Assert.Equal(chart.NameMaxWidth - 2 * Pad, placed.Request.WrapWidth);
        Assert.Equal(FakeMeasurer.Size(placed.Request), placed.Size);
        Assert.True(placed.Size.Width + 2 * Pad <= chart.NameMaxWidth);

        // Second batch re-measures exactly the wrapped name; the short one is not wrapped.
        Assert.Equal(2, measurer.Batches.Count);
        Assert.Equal(placed.Request, Assert.Single(measurer.Batches[1]));
        Assert.Null(grid.Texts.Single(t => t.Request.Text == "Design").Request.WrapWidth);
    }

    [Fact]
    public void Rows_WithWrappedNames_Grow_AndPushLaterRowsDown()
    {
        var chart = Sequential(3, D(2026, 1, 1));
        var plain = Run(chart).Grid;
        chart.Sections[1].Name = "A very long section name that will certainly have to wrap onto several lines";

        var grid = Run(chart).Grid;

        double Height(GridLayout g, int row) => g.RowEdges[row] - g.RowEdges[row + 1];
        var lines = FakeMeasurer.Lines(chart.Sections[1].Name, FakeMeasurer.BodyCharWidth, chart.NameMaxWidth - 2 * Pad).Count;
        Assert.True(lines >= 2);
        Assert.Equal(Height(plain, 0), Height(grid, 0), Eps);
        Assert.Equal(lines * FakeMeasurer.BodyLineHeight + 2 * Pad, Height(grid, 1), Eps);
        Assert.Equal(Height(plain, 2), Height(grid, 2), Eps);
        var growth = Height(grid, 1) - Height(plain, 1);
        Assert.Equal(plain.Bottom - growth, grid.Bottom, 1e-9);
    }

    [Fact]
    public void Rows_AreTheTallestCellPlusTwicePadding_IncludingExplicitLineBreaks()
    {
        var chart = Sequential(2, D(2026, 1, 1));
        chart.Sections[0].Name = "Two\nlines";

        var grid = Run(chart).Grid;

        Assert.Equal(2 * FakeMeasurer.BodyLineHeight + 2 * Pad, grid.RowEdges[0] - grid.RowEdges[1], Eps);
        Assert.Equal(FakeMeasurer.BodyLineHeight + 2 * Pad, grid.RowEdges[1] - grid.RowEdges[2], Eps);
    }

    [Fact]
    public void Title_LongerThanTheChart_WrapsAtTheChartWidth()
    {
        var chart = TwoSections();
        chart.Title = string.Join(' ', Enumerable.Repeat("Programme", 20));

        var grid = Run(chart).Grid;

        var title = grid.Texts.Single(t => t.Request.Style == TextStyleKey.Title);
        Assert.Equal(grid.Width - 2 * Pad, title.Request.WrapWidth);
        Assert.True(title.Size.Width + 2 * Pad <= grid.Width);
        Assert.Equal(-(title.Size.Height + 2 * Pad), grid.TitleBottom, Eps);
    }

    [Fact]
    public void BlankTitle_HasNoTitleRow()
    {
        var chart = TwoSections();
        chart.Title = " ";

        var grid = Run(chart).Grid;

        Assert.Equal(0, grid.TitleBottom);
        Assert.DoesNotContain(grid.Texts, t => t.Request.Style == TextStyleKey.Title);
    }

    // ---- Header tiers ---------------------------------------------------------------

    [Fact]
    public void HeaderTiers_AreTheTallestLabelPlusTwicePadding()
    {
        var grid = Run(TwoSections()).Grid;

        var tier = FakeMeasurer.HeaderLineHeight + 2 * Pad;
        Assert.Equal(tier, grid.TitleBottom - grid.Tier1Bottom, Eps);
        Assert.Equal(tier, grid.Tier1Bottom - grid.HeaderBottom, Eps);
        Assert.Equal(FakeMeasurer.TitleLineHeight + 2 * Pad, -grid.TitleBottom, Eps);
    }

    // ---- Anchoring ------------------------------------------------------------------

    [Fact]
    public void Text_IsAnchoredAtCellLeftPlusPadding_AndTopMinusPadding_DurationRight()
    {
        var layout = Run(TwoSections());
        var texts = layout.Primitives.OfType<TextPrimitive>().ToList();
        Assert.Equal(layout.Grid.Texts.Count, texts.Count);

        foreach (var (placed, text) in layout.Grid.Texts.Zip(texts))
        {
            Assert.Equal(placed.Cell.Top - Pad, text.Anchor.Y, Eps);
            Assert.Equal(text.Anchor.Y, text.MeasuredBox.Top);
            Assert.Equal(placed.Size.Height, text.MeasuredBox.Height, Eps);
            if (text.Align == TextAlign.Left)
            {
                Assert.Equal(placed.Cell.Left + Pad, text.Anchor.X, Eps);
                Assert.Equal(text.Anchor.X, text.MeasuredBox.Left);
            }
            else
            {
                Assert.Equal(placed.Cell.Right - Pad, text.Anchor.X, Eps);
                Assert.Equal(text.Anchor.X, text.MeasuredBox.Right);
            }
        }

        var durationColumn = layout.Grid.Columns.ToList().IndexOf(TableColumn.Duration);
        var right = layout.Grid.ColumnEdges[durationColumn + 1];
        Assert.All(texts.Where(t => t.Text is "46" or "56" or LayoutEngine.DurationHeader),
            t => Assert.Equal(TextAlign.Right, t.Align));
        Assert.Contains(texts, t => t.Text == "46" && Math.Abs(t.Anchor.X - (right - Pad)) < Eps);
    }

    // ---- Timescale labels: shortening, then thinning ---------------------------------

    [Fact]
    public void TimescaleLabels_UseFullNamesWhenTheyFit()
    {
        var layout = Run(OneYear(240));
        Assert.Equal(1, layout.Grid.Tiers[1].GroupSize);
        Assert.Equal(["2026"], TierLabels(layout, 0));
        Assert.Contains("September", TierLabels(layout, 1));
        Assert.Equal(12, TierLabels(layout, 1).Count);
    }

    [Fact]
    public void TimescaleLabels_ShortenJanuaryToJan()
    {
        var labels = TierLabels(Run(OneYear(150)), 1);
        Assert.Equal(["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"], labels);
    }

    [Theory]
    [InlineData(80, 2, new[] { "Jan", "Mar", "May", "Jul", "Sep", "Nov" })]
    [InlineData(40, 3, new[] { "Jan", "Apr", "Jul", "Oct" })]
    [InlineData(20, 6, new[] { "Jan", "Jul" })]
    [InlineData(10, 12, new[] { "Jan" })]
    public void MonthLabels_WhenJanDoesNotFit_ThinToEvery2nd3rd6th12th_NeverBelowThreeLetters(
        double timelineMm, int step, string[] expected)
    {
        var layout = Run(OneYear(timelineMm));
        var tier = layout.Grid.Tiers[1];

        Assert.Equal(step, tier.GroupSize);
        Assert.Equal(expected, TierLabels(layout, 1));
        Assert.Equal(expected.Length - 1, tier.Dividers.Count);
        Assert.All(layout.Grid.Texts, t => Assert.True(t.Request.Text.Length >= 3, t.Request.Text));
        Assert.True(GeometryChecker.Check(layout.Primitives, layout.Grid).IsClean);
    }

    [Fact]
    public void MonthThinning_StaysOnQuarters_WhenTheAxisStartsMidQuarter()
    {
        // Feb 2026..Jan 2027 at 40 mm: every 3rd month, on Jan/Apr/Jul/Oct. The
        // Feb–Mar and Jan edge groups can't hold "Feb"/"Jan" and join their neighbour.
        var chart = Chart.Create("Year", Id(1));
        chart.TimelineWidth = Mm(40);
        chart.Sections.Add(Section(1, D(2026, 2, 1), D(2027, 1, 31)));

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(3, grid.Tiers[1].GroupSize);
        Assert.Equal(["Apr", "Jul", "Oct"], TierLabels(layout, 1));
        Assert.Equal([grid.XOfDate(D(2026, 7, 1)), grid.XOfDate(D(2026, 10, 1))], grid.Tiers[1].Dividers);
        Assert.Equal(grid.XOfDate(D(2026, 4, 1)), TierTexts(layout, 1)[0].Cell.Left);
        Assert.True(GeometryChecker.Check(layout.Primitives, grid).IsClean);
    }

    // ---- Timescale labels: one format per tier, no blank edges ------------------------

    private static Chart EdgeChart(Timescale timescale, DateOnly first, DateOnly last, double timelineMm)
    {
        var chart = Chart.Create("Edges", Id(1));
        chart.Timescale = timescale;
        chart.TimelineWidth = Mm(timelineMm);
        chart.Sections.Add(Section(1, first, last));
        return chart;
    }

    /// <summary>Width of the widest shortest-format label in a tier, plus padding: no sliver is wider.</summary>
    private static double SliverWidth(TierCells tier) =>
        tier.Candidates.Max(c => FakeMeasurer.Size(c[^1]).Width) + 2 * Pad;

    /// <summary>
    /// The dates a label names: its unit, or for a tier thinned to every k-th
    /// tick, the k-unit block it heads (calendar-aligned like the thinning).
    /// </summary>
    private static (DateOnly From, DateOnly To) NamedPeriod(TimeUnit unit, DateOnly cellStart, int k, DateOnly axisStart)
    {
        var slot = TimescaleLabels.ThinningSlot(unit, cellStart, axisStart) / k * k;
        var from = unit switch
        {
            TimeUnit.Day => axisStart.AddDays(slot),
            TimeUnit.Week => TimescaleRules.UnitStart(TimeUnit.Week, axisStart).AddDays(7 * slot),
            TimeUnit.Month => new DateOnly(slot / 12, slot % 12 + 1, 1),
            TimeUnit.Quarter => new DateOnly(slot / 4, slot % 4 * 3 + 1, 1),
            _ => new DateOnly(slot, 1, 1),
        };
        var next = unit switch
        {
            TimeUnit.Day => from.AddDays(k),
            TimeUnit.Week => from.AddDays(7 * k),
            TimeUnit.Month => from.AddMonths(k),
            TimeUnit.Quarter => from.AddMonths(3 * k),
            _ => from.AddYears(k),
        };
        return (from, next.AddDays(-1));
    }

    /// <summary>
    /// The header's intent, per tier: every cell (between dividers, edge to
    /// edge) holds exactly one label that fits it; one format names every
    /// label; and every cell lies within the period its label names, except
    /// that a cell at either end of the timeline may run past it by less than
    /// one sliver. A merged edge never hides more than a label's width of
    /// another period.
    /// </summary>
    private static void AssertHeaderNamesItsPeriods(Chart chart, ChartLayout layout, string at)
    {
        var grid = layout.Grid;
        var axis = grid.Axis!;
        var cells = LayoutEngine.BuildCells(chart, Settings());

        for (var t = 0; t < 2; t++)
        {
            var tier = cells.Tiers[t];
            var k = grid.Tiers[t].GroupSize;
            var sliver = SliverWidth(tier);
            var edges = grid.Tiers[t].Dividers.Prepend(grid.TimelineLeft).Append(grid.Width).ToList();
            var texts = TierTexts(layout, t);
            var labels = string.Join(" | ", texts.Select(x => x.Request.Text));
            Assert.True(texts.Count == edges.Count - 1, $"{at}: tier {t + 1} has {edges.Count - 1} cells, {texts.Count} labels: {labels}");

            var formats = Enumerable.Range(0, tier.Candidates.Max(c => c.Count)).ToHashSet();
            for (var i = 0; i < texts.Count; i++)
            {
                var text = texts[i];
                Assert.True(text.Cell.Right == edges[i + 1] && text.Cell.Left >= edges[i], $"{at}: tier {t + 1} label {i} is not in its cell");
                Assert.True(text.Size.Width + 2 * Pad <= text.Cell.Width + Eps, $"{at}: '{text.Request.Text}' overflows");

                var cell = Enumerable.Range(0, tier.Cells.Count).Single(c => grid.XOfDate(tier.Cells[c].Start) == text.Cell.Left);
                formats.RemoveWhere(f => tier.Candidates[cell][Math.Min(f, tier.Candidates[cell].Count - 1)] != text.Request);

                var (from, to) = NamedPeriod(tier.Unit, tier.Cells[cell].Start, k, axis.Start);
                var before = grid.XOfDate(from) - edges[i];
                var after = edges[i + 1] - grid.XOfDate(to.AddDays(1));
                var what = $"{at}: tier {t + 1} cell '{text.Request.Text}' ({labels}) runs past {from}..{to} by";
                Assert.True(before < (i == 0 ? sliver : Eps), $"{what} {Units.ToMillimeters(before):F2} mm at the start");
                Assert.True(after < (i == texts.Count - 1 ? sliver : Eps), $"{what} {Units.ToMillimeters(after):F2} mm at the end");
            }
            Assert.True(formats.Count > 0, $"{at}: tier {t + 1} mixes formats: {labels}");
        }

        var report = GeometryChecker.Check(layout.Primitives, grid);
        Assert.True(report.IsClean, $"{at}: {report}");
    }

    [Fact]
    public void Tier1Weeks_UseOneFormat_AnEdgeWideEnoughForALabelShortensIt()
    {
        // Regression: Sat 31 Oct..Tue 24 Nov at 180 mm read
        // "31 Oct | 2026-11-02 | 2026-11-09 | 2026-11-16 | 23 Nov": the 2-day edge
        // weeks fell back to the short format. They can hold "31 Oct", so they
        // take part in the choice: the whole tier uses the short format.
        var chart = EdgeChart(Timescale.Auto, D(2026, 10, 31), D(2026, 11, 24), 180);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(new TierPair(TimeUnit.Week, TimeUnit.Day), grid.Axis!.Tiers);
        Assert.Equal(["31 Oct", "2 Nov", "9 Nov", "16 Nov", "23 Nov"], TierLabels(layout, 0));
        AssertHeaderNamesItsPeriods(chart, layout, "180 mm");
    }

    [Fact]
    public void Tier1Weeks_EdgesTooNarrowForAnyLabel_JoinTheirNeighbour_LabelsKeepTheirDates()
    {
        // The same chart at 150 mm: the 2-day edges (12 mm) are narrower than
        // "31 Oct" plus padding (13.8 mm), so they are slivers.
        var chart = EdgeChart(Timescale.Auto, D(2026, 10, 31), D(2026, 11, 24), 150);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(["2026-11-02", "2026-11-09", "2026-11-16"], TierLabels(layout, 0));
        Assert.Equal([grid.XOfDate(D(2026, 11, 9)), grid.XOfDate(D(2026, 11, 16))], grid.Tiers[0].Dividers);
        var texts = TierTexts(layout, 0);
        Assert.Equal(grid.XOfDate(D(2026, 11, 2)), texts[0].Cell.Left);
        Assert.Equal(grid.Width, texts[^1].Cell.Right);
        AssertHeaderNamesItsPeriods(chart, layout, "150 mm");
    }

    [Fact]
    public void GIMonths_June1To20_IsLabelledWhenItCanHoldAMonthLabel()
    {
        // GI-Months (weekly ticks from Mon 28 Sep 2026 to Sun 20 Jun 2027) at 240 mm:
        // June 1–20 (18 mm) holds "Jun 2027", so the tier uses "Oct 2026" style
        // even though full months would fit "October 2026". The 3-day September
        // sliver joins October, whose label still starts on 1 Oct.
        var chart = EdgeChart(Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19), 240);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(D(2026, 9, 28), grid.Axis!.Start);
        Assert.Equal(
            ["Oct 2026", "Nov 2026", "Dec 2026", "Jan 2027", "Feb 2027", "Mar 2027", "Apr 2027", "May 2027", "Jun 2027"],
            TierLabels(layout, 0));
        Assert.DoesNotContain(grid.XOfDate(D(2026, 10, 1)), grid.Tiers[0].Dividers);
        Assert.Equal(grid.XOfDate(D(2026, 11, 1)), grid.Tiers[0].Dividers[0]);
        Assert.Equal(grid.XOfDate(D(2027, 6, 1)), grid.Tiers[0].Dividers[^1]);
        Assert.Equal(grid.XOfDate(D(2026, 10, 1)), TierTexts(layout, 0)[0].Cell.Left);
        AssertHeaderNamesItsPeriods(chart, layout, "240 mm");
    }

    [Fact]
    public void GIMonths_June1To20_JoinsMayOnlyWhenNarrowerThanItsLabel()
    {
        // At 180 mm June 1–20 is 13.5 mm, narrower than "Jun '27" plus padding
        // (15.6 mm): a true sliver, so May's cell runs over it by less than one label.
        var chart = EdgeChart(Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19), 180);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(
            ["Oct 2026", "Nov 2026", "Dec 2026", "Jan 2027", "Feb 2027", "Mar 2027", "Apr 2027", "May 2027"],
            TierLabels(layout, 0));
        Assert.True(grid.Width - grid.XOfDate(D(2027, 6, 1)) < 7 * FakeMeasurer.HeaderCharWidth + 2 * Pad);
        AssertHeaderNamesItsPeriods(chart, layout, "180 mm");
    }

    [Fact]
    public void GIMonths_ThinnedToEvery2ndMonth_TheSeptemberSliverDoesNotNameTheOctoberGroup()
    {
        // At 140 mm February (14.7 mm) can't hold "Feb '27", so tier 1 thins to
        // Sep–Oct, Nov–Dec, … The first group is 3 days of September plus all of
        // October: October names it, from 1 Oct. The 51-day May–Jun edge group is
        // labelled, and the 31 days of October after the sliver only fit "Oct '26",
        // so that is the tier's format.
        var chart = EdgeChart(Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19), 140);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(2, grid.Tiers[0].GroupSize);
        Assert.Equal(["Oct '26", "Nov '26", "Jan '27", "Mar '27", "May '27"], TierLabels(layout, 0));
        Assert.Equal(grid.XOfDate(D(2026, 10, 1)), TierTexts(layout, 0)[0].Cell.Left);
        Assert.Equal(grid.XOfDate(D(2026, 11, 1)), grid.Tiers[0].Dividers[0]);
        AssertHeaderNamesItsPeriods(chart, layout, "140 mm");
    }

    [Fact]
    public void OneWeekAcrossAMonthEnd_TheTwoDaySliverJoinsApril()
    {
        // Weeks timescale, 31 Mar..1 Apr: the axis is Mon 30 Mar..Sun 5 Apr. At
        // 40 mm the 2 March days (11.4 mm) are a sliver; April names the week.
        var chart = EdgeChart(Timescale.Weeks, D(2026, 3, 31), D(2026, 4, 1), 40);

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(["April 2026"], TierLabels(layout, 0));
        Assert.Equal(grid.XOfDate(D(2026, 4, 1)), TierTexts(layout, 0)[0].Cell.Left);
        AssertHeaderNamesItsPeriods(chart, layout, "40 mm");
    }

    [Fact]
    public void Tier2_TheShortRemainderGroupJoinsThePreviousGroup()
    {
        // The GI-Days chart (2..24 Nov, 23 days) thinned to every 2nd day: the
        // lone 24th used to be a blank cell at the right edge.
        var chart = Chart.Create("Days", Id(1));
        chart.TimelineWidth = Mm(100);
        chart.Sections.Add(Section(1, D(2026, 11, 2), D(2026, 11, 24)));

        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(2, grid.Tiers[1].GroupSize);
        var labels = TierLabels(layout, 1);
        Assert.Equal(11, labels.Count);
        Assert.Equal("22", labels[^1]);
        Assert.Equal(grid.Width, TierTexts(layout, 1)[^1].Cell.Right);
        Assert.Equal(10, grid.Tiers[1].Dividers.Count);
        Assert.True(GeometryChecker.Check(layout.Primitives, grid).IsClean);
    }

    public static TheoryData<Timescale, DateOnly, DateOnly> EdgeCases() => new()
    {
        // Days (tier 1 weeks, clipped at both ends)
        { Timescale.Days, D(2026, 10, 29), D(2026, 11, 26) },   // the editor preview
        { Timescale.Days, D(2026, 11, 2), D(2026, 11, 24) },    // GI-Days
        { Timescale.Days, D(2026, 11, 1), D(2026, 11, 30) },    // Sunday start, Monday end: 1-day edge weeks
        { Timescale.Days, D(2026, 3, 4), D(2026, 3, 10) },
        // Weeks (tier 1 months, clipped by whole-week padding)
        { Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19) },    // GI-Months
        { Timescale.Weeks, D(2026, 1, 30), D(2026, 8, 3) },
        { Timescale.Weeks, D(2026, 3, 31), D(2026, 5, 1) },
        // Months (tier 1 years)
        { Timescale.Months, D(2026, 3, 2), D(2029, 7, 27) },    // GI-Multi-year
        { Timescale.Months, D(2026, 12, 1), D(2027, 1, 31) },   // 1-month edge years
        { Timescale.Months, D(2026, 11, 15), D(2028, 2, 10) },
        // Quarters (tier 1 years)
        { Timescale.Quarters, D(2020, 10, 1), D(2029, 3, 31) },
        { Timescale.Quarters, D(2026, 12, 1), D(2031, 1, 15) },
        { Timescale.Quarters, D(2001, 1, 1), D(2045, 12, 31) },
        // Multi-year under Auto: Years/Months, then Years/Quarters
        { Timescale.Auto, D(2026, 3, 2), D(2029, 7, 27) },
        { Timescale.Auto, D(2024, 11, 1), D(2034, 2, 1) },
    };

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void EveryTierCell_IncludingBothEdges_IsLabelled_AndNamesItsPeriod(Timescale timescale, DateOnly first, DateOnly last)
    {
        foreach (var timelineMm in new[] { 40.0, 60, 120, 140, 180, 240, 300 })
        {
            var chart = EdgeChart(timescale, first, last, timelineMm);
            AssertHeaderNamesItsPeriods(chart, Run(chart), $"{timescale} {first}..{last} at {timelineMm} mm");
        }
    }

    public static TheoryData<Timescale, DateOnly, DateOnly, double> ReviewProbes() => new()
    {
        // PR #7 review: each of these merged a whole calendar period into its neighbour.
        { Timescale.Months, D(2026, 11, 15), D(2028, 2, 10), 60 },
        { Timescale.Quarters, D(2026, 12, 1), D(2031, 1, 15), 60 },
        { Timescale.Auto, D(2024, 11, 1), D(2034, 2, 1), 60 },
        { Timescale.Months, D(2026, 3, 2), D(2029, 7, 27), 60 },    // GI-Multi-year
        { Timescale.Auto, D(2026, 3, 2), D(2029, 7, 27), 40 },
        { Timescale.Weeks, D(2026, 1, 30), D(2026, 8, 3), 60 },
        { Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19), 120 },   // GI-Months
        { Timescale.Weeks, D(2026, 10, 1), D(2027, 6, 19), 180 },
        { Timescale.Weeks, D(2026, 3, 31), D(2026, 4, 1), 60 },
        { Timescale.Days, D(2026, 10, 29), D(2026, 11, 26), 60 },
    };

    [Theory]
    [MemberData(nameof(ReviewProbes))]
    public void ReviewProbes_NoCellHidesMoreThanASliverOfAnotherPeriod(Timescale timescale, DateOnly first, DateOnly last, double timelineMm)
    {
        var chart = EdgeChart(timescale, first, last, timelineMm);
        AssertHeaderNamesItsPeriods(chart, Run(chart), $"{timescale} {first}..{last} at {timelineMm} mm");
    }

    [Theory]
    [InlineData(Timescale.Months, 300, new[] { "2026", "2027", "2028", "2029" })]   // GI-Multi-year
    [InlineData(Timescale.Months, 55, new[] { "'26", "'27", "'28", "'29" })]
    public void MultiYear_EdgeYearsWideEnoughForALabel_AreLabelled_InOneFormat(Timescale timescale, double timelineMm, string[] expected)
    {
        // Mar 2026..Jul 2029: 2026 (10 months) and 2029 (7 months) are partial
        // years. At 55 mm Jan–Jul 2029 is 9.3 mm: too narrow for "2029" plus
        // padding (10.2 mm) but not a sliver, so the whole tier takes "'26" style.
        var chart = EdgeChart(timescale, D(2026, 3, 2), D(2029, 7, 27), timelineMm);

        var layout = Run(chart);

        Assert.Equal(expected, TierLabels(layout, 0));
        AssertHeaderNamesItsPeriods(chart, layout, $"{timelineMm} mm");
    }

    [Fact]
    public void YearLabels_ShortenTo_Apostrophe26_AndQuartersThin()
    {
        var chart = Spanning(10, D(2020, 1, 1), D(2029, 12, 31));
        chart.TimelineWidth = Mm(90);

        var layout = Run(chart);

        Assert.Equal(new TierPair(TimeUnit.Year, TimeUnit.Quarter), layout.Grid.Axis!.Tiers);
        Assert.Equal(Enumerable.Range(20, 10).Select(y => $"'{y}"), TierLabels(layout, 0));
        Assert.Equal(4, layout.Grid.Tiers[1].GroupSize);
        Assert.All(TierLabels(layout, 1), l => Assert.Equal("Q1", l));
    }

    [Fact]
    public void TimescaleLabels_NeverOverflowTheirCell()
    {
        foreach (var width in new[] { 10.0, 20, 40, 80, 150, 240 })
        {
            var layout = Run(OneYear(width));
            foreach (var t in layout.Grid.Texts)
                Assert.True(t.Size.Width + 2 * Pad <= t.Cell.Width + Eps, $"'{t.Request.Text}' overflows at {width} mm");
        }
    }

    // ---- Timeline and bars -----------------------------------------------------------

    [Fact]
    public void Bars_RunFromStartDayLeftEdgeToEndDayRightEdge_InsetByPadding()
    {
        var chart = TwoSections();
        var layout = Run(chart);
        var grid = layout.Grid;
        var bars = layout.Primitives.OfType<FilledRectPrimitive>().ToList();

        Assert.Equal(2, bars.Count);
        var dayWidth = Chart.DefaultTimelineWidth / grid.Axis!.Days;
        for (var i = 0; i < 2; i++)
        {
            var s = chart.Sections[i];
            var bar = bars[i];
            Assert.Equal(s.Id, bar.SectionId);
            Assert.Equal(s.Color, bar.Color);
            Assert.Equal(grid.TimelineLeft + grid.Axis.OffsetOf(s.Start) * dayWidth, bar.Rect.Left, 1e-9);
            Assert.Equal(s.Duration * dayWidth, bar.Rect.Width, 1e-9);
            Assert.Equal(grid.RowEdges[i] - Pad, bar.Rect.Top, Eps);
            Assert.Equal(grid.RowEdges[i + 1] + Pad, bar.Rect.Bottom, Eps);
        }
    }

    [Fact]
    public void DayTicks_AreDrawnWhenADayIsAtLeast1mm()
    {
        var chart = Spanning(3, D(2026, 3, 2), D(2026, 4, 12)); // 42 days at 180 mm ≈ 4.3 mm/day
        var grid = Run(chart).Grid;

        Assert.Equal(TimeUnit.Day, grid.Axis!.Tiers.Tier2);
        Assert.Equal(41, grid.BodyTicks.Count);
    }

    [Fact]
    public void DayTicks_AreDroppedWhenADayIsNarrowerThan1mm_TheBodyKeepsOnlyTheLabelGroupDividers()
    {
        var chart = Spanning(5, D(2026, 1, 1), D(2026, 12, 31), Timescale.Days); // ≈ 0.49 mm/day
        var layout = Run(chart);
        var grid = layout.Grid;

        Assert.Equal(TimeUnit.Day, grid.Axis!.Tiers.Tier2);
        Assert.True(grid.Tiers[1].GroupSize > 1);
        Assert.Equal(grid.Tiers[1].Dividers, grid.BodyTicks);
        var spacing = grid.BodyTicks.Zip(grid.BodyTicks.Skip(1), (a, b) => b - a);
        Assert.All(spacing, s => Assert.True(s >= LayoutEngine.MinTickSpacing));
        Assert.True(GeometryChecker.Check(layout.Primitives, grid).IsClean);
    }

    // ---- Clean lines -------------------------------------------------------------------

    [Fact]
    public void EachRowSeparator_IsOneContinuousLineAcrossTheFullWidth()
    {
        var layout = Run(Sequential(6, D(2026, 1, 1)));
        var grid = layout.Grid;
        var lines = layout.Primitives.OfType<LinePrimitive>().ToList();

        for (var i = 1; i < grid.RowEdges.Count - 1; i++)
        {
            var y = grid.RowEdges[i];
            var line = Assert.Single(lines, l => l.Start.Y == y && l.End.Y == y);
            Assert.Equal(0, line.Start.X);
            Assert.Equal(grid.Width, line.End.X);
            Assert.Equal(LineStyleKey.Chart, line.Style);
        }
    }

    [Fact]
    public void BorderAndHeaderRules_UseTheHeaderStyle_AndWinWhereStylesWouldOverlap()
    {
        var layout = Run(Sequential(4, D(2026, 1, 1)));
        var grid = layout.Grid;
        var lines = layout.Primitives.OfType<LinePrimitive>().ToList();

        // The right border coincides with the timeline's last tick: only the header line is drawn.
        var right = Assert.Single(lines, l => l.Start.X == grid.Width && l.End.X == grid.Width);
        Assert.Equal(LineStyleKey.Header, right.Style);
        Assert.Equal((grid.Bottom, 0.0), (right.Start.Y, right.End.Y));

        foreach (var y in new[] { 0, grid.TitleBottom, grid.HeaderBottom, grid.Bottom })
            Assert.Equal(LineStyleKey.Header, Assert.Single(lines, l => l.Start.Y == y && l.End.Y == y).Style);
    }

    [Fact]
    public void ATickAlignedWithATier1Boundary_IsOneLineThroughHeaderAndBody()
    {
        // Years/Months over two years: the 1 Jan 2027 tick is also the year divider.
        var chart = Spanning(4, D(2026, 1, 1), D(2027, 12, 31));
        var layout = Run(chart);
        var grid = layout.Grid;
        var x = grid.XOfDate(D(2027, 1, 1));

        var line = Assert.Single(layout.Primitives.OfType<LinePrimitive>(), l => l.Start.X == x && l.End.X == x);
        Assert.Equal((grid.Bottom, grid.TitleBottom), (line.Start.Y, line.End.Y));
    }

    [Fact]
    public void EmptyChart_LaysOutWithoutATimeline()
    {
        var layout = Run(Chart.Create("Empty"));

        Assert.Null(layout.Grid.Axis);
        Assert.Empty(layout.Primitives.OfType<FilledRectPrimitive>());
        Assert.True(GeometryChecker.Check(layout.Primitives, layout.Grid).IsClean);
    }

    // ---- Measuring and determinism ----------------------------------------------------

    [Fact]
    public void Measuring_IsBatched_AndNeverRepeatsARequest()
    {
        var chart = Sequential(30, D(2026, 1, 1));
        chart.Sections[3].Name = "A very long section name that will certainly have to wrap onto several lines";
        chart.Title = string.Join(' ', Enumerable.Repeat("Programme", 30));
        var measurer = new FakeMeasurer();

        Run(chart, measurer);

        Assert.InRange(measurer.Batches.Count, 1, 3);
        var all = measurer.Batches.SelectMany(b => b).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Layout_IsDeterministic()
    {
        var chart = Spanning(25, D(2026, 2, 3), D(2027, 8, 19));
        Assert.Equal(Run(chart).Primitives, Run(chart).Primitives);
    }
}
