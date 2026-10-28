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

    private static List<string> TierLabels(ChartLayout layout, int tier)
    {
        var t = layout.Grid.Tiers[tier];
        return layout.Grid.Texts
            .Where(p => p.Cell.Top == t.Top && p.Cell.Bottom == t.Bottom && p.Cell.Left >= layout.Grid.TimelineLeft)
            .Select(p => p.Request.Text)
            .ToList();
    }

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

    [Fact]
    public void TimescaleLabels_ShortenJanToJ()
    {
        var labels = TierLabels(Run(OneYear(80)), 1);
        Assert.Equal(["J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D"], labels);
    }

    [Fact]
    public void TimescaleLabels_WhenEvenTheShortestDoesNotFit_LabelEverySecondTick()
    {
        var layout = Run(OneYear(40));
        var tier = layout.Grid.Tiers[1];

        Assert.Equal(2, tier.GroupSize);
        Assert.Equal(["J", "M", "M", "J", "S", "N"], TierLabels(layout, 1));
        Assert.Equal(5, tier.Dividers.Count);
    }

    [Fact]
    public void TimescaleLabels_ThenEveryFourthTick()
    {
        var layout = Run(OneYear(20));
        var tier = layout.Grid.Tiers[1];

        Assert.Equal(4, tier.GroupSize);
        Assert.Equal(["J", "M", "S"], TierLabels(layout, 1));
        Assert.Equal(2, tier.Dividers.Count);
        Assert.True(GeometryChecker.Check(layout.Primitives, layout.Grid).IsClean);
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
