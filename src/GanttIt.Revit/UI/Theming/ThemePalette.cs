using System.Windows;
using System.Windows.Media;
using GanttIt.Core.Theming;

namespace GanttIt.Revit.UI.Theming;

/// <summary>
/// The palette brushes for <see cref="GanttItTheme.Current"/>, merged first into
/// Theme.xaml so its styles and every view's <c>{StaticResource …Brush}</c> resolve
/// to the light or dark color. A new instance is built each time a Gantt-It window
/// (or dialog) loads Theme.xaml, which is how the theme is applied per window open.
/// </summary>
public sealed class ThemePalette : ResourceDictionary
{
    public ThemePalette()
    {
        foreach (var (key, color) in UiPalette.For(GanttItTheme.Current))
            Add(key, Brush(color));

        if (!GanttItTheme.IsDark) return;

        // Dark only: the built-in WPF templates that draw with system colors (the
        // default focus rectangle, text box context menus, scroll viewer corners)
        // pick these up instead of the light Windows colors.
        var palette = UiPalette.For(UiTheme.Dark);
        Add(SystemColors.WindowBrushKey, Brush(palette[UiPalette.Surface]));
        Add(SystemColors.WindowTextBrushKey, Brush(palette[UiPalette.Text]));
        Add(SystemColors.ControlBrushKey, Brush(palette[UiPalette.ScrollTrack]));
        Add(SystemColors.ControlTextBrushKey, Brush(palette[UiPalette.Text]));
        Add(SystemColors.GrayTextBrushKey, Brush(palette[UiPalette.MutedText]));
        Add(SystemColors.MenuBrushKey, Brush(palette[UiPalette.Surface]));
        Add(SystemColors.MenuTextBrushKey, Brush(palette[UiPalette.Text]));
        Add(SystemColors.InfoBrushKey, Brush(palette[UiPalette.ToolTip]));
        Add(SystemColors.InfoTextBrushKey, Brush(palette[UiPalette.ToolTipText]));
    }

    private static SolidColorBrush Brush(UiColor c)
    {
        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}
