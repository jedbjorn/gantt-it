using System.Collections.ObjectModel;
using System.Windows.Input;
using Autodesk.Revit.DB;
using GanttIt.Core.Dates;
using GanttIt.Core.Editing;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using GanttIt.Core.Validation;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Dialogs;
using GanttIt.Revit.UI.Mvvm;
using GanttIt.Revit.UI.Preview;
using GanttIt.Revit.UI.Settings;

namespace GanttIt.Revit.UI.Editor;

/// <summary>An inline notice (§5.1 "never a silent change"), dismissible.</summary>
public sealed record NoticeViewModel(string Message, string AutomationId);

/// <summary>A timescale choice for the combo box.</summary>
public sealed record TimescaleOption(Timescale Value, string Label);

/// <summary>
/// The chart editor (§2.2). Works on a copy of the chart; every edit runs the
/// Core scheduling rules (§5), re-validates (Generate is disabled while there
/// are errors) and schedules a preview redraw (§6).
/// </summary>
public sealed class EditorViewModel : ObservableObject
{
    public const string RestoredNoticeId = "Editor.RestoredNotice";
    private const int MaxNotices = 4;

    private readonly MainViewModel _main;
    private readonly IDialogService _dialogs;
    private readonly ChartEntry? _existing;
    private readonly IReadOnlyCollection<string> _otherViewNames;
    private bool _keepDraft;
    private bool _canGenerate;
    private string? _nameError;

    public EditorViewModel(MainViewModel main, IDialogService dialogs, ChartEntry? existing, Chart? draft)
    {
        _main = main;
        _dialogs = dialogs;
        _existing = existing;
        var backend = main.Session.Backend;

        _otherViewNames = ReadOtherViewNames(backend, existing?.ViewId, out var namesError);
        Chart = existing?.Load.Value?.Clone() ?? draft ?? NewChart(_otherViewNames, main.Palette);
        OriginalName = existing?.ViewName ?? Chart.Name;

        TimelineWidth = new LengthField(backend, Chart.TimelineWidth, v => { Chart.TimelineWidth = v; OnEdited(); });
        NameMaxWidth = new LengthField(backend, Chart.NameMaxWidth, v => { Chart.NameMaxWidth = v; OnEdited(); });
        TimelineWidth.PropertyChanged += (_, _) => Revalidate();
        NameMaxWidth.PropertyChanged += (_, _) => Revalidate();

        foreach (var section in Chart.Sections) Rows.Add(new SectionRowViewModel(this, section));

        Preview = new PreviewViewModel(main.Session, () => Chart);

        AddSectionCommand = new RelayCommand(AddSection);
        GenerateCommand = new RelayCommand(Generate, () => CanGenerate);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(Delete, () => IsExisting);
        SettingsCommand = new RelayCommand(() => OpenSettings());
        DismissNoticeCommand = new RelayCommand<NoticeViewModel>(n => Notices.Remove(n));

        if (draft is not null)
        {
            // §7.1: back after creating a legend by hand. Keep the draft until Generate succeeds or Cancel.
            _keepDraft = true;
            AddNotice("Restored the chart you were editing before Gantt-It closed. Press Generate to create it.", RestoredNoticeId);
        }

        if (namesError is not null) AddNotice(namesError);

        // Update is always a full, silent regeneration (§7.4, decision #16): no hand-edit warning.

        Revalidate();
        Preview.RenderNow();
    }

    /// <summary>The working copy. The stored chart is untouched until Generate/Update.</summary>
    public Chart Chart { get; }

    public bool IsExisting => _existing is not null;

    /// <summary>The legend view's name when the editor opened.</summary>
    public string OriginalName { get; }

    public string Heading => IsExisting ? $"Edit chart: {OriginalName}" : "New chart";

    /// <summary>"Generate" for a new chart, "Update" for an existing one (§2.2).</summary>
    public string GenerateText => IsExisting ? "Update" : "Generate";

    // ---- Chart properties (§2.2 item 1) ----------------------------------------------------

    public string Name
    {
        get => Chart.Name;
        set
        {
            if (Chart.Name == value) return;
            var title = EditorInput.TitleAfterRename(Chart.Name, value, Chart.Title);
            Chart.Name = value;
            if (title != Chart.Title)
            {
                Chart.Title = title;
                OnPropertyChanged(nameof(Title));
            }
            OnEdited();
        }
    }

