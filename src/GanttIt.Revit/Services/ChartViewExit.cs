using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GanttIt.Core.Views;

namespace GanttIt.Revit.Services;

/// <summary>
/// Gets the chart's view out of the way of its own deletion, and puts things back
/// when the delete doesn't happen.
/// <para>
/// Revit refuses to delete the active view ("ElementId cannot be deleted"), which is
/// exactly what a chart is after "Open view". <see cref="Leave"/> switches to another
/// view first: another open window if there is one, otherwise a floor plan, sheet,
/// drafting view or any other printable view (<see cref="ViewFallback"/>), trying the
/// next whenever Revit won't activate one. Windows merely showing the chart are left
/// open: deleting a view closes them itself. If a delete still fails while the chart
/// is open in a window, <see cref="CloseChartWindows"/> closes every such window for
/// one retry.
/// </para>
/// <para>
/// <see cref="Restore"/> undoes all of it after anything but a successful delete:
/// the chart is activated again when it was the active view (or a window of it was
/// closed), and the originally active view comes back on top.
/// </para>
/// </summary>
internal sealed class ChartViewExit
{
    private readonly UIDocument? _uidoc;
    private readonly ElementId _chartId;
    private readonly ElementId? _originalActiveId;
    private bool _switched;
    private bool _closedWindows;

    public ChartViewExit(UIDocument? uidoc, ElementId chartId)
    {
        _uidoc = uidoc;
        _chartId = chartId;
        _originalActiveId = Safe(() => uidoc?.ActiveView?.Id);
    }

    public const string NoTargetMessage =
        "The chart is Revit's active view and Gantt-It found no other view Revit would switch to. "
        + "Nothing was changed. Open another view, then delete the chart.";

    /// <summary>
    /// Makes sure the chart isn't the active view. Null when done (or nothing was
    /// needed); otherwise a message, with the original view still active.
    /// </summary>
    public string? Leave()
    {
        if (_uidoc is null || Safe(() => _uidoc.ActiveView?.Id) != _chartId) return null;
        var doc = _uidoc.Document;

        var open = Safe(() => _uidoc.GetOpenUIViews().Select(v => v.ViewId).ToList()) ?? [];
        var all = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var byId = all.ToDictionary(v => v.Id.Value);
        var openCandidates = open.Where(id => byId.ContainsKey(id.Value)).Select(id => Candidate(byId[id.Value]));
        var order = ViewFallback.Order(_chartId.Value, openCandidates, all.Select(Candidate));

        foreach (var id in order)
        {
            if (TryActivate(byId[id]))
            {
                _switched = true;
                return null;
            }
        }

        // Every candidate failed: make sure the user is still on the chart.
        if (Safe(() => _uidoc.ActiveView?.Id) != _chartId) TryActivate(doc.GetElement(_chartId) as View);
        return NoTargetMessage;
    }

    /// <summary>True when a window other than the active one still shows the chart.</summary>
    public bool ChartOpenInWindow =>
        _uidoc is not null && (Safe(() => _uidoc.GetOpenUIViews().Any(v => v.ViewId == _chartId)) ?? false);

    /// <summary>
    /// Closes every window showing the chart (tiled or replicated views), each in its own
    /// try. False when one couldn't be closed; <see cref="Restore"/> then reopens the chart.
    /// </summary>
    public bool CloseChartWindows()
    {
        if (_uidoc is null) return true;
        var ok = true;
        var windows = Safe(() => _uidoc.GetOpenUIViews().Where(v => v.ViewId == _chartId).ToList()) ?? [];
        foreach (var window in windows)
        {
            try
            {
                window.Close();
                _closedWindows = true;
            }
            catch (Exception)
            {
                ok = false;
            }
        }
        return ok;
    }

    /// <summary>
    /// After a delete that didn't succeed: back to the chart if the user was on it (or a
    /// window of it was closed), then the originally active view. Never throws.
    /// Returns a sentence for the result message when Revit wouldn't switch back.
    /// </summary>
    public string? Restore()
    {
        if (_uidoc is null || (!_switched && !_closedWindows)) return null;
        var doc = _uidoc.Document;
        var ok = doc.GetElement(_chartId) is View chart && TryActivate(chart);
        if (_originalActiveId is not null && _originalActiveId != _chartId && doc.GetElement(_originalActiveId) is View original)
            ok = TryActivate(original) && ok;
        _switched = false;
        _closedWindows = false;
        return ok ? null : "Revit did not switch back to the view you were on; open it again from the Project Browser.";
    }

    private bool TryActivate(View? view)
    {
        if (_uidoc is null || view is null) return false;
        try
        {
            _uidoc.ActiveView = view;
            return _uidoc.ActiveView?.Id == view.Id;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static ViewCandidate Candidate(View view) =>
        new(view.Id.Value, KindOf(view), Safe(() => view.IsTemplate) ?? true, Safe(() => view.CanBePrinted) ?? false);

    /// <summary>Revit view type → fallback rank. Revision and keynote schedules on title blocks are never targets.</summary>
    internal static ViewKind KindOf(View view)
    {
        if (view is ViewSchedule schedule
            && (Safe(() => schedule.IsTitleblockRevisionSchedule || schedule.IsInternalKeynoteSchedule) ?? true))
            return ViewKind.Unusable;
        return view.ViewType switch
        {
            ViewType.FloorPlan => ViewKind.FloorPlan,
            ViewType.DrawingSheet => ViewKind.Sheet,
            ViewType.DraftingView => ViewKind.Drafting,
            ViewType.Legend => ViewKind.Legend,
            ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal or ViewType.Undefined => ViewKind.Unusable,
            _ => ViewKind.Other,
        };
    }

    private static T? Safe<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool? Safe(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
