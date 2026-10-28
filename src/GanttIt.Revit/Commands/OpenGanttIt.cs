using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI;

namespace GanttIt.Revit.Commands;

/// <summary>
/// The ribbon button (D3): opens the modal Gantt-It window owned by Revit's
/// main window (D7). Generate, Update and Delete run from the window's button
/// handlers inside this command's API context, so no ExternalEvent is needed.
/// After the window closes, "Open view" activates the chosen legend (§2.1).
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenGanttIt : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiapp = commandData.Application;
        var uidoc = uiapp.ActiveUIDocument;
        if (uidoc?.Document is not { } document || document.IsFamilyDocument)
        {
            TaskDialog.Show("Gantt-It", "Open a project model to create Gantt charts. Charts are legend views, which families don't have.");
            return Result.Cancelled;
        }

        GanttItWindow window;
        try
        {
            var backend = new RevitBackend(uiapp);
            var session = new GanttItSession(backend, metrics => new WpfTextMeasurer(metrics), new DocumentDrafts(document));
            window = new GanttItWindow(session);
            _ = new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
        }
        catch (Exception ex)
        {
            // Nothing has run yet, so Failed can't undo any work. Revit reports this message.
            message = $"Gantt-It could not open: {ex.Message}";
            return Result.Failed;
        }

        // From here on the user may have committed Generate/Update/Delete transactions.
        // Revit undoes a command's changes when it returns Failed or Cancelled, so every
        // path below returns Succeeded.
        try
        {
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Gantt-It", $"Gantt-It closed because of an unexpected error:\n\n{ex.Message}");
            return Result.Succeeded;
        }

        if (window.ViewToOpen is { } viewId) OpenView(uidoc, document, viewId);
        return Result.Succeeded;
    }

    /// <summary>"Open view" (§2.1): activates the chosen legend; a failure is reported, never fatal.</summary>
    private static void OpenView(UIDocument uidoc, Document document, ElementId viewId)
    {
        try
        {
            if (document.GetElement(viewId) is View view) uidoc.ActiveView = view;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("Gantt-It", $"The legend could not be opened: {ex.Message}\n\nOpen it from the Project Browser instead.");
        }
    }
}
