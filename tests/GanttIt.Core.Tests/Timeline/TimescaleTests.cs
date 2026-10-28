using GanttIt.Core.Model;
using GanttIt.Core.Timeline;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Timeline;

/// <summary>A3: the Auto timescale thresholds (§5.3), overrides and axis padding.</summary>
public class TimescaleTests
{
    private static readonly TierPair WeeksDays = new(TimeUnit.Week, TimeUnit.Day);
    private static readonly TierPair MonthsWeeks = new(TimeUnit.Month, TimeUnit.Week);
    private static readonly TierPair YearsMonths = new(TimeUnit.Year, TimeUnit.Month);
    private static readonly TierPair YearsQuarters = new(TimeUnit.Year, TimeUnit.Quarter);

    [Fact]
    public void Auto_SixWeeksBoundary_42DaysIsWeeksDays_43DaysIsMonthsWeeks()
    {
        var start = D(2026, 3, 1);
        Assert.Equal(WeeksDays, TimescaleRules.ResolveAuto(start, start.AddDays(41)));
        Assert.Equal(MonthsWeeks, TimescaleRules.ResolveAuto(start, start.AddDays(42)));
    }

    [Fact]
    public void Auto_SingleDay_IsWeeksDays()
    {
        Assert.Equal(WeeksDays, TimescaleRules.ResolveAuto(D(2026, 3, 1), D(2026, 3, 1)));
    }

    [Fact]
    public void Auto_NineMonthBoundary_IsCalendarBased()
    {
        Assert.Equal(MonthsWeeks, TimescaleRules.ResolveAuto(D(2026, 1, 1), D(2026, 9, 30)));
        Assert.Equal(YearsMonths, TimescaleRules.ResolveAuto(D(2026, 1, 1), D(2026, 10, 1)));
        Assert.Equal(MonthsWeeks, TimescaleRules.ResolveAuto(D(2026, 2, 15), D(2026, 11, 14)));
        Assert.Equal(YearsMonths, TimescaleRules.ResolveAuto(D(2026, 2, 15), D(2026, 11, 15)));
    }

    [Fact]
    public void Auto_NineMonthBoundary_FromAMonthEnd_ClampsLikeAddMonths()
    {
        // 31 Jan + 9 months = 31 Oct, so the span may run to 30 Oct.
        Assert.Equal(MonthsWeeks, TimescaleRules.ResolveAuto(D(2026, 1, 31), D(2026, 10, 30)));
        Assert.Equal(YearsMonths, TimescaleRules.ResolveAuto(D(2026, 1, 31), D(2026, 10, 31)));
    }

    [Fact]
    public void Auto_FourYearBoundary_IsCalendarBased()
    {
        Assert.Equal(YearsMonths, TimescaleRules.ResolveAuto(D(2026, 1, 1), D(2029, 12, 31)));
        Assert.Equal(YearsQuarters, TimescaleRules.ResolveAuto(D(2026, 1, 1), D(2030, 1, 1)));
    }

    [Fact]
    public void Auto_DecadesSpan_IsYearsQuarters()
    {
        Assert.Equal(YearsQuarters, TimescaleRules.ResolveAuto(D(2000, 6, 1), D(2045, 1, 1)));
    }

    [Theory]
    [InlineData(Timescale.Days, TimeUnit.Week, TimeUnit.Day)]
    [InlineData(Timescale.Weeks, TimeUnit.Month, TimeUnit.Week)]
    [InlineData(Timescale.Months, TimeUnit.Year, TimeUnit.Month)]
    [InlineData(Timescale.Quarters, TimeUnit.Year, TimeUnit.Quarter)]
    public void ExplicitTimescale_OverridesAuto(Timescale timescale, TimeUnit tier1, TimeUnit tier2)
    {
        // A 10-year span would be Years/Quarters under Auto.
        Assert.Equal(new TierPair(tier1, tier2), TimescaleRules.Resolve(timescale, D(2020, 1, 1), D(2030, 1, 1)));
    }

    [Fact]
    public void Axis_PaddedToWholeWeeks_MondayToSunday()
    {
        // 7 Jan 2026 is a Wednesday; 50 days → Months/Weeks.
        var axis = TimeAxis.Create(Timescale.Auto, D(2026, 1, 7), D(2026, 2, 25));
        Assert.Equal(MonthsWeeks, axis.Tiers);
        Assert.Equal(D(2026, 1, 5), axis.Start);  // Monday
        Assert.Equal(D(2026, 3, 1), axis.End);    // Sunday
        Assert.Equal(0, axis.Days % 7);
    }

