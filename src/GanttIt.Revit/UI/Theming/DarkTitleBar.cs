using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GanttIt.Revit.UI.Theming;

/// <summary>
/// Gives a Gantt-It window a dark title bar in the dark theme (spec §2.5), through
/// DWM's immersive dark mode attribute. Best effort: Windows versions without it
/// keep the standard title bar.
/// </summary>
public static class DarkTitleBar
{
    /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE on Windows 10 20H1 and later, and Windows 11.</summary>
    private const int UseImmersiveDarkMode = 20;

    /// <summary>The same attribute's number on Windows 10 1809 to 1909.</summary>
    private const int UseImmersiveDarkModeBefore20H1 = 19;

    /// <summary>Call from the window's constructor; applies once its handle exists (before it is first shown).</summary>
    public static void Attach(Window window)
    {
        if (!GanttItTheme.IsDark) return;
        window.SourceInitialized += (_, _) => Apply(new WindowInteropHelper(window).Handle);
    }

    private static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBefore20H1, ref on, sizeof(int));
        }
        catch (Exception)
        {
            // dwmapi missing or the call unsupported: keep the light title bar.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
