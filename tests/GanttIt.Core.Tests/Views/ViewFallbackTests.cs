using GanttIt.Core.Views;

namespace GanttIt.Core.Tests.Views;

public class ViewFallbackTests
{
    private const long Chart = 100;

    private static ViewCandidate V(long id, ViewKind kind, bool template = false, bool printable = true) =>
        new(id, kind, template, printable);

    [Fact]
    public void OpenViews_ComeFirst_InWindowOrder_SkippingEveryWindowOfTheChart()
    {
        var chart = V(Chart, ViewKind.Legend);
        var open = new[] { chart, V(7, ViewKind.Other), chart, V(3, ViewKind.Sheet) };
        var all = new[] { chart, V(1, ViewKind.FloorPlan), V(3, ViewKind.Sheet), V(7, ViewKind.Other) };

        Assert.Equal(new long[] { 7, 3, 1 }, ViewFallback.Order(Chart, open, all));
    }

    [Fact]
    public void WithOnlyTheChartOpen_FloorPlansThenSheetsThenDraftingThenOthersThenLegends()
    {
        var all = new[]
        {
            V(Chart, ViewKind.Legend),
            V(50, ViewKind.Legend),
            V(40, ViewKind.Other),
            V(30, ViewKind.Drafting),
            V(21, ViewKind.Sheet),
            V(20, ViewKind.Sheet),
            V(10, ViewKind.FloorPlan),
        };

        Assert.Equal(new long[] { 10, 20, 21, 30, 40, 50 }, ViewFallback.Order(Chart, [V(Chart, ViewKind.Legend)], all));
    }

    [Fact]
    public void NeverTemplates_NonPrintableViews_UnusableKinds_OrTheChart()
    {
        var all = new[]
        {
            V(Chart, ViewKind.FloorPlan),
            V(1, ViewKind.FloorPlan, template: true),
            V(2, ViewKind.Other, printable: false), // e.g. the project browser
            V(3, ViewKind.Unusable),
            V(4, ViewKind.Drafting),
        };
        var open = new[] { V(2, ViewKind.Other, printable: false), V(1, ViewKind.FloorPlan, template: true) };

        Assert.Equal(new long[] { 4 }, ViewFallback.Order(Chart, open, all));
    }

    [Fact]
    public void NothingUsable_GivesAnEmptyList()
    {
        Assert.Empty(ViewFallback.Order(Chart, [V(Chart, ViewKind.Legend)], [V(Chart, ViewKind.Legend), V(9, ViewKind.Unusable)]));
    }
}
