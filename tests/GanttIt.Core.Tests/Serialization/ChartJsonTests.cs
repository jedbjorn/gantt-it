using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using GanttIt.Core.Serialization;
using GanttIt.Core.Tests.Support;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Serialization;

/// <summary>A4: chart JSON round-trips, newer schema versions are refused for editing, malformed JSON is reported.</summary>
public class ChartJsonTests
{
    private static Chart Sample()
    {
        var chart = Sequential(3, D(2026, 10, 1), days: 12, gap: 1);
        chart.Title = "Programme — Phase 1";
        chart.Timescale = Timescale.Weeks;
        chart.Columns = ChartColumns.Start | ChartColumns.Duration;
        chart.TimelineWidth = Units.FromMillimeters(210);
        chart.NameMaxWidth = Units.FromMillimeters(45);
        chart.Sections[1].Name = "Line\nbreak, \"quotes\" & ünïcode";
        chart.Sections[2].Color = Rgb.Parse("#0A0B0C");
        Scheduler.SetLink(chart, 0, true);
        Scheduler.SetLink(chart, 1, true);
        return chart;
    }

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var chart = Sample();

        var json = ChartJson.Serialize(chart);
        var result = ChartJson.Deserialize(json);

        Assert.Equal(LoadStatus.Ok, result.Status);
        Assert.True(result.IsEditable);
        Assert.Null(result.Error);
        var back = result.Value!;
        Assert.Equal(chart.Id, back.Id);
        Assert.Equal(chart.Name, back.Name);
        Assert.Equal(chart.Title, back.Title);
        Assert.Equal(chart.Timescale, back.Timescale);
        Assert.Equal(chart.Columns, back.Columns);
        Assert.Equal(chart.TimelineWidth, back.TimelineWidth);
        Assert.Equal(chart.NameMaxWidth, back.NameMaxWidth);
        Assert.Equal(chart.SchemaVersion, back.SchemaVersion);
        Assert.Equal(chart.Links, back.Links);
        Assert.Equal(
            chart.Sections.Select(s => (s.Id, s.Name, s.Start, s.End, s.Color)),
            back.Sections.Select(s => (s.Id, s.Name, s.Start, s.End, s.Color)));
        Assert.Equal(json, ChartJson.Serialize(back));
    }

    [Fact]
    public void Json_IsReadable_WithIsoDatesHexColorsAndNamedEnums()
    {
        var json = ChartJson.Serialize(Sample());

        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"start\":\"2026-10-01\"", json);
        Assert.Contains("\"color\":\"#0A0B0C\"", json);
        Assert.Contains("\"timescale\":\"Weeks\"", json);
        Assert.Contains("\"lagDays\":1", json);
    }

    [Fact]
    public void NewerSchemaVersion_LoadsReadOnly_AndIsNeverDowngraded()
    {
        var json = ChartJson.Serialize(Sample())
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":2")
            .Replace("\"title\":", "\"futureField\":{\"x\":1},\"title\":");

        var result = ChartJson.Deserialize(json);

        Assert.Equal(LoadStatus.NewerVersion, result.Status);
        Assert.False(result.IsEditable);
        Assert.Contains("newer", result.Error);
        var preview = Assert.IsType<Chart>(result.Value);
        Assert.Equal(2, preview.SchemaVersion);
        Assert.Throws<InvalidOperationException>(() => ChartJson.Serialize(preview));
    }

    [Fact]
    public void NewerSchemaVersion_WithAnUnreadableShape_IsStillReportedAsNewer()
    {
        var result = ChartJson.Deserialize("{\"schemaVersion\":7,\"sections\":\"a different shape\"}");

        Assert.Equal(LoadStatus.NewerVersion, result.Status);
        Assert.Null(result.Value);
        Assert.False(result.IsEditable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":1.5}")]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":-3}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":5}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":null}")]
    [InlineData("{\"schemaVersion\":1,\"name\":null}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":[{\"name\":\"a\",\"start\":\"2026-13-45\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":[{\"name\":\"a\",\"start\":\"2026-01-01\",\"end\":\"2026-01-02\",\"color\":\"blue\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"sections\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"timescale\":\"Fortnights\"}")]
    [InlineData("{\"schemaVersion\":1,\"timescale\":99}")]
    [InlineData("{\"schemaVersion\":1,\"timelineWidth\":\"wide\"}")]
    [InlineData("{\"schemaVersion\":1,\"links\":[{\"fromSectionId\":\"x\"}]}")]
    public void MalformedJson_IsReported_NeverThrown(string? json)
    {
        var result = ChartJson.Deserialize(json);

        Assert.Equal(LoadStatus.Invalid, result.Status);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void InconsistentChart_IsReported()
    {
        static string Mutate(Action<Chart> change)
        {
            var chart = Sample();
            change(chart);
            return ChartJson.Serialize(chart);
        }

        string[] cases =
        [
            Mutate(c => c.Sections[1].Id = c.Sections[0].Id),                                 // duplicate ids
            Mutate(c => c.Links.Add(new Link(c.Sections[0].Id, c.Sections[2].Id, 0))),        // not neighbours
            Mutate(c => c.Links.Add(new Link(c.Sections[2].Id, Id(999), 0))),                 // missing section
            Mutate(c => c.Links.Add(c.Links[0])),                                             // duplicate link
            Mutate(c => c.Sections[0].Start = D(1066, 10, 14)),                               // outside supported dates
            Mutate(c => c.Links[0] = c.Links[0] with { LagDays = 30 }),                      // lag contradicts dates
        ];

        foreach (var json in cases)
        {
            var result = ChartJson.Deserialize(json);
            Assert.Equal(LoadStatus.Invalid, result.Status);
            Assert.NotNull(result.Error);
        }
    }

    [Fact]
    public void HugeLinkLag_IsReported_SoItNeverReachesTheScheduler()
    {
        // Hand-edited JSON: a lag far beyond any date would make SetEnd/EnforceLinks overflow DateOnly.
        var json = ChartJson.Serialize(Sample()).Replace("\"lagDays\":1", "\"lagDays\":10000000");

        var result = ChartJson.Deserialize(json);

        Assert.Equal(LoadStatus.Invalid, result.Status);
        Assert.Null(result.Value);
        Assert.Contains("lag", result.Error);
    }

    [Fact]
    public void LinkLagThatDoesNotMatchTheDates_IsReported()
    {
        // The real gap is 1 day; a stored lag of 30 would silently break To.Start = From.End + 1 + LagDays.
        var json = ChartJson.Serialize(Sample()).Replace("\"lagDays\":1", "\"lagDays\":30");

        var result = ChartJson.Deserialize(json);

        Assert.Equal(LoadStatus.Invalid, result.Status);
        Assert.Contains("lag", result.Error);
    }

    [Fact]
    public void Duration_IsDerived_AndNotStored()
    {
        Assert.DoesNotContain("\"duration\"", ChartJson.Serialize(Sample()));
    }

    [Fact]
    public void NewerSchemaVersion_WithAStructurallyBrokenChart_HasNoPreview()
    {
        var result = ChartJson.Deserialize("{\"schemaVersion\":2,\"name\":\"x\",\"sections\":null}");

        Assert.Equal(LoadStatus.NewerVersion, result.Status);
        Assert.Null(result.Value);
        Assert.False(result.IsEditable);
    }

    [Fact]
    public void NewerSchemaVersion_Preview_CanBeLaidOut()
    {
        var json = ChartJson.Serialize(Sample()).Replace("\"schemaVersion\":1", "\"schemaVersion\":2");

        var preview = ChartJson.Deserialize(json).Value!;
        var layout = LayoutEngine.Layout(preview, Settings(), new FakeMeasurer());

        Assert.Equal(preview.Sections.Count, layout.Primitives.OfType<FilledRectPrimitive>().Count());
    }

    [Fact]
    public void Settings_WithADateFormatLayoutCannotUse_AreReported()
    {
        var result = SettingsJson.Deserialize("{\"schemaVersion\":1,\"dateFormat\":\"hh\",\"palette\":[\"#112233\"]}");

        Assert.Equal(LoadStatus.Invalid, result.Status);
        Assert.Null(result.Value);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Settings_RoundTrip_AndRefuseNewerVersions()
    {
        var settings = new ProjectSettings
        {
            TitleTextStyle = new StyleRef("uid-title", "Title 5mm"),
            HeaderTextStyle = new StyleRef("uid-header", "Header 3.5mm"),
            BodyTextStyle = new StyleRef("uid-body", "Body 2.5mm"),
            ChartLineStyle = new StyleRef("uid-thin", "Thin Lines"),
            HeaderLineStyle = new StyleRef("uid-wide", "Wide Lines"),
            DateFormat = "dd MMM yy",
            CellPadding = Units.FromMillimeters(2),
            Palette = [Rgb.Parse("#112233"), Rgb.Parse("#445566")],
        };

        var json = SettingsJson.Serialize(settings);
        var back = SettingsJson.Deserialize(json);

        Assert.True(back.IsEditable);
        Assert.Equal(json, SettingsJson.Serialize(back.Value!));
        Assert.Equal(settings.BodyTextStyle, back.Value!.BodyTextStyle);

        var newer = SettingsJson.Deserialize(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":3"));
        Assert.Equal(LoadStatus.NewerVersion, newer.Status);
        Assert.Throws<InvalidOperationException>(() => SettingsJson.Serialize(newer.Value!));
        Assert.Equal(LoadStatus.Invalid, SettingsJson.Deserialize("{\"schemaVersion\":1,\"palette\":[\"#12\"]}").Status);
    }

    [Fact]
    public void CopyRule_GivesTheLaterDuplicateANewId()
    {
        var original = Sample();
        var copy = ChartJson.Deserialize(ChartJson.Serialize(original)).Value!;
        var other = Sequential(1, D(2026, 1, 1));
        other.Id = Id(77);
        var next = 500;

        var reassigned = ChartCopies.ReassignDuplicateIds([original, other, copy], () => Id(next++));

        Assert.Same(copy, Assert.Single(reassigned));
        Assert.Equal(Id(500), copy.Id);
        Assert.Equal(Id(1000), original.Id);
        Assert.Equal(Id(77), other.Id);
    }

    [Fact]
    public void AssignNewId_ReplacesTheId()
    {
        var chart = Sample();
        var id = ChartCopies.AssignNewId(chart);

        Assert.Equal(id, chart.Id);
        Assert.NotEqual(Id(1000), id);
    }
}
