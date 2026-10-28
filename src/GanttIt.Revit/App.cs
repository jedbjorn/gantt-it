using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using GanttIt.Revit.Commands;

namespace GanttIt.Revit;

/// <summary>
/// Revit entry point: adds the "Gantt-It" panel with one button to the
/// built-in Add-Ins tab (D3).
/// </summary>
public sealed class App : IExternalApplication
{
    public const string PanelName = "Gantt-It";

    public Result OnStartup(UIControlledApplication application)
    {
        // CreateRibbonPanel without a tab name targets the built-in Add-Ins tab.
        var panel = application.CreateRibbonPanel(PanelName);
        var button = new PushButtonData(
            "GanttIt.Open",
            "Gantt-It",
            Assembly.GetExecutingAssembly().Location,
            typeof(OpenGanttIt).FullName)
        {
            ToolTip = "Create and edit Gantt charts as legend views.",
            LargeImage = LoadIcon("gantt_32.png"),
            Image = LoadIcon("gantt_16.png"),
        };
        panel.AddItem(button);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    /// <summary>Loads an icon shipped next to the DLL; a missing file leaves the button without one.</summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        var path = Path.Combine(directory, "Resources", "icons", fileName);
        if (!File.Exists(path)) return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path);
        image.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
        image.EndInit();
        image.Freeze();
        return image;
    }
}
