using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;

namespace GanttIt.Revit.UI.Preview;

/// <summary>
/// Draws the layout engine's primitives (§6) into a <see cref="DrawingContext"/>.
/// The editor preview and the home-screen thumbnails both use it (§2.1, §2.2).
/// Legend coordinates are feet, Y up, origin at the top-left of the border; the
/// chart is scaled uniformly to fit the target rectangle.
/// </summary>
public static class PreviewRenderer
{
    private static readonly Brush Paper = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)));
    private static readonly Dictionary<Rgb, Brush> Fills = [];
    private static readonly Dictionary<(string, bool, bool), Typeface> Typefaces = [];

    /// <summary>Draws <paramref name="layout"/> scaled to fit <paramref name="target"/>, centred horizontally, top-aligned.</summary>
    public static void Draw(
        DrawingContext dc,
        ChartLayout layout,
        IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> metrics,
        Rect target,
        double pixelsPerDip,
        bool thumbnail)
    {
        var grid = layout.Grid;
        var width = grid.Width;
        var height = -grid.Bottom;
        if (!(width > 0) || !(height > 0) || target.Width <= 0 || target.Height <= 0) return;

        var scale = Math.Min(target.Width / width, target.Height / height);
        var ox = target.Left + (target.Width - width * scale) / 2;
        var oy = target.Top;

        Rect ToRect(Box b) => new(ox + b.Left * scale, oy - b.Top * scale, b.Width * scale, b.Height * scale);
        Point ToPoint(Point2 p) => new(ox + p.X * scale, oy - p.Y * scale);

        dc.DrawRectangle(Paper, null, new Rect(ox, oy, width * scale, height * scale));

        var headerPen = Freeze(new Pen(Ink, thumbnail ? 0.8 : 1.3));
        var chartPen = Freeze(new Pen(Ink, thumbnail ? 0.4 : 0.6));

        // Same order as Emit: bars, then lines, then text.
        foreach (var p in layout.Primitives)
            if (p is FilledRectPrimitive bar)
                dc.DrawRectangle(Fill(bar.Color), null, ToRect(bar.Rect));

        foreach (var p in layout.Primitives)
            if (p is LinePrimitive line)
                dc.DrawLine(line.Style == LineStyleKey.Header ? headerPen : chartPen, ToPoint(line.Start), ToPoint(line.End));

        foreach (var p in layout.Primitives)
            if (p is TextPrimitive text && metrics.TryGetValue(text.Style, out var m))
                DrawText(dc, text, m, scale, ToRect(text.MeasuredBox).TopLeft, pixelsPerDip);
    }

    /// <summary>
    /// Draws one text with the calibrated metrics the measurer uses (§6.1): the
    /// style's font, bold/italic, em = text size / cap height, width scaled by
    /// width factor × calibration, line pitch as the line height. A single line
    /// is fitted to its measured width so right-aligned text lands exactly.
    /// </summary>
    private static void DrawText(DrawingContext dc, TextPrimitive t, TextStyleMetrics m, double scale, Point topLeft, double pixelsPerDip)
    {
        var typeface = TypefaceFor(m);
        var caps = typeface.CapsHeight > 0 ? typeface.CapsHeight : 0.7;
        var em = m.TextSizeFt / caps * scale;
        if (!(em > 0.05) || em > 30000) return;

        var text = new FormattedText(
            t.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, em, Ink,
            null, TextFormattingMode.Ideal, pixelsPerDip);

        var lineHeight = m.LinePitchFt * scale;
        if (lineHeight > 0.01 && lineHeight < 30000) text.LineHeight = lineHeight;

        var widthScale = m.WidthFactor * m.WidthCalibration;
        if (!(widthScale > 0)) widthScale = 1;

        if (t.WrapWidth is { } wrap && wrap > 0)
        {
            text.MaxTextWidth = wrap * scale / widthScale;
        }
        else
        {
            var natural = text.Width;
            var measured = t.MeasuredBox.Width * scale;
            if (natural > 0 && measured > 0) widthScale = measured / natural;
        }

        dc.PushTransform(new MatrixTransform(widthScale, 0, 0, 1, topLeft.X, topLeft.Y));
        dc.DrawText(text, new Point(0, 0));
        dc.Pop();
    }

    private static Typeface TypefaceFor(TextStyleMetrics m)
    {
        var key = (m.FontName, m.Bold, m.Italic);
        if (!Typefaces.TryGetValue(key, out var typeface))
        {
            typeface = new Typeface(
                new FontFamily(m.FontName),
                m.Italic ? FontStyles.Italic : FontStyles.Normal,
                m.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            Typefaces[key] = typeface;
        }
        return typeface;
    }

    private static Brush Fill(Rgb color)
    {
        if (!Fills.TryGetValue(color, out var brush))
        {
            brush = Freeze(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)));
            Fills[color] = brush;
        }
        return brush;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
