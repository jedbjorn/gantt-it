using Autodesk.Revit.DB;
using GanttIt.Core.Model;
using RevitColor = Autodesk.Revit.DB.Color;

namespace GanttIt.Revit.Services;

/// <summary>
/// The Gantt-It filled region types (§7.3): one solid-fill type per color, named
/// <c>Gantt-It Fill #RRGGBB</c>, shared by every chart. A type is duplicated from
/// any existing filled region type and all four fill fields are set explicitly,
/// because the base type may be a masking hatch (V4).
/// </summary>
internal sealed class FillTypes
{
    public const string Prefix = "Gantt-It Fill ";

    private readonly Document _doc;
    private readonly Dictionary<string, FilledRegionType> _byName;
    private ElementId? _solid;
    private ElementId? _invisible;

    public FillTypes(Document doc)
    {
        _doc = doc;
        _byName = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Id.Value).First(), StringComparer.Ordinal);
    }

    /// <summary>The type name for a color, e.g. <c>Gantt-It Fill #4F8EF7</c>.</summary>
    public static string NameFor(Rgb color) => Prefix + color.ToHex();

    /// <summary>True for a type name Gantt-It owns.</summary>
    public static bool IsGanttItName(string? name) =>
        name is not null && name.StartsWith(Prefix, StringComparison.Ordinal)
        && Rgb.TryParse(name[Prefix.Length..], out _) && name.Length == Prefix.Length + 7;

    /// <summary><c>&lt;Invisible lines&gt;</c>, found among the filled-region boundary styles by category (V4).</summary>
    public ElementId InvisibleLines => _invisible ??= FindInvisibleLines(_doc);

    /// <summary>
    /// The fill type for <paramref name="color"/>: reused when it exists (and reset
    /// if it was edited by hand), otherwise created. Must run inside a transaction.
    /// </summary>
    public ElementId Get(Rgb color)
    {
        var name = NameFor(color);
        if (_byName.TryGetValue(name, out var existing))
        {
            Apply(existing, color);
            return existing.Id;
        }

        var baseType = _byName.Values.OrderBy(t => IsGanttItName(t.Name) ? 1 : 0).ThenBy(t => t.Id.Value).FirstOrDefault()
                       ?? throw new InvalidOperationException("The model has no filled region type to duplicate.");
        var created = (FilledRegionType)baseType.Duplicate(name);
        Apply(created, color);
        _byName[name] = created;
        return created.Id;
    }

    private void Apply(FilledRegionType type, Rgb color)
    {
        _solid ??= FindSolidDrafting(_doc);
        if (type.ForegroundPatternId != _solid) type.ForegroundPatternId = _solid;
        var c = type.ForegroundPatternColor;
        if (c is null || !c.IsValid || c.Red != color.R || c.Green != color.G || c.Blue != color.B)
            type.ForegroundPatternColor = new RevitColor(color.R, color.G, color.B);
        if (type.BackgroundPatternId != ElementId.InvalidElementId) type.BackgroundPatternId = ElementId.InvalidElementId;
        if (type.IsMasking) type.IsMasking = false;
    }

    /// <summary>The solid drafting fill pattern, found by its definition (locale-independent).</summary>
    public static ElementId FindSolidDrafting(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .Where(f => f.GetFillPattern() is { IsSolidFill: true, Target: FillPatternTarget.Drafting })
            .OrderBy(f => f.Id.Value)
            .Select(f => f.Id)
            .FirstOrDefault()
        ?? throw new InvalidOperationException("The model has no solid drafting fill pattern.");

    /// <summary>
    /// <c>&lt;Invisible lines&gt;</c>. It is not a Lines subcategory and
    /// <c>Category.GetCategory(OST_InvisibleLines)</c> returns null, so it is taken from
    /// <see cref="FilledRegion.GetValidLineStyleIdsForFilledRegion"/> by category (V4).
    /// </summary>
    public static ElementId FindInvisibleLines(Document doc) =>
        FilledRegion.GetValidLineStyleIdsForFilledRegion(doc)
            .Select(id => doc.GetElement(id) as GraphicsStyle)
            .Where(g => g?.GraphicsStyleCategory?.BuiltInCategory == BuiltInCategory.OST_InvisibleLines)
            .Select(g => g!.Id)
            .FirstOrDefault()
        ?? throw new InvalidOperationException("The <Invisible lines> style was not found.");

    /// <summary>
    /// Deletes Gantt-It fill types that no filled region uses (§7.3). Types owned by
    /// another user in a workshared model are left alone. Must run inside a transaction.
    /// </summary>
    public static List<string> DeleteUnused(Document doc)
    {
        var used = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegion))
            .Select(e => e.GetTypeId())
            .ToHashSet();
        var deleted = new List<string>();
        foreach (var type in new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList())
        {
            if (!IsGanttItName(type.Name) || used.Contains(type.Id)) continue;
            if (Worksharing.OwnedByOther(doc, type.Id, out _)) continue;
            var name = type.Name;
            doc.Delete(type.Id);
            deleted.Add(name);
        }
        return deleted;
    }
}
