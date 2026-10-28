using GanttIt.Core.Editing;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Editing;

public class ChartSummaryTests
{
    [Fact]
    public void Span_covers_earliest_and_latest_date_of_any_section()
    {
        var chart = Sequential(3, D(2026, 10, 1), days: 10);
        chart.Sections[1].End = D(2026, 9, 1); // invalid row still counts

        Assert.True(ChartSummary.TryGetSpan(chart, out var first, out var last));
        Assert.Equal(D(2026, 9, 1), first);
        Assert.Equal(D(2026, 10, 30), last);
    }

    [Fact]
    public void Empty_chart_has_no_span()
    {
        var chart = Chart.Create("Empty");
        Assert.False(ChartSummary.TryGetSpan(chart, out _, out _));
        Assert.Equal("No sections", ChartSummary.DescribeSpan(chart, "yyyy-MM-dd"));
    }

    [Fact]
    public void Span_uses_the_date_format_and_falls_back_to_iso()
    {
        var chart = Sequential(2, D(2026, 10, 1), days: 10);
        Assert.Equal("01.10.2026 – 20.10.2026", ChartSummary.DescribeSpan(chart, "dd.MM.yyyy"));
        Assert.Equal("2026-10-01 – 2026-10-20", ChartSummary.DescribeSpan(chart, "HH:mm"));
        Assert.Equal("2026-10-01 – 2026-10-20", ChartSummary.DescribeSpan(chart, null));
    }

    [Theory]
    [InlineData(0, "0 sections")]
    [InlineData(1, "1 section")]
    [InlineData(12, "12 sections")]
    public void Section_count_text(int count, string expected) =>
        Assert.Equal(expected, ChartSummary.DescribeSections(count));

    [Theory]
    [InlineData(0, "Not placed")]
    [InlineData(1, "On 1 sheet")]
    [InlineData(2, "On 2 sheets")]
    public void Placement_text(int sheets, string expected) =>
        Assert.Equal(expected, ChartSummary.DescribePlacement(sheets));
}

public class RecentColorsTests
{
    private static readonly Rgb Red = Rgb.Parse("#FF0000");
    private static readonly Rgb Green = Rgb.Parse("#00FF00");
    private static readonly Rgb Blue = Rgb.Parse("#0000FF");

    [Fact]
    public void Most_recent_first_without_duplicates()
    {
        var recent = new RecentColors();
        recent.Add(Red);
        recent.Add(Green);
        recent.Add(Red);

        Assert.Equal([Red, Green], recent.Items);
    }

    [Fact]
    public void Oldest_drops_off_at_capacity()
    {
        var recent = new RecentColors(capacity: 2);
        recent.Add(Red);
        recent.Add(Green);
        recent.Add(Blue);

        Assert.Equal([Blue, Green], recent.Items);
    }

    [Fact]
    public void Capacity_must_be_positive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecentColors(0));
}

