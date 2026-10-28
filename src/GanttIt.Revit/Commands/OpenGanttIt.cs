using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace GanttIt.Revit.Commands;

/// <summary>
/// The ribbon button. For now it confirms the add-in loaded (Phase 0, V2);
/// the modal editor window replaces this placeholder in a later delivery.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenGanttIt : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var version = typeof(OpenGanttIt).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var revit = commandData.Application.Application;

        var dialog = new TaskDialog("Gantt-It")
        {
            MainInstruction = "Gantt-It is loaded",
            MainContent = $"Version {version} on Revit {revit.VersionNumber} ({revit.VersionBuild}).\n\n" +
                          "The chart editor is not available yet.",
        };
        dialog.Show();
        return Result.Succeeded;
    }
}