    public string Title
    {
        get => Chart.Title;
        set
        {
            if (Chart.Title == value) return;
            Chart.Title = value;
            OnEdited();
        }
    }

    public IReadOnlyList<TimescaleOption> TimescaleOptions { get; } =
    [
        new(Timescale.Auto, "Auto"),
        new(Timescale.Days, "Days"),
        new(Timescale.Weeks, "Weeks"),
        new(Timescale.Months, "Months"),
        new(Timescale.Quarters, "Quarters"),
    ];

    public Timescale Timescale
    {
        get => Chart.Timescale;
        set
        {
            if (Chart.Timescale == value) return;
            Chart.Timescale = value;
            OnEdited();
        }
    }

    public bool ShowStart
    {
        get => Chart.Columns.HasFlag(ChartColumns.Start);
        set => SetColumn(ChartColumns.Start, value);
    }

    public bool ShowEnd
    {
        get => Chart.Columns.HasFlag(ChartColumns.End);
        set => SetColumn(ChartColumns.End, value);
    }

    public bool ShowDuration
    {
        get => Chart.Columns.HasFlag(ChartColumns.Duration);
        set => SetColumn(ChartColumns.Duration, value);
    }

    /// <summary>Timeline width on paper, in project units (D11).</summary>
    public LengthField TimelineWidth { get; }

    /// <summary>Maximum name-column width on paper, in project units (D11).</summary>
    public LengthField NameMaxWidth { get; }

    /// <summary>
    /// The pattern the section rows' Start/End fields show and read: the project's date
    /// format (Settings, the same one the chart uses), or ISO when that pattern can't
    /// identify a day (<see cref="DateFormats.EntryPattern"/>).
    /// </summary>
    public string DateEntryPattern => DateFormats.EntryPattern(_main.Session.Settings.Settings.DateFormat);

    // ---- Sections (§2.2 item 2) --------------------------------------------------------------

    public ObservableCollection<SectionRowViewModel> Rows { get; } = [];

    public ObservableCollection<NoticeViewModel> Notices { get; } = [];

    /// <summary>Every validation problem, one line each; Generate is disabled while any exist.</summary>
    public ObservableCollection<string> Issues { get; } = [];

    public bool HasIssues => Issues.Count > 0;

    public string IssueSummary => Issues.Count switch
    {
        0 => IsExisting ? "Ready to update." : "Ready to generate.",
        1 => $"1 problem to fix before {Gerund}:",
        _ => $"{Issues.Count} problems to fix before {Gerund}:",
    };

    private string Gerund => IsExisting ? "updating" : "generating";

    public bool CanGenerate
    {
        get => _canGenerate;
        private set => SetField(ref _canGenerate, value);
    }

    public PreviewViewModel Preview { get; }

    public ICommand AddSectionCommand { get; }

    public ICommand GenerateCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand SettingsCommand { get; }

    public ICommand DismissNoticeCommand { get; }

    // ---- Scheduling edits, called by the rows -----------------------------------------------

    public void SetStart(SectionRowViewModel row, DateOnly start)
    {
        if (start == row.Section.Start) return;
        if (!CheckDate(start)) return;
        Scheduler.SetStart(Chart, row.Id, start);
        OnScheduleChanged();
    }

    public void SetEnd(SectionRowViewModel row, DateOnly end)
    {
        if (end == row.Section.End) return;
        if (!CheckDate(end)) return;
        Scheduler.SetEnd(Chart, row.Id, end);
        OnScheduleChanged();
    }

    public void SetDuration(SectionRowViewModel row, string text)
    {
        if (!EditorInput.TryParseDuration(text, out var days, out var error))
        {
            AddNotice($"\"{row.Name}\": {error}");
            return;
        }
        if (days == row.Section.Duration) return;
        if (!CheckDate(row.Section.Start.AddDays(days - 1))) return;
        Scheduler.SetDuration(Chart, row.Id, days);
        OnScheduleChanged();
    }

    /// <summary>Ticks or unticks the one link between rows <paramref name="fromIndex"/> and the next (D5, §5.1).</summary>
    public void SetLink(int fromIndex, bool linked)
    {
        Scheduler.SetLink(Chart, fromIndex, linked);
        OnScheduleChanged();
    }

