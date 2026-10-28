using System.Windows;
using System.Windows.Automation;

namespace GanttIt.Revit.UI.Dialogs;

/// <summary>
/// A small modal message or confirmation. One window class serves every
/// message so they look alike; the window's AutomationId says which one it is.
/// </summary>
public partial class MessageDialog : Window
{
    public const string MessageId = "MessageDialog";
    public const string DeleteConfirmId = "DeleteConfirmDialog";
    public const string NoLegendId = "NoLegendDialog";

    public MessageDialog(MessageDialogModel model, string automationId)
    {
        InitializeComponent();
        DataContext = model;
        AutomationProperties.SetAutomationId(this, automationId);
        if (model.Danger) ConfirmButton.Style = (Style)FindResource("DangerFilledButton");
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>What a <see cref="MessageDialog"/> shows.</summary>
public sealed record MessageDialogModel(
    string Title,
    string Heading,
    string Message,
    IReadOnlyList<string> Items,
    string ConfirmText = "OK",
    bool HasCancel = false,
    bool Danger = false)
{
    public bool HasItems => Items.Count > 0;
}
