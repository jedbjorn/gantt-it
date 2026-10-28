using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GanttIt.Core.Layout;
using GanttIt.Revit.Services;

namespace GanttIt.Revit.UI;

/// <summary>
/// The live preview's text measurer (§6.1), calibrated per text style against
/// Revit (decision #14, V9):
/// <list type="bullet">
/// <item>WPF <see cref="FormattedText"/> in <see cref="TextFormattingMode.Ideal"/> with the
/// type's font and bold/italic. Revit's Text Size is the cap height, so
/// em = size / the typeface's cap height.</item>
/// <item>Width = WPF width (trailing spaces included) × width factor ×
/// <see cref="TextStyleMetrics.WidthCalibration"/>, the factor measured from one
/// reference note in Revit.</item>
/// <item>Height = lines × <see cref="TextStyleMetrics.LinePitchFt"/>, Revit's fixed
/// per-type line pitch. A wrapped note is as wide as its set width, as in Revit.</item>
/// </list>
/// Must be used on a WPF (STA) thread, e.g. Revit's main thread.
/// </summary>
public sealed class WpfTextMeasurer : ITextMeasurer
{
    /// <summary>WPF device-independent pixels per foot (96 per inch).</summary>
    private const double DipPerFoot = 96.0 * 12.0;

    /// <summary>Used for a style missing from the metrics: Arial 2.5 mm, uncalibrated.</summary>
    private static readonly TextStyleMetrics Fallback = new("Arial", 2.5 / 304.8, false, false, 1.0, 1.0, 3.97 / 304.8);

    private static readonly Dictionary<(string Font, bool Bold, bool Italic), (Typeface Face, double CapsHeight)> Faces = [];

    private readonly IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> _metrics;

    public WpfTextMeasurer(IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public IReadOnlyList<TextSize> Measure(IReadOnlyList<TextRequest> requests) =>
        requests.Select(MeasureOne).ToList();

    private TextSize MeasureOne(TextRequest request)
    {
        var m = _metrics.TryGetValue(request.Style, out var found) ? found : Fallback;
        var k = m.WidthCalibration > 0 && double.IsFinite(m.WidthCalibration) ? m.WidthCalibration : 1.0;
        var wf = m.WidthFactor > 0 && double.IsFinite(m.WidthFactor) ? m.WidthFactor : 1.0;
        var pitch = m.LinePitchFt > 0 ? m.LinePitchFt : SingleLineHeightFt(m.FontName, m.TextSizeFt, m.Bold, m.Italic);

        var text = request.Text;
        if (request.WrapWidth is double setWidth)
        {
            // Wrap where Revit would: the WPF line length that scales to the set width.
            var ft = Format(text, m.FontName, m.TextSizeFt, m.Bold, m.Italic);
            ft.MaxTextWidth = Math.Max(1e-3, setWidth / (k * wf) * DipPerFoot);
            var lines = Math.Max(1, (int)Math.Round(ft.Height / SingleLineHeightDip(m.FontName, m.TextSizeFt, m.Bold, m.Italic)));
            return new TextSize(setWidth, lines * pitch);
        }

        var single = Format(text, m.FontName, m.TextSizeFt, m.Bold, m.Italic);
        var hardLines = Math.Max(1, (int)Math.Round(single.Height / SingleLineHeightDip(m.FontName, m.TextSizeFt, m.Bold, m.Italic)));
        return new TextSize(MaxLineWidthDip(text, m) / DipPerFoot * wf * k, hardLines * pitch);
    }

    /// <summary>Width of the widest hard line (Revit sizes an unwrapped note to its longest line).</summary>
    private static double MaxLineWidthDip(string text, TextStyleMetrics m)
    {
        var widest = 0.0;
        foreach (var line in Normalize(text).Split('\n'))
            widest = Math.Max(widest, Format(line, m.FontName, m.TextSizeFt, m.Bold, m.Italic).WidthIncludingTrailingWhitespace);
        return widest;
    }

    /// <summary>
    /// Uncalibrated single-line width in feet at width factor 1: the denominator of
    /// the per-type calibration factor (decision #14).
    /// </summary>
    public static double RawWidthFt(string text, string font, double sizeFt, bool bold, bool italic) =>
        Format(text, font, sizeFt, bold, italic).WidthIncludingTrailingWhitespace / DipPerFoot;

    /// <summary>Uncalibrated single-line height in feet (used only when no Revit pitch is known).</summary>
    public static double SingleLineHeightFt(string font, double sizeFt, bool bold, bool italic) =>
        SingleLineHeightDip(font, sizeFt, bold, italic) / DipPerFoot;

    private static double SingleLineHeightDip(string font, double sizeFt, bool bold, bool italic) =>
        Format("Hg", font, sizeFt, bold, italic).Height;

    private static FormattedText Format(string text, string font, double sizeFt, bool bold, bool italic)
    {
        var (face, capsHeight) = Face(font, bold, italic);
        var emDip = sizeFt / capsHeight * DipPerFoot;
        return new FormattedText(
            Normalize(text),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            face,
            Math.Max(emDip, 1e-3),
            Brushes.Black,
            null,
            TextFormattingMode.Ideal,
            1.0);
    }

    private static (Typeface Face, double CapsHeight) Face(string font, bool bold, bool italic)
    {
        var key = (font ?? "Arial", bold, italic);
        lock (Faces)
        {
            if (Faces.TryGetValue(key, out var cached)) return cached;
            var face = new Typeface(
                new FontFamily(key.Item1),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            // Cap height of the real glyphs; Arial's 0.716 if the font can't be resolved.
            var caps = face.TryGetGlyphTypeface(out var glyphs) && glyphs.CapsHeight > 0 ? glyphs.CapsHeight : 0.716;
            return Faces[key] = (face, caps);
        }
    }

    private static string Normalize(string text) => (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
}
