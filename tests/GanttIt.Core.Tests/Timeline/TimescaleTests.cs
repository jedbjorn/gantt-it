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
    public void MonthLabels_ShortenFromJanuaryToJan_NeverToASingleLetter()
    {
        Assert.Equal(["January", "Jan"], TimescaleLabels.Candidates(TimeUnit.Month, false, D(2026, 1, 1), "yyyy-MM-dd"));
        Assert.Equal(["May", "May"], TimescaleLabels.Candidates(TimeUnit.Month, false, D(2026, 5, 1), "yyyy-MM-dd"));
    }

    [Fact]
    public void NoLabelFormat_IsShorterThanItsUnitAllows()
    {
        // Months never drop below three letters, and no top-tier label is a bare
        // number that names neither month nor year. Day and week numbers in tier 2
        // sit under the tier that names their month.
        for (var month = 1; month <= 12; month++)
        {
            var start = D(2026, month, 1);
            Assert.All(TimescaleLabels.Candidates(TimeUnit.Month, false, start, "yyyy-MM-dd"), l => Assert.True(l.Length >= 3, l));
            Assert.All(TimescaleLabels.Candidates(TimeUnit.Month, true, start, "yyyy-MM-dd"), l => Assert.Matches(@"^[A-Z][a-z]{2,} ('\d\d|\d{4})$", l));
        }
        Assert.All(TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 11, 2), "yyyy-MM-dd"),
            l => Assert.True(l.Contains("-11-") || l.Contains("Nov"), l));
    }

    [Fact]
    public void ThinningSteps_ForMonthsAlignToQuartersAndHalfYears_OtherUnitsDouble()
    {
        Assert.Equal([1, 2, 3, 6, 12, 24, 48], TimescaleLabels.ThinningSteps(TimeUnit.Month).Take(7));
        Assert.Equal([1, 2, 4, 8, 16], TimescaleLabels.ThinningSteps(TimeUnit.Quarter).Take(5));
        Assert.Equal([1, 2, 4, 8, 16], TimescaleLabels.ThinningSteps(TimeUnit.Day).Take(5));
        Assert.All(TimescaleLabels.ThinningSteps(TimeUnit.Week), k => Assert.True(k > 0));
        Assert.All(TimescaleLabels.ThinningSteps(TimeUnit.Month), k => Assert.True(k > 0));
    }

    [Fact]
    public void ThinningBlocks_AreTheFullKUnitPeriod_ClampedToTheCalendar()
    {
        var axisStart = D(2026, 10, 29); // a Thursday
        Assert.Equal((D(2026, 11, 1), D(2026, 12, 31)), TimescaleLabels.ThinningBlock(TimeUnit.Month, D(2026, 12, 1), 2, axisStart));
        Assert.Equal((D(2026, 7, 1), D(2026, 12, 31)), TimescaleLabels.ThinningBlock(TimeUnit.Quarter, D(2026, 10, 1), 2, axisStart));
        Assert.Equal((D(2024, 1, 1), D(2027, 12, 31)), TimescaleLabels.ThinningBlock(TimeUnit.Year, D(2026, 1, 1), 4, axisStart));
        Assert.Equal((D(2026, 10, 31), D(2026, 11, 1)), TimescaleLabels.ThinningBlock(TimeUnit.Day, D(2026, 11, 1), 2, axisStart));
        Assert.Equal((D(2026, 10, 26), D(2026, 11, 1)), TimescaleLabels.ThinningBlock(TimeUnit.Week, axisStart, 1, axisStart));
        Assert.Equal((DateOnly.MinValue, D(4095, 12, 31)), TimescaleLabels.ThinningBlock(TimeUnit.Year, D(2040, 1, 1), 4096, axisStart));
        Assert.Equal(DateOnly.MaxValue, TimescaleLabels.ThinningBlock(TimeUnit.Year, D(9000, 1, 1), 8192, axisStart).End);
    }

    [Fact]
    public void ThinningSlots_AreCalendarAlignedForMonthsQuartersYears_AxisRelativeForDaysWeeks()
    {
        var axisStart = D(2026, 10, 29); // a Thursday
        int Slot(TimeUnit unit, DateOnly start) => TimescaleLabels.ThinningSlot(unit, start, axisStart);

        // Every 3rd month starts a quarter, every 6th a half-year, every 12th a year.
        var quarterStarts = Enumerable.Range(1, 12).Where(m => Slot(TimeUnit.Month, D(2026, m, 1)) % 3 == 0);
        Assert.Equal([1, 4, 7, 10], quarterStarts);
        Assert.Equal([1, 7], Enumerable.Range(1, 12).Where(m => Slot(TimeUnit.Month, D(2026, m, 1)) % 6 == 0));
        Assert.Equal([1, 3, 5, 7, 9, 11], Enumerable.Range(1, 12).Where(m => Slot(TimeUnit.Month, D(2026, m, 1)) % 2 == 0));
        Assert.Equal(0, Slot(TimeUnit.Quarter, D(2026, 1, 1)) % 2);
        Assert.Equal(1, Slot(TimeUnit.Quarter, D(2026, 4, 1)) % 2);
        Assert.Equal(2026, Slot(TimeUnit.Year, D(2026, 1, 1)));

        Assert.Equal(0, Slot(TimeUnit.Day, axisStart));
        Assert.Equal(4, Slot(TimeUnit.Day, D(2026, 11, 2)));
        Assert.Equal(0, Slot(TimeUnit.Week, axisStart));          // the clipped first week
        Assert.Equal(1, Slot(TimeUnit.Week, D(2026, 11, 2)));
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
        Assert.Equal(["October 2026", "Oct 2026", "Oct '26"],
            TimescaleLabels.Candidates(TimeUnit.Month, true, D(2026, 10, 1), "yyyy-MM-dd"));
    }

    [Fact]
    public void WeekAndQuarterLabels()
    {
        Assert.Equal(["2026-10-19", "19 Oct"], TimescaleLabels.Candidates(TimeUnit.Week, true, D(2026, 10, 19), "yyyy-MM-dd"));
        Assert.Equal(["19"], TimescaleLabels.Candidates(TimeUnit.Week, false, D(2026, 10, 19), "yyyy-MM-dd"));
        Assert.Equal(["Q4"], TimescaleLabels.Candidates(TimeUnit.Quarter, false, D(2026, 10, 1), "yyyy-MM-dd"));
    }
}