public class EditorInputTests
{
    [Theory]
    [InlineData("7", 7)]
    [InlineData(" 1 ", 1)]
    [InlineData("365", 365)]
    public void Valid_durations_parse(string text, int expected)
    {
        Assert.True(EditorInput.TryParseDuration(text, out var days, out var error));
        Assert.Equal(expected, days);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("999999")]
    public void Invalid_durations_are_rejected_with_a_message(string? text)
    {
        Assert.False(EditorInput.TryParseDuration(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Max_duration_spans_the_supported_range() =>
        Assert.Equal(SupportedDates.Max, SupportedDates.Min.AddDays(EditorInput.MaxDurationDays - 1));

    [Fact]
    public void Title_follows_a_rename_while_it_equals_the_name() =>
        Assert.Equal("Phase 2", EditorInput.TitleAfterRename("Phase 1", "Phase 2", "Phase 1"));

    [Fact]
    public void A_custom_title_survives_a_rename() =>
        Assert.Equal("Master schedule", EditorInput.TitleAfterRename("Phase 1", "Phase 2", "Master schedule"));

    [Theory]
    // from, over, after, count → Scheduler.Move target
    [InlineData(0, 2, false, 4, 1)] // drop above row 2: lands between rows 1 and 2
    [InlineData(0, 2, true, 4, 2)]  // drop below row 2
    [InlineData(3, 0, false, 4, 0)] // to the top
    [InlineData(0, 3, true, 4, 3)]  // to the bottom
    [InlineData(2, 2, false, 4, 2)] // onto itself
    [InlineData(2, 2, true, 4, 2)]
    [InlineData(2, 1, true, 4, 2)]  // just below the row above = where it is
    [InlineData(1, 9, true, 4, 3)]  // clamped
    public void Drop_index(int from, int over, bool after, int count, int expected) =>
        Assert.Equal(expected, EditorInput.DropIndex(from, over, after, count));

    [Fact]
    public void Drop_index_matches_scheduler_move()
    {
        // Dragging row 0 below row 2 must put it third.
        var chart = Sequential(4, D(2026, 1, 1));
        var moved = chart.Sections[0].Id;
        Scheduler.Move(chart, 0, EditorInput.DropIndex(0, 2, true, 4));
        Assert.Equal(2, chart.IndexOf(moved));
    }

    [Fact]
    public void Supported_date_check()
    {
        Assert.True(EditorInput.IsSupportedDate(D(2026, 1, 1), out var ok));
        Assert.Null(ok);
        Assert.False(EditorInput.IsSupportedDate(D(1850, 1, 1), out var error));
        Assert.Contains("1900", error);
    }
}

public class CloneTests
{
    [Fact]
    public void Chart_clone_is_deep()
    {
        var chart = Sequential(3, D(2026, 1, 1));
        Scheduler.SetLink(chart, 0, true);
        var copy = chart.Clone();

        copy.Sections[0].Name = "Changed";
        Scheduler.SetStart(copy, copy.Sections[0].Id, D(2026, 2, 1));
        Scheduler.SetLink(copy, 1, true);
        copy.Sections.RemoveAt(2);

        Assert.Equal("Section 1", chart.Sections[0].Name);
        Assert.Equal(D(2026, 1, 1), chart.Sections[0].Start);
        Assert.Equal(D(2026, 1, 11), chart.Sections[1].Start);
        Assert.Single(chart.Links);
        Assert.Equal(3, chart.Sections.Count);
    }

    [Fact]
    public void Chart_clone_keeps_every_field()
    {
        var chart = Sequential(2, D(2026, 1, 1));
        chart.Title = "T";
        chart.Timescale = Timescale.Weeks;
        chart.Columns = ChartColumns.Start;
        chart.TimelineWidth = 1.25;
        chart.NameMaxWidth = 0.5;
        Scheduler.SetLink(chart, 0, true);

        var copy = chart.Clone();

        Assert.Equal(chart.Id, copy.Id);
        Assert.Equal(chart.Name, copy.Name);
        Assert.Equal("T", copy.Title);
        Assert.Equal(Timescale.Weeks, copy.Timescale);
        Assert.Equal(ChartColumns.Start, copy.Columns);
        Assert.Equal(1.25, copy.TimelineWidth);
        Assert.Equal(0.5, copy.NameMaxWidth);
        Assert.Equal(chart.Links, copy.Links);
        Assert.Equal(chart.SchemaVersion, copy.SchemaVersion);
        Assert.Equal(chart.Sections.Select(s => (s.Id, s.Name, s.Start, s.End, s.Color)),
            copy.Sections.Select(s => (s.Id, s.Name, s.Start, s.End, s.Color)));
    }

    [Fact]
    public void Settings_clone_is_independent()
    {
        var settings = new ProjectSettings { TitleTextStyle = new StyleRef("u1", "Title"), DateFormat = "dd.MM.yyyy" };
        var copy = settings.Clone();

        copy.TitleTextStyle = new StyleRef("u2", "Other");
        copy.Palette.Clear();
        copy.CellPadding = 1;

        Assert.Equal(new StyleRef("u1", "Title"), settings.TitleTextStyle);
        Assert.Equal(12, settings.Palette.Count);
        Assert.Equal(ProjectSettings.DefaultCellPadding, settings.CellPadding);
        Assert.Equal("dd.MM.yyyy", copy.DateFormat);
    }
}
