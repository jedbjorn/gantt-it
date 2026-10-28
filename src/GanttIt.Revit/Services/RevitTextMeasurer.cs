using Autodesk.Revit.DB;
using GanttIt.Core.Layout;

namespace GanttIt.Revit.Services;

/// <summary>
/// Measures text with Revit's own text engine (§6.1, V9). Inside the generate
/// transaction it creates one TextNote per request at the origin, regenerates once
/// per batch and reads <see cref="TextElement.Width"/> / <see cref="TextElement.Height"/>,
/// which are the text area in paper units (the bounding box adds the type's
/// Leader/Border Offset on every side).
/// <para>
/// Notes are kept keyed by request, as the <see cref="ITextMeasurer"/> contract
/// requires: <see cref="Place"/> moves a measured note into place by setting
/// <see cref="TextNote.Coord"/> (moving is exact, V9), creates another note when the
/// same text is drawn again, and <see cref="DeleteUnused"/> removes label
/// candidates that were never drawn.
/// </para>
/// </summary>
internal sealed class RevitTextMeasurer : ITextMeasurer
{
    private readonly Document _doc;
    private readonly ElementId _viewId;
    private readonly IReadOnlyDictionary<TextStyleKey, ElementId> _types;
    private readonly Dictionary<TextRequest, Entry> _notes = [];
    private readonly Dictionary<ElementId, (double Min, double Max)> _widthLimits = [];

    public RevitTextMeasurer(Document doc, View view, IReadOnlyDictionary<TextStyleKey, ElementId> textTypes)
    {
        _doc = doc;
        _viewId = view.Id;
        _types = textTypes;
    }

    /// <summary>Number of Revit TextNotes created so far (measured plus copies).</summary>
    public int NotesCreated { get; private set; }

    public IReadOnlyList<TextSize> Measure(IReadOnlyList<TextRequest> requests)
    {
        var fresh = new List<Entry>();
        foreach (var request in requests)
        {
            if (_notes.ContainsKey(request)) continue;
            var entry = new Entry(Create(request, XYZ.Zero, HorizontalTextAlignment.Left));
            _notes[request] = entry;
            fresh.Add(entry);
        }

        if (fresh.Count > 0) _doc.Regenerate();
        foreach (var entry in fresh) entry.Size = new TextSize(entry.Note.Width, entry.Note.Height);

        return requests.Select(r => _notes[r].Size).ToList();
    }

    /// <summary>
    /// Puts a note for <paramref name="text"/> at its anchor: the measured note the
    /// first time, a new identical note for every further use.
    /// </summary>
    public TextNote Place(TextPrimitive text)
    {
        var request = new TextRequest(text.Text, text.Style, text.WrapWidth);
        var align = text.Align == TextAlign.Right ? HorizontalTextAlignment.Right : HorizontalTextAlignment.Left;
        var anchor = new XYZ(text.Anchor.X, text.Anchor.Y, 0);

        if (_notes.TryGetValue(request, out var entry) && !entry.Used)
        {
            entry.Used = true;
            if (entry.Note.HorizontalAlignment != align) entry.Note.HorizontalAlignment = align;
            entry.Note.Coord = anchor;
            return entry.Note;
        }
        return Create(request, anchor, align);
    }

    /// <summary>Deletes the measured notes that were never placed (unused label candidates).</summary>
    public int DeleteUnused()
    {
        var unused = _notes.Values.Where(e => !e.Used).Select(e => e.Note.Id).ToList();
        if (unused.Count > 0) _doc.Delete(unused);
        foreach (var key in _notes.Where(kv => !kv.Value.Used).Select(kv => kv.Key).ToList()) _notes.Remove(key);
        return unused.Count;
    }

    private TextNote Create(TextRequest request, XYZ at, HorizontalTextAlignment align)
    {
        var typeId = _types[request.Style];
        var options = new TextNoteOptions(typeId)
        {
            HorizontalAlignment = align,
            VerticalAlignment = VerticalTextAlignment.Top,
        };
        var text = RevitText(request.Text);
        NotesCreated++;
        if (request.WrapWidth is double width)
            return TextNote.Create(_doc, _viewId, at, ClampWidth(typeId, width), text, options);
        return TextNote.Create(_doc, _viewId, at, text, options);
    }

    /// <summary>A note width Revit accepts (0.3048 mm … 2133.6 mm, V9).</summary>
    private double ClampWidth(ElementId typeId, double width)
    {
        if (!_widthLimits.TryGetValue(typeId, out var limits))
        {
            limits = (TextElement.GetMinimumAllowedWidth(_doc, typeId), TextElement.GetMaximumAllowedWidth(_doc, typeId));
            _widthLimits[typeId] = limits;
        }
        return Math.Clamp(width, limits.Min, limits.Max);
    }

    /// <summary>Revit separates lines with a carriage return.</summary>
    public static string RevitText(string text) => text.Replace("\r\n", "\r").Replace('\n', '\r');

    private sealed class Entry(TextNote note)
    {
        public TextNote Note { get; } = note;

        public TextSize Size { get; set; }

        public bool Used { get; set; }
    }
}
