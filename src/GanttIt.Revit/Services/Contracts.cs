using Autodesk.Revit.DB;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Serialization;

namespace GanttIt.Revit.Services;

// The boundary between the Revit backend (lane C1) and the WPF UI (lane C2).
// Both lanes commit this file byte-for-byte; change it only through the Planner.

/// <summary>A text or line style the user can pick in Settings (§2.3).</summary>
public sealed record StyleOption(string UniqueId, string Name);

/// <summary>
/// Calibrated metrics for one text style (decision #14). The UI's preview measurer
/// scales WPF measurements by <see cref="WidthCalibration"/> and uses
/// <see cref="LinePitchFt"/> as the line height.
/// </summary>
public sealed record TextStyleMetrics(
    string FontName,
    double TextSizeFt,
    bool Bold,
    bool Italic,
    double WidthFactor,
    double WidthCalibration,
    double LinePitchFt);

/// <summary>One live chart legend in the active document (§2.1, §7.2). Detached copies are never listed.</summary>
/// <param name="Load">The stored chart.</param>
/// <param name="Sheets">"Number - Name" of every sheet the legend is placed on, empty when not placed.</param>
public sealed record ChartEntry(ElementId ViewId, string ViewName, LoadResult<Chart> Load, IReadOnlyList<string> Sheets);

/// <param name="Settings">The stored settings, or defaults matched by the per-user default names when none are stored.</param>
/// <param name="Stored">Whether the model already holds settings. False means the UI opens Settings before the first New/Generate (§2.1).</param>
/// <param name="MissingStyles">Human-readable names of setting fields whose style no longer exists (§2.3, shown in red).</param>
public sealed record SettingsState(ProjectSettings Settings, bool Stored, IReadOnlyList<string> MissingStyles, string? Error);

public enum ActionStatus
{
    Ok,
    /// <summary>No legend exists to duplicate (§7.1): the UI shows the explanatory modal and keeps the editor open.</summary>
    NoLegend,
    /// <summary>Refused before any transaction: owned by someone else (§7.5), missing styles, invalid chart, or newer schema.</summary>
    Blocked,
    /// <summary>The transaction failed and was rolled back (§7.4).</summary>
    Failed,
}

public sealed record ActionResult(ActionStatus Status, string? Message = null, ElementId? ViewId = null)
{
    public bool Succeeded => Status == ActionStatus.Ok;
}

/// <summary>Everything the UI needs from Revit. All methods run in the command's API context (D7).</summary>
public interface IGanttItBackend
{
    string FormatLength(double feet);
    bool TryParseLength(string text, out double feet);

    IReadOnlyList<StyleOption> TextStyles();
    IReadOnlyList<StyleOption> LineStyles();

    SettingsState LoadSettings();
    ActionResult SaveSettings(ProjectSettings settings);
    /// <summary>Writes the style names (only) to %AppData%\GanttIt\defaults.json (§2.3).</summary>
    void SaveUserDefaults(ProjectSettings settings);

    IReadOnlyList<ChartEntry> ListCharts();
    /// <summary>Names of every view except <paramref name="exceptViewId"/>, for ChartValidator's collision check.</summary>
    IReadOnlyCollection<string> OtherViewNames(ElementId? exceptViewId);

    /// <summary>Measures one reference string per configured text style in a rolled-back transaction (decision #14).</summary>
    IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> CalibrateTextStyles(ProjectSettings settings);

    /// <summary>Null when editable; otherwise a message naming the owner (§7.5). Null view = only the settings DataStorage.</summary>
    string? CheckEditable(ElementId? chartViewId);

    ActionResult Generate(Chart chart, ProjectSettings settings);
    ActionResult Update(ElementId viewId, Chart chart, ProjectSettings settings);
    ActionResult Delete(ElementId viewId);
    ActionResult RegenerateAll(ProjectSettings settings);
}
