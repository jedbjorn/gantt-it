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

        try
        {
            var backend = new RevitBackend(uiapp);
            var session = new GanttItSession(backend, metrics => new WpfTextMeasurer(metrics), new DocumentDrafts(document));
            var window = new GanttItWindow(session);
            _ = new WindowInteropHelper(window) { Owner = uiapp.MainWindowHandle };
            window.ShowDialog();

            if (window.ViewToOpen is { } viewId && document.GetElement(viewId) is View view)
                uidoc.ActiveView = view;
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            // Revit reports a failed command with this message.
            message = $"Gantt-It could not open: {ex.Message}";
            return Result.Failed;
        }
    }
}