    [Fact]
    public void Axis_PaddedToWholeMonths()
    {
        var axis = TimeAxis.Create(Timescale.Auto, D(2026, 2, 10), D(2027, 5, 3));
        Assert.Equal(YearsMonths, axis.Tiers);
        Assert.Equal(D(2026, 2, 1), axis.Start);
        Assert.Equal(D(2027, 5, 31), axis.End);
    }

    [Fact]
    public void Axis_PaddedToWholeQuarters()
    {
        var axis = TimeAxis.Create(Timescale.Auto, D(2026, 5, 10), D(2031, 8, 3));
        Assert.Equal(YearsQuarters, axis.Tiers);
        Assert.Equal(D(2026, 4, 1), axis.Start);
        Assert.Equal(D(2031, 9, 30), axis.End);
    }

    [Fact]
    public void Axis_InDays_IsNotPadded()
    {
        var axis = TimeAxis.Create(Timescale.Auto, D(2026, 1, 7), D(2026, 1, 20));
        Assert.Equal(D(2026, 1, 7), axis.Start);
        Assert.Equal(D(2026, 1, 20), axis.End);
    }

    [Fact]
    public void Tier1Cells_AreClippedToTheAxis()
    {
        var axis = TimeAxis.Create(Timescale.Auto, D(2026, 1, 7), D(2026, 2, 25));
        var months = axis.Cells(TimeUnit.Month);

        Assert.Equal(3, months.Count);
        Assert.Equal(new TimeCell(D(2026, 1, 5), D(2026, 1, 31)), months[0]);
        Assert.Equal(new TimeCell(D(2026, 2, 1), D(2026, 2, 28)), months[1]);
        Assert.Equal(new TimeCell(D(2026, 3, 1), D(2026, 3, 1)), months[2]);
        Assert.Equal(axis.Days, months.Sum(c => c.Days));
    }

    [Fact]
    public void AxisForChart_UsesEarliestStartAndLatestEnd()
    {
        var chart = Chart.Create("c");
        chart.Sections.Add(Section(1, D(2026, 3, 10), D(2026, 3, 12)));
        chart.Sections.Add(Section(2, D(2026, 3, 2), D(2026, 3, 5)));
        chart.Sections.Add(Section(3, D(2026, 3, 20), D(2026, 3, 18))); // end before start: still covered

        var axis = TimeAxis.ForChart(chart)!;

        Assert.Equal(D(2026, 3, 2), axis.Start);
        Assert.Equal(D(2026, 3, 20), axis.End);
        Assert.Null(TimeAxis.ForChart(Chart.Create("empty")));
    }

    [Fact]
    public void MonthLabels_ShortenFromJanuaryToJanToJ()
    {
        Assert.Equal(["January", "Jan", "J"], TimescaleLabels.Candidates(TimeUnit.Month, false, D(2026, 1, 1), "yyyy-MM-dd"));
    }

    [Fact]
    public void YearLabels_ShortenFrom2026ToApostrophe26()
    {
        Assert.Equal(["2026", "'26"], TimescaleLabels.Candidates(TimeUnit.Year, true, D(2026, 1, 1), "yyyy-MM-dd"));
        Assert.Equal(["2005", "'05"], TimescaleLabels.Candidates(TimeUnit.Year, true, D(2005, 1, 1), "yyyy-MM-dd"));
    }

    [Fact]
    public void Tier1MonthLabels_CarryTheYear()
    {
        Assert.Equal(["October 2026", "Oct 2026", "Oct '26", "Oct", "O"],
            TimescaleLabels.Candidates(TimeUnit.Month, true, D(2026, 10, 1), "yyyy-MM-dd"));
    }

    [Fact]
    public void WeekAndQuarterLabels()
    {
        Assert.Equal(["2026-10-19", "19 Oct", "19"], TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 10, 19), "yyyy-MM-dd"));
        Assert.Equal(["19"], TimescaleLabels.Candidates(TimeUnit.Week, false, D(2026, 10, 19), "yyyy-MM-dd"));
        Assert.Equal(["Q4"], TimescaleLabels.Candidates(TimeUnit.Quarter, false, D(2026, 10, 1), "yyyy-MM-dd"));
    }
}
