using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace GanttIt.Spike;

/// <summary>WPF FormattedText measurement, run inside the Revit process (main/UI thread).
/// Everything is measured at emSize = 100 DIP so ratios to the em can be read off directly.</summary>
internal static class WpfMeasure
{
    public const double Em = 100.0;

    public static Typeface Face(string font, bool bold, bool italic) =>
        new(new FontFamily(font), italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

    public static FormattedText Make(string text, string font, bool bold, bool italic, double em, double? maxWidth)
    {
        var ft = new FormattedText(text.Replace("\r", "\n"), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Face(font, bold, italic), em, Brushes.Black, 1.0);
        if (maxWidth.HasValue) ft.MaxTextWidth = maxWidth.Value;
        return ft;
    }

    /// <summary>Raw FormattedText numbers at em=100 (single line, or wrapped at maxWidthEm100 if given).</summary>
    public static JsonObject Measure(string text, string font, bool bold, bool italic, double? maxWidthEm100 = null)
    {
        var ft = Make(text, font, bold, italic, Em, maxWidthEm100);
        var ink = ft.BuildGeometry(new Point(0, 0)).Bounds;
        var one = Make("Hg", font, bold, italic, Em, null);
        var o = new JsonObject
        {
            ["width"] = Util.N(ft.Width), ["widthTrailingWs"] = Util.N(ft.WidthIncludingTrailingWhitespace),
            ["height"] = Util.N(ft.Height), ["extent"] = Util.N(ft.Extent), ["baseline"] = Util.N(ft.Baseline),
            ["overhangLeading"] = Util.N(ft.OverhangLeading), ["overhangTrailing"] = Util.N(ft.OverhangTrailing),
            ["overhangAfter"] = Util.N(ft.OverhangAfter),
            ["inkX"] = Util.N(ink.IsEmpty ? double.NaN : ink.X), ["inkY"] = Util.N(ink.IsEmpty ? double.NaN : ink.Y),
            ["inkW"] = Util.N(ink.IsEmpty ? double.NaN : ink.Width), ["inkH"] = Util.N(ink.IsEmpty ? double.NaN : ink.Height),
            ["singleLineHeight"] = Util.N(one.Height),
            ["lines"] = Util.N(ft.Height / one.Height, 3),
        };
        return o;
    }

    public static JsonObject FontInfo(string font, bool bold, bool italic)
    {
        var tf = Face(font, bold, italic);
        var o = new JsonObject { ["requested"] = font, ["lineSpacing"] = Util.N(tf.FontFamily.LineSpacing), ["familyBaseline"] = Util.N(tf.FontFamily.Baseline) };
        if (tf.TryGetGlyphTypeface(out var gt))
        {
            o["fontUri"] = gt.FontUri?.ToString();
            o["familyName"] = gt.FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var n) ? n : null;
            o["capsHeight"] = Util.N(gt.CapsHeight); o["xHeight"] = Util.N(gt.XHeight);
            o["ascent"] = Util.N(gt.Baseline); o["height"] = Util.N(gt.Height);
            o["underlinePosition"] = Util.N(gt.UnderlinePosition);
        }
        else o["glyphTypeface"] = "not found (fallback font?)";
        return o;
    }

    public static JsonArray InstalledFonts() =>
        new(Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).Select(s => (JsonNode)s).ToArray());
}
