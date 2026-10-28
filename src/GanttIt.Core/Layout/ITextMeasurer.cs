namespace GanttIt.Core.Layout;

/// <summary>
/// One string to measure. Records compare by value, so identical requests are
/// measured once. <see cref="WrapWidth"/> is the note width to wrap at (feet),
/// or null for an unwrapped note.
/// </summary>
public sealed record TextRequest(string Text, TextStyleKey Style, double? WrapWidth = null);

/// <summary>Measured extents of a text note on paper, in feet.</summary>
public readonly record struct TextSize(double Width, double Height);

/// <summary>
/// Measures text the way the target renders it (§6.1). Implemented by
/// <c>RevitTextMeasurer</c> (real TextNotes) and <c>WpfTextMeasurer</c> (preview).
/// </summary>
public interface ITextMeasurer
{
    /// <summary>
    /// Measures every request and returns the sizes in the same order. Batched
    /// so the Revit measurer can create all notes and call <c>Regenerate</c>
    /// once. The layout engine calls this at most three times per layout (all
    /// texts, then wrapped names, then a wrapped title) and never repeats a
    /// request it has already measured.
    /// </summary>
    IReadOnlyList<TextSize> Measure(IReadOnlyList<TextRequest> requests);
}
