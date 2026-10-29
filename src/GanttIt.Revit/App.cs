using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using GanttIt.Core.Theming;
using GanttIt.Revit.Commands;
using GanttIt.Revit.UI.Theming;

namespace GanttIt.Revit;

/// <summary>
/// Revit entry point: adds the "Gantt-It" panel with one button to the
/// built-in Add-Ins tab (D3).
/// </summary>
public sealed class App : IExternalApplication
{
    public const string PanelName = "Gantt-It";

    private PushButton? _button;

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
        };
        _button = panel.AddItem(button) as PushButton;
        // The icon follows Revit's ribbon (§2.5): a dark tile on the dark theme.
        SetIcons(GanttItTheme.Detect());
        application.ThemeChanged += OnThemeChanged;
#if DEBUG
        // Debug builds only (§8.1): the Self-Test button, autorun harness and dialog handler.
        SelfTest.SelfTestHarness.Register(application, panel);
#endif
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.ThemeChanged -= OnThemeChanged;
        return Result.Succeeded;
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        if (e.ThemeChangedType == ThemeType.UITheme) SetIcons(GanttItTheme.Detect());
    }

    private void SetIcons(UiTheme theme)
    {
        if (_button is null) return;
        var suffix = theme == UiTheme.Dark ? "_dark" : "";
        // A missing dark icon falls back to the light one; with neither, the button has no icon.
        _button.LargeImage = LoadIcon($"gantt_32{suffix}.png") ?? LoadIcon("gantt_32.png");
        _button.Image = LoadIcon($"gantt_16{suffix}.png") ?? LoadIcon("gantt_16.png");
    }

    /// <summary>Loads an icon shipped next to the DLL; null when the file is missing.</summary>
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