    /// <summary>Drag reorder. Links on both sides of the moved row are removed, with a notice (§5.1).</summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex || fromIndex < 0 || toIndex < 0 || fromIndex >= Rows.Count || toIndex >= Rows.Count) return;
        var notice = Scheduler.Move(Chart, fromIndex, toIndex);
        Rows.Move(fromIndex, toIndex);
        if (notice is not null) AddNotice(notice.Message);
        OnScheduleChanged();
    }

    public void RemoveSection(SectionRowViewModel row)
    {
        var notice = Scheduler.RemoveSection(Chart, row.Id);
        Rows.Remove(row);
        if (notice is not null) AddNotice(notice.Message);
        OnScheduleChanged();
    }

    public void PickColor(SectionRowViewModel row)
    {
        if (_dialogs.PickColor(row.Section.Color, _main.Palette) is not { } color || color == row.Section.Color) return;
        row.Section.Color = color;
        row.Refresh();
        OnEdited();
    }

    /// <summary>Any edit: re-validate and redraw the preview.</summary>
    public void OnEdited()
    {
        Revalidate();
        Preview.Request();
    }

    /// <summary>Called when the editor is replaced or the window closes.</summary>
    public void Detach() => Preview.Stop();

    /// <summary>The window is closing (X / Esc): a §7.1 draft keeps its latest edits.</summary>
    public void OnWindowClosing()
    {
        if (_keepDraft) _main.Session.Drafts.Save(Chart);
    }

    // ---- Actions -----------------------------------------------------------------------------

    private void AddSection()
    {
        var section = Scheduler.AddSection(Chart, _main.Palette, DateOnly.FromDateTime(DateTime.Today));
        Rows.Add(new SectionRowViewModel(this, section));
        OnScheduleChanged();
    }

    private void Generate()
    {
        // §2.1: with no stored settings, the first Generate opens Settings before continuing.
        if (!_main.Session.Settings.Stored)
        {
            if (!OpenSettings()) return;
        }

        Revalidate();
        if (!CanGenerate) return;

        var backend = _main.Session.Backend;
        if (_existing is not null)
        {
            var owner = Guard(() => backend.CheckEditable(_existing.ViewId));
            if (owner is not null)
            {
                _dialogs.ShowMessage("Chart is not editable", owner);
                return;
            }
        }

        var settings = _main.Session.Settings.Settings;
        ActionResult result;
        try
        {
            result = _existing is null
                ? backend.Generate(Chart.Clone(), settings)
                : backend.Update(_existing.ViewId, Chart.Clone(), settings);
        }
        catch (Exception ex)
        {
            result = new ActionResult(ActionStatus.Failed, ex.Message);
        }

        switch (result.Status)
        {
            case ActionStatus.Ok:
                _keepDraft = false;
                _main.Session.Drafts.Clear();
                _main.ShowHome(_existing is null ? $"Generated \"{Chart.Name.Trim()}\"." : $"Updated \"{Chart.Name.Trim()}\".");
                break;
            case ActionStatus.NoLegend:
                // §7.1 / A10: explain, keep everything in the editor, and park it in case the user closes Gantt-It.
                _keepDraft = true;
                _main.Session.Drafts.Save(Chart);
                _dialogs.ShowNoLegend();
                break;
            default: // Blocked or Failed
                _dialogs.ShowMessage($"Chart was not {Past}", MainViewModel.ResultMessage(result, Past));
                break;
        }
    }

    private string Past => IsExisting ? "updated" : "generated";

    private void Cancel()
    {
        _keepDraft = false;
        _main.Session.Drafts.Clear();
        _main.ShowHome();
    }

    private void Delete()
    {
        if (_existing is null) return;
        _main.DeleteChart(_existing.ViewId, _existing.ViewName, _existing.Sheets);
    }

    private bool OpenSettings()
    {
        if (!_main.OpenSettings()) return false;
        // New settings: recalibrated preview (decision #14), new date format, new missing-style state.
        foreach (var row in Rows) row.Refresh();
        Revalidate();
        Preview.RenderNow();
        return true;
    }

    // ---- Validation --------------------------------------------------------------------------

    private void Revalidate()
    {
        var session = _main.Session;
        var settings = session.Settings.Settings;
        var issues = ChartValidator.Validate(Chart, settings, _otherViewNames);
        var lines = new List<string>();

        if (TimelineWidth.Error is { } tw) lines.Add($"Timeline width: {tw}");
        if (NameMaxWidth.Error is { } nw) lines.Add($"Name column width: {nw}");
        lines.AddRange(issues.Select(i => i.Message));

        // With stored settings, every style must exist (§2.3). Without, Generate opens Settings first (§2.1).
        if (session.SettingsReadOnly)
        {
            // §7.2: settings from a newer Gantt-It can't be drawn with or saved over here.
            lines.Add($"Settings: {session.Settings.Error ?? "the settings were written by a newer version of Gantt-It and are read-only here."}");
        }
        else if (session.Settings.Stored)
        {
            foreach (var missing in session.Settings.MissingStyles)
                lines.Add($"Settings: the {missing.ToLowerInvariant()} no longer exists. Choose a replacement in Settings.");
            foreach (var (label, style) in StyleFields(settings))
                if (style is null && !session.Settings.MissingStyles.Contains(label, StringComparer.OrdinalIgnoreCase))
                    lines.Add($"Settings: choose a {label.ToLowerInvariant()} in Settings.");
        }

        foreach (var row in Rows)
        {
            var rowIssues = issues.Where(i => i.SectionId == row.Id).ToList();
            row.SetIssues(
                rowIssues.Count == 0 ? null : string.Join("\n", rowIssues.Select(i => i.Message)),
                rowIssues.Any(i => i.Code == IssueCode.SectionNameBlank),
                rowIssues.Any(i => i.Code is IssueCode.EndBeforeStart or IssueCode.DateOutOfRange or IssueCode.LinkInvalid));
        }

        Issues.Clear();
        foreach (var line in lines.Distinct()) Issues.Add(line);
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(IssueSummary));
        NameError = issues
            .FirstOrDefault(i => i.Code is IssueCode.ChartNameBlank or IssueCode.ChartNameInUse or IssueCode.ChartNameInvalidCharacters)
            ?.Message;
        CanGenerate = Issues.Count == 0;
        RelayCommand.Refresh();
    }

    /// <summary>The chart-name problem, shown under the name field.</summary>
    public string? NameError
    {
        get => _nameError;
        private set
        {
            if (SetField(ref _nameError, value)) OnPropertyChanged(nameof(HasNameError));
        }
    }

    public bool HasNameError => NameError is not null;

    private static IEnumerable<(string Label, StyleRef? Style)> StyleFields(ProjectSettings s) =>
    [
        (SettingsViewModel.TitleTextLabel, s.TitleTextStyle),
        (SettingsViewModel.HeaderTextLabel, s.HeaderTextStyle),
        (SettingsViewModel.BodyTextLabel, s.BodyTextStyle),
        (SettingsViewModel.ChartLineLabel, s.ChartLineStyle),
        (SettingsViewModel.HeaderLineLabel, s.HeaderLineStyle),
    ];

    // ---- Helpers -----------------------------------------------------------------------------

    private void OnScheduleChanged()
    {
        foreach (var row in Rows) row.Refresh();
        OnEdited();
    }

    private void SetColumn(ChartColumns column, bool on)
    {
        var columns = on ? Chart.Columns | column : Chart.Columns & ~column;
        if (columns == Chart.Columns) return;
        Chart.Columns = columns;
        OnEdited();
    }

    private bool CheckDate(DateOnly date)
    {
        if (EditorInput.IsSupportedDate(date, out var error)) return true;
        AddNotice(error!);
        OnScheduleChanged(); // snap the pickers back
        return false;
    }

    private void AddNotice(string message, string automationId = "Editor.Notice")
    {
        Notices.Insert(0, new NoticeViewModel(message, automationId));
        while (Notices.Count > MaxNotices) Notices.RemoveAt(Notices.Count - 1);
    }

    private static Chart NewChart(IReadOnlyCollection<string> otherViewNames, IReadOnlyList<Rgb> palette)
    {
        var chart = Chart.Create(EditorInput.UniqueChartName(otherViewNames));
        Scheduler.AddSection(chart, palette, DateOnly.FromDateTime(DateTime.Today));
        return chart;
    }

    private static IReadOnlyCollection<string> ReadOtherViewNames(IGanttItBackend backend, ElementId? viewId, out string? error)
    {
        error = null;
        try
        {
            return backend.OtherViewNames(viewId);
        }
        catch (Exception ex)
        {
            error = $"The model's view names could not be read, so a name clash will only be caught by Revit: {ex.Message}";
            return [];
        }
    }

    private static string? Guard(Func<string?> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return $"Gantt-It could not check who owns this chart: {ex.Message}";
        }
    }
}
