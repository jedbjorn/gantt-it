using GanttIt.Core.Layout;
using GanttIt.Core.Model;

namespace GanttIt.Core.Tests.Support;

/// <summary>
/// Deterministic monospace measurer: every character of a style has the same
/// width and every line the same height. Wraps greedily at spaces when a wrap
/// width is given, breaking words longer than the wrap width. Records each batch.
/// </summary>
public sealed class FakeMeasurer : ITextMeasurer
{
    public static readonly double TitleCharWidth = Units.FromMillimeters(2.5);
    public static readonly double TitleLineHeight = Units.FromMillimeters(5);
    public static readonly double HeaderCharWidth = Units.FromMillimeters(1.8);
    public static readonly double HeaderLineHeight = Units.FromMillimeters(3.5);
    public static readonly double BodyCharWidth = Units.FromMillimeters(1.6);
    public static readonly double BodyLineHeight = Units.FromMillimeters(3);

    public List<IReadOnlyList<TextRequest>> Batches { get; } = [];

    public IReadOnlyList<TextSize> Measure(IReadOnlyList<TextRequest> requests)
    {
        Batches.Add(requests.ToList());
        return requests.Select(Size).ToList();
    }

    public static TextSize Size(TextRequest r)
    {
        var (charWidth, lineHeight) = Metrics(r.Style);
        var lines = Lines(r.Text, charWidth, r.WrapWidth);
        return new TextSize(lines.Max(l => l.Length) * charWidth, lines.Count * lineHeight);
    }

    public static (double CharWidth, double LineHeight) Metrics(TextStyleKey style) => style switch
    {
        TextStyleKey.Title => (TitleCharWidth, TitleLineHeight),
        TextStyleKey.Header => (HeaderCharWidth, HeaderLineHeight),
        _ => (BodyCharWidth, BodyLineHeight),
    };

    public static List<string> Lines(string text, double charWidth, double? wrapWidth)
    {
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (wrapWidth is null) return [.. paragraphs];

        var max = Math.Max(1, (int)Math.Floor(wrapWidth.Value / charWidth + 1e-9));
        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var w = word;
                while (w.Length > max)
                {
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    lines.Add(w[..max]);
                    w = w[max..];
                }
                var candidate = line.Length == 0 ? w : line + " " + w;
                if (candidate.Length <= max) line = candidate;
                else { lines.Add(line); line = w; }
            }
            lines.Add(line);
        }
        return lines;
    }
}
