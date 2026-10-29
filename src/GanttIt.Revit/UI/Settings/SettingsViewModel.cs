using System.Windows.Input;
using GanttIt.Core.Dates;
using GanttIt.Core.Localization;
using GanttIt.Core.Model;
using GanttIt.Revit.Resources;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Dialogs;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Settings;

/// <summary>A choice in the date format list: a preset pattern, or Custom (null pattern).</summary>
public sealed record DateFormatOption(string Label, string? Pattern)
{
    public bool IsCustom => Pattern is null;
}

/// <summary>A chart language choice, listed by the language's own name (English, Français, …).</summary>
public sealed record ChartLanguageOption(Language Value, string Label);

/// <summary>
/// Per-project settings (§2.3): the five styles read live from the model, the
/// date format, cell padding, "Use as my default for new projects" and
/// "Regenerate all charts now". Edits a copy; nothing is written until Save.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    // Field labels match the §2.3 table and the backend's SettingsState.MissingStyles names
    // (StyleReader uses the same resources, in the same UI language).
    public static string TitleTextLabel => Strings.Style_TitleText;
    public static string HeaderTextLabel => Strings.Style_HeaderText;
    public static string BodyTextLabel => Strings.Style_BodyText;
    public static string ChartLineLabel => Strings.Style_ChartLine;
    public static string HeaderLineLabel => Strings.Style_HeaderLine;

    private static readonly DateOnly Sample = new(2026, 12, 31);

    private readonly GanttItSession _session;
    private readonly IDialogService _dialogs;
    private readonly ProjectSettings _settings;
    private DateFormatOption _dateFormatOption;
    private string _customPattern;
    private ChartLanguageOption _chartLanguage;
    private bool _useAsDefault;

    public SettingsViewModel(GanttItSession session, IDialogService dialogs)
    {
        _session = session;
        _dialogs = dialogs;
        var state = session.Settings;
        _settings = state.Settings.Clone();
        LoadError = state.Error;
        IsFirstRun = !state.Stored;
        IsReadOnly = session.SettingsReadOnly;

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
            new DateFormatOption(Strings.Settings_CustomPattern, null),
        ];
        var preset = DateFormatOptions.FirstOrDefault(o => o.Pattern == _settings.DateFormat);
        _dateFormatOption = preset ?? DateFormatOptions[^1];
        _customPattern = preset is null ? _settings.DateFormat : "";

        ChartLanguageOptions = Languages.All.Select(l => new ChartLanguageOption(l, Languages.NativeName(l))).ToList();
        _chartLanguage = ChartLanguageOptions.FirstOrDefault(o => o.Value == _settings.ChartLanguage) ?? ChartLanguageOptions[0];

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

    /// <summary>Settings from a newer Gantt-It: shown, never saved over (§7.2).</summary>
    public bool IsReadOnly { get; }

    public bool IsEditable => !IsReadOnly;

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

    /// <summary>31 Dec 2026 as the chart will draw it: the pattern with the chart language's month names.</summary>
    public string DateFormatSample =>
        DateFormatError is null ? Strings.Settings_DateSample(DateFormats.Format(Sample, DateFormat, ChartLanguage.Value)) : "";

    /// <summary>The four chart languages (§2.6), in their own names.</summary>
    public IReadOnlyList<ChartLanguageOption> ChartLanguageOptions { get; }

    /// <summary>The language of the text drawn on charts (§2.6): headers, months, quarters, default names.</summary>
    public ChartLanguageOption ChartLanguage
    {
        get => _chartLanguage;
        set
        {
            if (value is null || !SetField(ref _chartLanguage, value)) return;
            OnChanged();
        }
    }

    public LengthField CellPadding { get; }

    public string? CellPaddingError =>
        CellPadding.Error ?? (CellPadding.Value > 0 ? null : Strings.Settings_CellPaddingPositive);

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
                : Strings.Settings_MissingSummary(string.Join(", ", missing));
        }
    }

    public bool CanSave =>
        !IsReadOnly && Pickers.All(p => p.Selected is not null) && DateFormatError is null && CellPaddingError is null;

    public ICommand SaveCommand { get; }

    public ICommand RegenerateAllCommand { get; }

    /// <summary>Writes the settings to the model; on success also the per-user defaults when asked.</summary>
    public bool Save()
    {
        if (!CanSave) return false;

        var owner = Guard(() => _session.Backend.CheckEditable(null));
        if (owner is not null)
        {
            _dialogs.ShowMessage(Strings.Settings_NotEditableTitle, owner);
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
            _dialogs.ShowMessage(Strings.Settings_NotSavedTitle, result.Message ?? Strings.Settings_NotSaved);
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
                _dialogs.ShowMessage(Strings.Settings_DefaultsNotSavedTitle, Strings.Settings_DefaultsNotSaved(ex.Message));
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
            result.Succeeded ? Strings.Settings_RegeneratedTitle : Strings.Settings_NotRegeneratedTitle,
            result.Message ?? (result.Succeeded ? Strings.Settings_Regenerated : Strings.Settings_NotRegenerated));
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
        s.ChartLanguage = ChartLanguage.Value;
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
            return Strings.Settings_OwnerCheckFailed(ex.Message);
        }
    }
}
