using Autodesk.Revit.DB;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;

namespace GanttIt.Revit.Services;

/// <summary>The element ids a chart is drawn with, resolved from <see cref="ProjectSettings"/>.</summary>
internal sealed record ResolvedStyles(
    ElementId TitleText,
    ElementId HeaderText,
    ElementId BodyText,
    ElementId HeaderLine,
    ElementId ChartLine)
{
    public ElementId Text(TextStyleKey key) => key switch
    {
        TextStyleKey.Title => TitleText,
        TextStyleKey.Header => HeaderText,
        _ => BodyText,
    };

    public ElementId Line(LineStyleKey key) => key == LineStyleKey.Header ? HeaderLine : ChartLine;

    public IReadOnlyDictionary<TextStyleKey, ElementId> TextTypes => new Dictionary<TextStyleKey, ElementId>
    {
        [TextStyleKey.Title] = TitleText,
        [TextStyleKey.Header] = HeaderText,
        [TextStyleKey.Body] = BodyText,
    };
}

/// <summary>
/// Reads the model's text types and line styles (§2.3). References resolve by
/// UniqueId first and fall back to the exact stored name (the name read from
/// this or another model, never a hard-coded one). Built-in line styles are found
/// by their <see cref="BuiltInCategory"/>: their names carry angle brackets that
/// differ between versions and locales (V5).
/// </summary>
internal static class StyleReader
{
    public static IReadOnlyList<StyleOption> TextStyles(Document doc) =>
        TextTypes(doc).Select(t => new StyleOption(t.UniqueId, t.Name)).ToList();

