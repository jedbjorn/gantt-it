using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Scheduling;

/// <summary>A2: the scheduling rules of §5.1 and §5.2, one named test per rule.</summary>
public class SchedulerTests
{
    /// <summary>Four 10-day sections, 2-day gaps: A 1–10 Jan, B 13–22, C 25 Jan–3 Feb, D 6–15 Feb.</summary>
    private static Chart FourSections() => Sequential(4, D(2026, 1, 1), days: 10, gap: 2);

    private static (DateOnly Start, DateOnly End)[] Dates(Chart chart) =>
        chart.Sections.Select(s => (s.Start, s.End)).ToArray();

    private static void AssertLinksHold(Chart chart)
    {
        foreach (var link in chart.Links)
        {
            var from = chart.GetSection(link.FromSectionId);
            var to = chart.GetSection(link.ToSectionId);
            Assert.Equal(from.End.AddDays(1 + link.LagDays), to.Start);
        }
    }

    // ---- §5.2 row: Start ------------------------------------------------------------

    [Fact]
    public void StartEdit_ShiftsTheSection_AndKeepsItsDuration()
    {
        var chart = FourSections();
        var b = chart.Sections[1];

        Scheduler.SetStart(chart, b.Id, D(2026, 1, 20));

        Assert.Equal(D(2026, 1, 20), b.Start);
        Assert.Equal(D(2026, 1, 29), b.End);
        Assert.Equal(10, b.Duration);
    }

