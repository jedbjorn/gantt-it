using GanttIt.Core.Model;

namespace GanttIt.Core.Layout;

/// <summary>
/// A point in legend coordinates, in feet. The origin is the top-left corner of
/// the chart's outer border; X grows right and Y grows <b>up</b> (Revit's
/// legend convention), so every point of a chart has <c>Y ≤ 0</c>.
/// </summary>
public readonly record struct Point2(double X, double Y);

/// <summary>An axis-aligned box in legend coordinates (feet, Y-up).</summary>
public readonly record struct Box(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;

    public double Height => Top - Bottom;
}

public enum TextStyleKey
{
    Title,
    Header,
    Body,
}

public enum LineStyleKey
{
    /// <summary>Outer border and header rules.</summary>
    Header,

    /// <summary>Row separators, column dividers and timescale grid lines.</summary>
    Chart,
}

/// <summary>Horizontal alignment of a text note. Vertical alignment is always Top.</summary>
public enum TextAlign
{
    Left,
    Right,
}

/// <summary>A drawing primitive. The Revit generator and the WPF preview both draw exactly these.</summary>
public abstract record Primitive;

/// <summary>
/// A text note. <see cref="Anchor"/> is the note's insertion point for its
/// alignment with vertical alignment Top: the top-left corner of the text for
/// <see cref="TextAlign.Left"/>, the top-right corner for <see cref="TextAlign.Right"/>.
/// It sits at <c>cell.left + padding</c> (or <c>cell.right − padding</c>) and
/// <c>cell.top − padding</c>. <see cref="WrapWidth"/>, when set, is the note
/// width Revit wraps at. <see cref="MeasuredBox"/> is where the measured text
/// lands, so the Revit side can verify placement after moving the note.
/// </summary>
public sealed record TextPrimitive(
    string Text,
    TextStyleKey Style,
    TextAlign Align,
    Point2 Anchor,
    double? WrapWidth,
    Box MeasuredBox) : Primitive;

/// <summary>A straight detail line, always horizontal or vertical, endpoints on the grid.</summary>
public sealed record LinePrimitive(Point2 Start, Point2 End, LineStyleKey Style) : Primitive;

/// <summary>A solid bar for one section (a FilledRegion with invisible boundary in Revit).</summary>
public sealed record FilledRectPrimitive(Box Rect, Rgb Color, Guid SectionId) : Primitive;
