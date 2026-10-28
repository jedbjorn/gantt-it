using System.Windows.Input;
using GanttIt.Core.Dates;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Dialogs;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Settings;

/// <summary>A choice in the date format list: a preset pattern, or Custom (null pattern).</summary>
public sealed record DateFormatOption(string Label, string? Pattern)
{
    public bool IsCustom => Pattern is null;
}

/// <summary>
/// Per-project settings (§2.3): the five styles read live from the model, the
/// date format, cell padding, "Use as my default for new projects" and
/// "Regenerate all charts now". Edits a copy; nothing is written until Save.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    // Field labels match the §2.3 table and the backend's SettingsState.MissingStyles names.
    public const string TitleTextLabel = "Title text style";
    public const string HeaderTextLabel = "Header text style";
    public const string BodyTextLabel = "Body text style";
    public const string ChartLineLabel = "Chart line style";
    public const string HeaderLineLabel = "Header line style";

    private static readonly DateOnly Sample = new(2026, 12, 31);

    private readonly GanttItSession _session;
    private readonly IDialogService _dialogs;
    private readonly ProjectSettings _settings;
    private DateFormatOption _dateFormatOption;
    private string _customPattern;
    private bool _useAsDefault;

    public SettingsViewModel(GanttItSession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        var state = session.Settings;
        _settings = state.Settings.Clone();
        LoadError = state.Error;
        IsFirstRun = !state.Stored;

        var backend = session.Backend;
        var textStyles = Safe(backend.TextStyles);
        var lineStyles = Safe(backend.LineStyles);
        bool Reported(string label) => state.MissingStyles.Any(m => string.Equals(m, label, StringComparison.OrdinalIgnoreCase));

        TitleText = new(TitleTextLabel, "Settings.TitleTextStyle", textStyles, _settings.TitleTextStyle, Reported(TitleTextLabel));
        HeaderText = new(HeaderTextLabel, "Settings.HeaderTextStyle", textStyles, _settings.HeaderTextStyle, Reported(HeaderTextLabel));
        BodyText = new(BodyTextLabel, "Settings.BodyTextStyle", textStyles, _settings.BodyTextStyle, Reported(BodyTextLabel));
        ChartLine = new(ChartLineLabel, "Settings.ChartLineStyle", lineStyles, _settings.ChartLineStyle, Reported(ChartLineLabel));
        HeaderLine = new(HeaderLineLabel, "Settings.HeaderLineStyle", lineStyles, _settings.HeaderLineStyle, Reported(HeaderLineLabel));
        foreach (var picker in Pickers) picker.SelectionChanged += OnChanged;

        DateFormatOptions =
        [
            .. DateFormats.Presets.Select(p => new DateFormatOption($"{p}    ({DateFormats.Format(Sample, p)})", p)),
            new DateFormatOption("Custom pattern…", null),
        ];
        var preset = DateFormatOptions.FirstOrDefault(o => o.Pattern == _settings.DateFormat);
        _dateFormatOption = preset ?? DateFormatOptions[^1];
        _customPattern = preset is null ? _settings.DateFormat : "";

        CellPadding = new LengthField(backend, _settings.CellPadding, _ => OnChanged());
        CellPadding.PropertyChanged += (_, _) => OnChanged();

        SaveCommand = new RelayCommand(() => { if (Save()) CloseRequested?.Invoke(); }, () => CanSave);
        RegenerateAllCommand = new RelayCommand(RegenerateAll, () => CanSave);
    }

    /// <summary>Raised when the window should close (after Save or Regenerate; Cancel closes the window itself).</summary>
    public event Action? CloseRequested;

    /// <summary>True once settings were written to the model in this dialog.</summary>
    public bool Saved { get; private set; }

    /// <summary>No settings are stored yet: Settings opened before the first New or Generate (§2.1).</summary>
    public bool IsFirstRun { get; }

    public string? LoadError { get; }

    public StylePickerViewModel TitleText { get; }

    public StylePickerViewModel HeaderText { get; }

    public StylePickerViewModel BodyText { get; }

    public StylePickerViewModel ChartLine { get; }

    public StylePickerViewModel HeaderLine { get; }

    public IReadOnlyList<StylePickerViewModel> Pickers => [TitleText, HeaderText, BodyText, ChartLine, HeaderLine];

    public IReadOnlyList<DateFormatOption> DateFormatOptions { get; }

    public DateFormatOption DateFormatOption
    {
        get => _dateFormatOption;
        set
        {
            if (!SetField(ref _dateFormatOption, value)) return;
            OnPropertyChanged(nameof(IsCustomDateFormat));
            OnChanged();
        }
    }

    public bool IsCustomDateFormat => DateFormatOption.IsCustom;

    public string CustomPattern
    {
        get => _customPattern;
        set
        {
            if (SetField(ref _customPattern, value)) OnChanged();
        }
    }

    /// <summary>The pattern that will be saved.</summary>
    public string DateFormat => DateFormatOption.Pattern ?? CustomPattern;

    public string? DateFormatError => DateFormats.IsValidPattern(DateFormat, out var error) ? null : error;

    public bool HasDateFormatError => DateFormatError is not null;

    public string DateFormatSample =>
        DateFormatError is null ? $"31 December 2026 shows as {DateFormats.Format(Sample, DateFormat)}" : "";

    public LengthField CellPadding { get; }

    public string? CellPaddingError =>
        CellPadding.Error ?? (CellPadding.Value > 0 ? null : "The cell padding must be greater than zero.");

    public bool HasCellPaddingError => CellPaddingError is not null;

    public bool UseAsDefault
    {
        get => _useAsDefault;
        set => SetField(ref _useAsDefault, value);
    }

    /// <summary>Styles that no longer exist, listed at the top in red (§2.3).</summary>
    public string? MissingSummary
    {
        get
        {
            var missing = Pickers.Where(p => p.IsMissing).Select(p => p.Label).ToList();
            return missing.Count == 0
                ? null
                : $"Some styles no longer exist in this model ({string.Join(", ", missing)}). Charts can't be generated until each is replaced.";
        }
    }

    public bool CanSave =>
        Pickers.All(p => p.Selected is not null) && DateFormatError is null && CellPaddingError is null;

    public ICommand SaveCommand { get; }

    public ICommand RegenerateAllCommand { get; }

    /// <summary>Writes the settings to the model; on success also the per-user defaults when asked.</summary>
    public bool Save()
    {
        if (!CanSave) return false;

        var owner = Guard(() => _session.Backend.CheckEditable(null));
        if (owner is not null)
        {
            _dialogs.ShowMessage("Settings are not editable", owner);
            return false;
        }

        var settings = Build();
        ActionResult result;
        try
        {
            result = _session.Backend.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            result = new ActionResult(ActionStatus.Failed, ex.Message);
        }
        if (!result.Succeeded)
        {
            _dialogs.ShowMessage("Settings were not saved", result.Message ?? "The settings could not be saved.");
            return false;
        }

        Saved = true;
        _session.ReloadSettings();

        if (UseAsDefault)
        {
            try
            {
                _session.Backend.SaveUserDefaults(settings);
            }
            catch (Exception ex)
            {
                _dialogs.ShowMessage("Defaults were not saved",
                    $"The settings were saved to this model, but your defaults for new projects could not be written: {ex.Message}");
            }
        }
        return true;
    }

    /// <summary>"Regenerate all charts now" (§2.3): saves, then redraws every chart with the new settings.</summary>
    private void RegenerateAll()
    {
        if (!Save()) return;
        ActionResult result;
        try
        {
            result = _session.Backend.RegenerateAll(_session.Settings.Settings);
        }
        catch (Exception ex)
        {
            result = new ActionResult(ActionStatus.Failed, ex.Message);
        }
        _dialogs.ShowMessage(
            result.Succeeded ? "Charts regenerated" : "Charts were not regenerated",
            result.Message ?? (result.Succeeded
                ? "Every chart in this model was redrawn with the new settings."
                : "The charts could not be regenerated. The model was not changed."));
        CloseRequested?.Invoke();
    }

    private ProjectSettings Build()
    {
        var s = _settings.Clone();
        s.TitleTextStyle = TitleText.ToRef();
        s.HeaderTextStyle = HeaderText.ToRef();
        s.BodyTextStyle = BodyText.ToRef();
        s.ChartLineStyle = ChartLine.ToRef();
        s.HeaderLineStyle = HeaderLine.ToRef();
        s.DateFormat = DateFormat.Trim();
        s.CellPadding = CellPadding.Value;
        return s;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(DateFormat));
        OnPropertyChanged(nameof(DateFormatError));
        OnPropertyChanged(nameof(HasDateFormatError));
        OnPropertyChanged(nameof(DateFormatSample));
        OnPropertyChanged(nameof(CellPaddingError));
        OnPropertyChanged(nameof(HasCellPaddingError));
        OnPropertyChanged(nameof(MissingSummary));
        OnPropertyChanged(nameof(CanSave));
        RelayCommand.Refresh();
    }

    private static IReadOnlyList<StyleOption> Safe(Func<IReadOnlyList<StyleOption>> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private string? Guard(Func<string?> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex)
        {
            return $"Gantt-It could not check who owns the settings: {ex.Message}";
        }
    }
}