    [Fact]
    public void StartEdit_ShiftsTheWholeChain_EarlierAndLaterSections()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true); // A–B
        Scheduler.SetLink(chart, 1, true); // B–C
        var before = Dates(chart);

        Scheduler.SetStart(chart, chart.Sections[1].Id, chart.Sections[1].Start.AddDays(5));

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(before[i].Start.AddDays(5), chart.Sections[i].Start);
            Assert.Equal(before[i].End.AddDays(5), chart.Sections[i].End);
        }
        Assert.Equal(before[3], (chart.Sections[3].Start, chart.Sections[3].End)); // D is not in the chain
        AssertLinksHold(chart);
    }

    [Fact]
    public void StartEdit_EarlierDate_ShiftsTheChainBackwards()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 2, true); // C–D
        var before = Dates(chart);

        Scheduler.SetStart(chart, chart.Sections[3].Id, chart.Sections[3].Start.AddDays(-4));

        Assert.Equal(before[2].Start.AddDays(-4), chart.Sections[2].Start);
        Assert.Equal(before[3].Start.AddDays(-4), chart.Sections[3].Start);
        Assert.Equal(before[1], (chart.Sections[1].Start, chart.Sections[1].End));
        AssertLinksHold(chart);
    }

    [Fact]
    public void StartEdit_UnlinkedSection_MovesAlone()
    {
        var chart = FourSections();
        var before = Dates(chart);

        Scheduler.SetStart(chart, chart.Sections[1].Id, chart.Sections[1].Start.AddDays(3));

        Assert.Equal(before[0], (chart.Sections[0].Start, chart.Sections[0].End));
        Assert.Equal(before[2], (chart.Sections[2].Start, chart.Sections[2].End));
    }

    // ---- §5.2 row: End --------------------------------------------------------------

    [Fact]
    public void EndEdit_ChangesDuration_ShiftsLaterLinkedSections_LeavesEarlierOnes()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true); // A–B
        Scheduler.SetLink(chart, 1, true); // B–C
        Scheduler.SetLink(chart, 2, true); // C–D
        var before = Dates(chart);

        Scheduler.SetEnd(chart, chart.Sections[1].Id, before[1].End.AddDays(6));

        Assert.Equal(before[0], (chart.Sections[0].Start, chart.Sections[0].End)); // earlier: unchanged
        Assert.Equal(before[1].Start, chart.Sections[1].Start);
        Assert.Equal(16, chart.Sections[1].Duration);
        for (var i = 2; i < 4; i++)
        {
            Assert.Equal(before[i].Start.AddDays(6), chart.Sections[i].Start);
            Assert.Equal(10, chart.Sections[i].Duration); // each later section keeps its duration
        }
        AssertLinksHold(chart);
    }

    [Fact]
    public void EndEdit_StopsAtTheEndOfTheChain()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 1, true); // B–C only
        var before = Dates(chart);

        Scheduler.SetEnd(chart, chart.Sections[1].Id, before[1].End.AddDays(-3));

        Assert.Equal(before[2].Start.AddDays(-3), chart.Sections[2].Start);
        Assert.Equal(before[3], (chart.Sections[3].Start, chart.Sections[3].End)); // C–D not linked
    }

    // ---- §5.2 row: Duration ---------------------------------------------------------

    [Fact]
    public void DurationEdit_ChangesEnd_AndShiftsLaterLinkedSectionsLikeAnEndEdit()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true);
        Scheduler.SetLink(chart, 1, true);
        var before = Dates(chart);

        Scheduler.SetDuration(chart, chart.Sections[0].Id, 4);

        Assert.Equal(before[0].Start, chart.Sections[0].Start);
        Assert.Equal(before[0].Start.AddDays(3), chart.Sections[0].End);
        Assert.Equal(before[1].Start.AddDays(-6), chart.Sections[1].Start);
        Assert.Equal(before[2].Start.AddDays(-6), chart.Sections[2].Start);
        Assert.Equal(before[3], (chart.Sections[3].Start, chart.Sections[3].End));
        AssertLinksHold(chart);
    }

    [Fact]
    public void DurationEdit_BelowOneDay_IsRejected()
    {
        var chart = FourSections();
        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.SetDuration(chart, chart.Sections[0].Id, 0));
    }

    // ---- §5.2 row: Toggle link on / §5.1 ------------------------------------------

    [Theory]
    [InlineData(0, 2)]   // the default 2-day gap
    [InlineData(-2, 0)]  // back-to-back: B starts the day after A ends
    [InlineData(-5, -3)] // overlap → negative lag
    public void TickingLink_RecordsTheCurrentGapAsLag_AndChangesNoDates(int moveB, int expectedLag)
    {
        var chart = FourSections();
        var b = chart.Sections[1];
        b.Start = b.Start.AddDays(moveB);
        b.End = b.End.AddDays(moveB);
        var before = Dates(chart);

        Scheduler.SetLinkToFollowing(chart, chart.Sections[0].Id, true);

        var link = Assert.Single(chart.Links);
        Assert.Equal(new Link(Id(1), Id(2), expectedLag), link);
        Assert.Equal(before, Dates(chart));
        AssertLinksHold(chart);
    }

    [Fact]
    public void TickingLink_FromEitherRow_IsTheSameSharedLink()
    {
        var chart = FourSections();

        Scheduler.SetLinkToPrevious(chart, chart.Sections[2].Id, true);

        Assert.True(Scheduler.IsLinkedToFollowing(chart, chart.Sections[1].Id));
        Assert.True(Scheduler.IsLinkedToPrevious(chart, chart.Sections[2].Id));
        Scheduler.SetLinkToFollowing(chart, chart.Sections[1].Id, true); // ticking the other box is a no-op
        Assert.Single(chart.Links);
    }

    [Fact]
    public void UntickingLink_RemovesIt_AndChangesNoDates()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true);
        Scheduler.SetEnd(chart, chart.Sections[0].Id, chart.Sections[0].End.AddDays(2)); // B follows A
        var before = Dates(chart);

        Scheduler.SetLinkToPrevious(chart, chart.Sections[1].Id, false);

        Assert.Empty(chart.Links);
        Assert.Equal(before, Dates(chart));
        Assert.False(Scheduler.IsLinkedToFollowing(chart, chart.Sections[0].Id));
    }

    [Fact]
    public void Link_IsUnavailableOnTheFirstRowsPreviousAndLastRowsFollowing()
    {
        var chart = FourSections();
        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.SetLinkToPrevious(chart, chart.Sections[0].Id, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => Scheduler.SetLinkToFollowing(chart, chart.Sections[3].Id, true));
    }

    [Fact]
    public void Propagation_EnforcesToStartEqualsFromEndPlusOnePlusLag_WithOverlap()
    {
        var chart = FourSections();
        var b = chart.Sections[1];
        b.Start = D(2026, 1, 8); // overlaps A (ends 10 Jan) by 3 days
        b.End = D(2026, 1, 17);
        Scheduler.SetLink(chart, 0, true);
        Assert.Equal(-3, chart.Links[0].LagDays);

        Scheduler.SetEnd(chart, chart.Sections[0].Id, D(2026, 1, 15));

        Assert.Equal(D(2026, 1, 13), b.Start); // 15 + 1 − 3
        Assert.Equal(10, b.Duration);
    }

    [Fact]
    public void EnforceLinks_RepairsChainsAfterOutsideEdits()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true);
        Scheduler.SetLink(chart, 1, true);
        chart.Sections[0].End = chart.Sections[0].End.AddDays(4); // hand edit, no propagation

        Scheduler.EnforceLinks(chart);

        AssertLinksHold(chart);
        Assert.Equal(10, chart.Sections[2].Duration);
    }

    // ---- §5.1 reorder -------------------------------------------------------------

    [Fact]
    public void Reorder_RemovesTheMovedSectionsLinksOnBothSides_AndReturnsANotice()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true); // A–B
        Scheduler.SetLink(chart, 1, true); // B–C
        var before = Dates(chart);

        var notice = Scheduler.Move(chart, 1, 3); // B to the end

        Assert.NotNull(notice);
        Assert.Equal(2, notice.RemovedLinks.Count);
        Assert.Contains("removed 2 links", notice.Message);
        Assert.Empty(chart.Links);
        Assert.Equal([Id(1), Id(3), Id(4), Id(2)], chart.Sections.Select(s => s.Id));
        Assert.Equal(before[1], (chart.Sections[3].Start, chart.Sections[3].End)); // no date changes
    }

    [Fact]
    public void Reorder_IntoALinkedPair_RemovesTheLinkItNowSplits()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 1, true); // B–C

        var notice = Scheduler.Move(chart, 3, 2); // D between B and C

        Assert.NotNull(notice);
        Assert.Equal(new Link(Id(2), Id(3), 2), Assert.Single(notice.RemovedLinks));
        Assert.Empty(chart.Links);
    }

    [Fact]
    public void Reorder_KeepsLinksElsewhere_AndWithoutRemovalsReturnsNoNotice()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 2, true); // C–D

        var notice = Scheduler.Move(chart, 1, 0); // swap A and B

        Assert.Null(notice);
        Assert.Single(chart.Links);
    }

    // ---- §2.2 add / delete ------------------------------------------------------------

    [Fact]
    public void AddSection_StartsTheDayAfterThePreviousEnd_Lasts7Days_TakesTheNextPaletteColor()
    {
        var chart = FourSections();
        chart.Sections[3].Color = Palette.Default[4];

        var added = Scheduler.AddSection(chart, Palette.Default, D(2000, 1, 1), id: Id(5));

        Assert.Equal(chart.Sections[3].End.AddDays(1), added.Start);
        Assert.Equal(7, added.Duration);
        Assert.Equal(Palette.Default[5], added.Color);
        Assert.Equal("Section 5", added.Name);
        Assert.Same(added, chart.Sections[^1]);
    }

    [Fact]
    public void AddSection_ToAnEmptyChart_StartsOnTheGivenDate_WithTheFirstColor()
    {
        var chart = Chart.Create("Empty");

        var added = Scheduler.AddSection(chart, Palette.Default, D(2026, 3, 2));

        Assert.Equal(D(2026, 3, 2), added.Start);
        Assert.Equal(D(2026, 3, 8), added.End);
        Assert.Equal(Palette.Default[0], added.Color);
    }

    [Fact]
    public void RemoveSection_DropsItsLinks_AndDoesNotLinkItsFormerNeighbours()
    {
        var chart = FourSections();
        Scheduler.SetLink(chart, 0, true);
        Scheduler.SetLink(chart, 1, true);

        var notice = Scheduler.RemoveSection(chart, Id(2));

        Assert.NotNull(notice);
        Assert.Equal(2, notice.RemovedLinks.Count);
        Assert.Empty(chart.Links);
        Assert.Equal(3, chart.Sections.Count);
    }
}
