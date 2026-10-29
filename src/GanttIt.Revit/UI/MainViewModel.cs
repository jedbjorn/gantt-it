using Autodesk.Revit.DB;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Dialogs;
using GanttIt.Revit.UI.Editor;
using GanttIt.Revit.UI.Home;
using GanttIt.Revit.UI.Mvvm;
using GanttIt.Revit.UI.Settings;

namespace GanttIt.Revit.UI;

/// <summary>
/// The Gantt-It window: navigates between Home (§2.1) and the editor (§2.2),
/// and owns the actions both screens share (Settings, Delete, Open view).
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private ObservableObject? _page;

    public MainViewModel(GanttItSession session, IDialogService dialogs)
    {
        Session = session;
        _dialogs = dialogs;
    }

    public GanttItSession Session { get; }

    /// <summary>The current screen: a <see cref="HomeViewModel"/> or an <see cref="EditorViewModel"/>.</summary>
    public ObservableObject? Page
    {
        get => _page;
        private set
        {
            if (_page is EditorViewModel old && !ReferenceEquals(old, value)) old.Detach();
            if (SetField(ref _page, value)) OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => Page switch
    {
        EditorViewModel { IsExisting: true } e => $"Gantt-It — Edit {e.OriginalName}",
        EditorViewModel => "Gantt-It — New chart",
        _ => "Gantt-It",
    };

    /// <summary>Set by Open view; the command activates this legend after the window closes (§2.1).</summary>
    public ElementId? ViewToOpen { get; private set; }

    /// <summary>Raised when the window should close.</summary>
    public event Action? CloseRequested;

    /// <summary>First screen: the editor with a parked §7.1 draft, otherwise Home.</summary>
    public void Start()
    {
        var draft = Session.Drafts.Load();
        if (draft is not null) Page = new EditorViewModel(this, _dialogs, null, draft);
        else ShowHome();
    }

    public void ShowHome(string? status = null) => Page = new HomeViewModel(this, status);

    /// <summary>New chart (§2.1). With no stored settings, Settings opens first and must be saved.</summary>
    public void NewChart()
    {
        if (!Session.Settings.Stored && !OpenSettings()) return;
        Session.Drafts.Clear();
        Page = new EditorViewModel(this, _dialogs, null, null);
    }

    /// <summary>Edit (§2.1): refused with the owner's name when the legend is owned by someone else (§7.5).</summary>
    public void EditChart(ChartEntry entry)
    {
        if (entry.Load is not { IsEditable: true, Value: not null }) return;
        var owner = CheckEditable(entry.ViewId);
        if (owner is not null)
        {
            _dialogs.ShowMessage("Chart is not editable", owner);
            return;
        }
        Page = new EditorViewModel(this, _dialogs, entry, null);
    }

    /// <summary>Open view (§2.1): closes the dialog; the command then activates the legend.</summary>
    public void OpenView(ElementId viewId)
    {
        ViewToOpen = viewId;
        CloseRequested?.Invoke();
    }

    /// <summary>Settings (§2.3). True when saved; settings are then reloaded and the preview recalibrates.</summary>
    public bool OpenSettings() => _dialogs.ShowSettings(new SettingsViewModel(Session, _dialogs));

    /// <summary>
    /// Delete (§7.4, A8): checks ownership, confirms while listing the sheets the
    /// legend is placed on, then deletes. True when the chart was deleted.
    /// </summary>
    public bool DeleteChart(ElementId viewId, string name, IReadOnlyList<string> sheets)
    {
        var owner = CheckEditable(viewId);
        if (owner is not null)
        {
            _dialogs.ShowMessage("Chart is not editable", owner);
            return false;
        }

        var message = sheets.Count == 0
            ? $"Delete the chart \"{name}\" and its legend view? It is not placed on any sheet."
            : $"Delete the chart \"{name}\" and its legend view? It is placed on {(sheets.Count == 1 ? "this sheet" : $"these {sheets.Count} sheets")}, " +
              "and deleting the view removes its viewports from them:";
        if (!_dialogs.Confirm("Delete chart", message, sheets, "Delete", danger: true, MessageDialog.DeleteConfirmId))
            return false;

        ActionResult result;
        try
        {
            result = Session.Backend.Delete(viewId);
        }
        catch (Exception ex)
        {
            result = new ActionResult(ActionStatus.Failed, ex.Message);
        }
        if (!result.Succeeded)
        {
            _dialogs.ShowMessage("Chart was not deleted", ResultMessage(result, "deleted"));
            return false;
        }

        ShowHome($"Deleted \"{name}\".");
        return true;
    }

    /// <summary>Called as the window closes: keeps a §7.1 draft with its latest edits.</summary>
    public void OnClosing()
    {
        if (Page is EditorViewModel editor) editor.OnWindowClosing();
        Page = null;
    }

    /// <summary>A user-facing message for a failed or refused action.</summary>
    public static string ResultMessage(ActionResult result, string verb) => result.Status switch
    {
        ActionStatus.Failed => (result.Message ?? $"The chart could not be {verb}.") +
                               "\n\nThe change was rolled back; the model is as it was.",
        _ => result.Message ?? $"The chart could not be {verb}.",
    };

    private string? CheckEditable(ElementId? viewId)
    {
        try
        {
            return Session.Backend.CheckEditable(viewId);
        }
        catch (Exception ex)
        {
            return $"Gantt-It could not check who owns this chart: {ex.Message}";
        }
    }

    /// <summary>The palette for new sections: the settings palette, or the default when it is empty.</summary>
    public IReadOnlyList<Rgb> Palette =>
        Session.Settings.Settings.Palette is { Count: > 0 } p ? p : Core.Model.Palette.Default;
}