    public static List<TextNoteType> TextTypes(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<StyleOption> LineStyles(Document doc) =>
        LineGraphicsStyles(doc).Select(g => new StyleOption(g.UniqueId, g.Name)).ToList();

    /// <summary>
    /// The projection graphics styles of the Lines subcategories that a detail line
    /// accepts. The valid set is asked from Revit itself (a probe detail curve in a
    /// rolled-back transaction, <c>CurveElement.GetLineStyleIds</c>), which excludes
    /// styles such as room or area boundaries; if no probe is possible, every Lines
    /// subcategory is offered.
    /// </summary>
    public static List<GraphicsStyle> LineGraphicsStyles(Document doc)
    {
        var all = AllLineSubcategoryStyles(doc);
        HashSet<ElementId>? valid = null;
        try
        {
            valid = RevitUtil.RolledBack(doc, "Gantt-It: list line styles", () =>
            {
                var view = RevitUtil.ProbeView(doc) ?? RevitUtil.CreateProbeDraftingView(doc);
                var probe = doc.Create.NewDetailCurve(view, Autodesk.Revit.DB.Line.CreateBound(XYZ.Zero, new XYZ(1, 0, 0)));
                return probe.GetLineStyleIds().ToHashSet();
            });
        }
        catch (Exception)
        {
            // Read-only document or no view to probe in: offer every Lines subcategory.
        }

        return all
            .Where(g => valid is null || valid.Contains(g.Id))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<GraphicsStyle> AllLineSubcategoryStyles(Document doc)
    {
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        var result = new List<GraphicsStyle>();
        foreach (Category sub in lines.SubCategories)
        {
            var gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
            if (gs is not null) result.Add(gs);
        }
        return result;
    }

    /// <summary>A built-in line style found by category (never by name), or null.</summary>
    public static GraphicsStyle? BuiltInLineStyle(Document doc, BuiltInCategory category)
    {
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        foreach (Category sub in lines.SubCategories)
            if (sub.BuiltInCategory == category)
                return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        return null;
    }

    public static TextNoteType? ResolveText(Document doc, StyleRef? style)
    {
        if (style is null) return null;
        if (TryGet(doc, style.UniqueId) is TextNoteType byId) return byId;
        return TextTypes(doc).FirstOrDefault(t => string.Equals(t.Name, style.Name, StringComparison.Ordinal));
    }

    public static GraphicsStyle? ResolveLine(Document doc, StyleRef? style)
    {
        if (style is null) return null;
        if (TryGet(doc, style.UniqueId) is GraphicsStyle byId && IsLineStyle(byId)) return byId;
        return AllLineSubcategoryStyles(doc).FirstOrDefault(g => string.Equals(g.Name, style.Name, StringComparison.Ordinal));
    }

    private static bool IsLineStyle(GraphicsStyle gs) =>
        gs.GraphicsStyleType == GraphicsStyleType.Projection
        && gs.GraphicsStyleCategory?.Parent?.BuiltInCategory == BuiltInCategory.OST_Lines;

    private static Element? TryGet(Document doc, string? uniqueId)
    {
        if (string.IsNullOrEmpty(uniqueId)) return null;
        try
        {
            return doc.GetElement(uniqueId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The five setting fields, with the labels shown to the user (§2.3).</summary>
    public static IEnumerable<(string Label, StyleRef? Ref, bool IsText)> Fields(ProjectSettings s)
    {
        yield return ("Title text style", s.TitleTextStyle, true);
        yield return ("Header text style", s.HeaderTextStyle, true);
        yield return ("Body text style", s.BodyTextStyle, true);
        yield return ("Chart line style", s.ChartLineStyle, false);
        yield return ("Header line style", s.HeaderLineStyle, false);
    }

    /// <summary>Labels of the set fields whose style no longer resolves.</summary>
    public static List<string> Missing(Document doc, ProjectSettings settings) =>
        Fields(settings)
            .Where(f => f.Ref is not null && (f.IsText ? ResolveText(doc, f.Ref) is null : ResolveLine(doc, f.Ref) is null))
            .Select(f => $"{f.Label} \"{f.Ref!.Name}\"")
            .ToList();

    /// <summary>
    /// Resolves all five styles, or returns null with the reason: a style that is not
    /// chosen yet or no longer exists blocks Generate (§2.3).
    /// </summary>
    public static ResolvedStyles? Resolve(Document doc, ProjectSettings settings, out string? problem)
    {
        var unset = Fields(settings).Where(f => f.Ref is null).Select(f => f.Label).ToList();
        if (unset.Count > 0)
        {
            problem = "Choose a style in Settings for: " + string.Join(", ", unset) + ".";
            return null;
        }
        var missing = Missing(doc, settings);
        if (missing.Count > 0)
        {
            problem = "These styles no longer exist in the model; choose replacements in Settings: " + string.Join(", ", missing) + ".";
            return null;
        }

        problem = null;
        return new ResolvedStyles(
            ResolveText(doc, settings.TitleTextStyle)!.Id,
            ResolveText(doc, settings.HeaderTextStyle)!.Id,
            ResolveText(doc, settings.BodyTextStyle)!.Id,
            ResolveLine(doc, settings.HeaderLineStyle)!.Id,
            ResolveLine(doc, settings.ChartLineStyle)!.Id);
    }

    /// <summary>
    /// Points every reference in <paramref name="settings"/> at the element it
    /// resolves to now, so a name fallback picks up the current UniqueId.
    /// </summary>
    public static void Refresh(Document doc, ProjectSettings settings)
    {
        settings.TitleTextStyle = RefOf(ResolveText(doc, settings.TitleTextStyle)) ?? settings.TitleTextStyle;
        settings.HeaderTextStyle = RefOf(ResolveText(doc, settings.HeaderTextStyle)) ?? settings.HeaderTextStyle;
        settings.BodyTextStyle = RefOf(ResolveText(doc, settings.BodyTextStyle)) ?? settings.BodyTextStyle;
        settings.ChartLineStyle = RefOf(ResolveLine(doc, settings.ChartLineStyle)) ?? settings.ChartLineStyle;
        settings.HeaderLineStyle = RefOf(ResolveLine(doc, settings.HeaderLineStyle)) ?? settings.HeaderLineStyle;
    }

    public static StyleRef? RefOf(Element? e) => e is null ? null : new StyleRef(e.UniqueId, e.Name);

    /// <summary>Font parameters of a text type, as Revit stores them.</summary>
    public static (string Font, double SizeFt, bool Bold, bool Italic, double WidthFactor, double OffsetFt) Font(TextNoteType t) =>
        (t.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString() ?? "Arial",
         t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? Core.Model.Units.FromMillimeters(2.5),
         (t.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.AsInteger() ?? 0) != 0,
         (t.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC)?.AsInteger() ?? 0) != 0,
         t.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? 1.0,
         t.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET)?.AsDouble() ?? 0.0);
}
