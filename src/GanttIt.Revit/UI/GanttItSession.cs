using GanttIt.Core.Editing;
using GanttIt.Core.Layout;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Preview;

namespace GanttIt.Revit.UI;

/// <summary>Builds the preview's text measurer from calibrated metrics (the command supplies <c>WpfTextMeasurer</c>).</summary>
public delegate ITextMeasurer TextMeasurerFactory(IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> metrics);

/// <summary>
/// State shared by every screen of one Gantt-It window: the backend, the
/// current settings and the calibrated preview context.
/// </summary>
public sealed class GanttItSession
{
    private readonly TextMeasurerFactory _measurerFactory;
    private PreviewContext? _preview;

    public GanttItSession(IGanttItBackend backend, TextMeasurerFactory measurerFactory, IDraftStore drafts)
    {
        Backend = backend;
        _measurerFactory = measurerFactory;
        Drafts = drafts;
        Settings = LoadSettings();
    }

    public IGanttItBackend Backend { get; }

    public IDraftStore Drafts { get; }

    /// <summary>The color picker's recent colors, kept for the Revit session (§2.4).</summary>
    public static RecentColors RecentColors { get; } = new();

    public SettingsState Settings { get; private set; }

    /// <summary>Reads settings again after Settings saved them; the next preview recalibrates.</summary>
    public void ReloadSettings()
    {
        Settings = LoadSettings();
        _preview = null;
    }

    /// <summary>
    /// The calibrated preview context. Calibration (a rolled-back transaction
    /// in Revit) runs on first use and again after every settings change
    /// (decision #14). The window is modal, so text types can't change in between.
    /// </summary>
    public PreviewContext Preview => _preview ??= Calibrate();

    private SettingsState LoadSettings()
    {
        try
        {
            return Backend.LoadSettings();
        }
        catch (Exception ex)
        {
            return new SettingsState(new Core.Model.ProjectSettings(), false, [], $"The settings could not be read: {ex.Message}");
        }
    }

    private PreviewContext Calibrate()
    {
        IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>? calibrated = null;
        string? note = null;
        try
        {
            calibrated = Backend.CalibrateTextStyles(Settings.Settings);
        }
        catch (Exception ex)
        {
            note = $"Preview text sizes are approximate: the text styles could not be calibrated ({ex.Message}).";
        }

        var metrics = FallbackMetrics.Complete(calibrated, out var approximated);
        if (approximated)
            note ??= "Preview text sizes are approximate until every text style is chosen in Settings.";

        ITextMeasurer measurer;
        try
        {
            measurer = _measurerFactory(metrics);
        }
        catch (Exception ex)
        {
            measurer = new UnavailableMeasurer(ex.Message);
        }
        return new PreviewContext(metrics, measurer, note);
    }

    /// <summary>Makes every layout fail with a readable message when the measurer can't be built.</summary>
    private sealed class UnavailableMeasurer(string reason) : ITextMeasurer
    {
        public IReadOnlyList<TextSize> Measure(IReadOnlyList<TextRequest> requests) =>
            throw new InvalidOperationException($"the preview measurer is unavailable ({reason}).");
    }
}
