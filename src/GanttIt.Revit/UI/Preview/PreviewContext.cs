using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Validation;
using GanttIt.Revit.Resources;
using GanttIt.Revit.Services;

namespace GanttIt.Revit.UI.Preview;

/// <summary>
/// A calibrated preview measurer and the metrics it was built from (§6.1,
/// decision #14). The renderer draws text with the same metrics, so the
/// preview's text fits the cells the layout engine sized for it.
/// </summary>
/// <param name="Note">Shown under the preview when the metrics are approximate.</param>
public sealed record PreviewContext(
    IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> Metrics,
    ITextMeasurer Measurer,
    string? Note)
{
    /// <summary>
    /// Runs the shared layout engine (§6). Never throws: a chart or settings
    /// the engine can't lay out yields null and a message for the preview area.
    /// </summary>
    public ChartLayout? TryLayout(Chart chart, ProjectSettings settings, out string? message)
    {
        message = null;
        var settingsIssues = ChartValidator.ValidateSettings(settings);
        if (settingsIssues.Count > 0)
        {
            message = Strings.Preview_None(settingsIssues[0].Message);
            return null;
        }
        if (!(chart.TimelineWidth > 0) || !(chart.NameMaxWidth > 0))
        {
            message = Strings.Preview_WidthsZero;
            return null;
        }

        try
        {
            return LayoutEngine.Layout(chart, settings, Measurer);
        }
        catch (Exception ex)
        {
            message = Strings.Preview_None(ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Stand-in metrics for text styles the calibration could not provide (no
/// settings yet, or a style that no longer exists). Arial at common Revit
/// sizes; the preview says it is approximate.
/// </summary>
public static class FallbackMetrics
{
    private static readonly IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> Defaults =
        new Dictionary<TextStyleKey, TextStyleMetrics>
        {
            [TextStyleKey.Title] = Make(5.0, bold: true),
            [TextStyleKey.Header] = Make(2.5, bold: true),
            [TextStyleKey.Body] = Make(2.5, bold: false),
        };

    /// <summary>Calibrated entries where present, defaults for the rest.</summary>
    public static IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> Complete(
        IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>? calibrated, out bool approximated)
    {
        approximated = false;
        var result = new Dictionary<TextStyleKey, TextStyleMetrics>();
        foreach (var key in Enum.GetValues<TextStyleKey>())
        {
            if (calibrated is not null && calibrated.TryGetValue(key, out var m) && IsUsable(m))
            {
                result[key] = m;
            }
            else
            {
                result[key] = Defaults[key];
                approximated = true;
            }
        }
        return result;
    }

    private static bool IsUsable(TextStyleMetrics m) =>
        !string.IsNullOrWhiteSpace(m.FontName)
        && m.TextSizeFt > 0 && double.IsFinite(m.TextSizeFt)
        && m.WidthFactor > 0 && double.IsFinite(m.WidthFactor)
        && m.WidthCalibration > 0 && double.IsFinite(m.WidthCalibration)
        && m.LinePitchFt > 0 && double.IsFinite(m.LinePitchFt);

    private static TextStyleMetrics Make(double sizeMm, bool bold) =>
        new("Arial", Units.FromMillimeters(sizeMm), bold, false, 1.0, 1.0, Units.FromMillimeters(sizeMm * 1.6));
}
