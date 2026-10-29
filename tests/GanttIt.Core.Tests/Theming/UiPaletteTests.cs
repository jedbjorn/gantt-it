using GanttIt.Core.Theming;
using Xunit.Abstractions;

namespace GanttIt.Core.Tests.Theming;

public class UiPaletteTests(ITestOutputHelper output)
{
    public static TheoryData<UiTheme> Themes => new() { UiTheme.Light, UiTheme.Dark };

    [Theory]
    [MemberData(nameof(Themes))]
    public void EveryPairing_MeetsItsContrastMinimum(UiTheme theme)
    {
        var palette = UiPalette.For(theme);
        var failures = new List<string>();
        foreach (var r in UiPalette.Requirements)
        {
            var ratio = UiColor.ContrastRatio(palette[r.Foreground], palette[r.Background]);
            output.WriteLine($"{theme,-5} {r.Foreground,-24} on {r.Background,-16} {ratio,5:0.00}:1 (min {r.Minimum}) {r.Use}");
            if (ratio < r.Minimum) failures.Add($"{r.Foreground} on {r.Background}: {ratio:0.00} < {r.Minimum}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void BothPalettes_DefineTheSameKeys()
    {
        var light = UiPalette.For(UiTheme.Light).Keys.OrderBy(k => k, StringComparer.Ordinal);
        var dark = UiPalette.For(UiTheme.Dark).Keys.OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(light, dark);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void TextAndBackgroundKeys_AreOpaque(UiTheme theme)
    {
        // Contrast is only meaningful against a solid background; only the chip outlines are translucent.
        var palette = UiPalette.For(theme);
        foreach (var r in UiPalette.Requirements)
        {
            Assert.True(palette[r.Foreground].IsOpaque, r.Foreground);
            Assert.True(palette[r.Background].IsOpaque, r.Background);
        }
    }

    [Fact]
    public void LightPalette_KeepsTheOriginalColors()
    {
        // The light theme is the palette Gantt-It shipped with before dark mode.
        var light = UiPalette.For(UiTheme.Light);
        Assert.Equal("#F4F5F7", light[UiPalette.Window].ToString());
        Assert.Equal("#FFFFFF", light[UiPalette.Surface].ToString());
        Assert.Equal("#1F2328", light[UiPalette.Text].ToString());
        Assert.Equal("#5E6672", light[UiPalette.MutedText].ToString());
        Assert.Equal("#2F6AD0", light[UiPalette.Accent].ToString());
        Assert.Equal("#C42B1C", light[UiPalette.Error].ToString());
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.0)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.0)]
    [InlineData("#767676", "#FFFFFF", 4.54)]
    public void ContrastRatio_MatchesWcag(string foreground, string background, double expected)
    {
        Assert.Equal(expected, UiColor.ContrastRatio(UiColor.Parse(foreground), UiColor.Parse(background)), 2);
    }

    [Fact]
    public void Parse_ReadsAlpha_AndCompositesOverTheBackground()
    {
        var half = UiColor.Parse("#80000000");
        Assert.Equal(new UiColor(0x80, 0, 0, 0), half);
        Assert.Equal(UiColor.Parse("#7F7F7F"), half.Over(UiColor.Parse("#FFFFFF")));
        Assert.Throws<FormatException>(() => UiColor.Parse("#12345"));
    }
}
