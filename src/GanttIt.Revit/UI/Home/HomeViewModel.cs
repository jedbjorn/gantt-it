using System.Collections.ObjectModel;
using System.Windows.Input;
using GanttIt.Core.Editing;
using GanttIt.Core.Layout;
using GanttIt.Core.Serialization;
using GanttIt.Revit.Resources;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Home;

/// <summary>The "New chart" card, always first (§2.1).</summary>
public sealed class NewChartCardViewModel(ICommand newChart)
{
    public ICommand NewChartCommand { get; } = newChart;

    public string AutomationId => "Home.NewChartCard";

    public string AutomationName => Strings.Home_NewChart;
}

/// <summary>One chart legend in the model (§2.1).</summary>
public sealed class ChartCardViewModel
{
    public ChartCardViewModel(MainViewModel main, ChartEntry entry, ChartLayout? thumbnail,
        IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> metrics, string dateFormat)
    {
        Entry = entry;
        Name = entry.ViewName;
        Thumbnail = thumbnail;
        Metrics = metrics;
        PlacementText = ChartSummary.DescribePlacement(entry.Sheets.Count);

        var chart = entry.Load.Value;
        SpanText = chart is null ? "" : ChartSummary.DescribeSpan(chart, dateFormat);
        SectionsText = chart is null ? "" : ChartSummary.DescribeSections(chart.Sections.Count);
        CanEdit = entry.Load.IsEditable && chart is not null;
        // A chart from a newer Gantt-It is read-only here (§3, Beyond Intention): no Edit, no Delete.
        CanDelete = entry.Load.Status != LoadStatus.NewerVersion;
        StatusText = entry.Load.Status switch
        {
            LoadStatus.NewerVersion => entry.Load.Error ?? Strings.Card_NewerVersion,
            LoadStatus.Invalid => Strings.Card_Invalid(entry.Load.Error),
            _ => null,
        };
        IsInvalid = entry.Load.Status == LoadStatus.Invalid;

        EditCommand = new RelayCommand(() => main.EditChart(entry), () => CanEdit);
        OpenViewCommand = new RelayCommand(() => main.OpenView(entry.ViewId));
        DeleteCommand = new RelayCommand(() => main.DeleteChart(entry.ViewId, entry.ViewName, entry.Sheets), () => CanDelete);
    }

    public ChartEntry Entry { get; }

    public string Name { get; }

    public string AutomationId => "Home.ChartCard";

    public string AutomationName => Name;

    public string SpanText { get; }

    public string SectionsText { get; }

    public string PlacementText { get; }

    public bool IsPlaced => Entry.Sheets.Count > 0;

    public string SheetsToolTip => IsPlaced ? string.Join("\n", Entry.Sheets) : Strings.Card_NotPlacedTip;

    public string ThumbnailName => Strings.Card_ThumbnailName(Name);

    public ChartLayout? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    public IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> Metrics { get; }

    /// <summary>The newer-version or invalid-data message; null for an editable chart.</summary>
    public string? StatusText { get; }

    public bool IsInvalid { get; }

    public bool CanEdit { get; }

    public bool CanDelete { get; }

    public ICommand EditCommand { get; }

    public ICommand OpenViewCommand { get; }

    public ICommand DeleteCommand { get; }

    public string EditName => Strings.Card_EditName(Name);

    public string OpenViewName => Strings.Card_OpenViewName(Name);

    public string DeleteName => Strings.Card_DeleteName(Name);
}

/// <summary>Home (§2.1): New chart, one card per chart, and the Settings gear.</summary>
public sealed class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private string? _status;
    private string? _error;

    public HomeViewModel(MainViewModel main, string? status)
    {
        _main = main;
        _status = status;
        NewChartCommand = new RelayCommand(main.NewChart);
        SettingsCommand = new RelayCommand(OpenSettings);
        Load();
    }

    public ObservableCollection<object> Cards { get; } = [];

    public ICommand NewChartCommand { get; }

    public ICommand SettingsCommand { get; }

    /// <summary>The outcome of the last action (e.g. "Generated "Phase 1"."), for the user and the VM driver.</summary>
    public string? Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    /// <summary>A problem reading the charts or the settings.</summary>
    public string? Error
    {
        get => _error;
        private set => SetField(ref _error, value);
    }

    public string? SettingsWarning
    {
        get
        {
            var state = _main.Session.Settings;
            if (_main.Session.SettingsReadOnly) return null; // the Error banner says why; nothing to replace
            if (!state.Stored) return Strings.Home_NoSettings;
            return state.MissingStyles.Count == 0
                ? null
                : Strings.Home_MissingStyles(string.Join(", ", state.MissingStyles));
        }
    }

    public bool HasCharts => Cards.Count > 1;

    private void OpenSettings()
    {
        if (!_main.OpenSettings()) return;
        Status = Strings.Status_SettingsSaved;
        Load();
    }

    private void Load()
    {
        Cards.Clear();
        Cards.Add(new NewChartCardViewModel(NewChartCommand));

        var session = _main.Session;
        Error = session.Settings.Error;

        IReadOnlyList<ChartEntry> entries;
        try
        {
            entries = session.Backend.ListCharts();
        }
        catch (Exception ex)
        {
            Error = Strings.Home_ListFailed(ex.Message);
            entries = [];
        }

        var settings = session.Settings.Settings;
        var preview = entries.Count > 0 ? session.Preview : null;
        foreach (var entry in entries.OrderBy(e => e.ViewName, StringComparer.CurrentCultureIgnoreCase))
        {
            var thumbnail = preview is not null && entry.Load.Value is { } chart
                ? preview.TryLayout(chart, settings, out _)
                : null;
            Cards.Add(new ChartCardViewModel(_main, entry, thumbnail, preview?.Metrics ?? FallbackMetricsOnly(), settings.DateFormat));
        }

        OnPropertyChanged(nameof(HasCharts));
        OnPropertyChanged(nameof(SettingsWarning));
    }

    private static IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> FallbackMetricsOnly() =>
        Preview.FallbackMetrics.Complete(null, out _);
}
