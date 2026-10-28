using System.Windows.Controls;

namespace GanttIt.Revit.UI.Swatches;

/// <summary>The custom color picker control (§2.4). Bind its DataContext to a <see cref="ColorPickerViewModel"/>.</summary>
public partial class ColorPicker : UserControl
{
    public ColorPicker() => InitializeComponent();
}
