using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using GanttIt.Revit.Resources;

namespace GanttIt.Revit.Services;

/// <summary>
/// Worksharing ownership checks (§7.5). Editing needs the chart's legend view, the
/// elements Gantt-It drew in it and the settings DataStorage to be editable. When
/// another user owns one of them, the action is refused before any transaction
/// starts, naming the owner. v1 does no borrowing beyond what Revit does implicitly.
/// </summary>
internal static class Worksharing
{
    /// <summary>True when <paramref name="id"/> is checked out by another user in a workshared model.</summary>
    public static bool OwnedByOther(Document doc, ElementId id, out string owner)
    {
        owner = "";
        if (!doc.IsWorkshared || id == ElementId.InvalidElementId) return false;
        var status = WorksharingUtils.GetCheckoutStatus(doc, id, out owner);
        return status == CheckoutStatus.OwnedByOtherUser;
    }

    /// <summary>
    /// True when <paramref name="id"/> was changed or deleted in central since this
    /// local copy last synchronized: editing it would fail until Reload Latest.
    /// </summary>
    public static bool OutOfDate(Document doc, ElementId id)
    {
        if (!doc.IsWorkshared || id == ElementId.InvalidElementId) return false;
        var status = WorksharingUtils.GetModelUpdatesStatus(doc, id);
        return status is ModelUpdatesStatus.UpdatedInCentral or ModelUpdatesStatus.DeletedInCentral;
    }

    /// <summary>Null when every element is editable; otherwise a message naming each owner.</summary>
    public static string? CheckEditable(Document doc, IEnumerable<ElementId> ids)
    {
        if (!doc.IsWorkshared) return null;

        var owners = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids.Distinct())
        {
            if (!OwnedByOther(doc, id, out var owner)) continue;
            if (!owners.TryGetValue(owner, out var what)) owners[owner] = what = [];
            if (what.Count < 3) what.Add(Describe(doc, id));
            else if (what.Count == 3) what.Add("…");
        }
        if (owners.Count == 0) return null;

        var parts = owners.Select(o => $"{(string.IsNullOrEmpty(o.Key) ? Strings.Ws_AnotherUser : o.Key)} ({string.Join(", ", o.Value)})");
        return Strings.Ws_Owned(string.Join("; ", parts));
    }

    private static string Describe(Document doc, ElementId id)
    {
        var e = doc.GetElement(id);
        return e switch
        {
            View v => Strings.Ws_View(v.Name),
            DataStorage => Strings.Ws_Settings,
            null => Strings.Ws_Element(id.Value),
            _ => $"{e.Category?.Name ?? e.GetType().Name} {id.Value}",
        };
    }
}
