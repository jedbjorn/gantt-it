using System.Windows;
using System.Windows.Interop;
using GanttIt.Core.Model;
using GanttIt.Revit.UI.Dialogs;

namespace GanttIt.Revit.UI.Swatches;

/// <summary>Hosts the <see cref="ColorPicker"/> as a small modal window opened from a section's swatch.</summary>
public partial class ColorPickerDialog : Window
{
    /// <param name="anchor">Screen position (DIPs) to open next to, usually the pointer; null centres on the owner.</param>
    public ColorPickerDialog(Rgb current, IReadOnlyList<Rgb> palette, Point? anchor)
    {
        InitializeComponent();
        var model = new ColorPickerViewModel(current, palette, GanttItSession.RecentColors, MoreColors);
        model.Picked += color =>
        {
            Result = color;
            DialogResult = true;
        };
        DataContext = model;

        if (anchor is { } p)
        {
            // Keep the whole picker on the work area.
            var area = SystemParameters.WorkArea;
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
}
