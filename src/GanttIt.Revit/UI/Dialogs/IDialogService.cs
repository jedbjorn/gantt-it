using GanttIt.Core.Model;
using GanttIt.Revit.UI.Settings;

namespace GanttIt.Revit.UI.Dialogs;

/// <summary>The modal dialogs the view-models open, so they never touch windows directly.</summary>
public interface IDialogService
{
    /// <summary>An informational or error message with one OK button.</summary>
    void ShowMessage(string title, string message, string automationId = MessageDialog.MessageId);

    /// <summary>A confirmation listing <paramref name="items"/> (e.g. sheets). True when the user confirms.</summary>
    bool Confirm(string title, string message, IReadOnlyList<string> items, string confirmText, bool danger, string automationId);

    /// <summary>The §7.1 explanation shown when the model has no legend to duplicate.</summary>
    void ShowNoLegend();

    /// <summary>Settings (§2.3). True when the settings were saved.</summary>
    bool ShowSettings(SettingsViewModel settings);

    /// <summary>The color picker (§2.4). Null when cancelled.</summary>
    Rgb? PickColor(Rgb current, IReadOnlyList<Rgb> palette);
}
