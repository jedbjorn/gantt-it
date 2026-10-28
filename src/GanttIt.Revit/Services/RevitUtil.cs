using System.Reflection;
using Autodesk.Revit.DB;

namespace GanttIt.Revit.Services;

/// <summary>Small Revit helpers shared by the backend services.</summary>
internal static class RevitUtil
{
    /// <summary>The add-in's informational version, stored as <c>AppVersion</c> in every entity.</summary>
    public static string AppVersion { get; } =
        typeof(RevitUtil).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(RevitUtil).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>Every legend view that is not a template, in ascending ElementId order.</summary>
    public static List<View> Legends(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate)
            .OrderBy(v => v.Id.Value)
            .ToList();

    /// <summary>"Number - Name" of every sheet the view is placed on (viewport collector, V8).</summary>
    public static List<string> SheetsShowing(Document doc, ElementId viewId) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Where(vp => vp.ViewId == viewId)
            .Select(vp => doc.GetElement(vp.SheetId) as ViewSheet)
            .Where(s => s is not null)
            .Select(s => $"{s!.SheetNumber} - {s.Name}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Names of every view except <paramref name="exceptViewId"/> and sheets (whose
    /// names are not view names), for ChartValidator's collision check. Templates
    /// are included: being stricter than Revit never lets a colliding name through.
    /// </summary>
    public static List<string> OtherViewNames(Document doc, ElementId? exceptViewId) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v is not ViewSheet
                        && (exceptViewId is null || v.Id != exceptViewId)
                        && v.ViewType is not (ViewType.ProjectBrowser or ViewType.SystemBrowser or ViewType.Internal or ViewType.Undefined))
            .Select(v => v.Name)
            .ToList();

    /// <summary>A view that accepts text notes and detail curves, for rolled-back probes; null when none exists.</summary>
    public static View? ProbeView(Document doc)
    {
        var legend = Legends(doc).FirstOrDefault();
        if (legend is not null) return legend;
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewDrafting))
            .Cast<View>()
            .Where(v => !v.IsTemplate)
            .OrderBy(v => v.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>Creates a throwaway drafting view; only ever called inside a transaction that is rolled back.</summary>
    public static View CreateProbeDraftingView(Document doc)
    {
        var type = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(t => t.ViewFamily == ViewFamily.Drafting);
        return ViewDrafting.Create(doc, type.Id);
    }

    /// <summary>
    /// Runs <paramref name="body"/> in a transaction (or a sub-transaction when one is
    /// already open) and always rolls it back. Used for measurements that must not
    /// leave anything in the model or the Undo list.
    /// </summary>
    public static T RolledBack<T>(Document doc, string name, Func<T> body)
    {
        if (doc.IsModifiable)
        {
            using var sub = new SubTransaction(doc);
            sub.Start();
            try
            {
                return body();
            }
            finally
            {
                if (sub.HasStarted() && !sub.HasEnded()) sub.RollBack();
            }
        }

        using var t = new Transaction(doc, name);
        var options = t.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new GanttItFailures());
        options.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(options);
        t.Start();
        try
        {
            return body();
        }
        finally
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
        }
    }
}
