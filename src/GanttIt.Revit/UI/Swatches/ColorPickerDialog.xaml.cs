using System.Windows;
using System.Windows.Interop;
using GanttIt.Core.Model;
using GanttIt.Revit.UI.Dialogs;

namespace GanttIt.Revit.UI.Swatches;

/// <summary>Hosts the <see cref="ColorPicker"/> as a small modal window opened from a section's swatch.</summary>
public partial class ColorPickerDialog : Window
{
    /// <param name="anchor">Screen position (DIPs) to open next to, usually the pointer; null centres on the owner.</param>
    /// <param name="workArea">Work area (DIPs) of the owner's monitor; the picker is kept inside it.</param>
    public ColorPickerDialog(Rgb current, IReadOnlyList<Rgb> palette, Point? anchor, Rect workArea)
    {
        InitializeComponent();
        var model = new ColorPickerViewModel(current, palette, GanttItSession.RecentColors, MoreColors, PickFromScreen);
        model.Picked += color =>
        {
            Result = color;
            DialogResult = true;
        };
        DataContext = model;

        if (anchor is { } p)
        {
            // Keep the whole picker on the owner's monitor.
            var area = workArea;
            Left = Math.Max(area.Left, Math.Min(p.X - 20, area.Right - 250));
            Top = Math.Max(area.Top, Math.Min(p.Y + 14, area.Bottom - 380));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    public Rgb? Result { get; private set; }

    private Rgb? MoreColors(Rgb current) => NativeColorDialog.Show(new WindowInteropHelper(this).Handle, current);

    /// <summary>The eyedropper: hides this picker and the Gantt-It window while the user picks a pixel.</summary>
    private Rgb? PickFromScreen() => ScreenColorPicker.Pick(this);
}
