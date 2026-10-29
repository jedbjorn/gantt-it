namespace GanttIt.Core.Views;

/// <summary>How a Revit view ranks as somewhere to switch to before deleting a chart.</summary>
public enum ViewKind
{
    FloorPlan,
    Sheet,
    Drafting,
    /// <summary>Any other graphical view (sections, elevations, 3D, schedules, ceiling plans, …).</summary>
    Other,
    /// <summary>Legends rank last: they are often other Gantt-It charts.</summary>
    Legend,
    /// <summary>Never a target: browsers, internal and undefined view types.</summary>
    Unusable,
}

/// <summary>What the ranking needs to know about one view.</summary>
/// <param name="Id">The view's element id value.</param>
/// <param name="Kind">Its rank, from the Revit view type.</param>
/// <param name="IsTemplate">View templates are never activated.</param>
/// <param name="IsPrintable">Revit's <c>View.CanBePrinted</c>: false for browsers and other system views.</param>
public sealed record ViewCandidate(long Id, ViewKind Kind, bool IsTemplate, bool IsPrintable);

/// <summary>
/// Picks the views to try activating when the chart being deleted is Revit's
/// active view (Revit refuses to delete the active view). Pure, so the order is
/// unit-tested; the Revit side tries each id in turn until one activates.
/// </summary>
public static class ViewFallback
{
    /// <summary>
    /// The views to try, best first: the other views already open in a window
    /// (in the given order), then every other usable view: floor plans, sheets,
    /// drafting views, any other printable view, legends; by id within a kind.
    /// Never the chart itself, a view template, a non-printable view or an
    /// <see cref="ViewKind.Unusable"/> one, and no id twice.
    /// </summary>
    /// <param name="chartId">The chart's own view (every window showing it is skipped).</param>
    /// <param name="openViews">Views open in a window, in window order; may include the chart.</param>
    /// <param name="allViews">Every view in the document.</param>
    public static IReadOnlyList<long> Order(long chartId, IEnumerable<ViewCandidate> openViews, IEnumerable<ViewCandidate> allViews)
    {
        var seen = new HashSet<long> { chartId };
        var result = new List<long>();
        foreach (var v in openViews)
            if (IsUsable(v) && seen.Add(v.Id)) result.Add(v.Id);
        foreach (var v in allViews.Where(IsUsable).OrderBy(v => v.Kind).ThenBy(v => v.Id))
            if (seen.Add(v.Id)) result.Add(v.Id);
        return result;
    }

    public static bool IsUsable(ViewCandidate view) =>
        view.Kind != ViewKind.Unusable && !view.IsTemplate && view.IsPrintable;
}
