using Autodesk.Revit.DB;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Revit.UI;

namespace GanttIt.Revit.Services;

/// <summary>
/// Per-type calibration of the preview measurer (decision #14, V9): one reference
/// note per text type is measured in Revit inside a rolled-back transaction. The
/// width factor k = Revit width / (WPF width × width factor), and the note's
/// single-line height is Revit's fixed line pitch for that type.
/// </summary>
internal static class TextCalibration
{
    /// <summary>The Phase 0 reference string: max 0.437 mm error on realistic strings.</summary>
    public const string ReferenceText = "Permitting & Approvals";

    public static IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> Calibrate(Document doc, ProjectSettings settings)
    {
        var types = new Dictionary<TextStyleKey, TextNoteType>();
        foreach (var (key, style) in new[]
                 {
                     (TextStyleKey.Title, settings.TitleTextStyle),
                     (TextStyleKey.Header, settings.HeaderTextStyle),
                     (TextStyleKey.Body, settings.BodyTextStyle),
                 })
        {
            if (StyleReader.ResolveText(doc, style) is { } type) types[key] = type;
        }

        var measured = MeasureReference(doc, types.Values.DistinctBy(t => t.Id).ToList());

        var result = new Dictionary<TextStyleKey, TextStyleMetrics>();
        foreach (var (key, type) in types)
        {
            var (font, size, bold, italic, wf, _) = StyleReader.Font(type);
            var raw = WpfTextMeasurer.RawWidthFt(ReferenceText, font, size, bold, italic);
            double k = 1.0, pitch = WpfTextMeasurer.SingleLineHeightFt(font, size, bold, italic);
            if (measured.TryGetValue(type.Id, out var revit))
            {
                if (raw > 0 && wf > 0) k = revit.Width / (raw * wf);
                if (revit.Height > 0) pitch = revit.Height;
            }
            result[key] = new TextStyleMetrics(font, size, bold, italic, wf, k, pitch);
        }
        return result;
    }

    /// <summary>Revit's width and height of the reference string per type; empty when no probe was possible.</summary>
    private static Dictionary<ElementId, (double Width, double Height)> MeasureReference(Document doc, List<TextNoteType> types)
    {
        if (types.Count == 0) return [];
        try
        {
            return RevitUtil.RolledBack(doc, "Gantt-It: calibrate text", () =>
            {
                var view = RevitUtil.ProbeView(doc) ?? RevitUtil.CreateProbeDraftingView(doc);
                var notes = types.ToDictionary(
                    t => t.Id,
                    t => TextNote.Create(doc, view.Id, XYZ.Zero, ReferenceText, new TextNoteOptions(t.Id)
                    {
                        HorizontalAlignment = HorizontalTextAlignment.Left,
                        VerticalAlignment = VerticalTextAlignment.Top,
                    }));
                doc.Regenerate();
                return notes.ToDictionary(kv => kv.Key, kv => (kv.Value.Width, kv.Value.Height));
            });
        }
        catch (Exception)
        {
            return [];
        }
    }
}
