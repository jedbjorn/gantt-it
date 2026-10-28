using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Tests.Support;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Layout;

/// <summary>
/// The A5 rule set (§6.3) run against Emit output for a spread of charts, plus
/// proof that the checker catches each kind of violation.
/// </summary>
public class GeometryCheckerTests
{
    public static TheoryData<string> ChartNames() =>
        [.. Charts().Keys];

    private static Dictionary<string, Func<Chart>> Charts() => new()
    {
        ["two weeks (Weeks/Days)"] = () => Sequential(3, D(2026, 3, 4), days: 4),
        ["four months (Months/Weeks)"] = () => Sequential(8, D(2026, 2, 11), days: 12, gap: 3),
        ["three years (Years/Months)"] = () => Spanning(12, D(2026, 5, 17), D(2029, 2, 3)),
        ["decade (Years/Quarters)"] = () => Spanning(20, D(2025, 7, 1), D(2036, 3, 31)),
        ["no optional columns"] = () => WithColumns(Sequential(5, D(2026, 1, 1)), ChartColumns.None),
        ["duration only"] = () => WithColumns(Sequential(5, D(2026, 1, 1)), ChartColumns.Duration),
        ["wrapped names and line breaks"] = WrappedNames,
        ["long wrapped title"] = LongTitle,
        ["days forced over two years"] = () => Spanning(10, D(2026, 1, 1), D(2027, 12, 31), Timescale.Days),
        ["weeks forced over a decade"] = () => Spanning(10, D(2026, 1, 1), D(2035, 12, 31), Timescale.Weeks),
        ["narrow timeline"] = () => WithTimeline(Spanning(6, D(2026, 1, 1), D(2026, 12, 31)), 15),
        ["overlapping sections"] = Overlapping,
        ["single day"] = () => Spanning(1, D(2026, 6, 15), D(2026, 6, 15)),
        ["empty"] = () => Chart.Create("Empty"),
    };

    [Theory]
    [MemberData(nameof(ChartNames))]
    public void EmitOutput_PassesTheCleanLineRules(string name)
    {
        var chart = Charts()[name]();
        var layout = LayoutEngine.Layout(chart, Settings(), new FakeMeasurer());

        var report = GeometryChecker.Check(layout.Primitives, layout.Grid);

        Assert.True(report.IsClean, report.ToString());
    }

    [Fact]
    public void VeryLargeChart_StaysBounded_AndClean()
    {
        // Beyond Intention: hundreds of sections over decades.
        var chart = Spanning(400, D(1995, 3, 1), D(2041, 10, 31));
        chart.Sections[17].Name = "An exceptionally long section name that wraps across several lines in the name column";

        var layout = LayoutEngine.Layout(chart, Settings(), new FakeMeasurer());
        var grid = layout.Grid;

        Assert.True(GeometryChecker.Check(layout.Primitives, grid).IsClean);

        // Bounded: width is the table plus the fixed timeline; lines grow with rows, not days.
        Assert.Equal(grid.TimelineLeft + chart.TimelineWidth, grid.Width, 1e-12);
        var lines = layout.Primitives.OfType<LinePrimitive>().Count();
        Assert.True(lines < chart.Sections.Count + 300, $"{lines} lines");
        Assert.Equal(chart.Sections.Count, layout.Primitives.OfType<FilledRectPrimitive>().Count());
        Assert.All(layout.Primitives.OfType<TextPrimitive>(), t =>
            Assert.True(t.MeasuredBox.Left >= 0 && t.MeasuredBox.Right <= grid.Width));
    }

    [Fact]
    public void Checker_FlagsTextCrossingALine()
    {
        var report = GeometryChecker.Check(
            [new Box(0.1, 0.1, 0.3, 0.2)],
            [(new Point2(0.2, 0), new Point2(0.2, 1))],
            [0, 0.2, 1], [0, 1]);

        Assert.Equal(ViolationKind.TextCrossesLine, Assert.Single(report.Violations).Kind);
    }

    [Fact]
    public void Checker_FlagsTextTouchingALine()
    {
        var report = GeometryChecker.Check(
            [new Box(0.1, 0.1, 0.3, 0.2)],
            [(new Point2(0, 0.2), new Point2(1, 0.2))],
            [0, 1], [0.2]);

        Assert.Equal(ViolationKind.TextCrossesLine, Assert.Single(report.Violations).Kind);
    }

    [Fact]
    public void Checker_FlagsAnEndpointOffTheGrid_BeyondTolerance()
    {
        var onGrid = GeometryChecker.Check([], [(new Point2(0, 0), new Point2(1 + 5e-7, 0))], [0, 1], [0]);
        var offGrid = GeometryChecker.Check([], [(new Point2(0, 0), new Point2(1 + 2e-6, 0))], [0, 1], [0]);

        Assert.True(onGrid.IsClean);
        Assert.Equal(ViolationKind.EndpointOffGrid, Assert.Single(offGrid.Violations).Kind);
    }

    [Fact]
    public void Checker_FlagsOverlappingCollinearSegments_ButNotOnesThatOnlyMeet()
    {
        double[] grid = [0, 1, 2, 3];
        var meeting = GeometryChecker.Check([],
            [(new Point2(0, 0), new Point2(1, 0)), (new Point2(1, 0), new Point2(2, 0))], grid, [0]);
        var overlapping = GeometryChecker.Check([],
            [(new Point2(0, 0), new Point2(2, 0)), (new Point2(1, 0), new Point2(3, 0))], grid, [0]);
        var vertical = GeometryChecker.Check([],
            [(new Point2(1, 0), new Point2(1, 3)), (new Point2(1, 2), new Point2(1, 1))], grid, grid);
        var diagonal = GeometryChecker.Check([],
            [(new Point2(0, 0), new Point2(2, 2)), (new Point2(1, 1), new Point2(3, 3))], grid, grid);

        Assert.True(meeting.IsClean);
        Assert.Equal(ViolationKind.CollinearOverlap, Assert.Single(overlapping.Violations).Kind);
        Assert.Equal(ViolationKind.CollinearOverlap, Assert.Single(vertical.Violations).Kind);
        Assert.Equal(ViolationKind.CollinearOverlap, Assert.Single(diagonal.Violations).Kind);
    }

    private static Chart WithColumns(Chart chart, ChartColumns columns)
    {
        chart.Columns = columns;
        return chart;
    }

    private static Chart WithTimeline(Chart chart, double mm)
    {
        chart.TimelineWidth = Units.FromMillimeters(mm);
        return chart;
    }

    private static Chart WrappedNames()
    {
        var chart = Sequential(6, D(2026, 4, 1), days: 9);
        chart.Sections[0].Name = "Site preparation, enabling works and temporary services for the whole campus";
        chart.Sections[2].Name = "Two\nlines";
        chart.Sections[4].Name = "Supercalifragilisticexpialidocious-unbreakable-word-longer-than-the-column";
        return chart;
    }

    private static Chart LongTitle()
    {
        var chart = Sequential(3, D(2026, 1, 1));
        chart.Title = string.Join(' ', Enumerable.Repeat("Masterplan", 40));
        return chart;
    }

    private static Chart Overlapping()
    {
        var chart = Sequential(4, D(2026, 1, 1));
        chart.Sections[1].Start = D(2025, 12, 28);
        chart.Sections[3].End = D(2026, 3, 1);
        return chart;
    }
}
