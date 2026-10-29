using Autodesk.Revit.UI;
using GanttIt.Core.Theming;
using Microsoft.Win32;

namespace GanttIt.Revit.UI.Theming;

/// <summary>
/// Which palette Gantt-It's windows use (spec §2.5, decision #21): Revit's UI theme.
/// Read when the Gantt-It window opens; the window is modal (D7), so Revit's theme
/// can't change while it, or any dialog it owns, is open.
/// </summary>
public static class GanttItTheme
{
    /// <summary>The theme of the window being opened (and its dialogs). Light until the first <see cref="Refresh"/>.</summary>
    public static UiTheme Current { get; private set; } = UiTheme.Light;

    public static bool IsDark => Current == UiTheme.Dark;

    /// <summary>Reads Revit's theme again; called as the Gantt-It window opens, before it loads its XAML.</summary>
    public static void Refresh() => Current = Detect();

    /// <summary>
    /// Revit's effective UI theme. With Revit's "Use system setting" option on
    /// (<see cref="UIThemeManager.FollowSystemColorTheme"/>) the Windows app theme is
    /// read directly, so the result is right whether or not
    /// <see cref="UIThemeManager.CurrentTheme"/> resolves the system setting itself.
    /// Anything unreadable falls back to light.
    /// </summary>
    public static UiTheme Detect()
    {
#if DEBUG
        // Debug builds only: GANTTIT_THEME=dark|light lets the VM lane capture both themes quickly.
        switch (Environment.GetEnvironmentVariable("GANTTIT_THEME")?.Trim().ToLowerInvariant())
        {
            case "dark": return UiTheme.Dark;
            case "light": return UiTheme.Light;
        }
#endif
        try
        {
            if (UIThemeManager.FollowSystemColorTheme && WindowsAppTheme() is { } system) return system;
            return UIThemeManager.CurrentTheme == UITheme.Dark ? UiTheme.Dark : UiTheme.Light;
        }
        catch (Exception)
        {
            return UiTheme.Light;
        }
    }

    /// <summary>The Windows "app mode" (Settings › Personalization › Colors), or null when it can't be read.</summary>
    private static UiTheme? WindowsAppTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light ? (light == 0 ? UiTheme.Dark : UiTheme.Light) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
