using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GanttIt.Core.Model;
using GanttIt.Revit.Resources;
using GanttIt.Revit.UI.Swatches;
using GanttIt.Revit.UI.Settings;

namespace GanttIt.Revit.UI.Dialogs;

/// <summary>Opens Gantt-It's modal dialogs, each owned by the main Gantt-It window.</summary>
public sealed class DialogService(Window owner) : IDialogService
{
    public void ShowMessage(string title, string message, string automationId = MessageDialog.MessageId) =>
        Show(new MessageDialogModel(title, title, message, []), automationId);

    public bool Confirm(string title, string message, IReadOnlyList<string> items, string confirmText, bool danger, string automationId) =>
        Show(new MessageDialogModel(title, title, message, items, confirmText, HasCancel: true, Danger: danger), automationId);

    public void ShowNoLegend() => Show(
        new MessageDialogModel(
            "Gantt-It",
            Strings.NoLegend_Heading,
            Strings.NoLegend_Message,
            [Strings.NoLegend_Step1, Strings.NoLegend_Step2, Strings.NoLegend_Step3]),
        MessageDialog.NoLegendId);

    public bool ShowSettings(SettingsViewModel settings)
    {
        var window = new SettingsWindow(settings) { Owner = owner };
        window.ShowDialog();
        return settings.Saved;
    }

    public Rgb? PickColor(Rgb current, IReadOnlyList<Rgb> palette)
    {
        var dialog = new ColorPickerDialog(current, palette, PointerOnScreen(), MonitorWorkArea.Of(owner)) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private bool Show(MessageDialogModel model, string automationId)
    {
        var dialog = new MessageDialog(model, automationId) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    /// <summary>The pointer position in screen DIPs, so the picker opens beside the swatch that was clicked.</summary>
    private Point? PointerOnScreen()
    {
        if (PresentationSource.FromVisual(owner) is not { CompositionTarget: { } target }) return null;
        var device = owner.PointToScreen(Mouse.GetPosition(owner));
        return target.TransformFromDevice.Transform(device);
    }
}
