using System.Globalization;
using GanttIt.Core.Dates;
using GanttIt.Core.Model;
using static GanttIt.Core.Tests.Support.TestCharts;

namespace GanttIt.Core.Tests.Model;

public class ModelTests
{
    [Fact]
    public void Defaults_AreThePaperLengthsOfTheSpec_InFeet()
    {
        var chart = Chart.Create("Chart");
        Assert.Equal(180, Units.ToMillimeters(chart.TimelineWidth), 9);
        Assert.Equal(60, Units.ToMillimeters(chart.NameMaxWidth), 9);
        Assert.Equal(1.5, Units.ToMillimeters(new ProjectSettings().CellPadding), 9);
        Assert.Equal("Chart", chart.Title);
        Assert.Equal(SchemaVersions.Chart, chart.SchemaVersion);
    }

    [Fact]
    public void Duration_IsInclusive()
    {
        Assert.Equal(1, Section(1, D(2026, 1, 1), D(2026, 1, 1)).Duration);
        Assert.Equal(31, Section(1, D(2026, 1, 1), D(2026, 1, 31)).Duration);
    }

    [Fact]
    public void DefaultPalette_HasTwelveDistinctColors()
    {
        Assert.Equal(12, Palette.Default.Count);
        Assert.Equal(12, Palette.Default.Distinct().Count());
        Assert.Equal(12, new ProjectSettings().Palette.Count);
    }

    [Fact]
    public void NextColor_FollowsTheLastSectionsColor_AndWraps()
    {
        var sections = new List<Section> { Section(1, D(2026, 1, 1), D(2026, 1, 2)) };
        sections[0].Color = Palette.Default[11];
        Assert.Equal(Palette.Default[0], Palette.NextColor(Palette.Default, sections));

        sections[0].Color = Rgb.Parse("#010203"); // hand-picked, not in the palette
        Assert.Equal(Palette.Default[1], Palette.NextColor(Palette.Default, sections));
        Assert.Equal(Palette.Default[0], Palette.NextColor(Palette.Default, []));
    }

    [Theory]
    [InlineData("#4F8EF7", 0x4F, 0x8E, 0xF7)]
    [InlineData("4f8ef7", 0x4F, 0x8E, 0xF7)]
    [InlineData(" #000000 ", 0, 0, 0)]
    public void Rgb_ParsesHex(string text, byte r, byte g, byte b)
    {
        Assert.True(Rgb.TryParse(text, out var c));
        Assert.Equal(new Rgb(r, g, b), c);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#1234567")]
    public void Rgb_RejectsBadHex(string? text) => Assert.False(Rgb.TryParse(text, out _));

    [Fact]
    public void Rgb_FormatsUpperCaseHex() => Assert.Equal("#0A0B0C", new Rgb(10, 11, 12).ToHex());

    [Theory]
    [InlineData(DateFormats.Iso, "2026-03-07")]
    [InlineData(DateFormats.UsSlash, "03/07/2026")]
    [InlineData(DateFormats.EuSlash, "07/03/2026")]
    [InlineData(DateFormats.EuDot, "07.03.2026")]
    [InlineData("d MMM yyyy", "7 Mar 2026")]
    public void DateFormats_AreCultureInvariant(string pattern, string expected)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "fr-FR", "th-TH", "ar-SA", "en-US" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(expected, DateFormats.Format(D(2026, 3, 7), pattern));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("yyyy-MM-dd", true)]
    [InlineData("dd MMM yy", true)]
    [InlineData("", false)]
    [InlineData("yyyy-MM-dd HH:mm", false)]
    [InlineData("%", false)]
    public void DateFormats_ValidateCustomPatterns(string pattern, bool valid)
    {
        Assert.Equal(valid, DateFormats.IsValidPattern(pattern, out var error));
        Assert.Equal(valid, error is null);
    }
}
