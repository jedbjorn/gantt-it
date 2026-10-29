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
    /// Deletes the Gantt-It fill types nothing uses (§7.3). <see cref="GanttItTransaction.Run"/>
    /// calls it at the end of every Gantt-It action, inside the action's transaction,
    /// so it is part of the same Undo step and rolls back with it. Must run inside a
    /// transaction.
    /// <para>
    /// Deleting a <see cref="FilledRegionType"/> deletes every element of that type,
    /// so a type is deleted only when its name is a Gantt-It name and nothing
    /// references it. It is kept when:
    /// </para>
    /// <list type="bullet">
    /// <item>any <see cref="FilledRegion"/> in the document uses it: a chart, a detached
    /// copy (decision #17), or the user's own region, also inside a detail group or a
    /// design option (a document-wide collector returns those);</item>
    /// <item>Revit reports any element that would be deleted with it
    /// (<see cref="Element.GetDependentElements"/>), which also covers references the
    /// collector can't see, such as the members of a group type with no placed
    /// instance;</item>
    /// <item>another user owns it, or it is out of date with central, in a workshared
    /// model (editing it would fail the whole action);</item>
    /// <item>it is the model's last filled region type;</item>
    /// <item>checking or deleting it throws: each type is tried in its own
    /// sub-transaction, so one refusal keeps that type and never fails the action.</item>
    /// </list>
    /// Being the Filled Region tool's default type is not a use: Revit allows deleting
    /// the default type (the Self-Test records what the default becomes).
    /// </summary>
    public static FillPurge DeleteUnused(Document doc)
    {
        var used = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegion))
            .Select(e => e.GetTypeId())
            .ToHashSet();
        var all = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var remaining = all.Count;
        var purge = new FillPurge();
        foreach (var type in all)
        {
            var name = type.Name;
            if (!IsGanttItName(name) || used.Contains(type.Id)) continue;

            using var sub = new SubTransaction(doc);
            try
            {
                var reason = KeepReason(doc, type, remaining);
                if (reason is not null)
                {
                    purge.Kept.Add((name, reason));
                    continue;
                }
                sub.Start();
                doc.Delete(type.Id);
                sub.Commit();
                remaining--;
                purge.Deleted.Add(name);
            }
            catch (Exception ex)
            {
                if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack();
                purge.Kept.Add((name, $"Revit refused: {ex.Message}"));
            }
        }
#if DEBUG
        LastPurge = purge;
#endif
        return purge;
    }

    /// <summary>Why an unused Gantt-It type must stay, or null when it can be deleted.</summary>
    private static string? KeepReason(Document doc, FilledRegionType type, int remaining)
    {
        if (remaining <= 1) return "the model's last filled region type";
        if (Worksharing.OwnedByOther(doc, type.Id, out var owner)) return $"owned by {(owner.Length > 0 ? owner : "another user")}";
        if (Worksharing.OutOfDate(doc, type.Id)) return "out of date with central";
        var dependents = type.GetDependentElements(null).Where(id => id != type.Id).ToList();
        if (dependents.Count > 0)
            return $"deleting it would also delete {dependents.Count} element{(dependents.Count == 1 ? "" : "s")}: "
                   + string.Join(", ", dependents.Take(5).Select(id => Describe(doc, id)))
                   + (dependents.Count > 5 ? ", …" : "");
        return null;
    }

    /// <summary>An element's id, class and category, for a kept type's reason (VM diagnosis).</summary>
    private static string Describe(Document doc, ElementId id)
    {
        var e = doc.GetElement(id);
        return e is null ? $"{id.Value} (not found)" : $"{id.Value} {e.GetType().Name}/{e.Category?.Name ?? "no category"}";
    }

#if DEBUG
    /// <summary>The last purge, for the Self-Test's checks.</summary>
    internal static FillPurge? LastPurge { get; private set; }
#endif
}

/// <summary>What one fill-type purge deleted, and which unused Gantt-It types it kept and why.</summary>
internal sealed class FillPurge
{
    public List<string> Deleted { get; } = [];

    public List<(string Name, string Reason)> Kept { get; } = [];
}
