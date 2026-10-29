using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Serialization;
using GanttIt.Core.Validation;

namespace GanttIt.Revit.Services;

/// <summary>
/// The Revit side of the UI contract (lane C1). Every call works on the active
/// document of <see cref="UIApplication"/> and runs in the command's API context (D7).
/// </summary>
public sealed class RevitBackend : IGanttItBackend
{
    private readonly UIApplication _uiapp;

    public RevitBackend(UIApplication uiapp)
    {
        _uiapp = uiapp ?? throw new ArgumentNullException(nameof(uiapp));
    }

    private Document Doc => _uiapp.ActiveUIDocument?.Document
                            ?? throw new InvalidOperationException("Gantt-It needs an open project.");

    private ChartGenerator Generator => new(Doc);

    // ------------------------------------------------------------------ units (D11)

    public string FormatLength(double feet) => LengthFormat.Format(Doc, feet);

    public bool TryParseLength(string text, out double feet) => LengthFormat.TryParse(Doc, text, out feet);

    // ------------------------------------------------------------------ styles (§2.3)

    public IReadOnlyList<StyleOption> TextStyles() => StyleReader.TextStyles(Doc);

    public IReadOnlyList<StyleOption> LineStyles() => StyleReader.LineStyles(Doc);

    // ------------------------------------------------------------------ settings (§2.3, §7.2)

    public SettingsState LoadSettings()
    {
        var doc = Doc;
        var json = ChartStore.ReadSettingsJson(doc);
        if (json is null)
        {
            var defaults = DefaultSettings(doc);
            return new SettingsState(defaults, false, StyleReader.Missing(doc, defaults), null);
        }

        var load = SettingsJson.Deserialize(json);
        switch (load.Status)
        {
            case LoadStatus.Ok:
            {
                var settings = load.Value!;
                StyleReader.Refresh(doc, settings);
                return new SettingsState(settings, true, StyleReader.Missing(doc, settings), null);
            }
            case LoadStatus.NewerVersion:
            {
                var settings = load.Value ?? DefaultSettings(doc);
                return new SettingsState(settings, true, StyleReader.Missing(doc, settings), load.Error);
            }
            default:
            {
                // Unreadable settings: the UI opens Settings so they can be saved again.
                var settings = DefaultSettings(doc);
                return new SettingsState(settings, false, StyleReader.Missing(doc, settings),
                    $"The stored Gantt-It settings could not be read and will be replaced when saved. {load.Error}");
            }
        }
    }

    public ActionResult SaveSettings(ProjectSettings settings)
    {
        var doc = Doc;
        if (settings.SchemaVersion > SchemaVersions.Settings)
            return new ActionResult(ActionStatus.Blocked, "These settings were written by a newer version of Gantt-It and are read-only here.");
        if (ChartStore.ReadSettingsJson(doc) is { } stored && SettingsJson.Deserialize(stored).Status == LoadStatus.NewerVersion)
            return new ActionResult(ActionStatus.Blocked, "The model's settings were written by a newer version of Gantt-It and are read-only here.");
        if (ChartValidator.ValidateSettings(settings) is { Count: > 0 } issues)
            return new ActionResult(ActionStatus.Blocked, string.Join(" ", issues.Select(i => i.Message)));
        if (CheckEditable(null) is { } owned)
            return new ActionResult(ActionStatus.Blocked, owned);

        return GanttItTransaction.Run(doc, GanttItTransaction.Name("Save settings", null), () =>
        {
            ChartStore.WriteSettings(doc, settings);
            return (null, null);
        });
    }

    /// <summary>Never throws into the UI: a file that can't be written is traced and skipped.</summary>
    public void SaveUserDefaults(ProjectSettings settings) => UserDefaults.TrySave(settings);

    /// <summary>
    /// Settings for a model that has none: the per-user default names first, then
    /// the model's default text type and the built-in Thin/Medium line styles
    /// (found by category, never by name).
    /// </summary>
    internal static ProjectSettings DefaultSettings(Document doc)
    {
        var names = UserDefaults.Load();
        var textTypes = StyleReader.TextTypes(doc);
        var defaultText = doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType
                          ?? textTypes.FirstOrDefault();

        TextNoteType? Text(string? name) =>
            (name is null ? null : textTypes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))) ?? defaultText;

        var lineStyles = StyleReader.LineGraphicsStyles(doc);
        GraphicsStyle? Line(string? name, BuiltInCategory fallback) =>
            (name is null ? null : lineStyles.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.Ordinal)))
            ?? StyleReader.BuiltInLineStyle(doc, fallback);

        return new ProjectSettings
        {
            TitleTextStyle = StyleReader.RefOf(Text(names?.TitleTextStyle)),
            HeaderTextStyle = StyleReader.RefOf(Text(names?.HeaderTextStyle)),
            BodyTextStyle = StyleReader.RefOf(Text(names?.BodyTextStyle)),
            ChartLineStyle = StyleReader.RefOf(Line(names?.ChartLineStyle, BuiltInCategory.OST_CurvesThinLines)),
            HeaderLineStyle = StyleReader.RefOf(Line(names?.HeaderLineStyle, BuiltInCategory.OST_CurvesMediumLines)),
        };
    }

    // ------------------------------------------------------------------ charts (§2.1, §7.2)

    public IReadOnlyList<ChartEntry> ListCharts()
    {
        var doc = Doc;
        // Live charts only (§7.2 v1.1): detached copies are plain legends and never listed.
        var loaded = ChartStore.Live(doc)
            .Select(s => (Stored: s, Load: ChartJson.Deserialize(s.ChartJson)))
            .ToList();

        foreach (var (stored, load) in loaded)
            if (load.Value is not null) load.Value.Name = stored.View.Name; // the legend's name is the chart's name

        return loaded
            .Select(l => new ChartEntry(l.Stored.View.Id, l.Stored.View.Name, l.Load, RevitUtil.SheetsShowing(doc, l.Stored.View.Id)))
            .ToList();
    }

    public IReadOnlyCollection<string> OtherViewNames(ElementId? exceptViewId) => RevitUtil.OtherViewNames(Doc, exceptViewId);

    public IReadOnlyDictionary<TextStyleKey, TextStyleMetrics> CalibrateTextStyles(ProjectSettings settings) =>
        TextCalibration.Calibrate(Doc, settings);

    public string? CheckEditable(ElementId? chartViewId)
    {
        var doc = Doc;
        View? view = null;
        if (chartViewId is not null && chartViewId != ElementId.InvalidElementId)
        {
            view = doc.GetElement(chartViewId) as View;
            if (view is null) return "The chart's legend view no longer exists.";
        }
        return Generator.EditableProblem(view);
    }

    // ------------------------------------------------------------------ actions (§7.4)

    public ActionResult Generate(Chart chart, ProjectSettings settings) => Generator.Generate(chart, settings);

    public ActionResult Update(ElementId viewId, Chart chart, ProjectSettings settings) => Generator.Update(viewId, chart, settings);

    /// <summary>
    /// Delete (§7.4) with the active-view step around it (<see cref="ChartViewExit"/>):
    /// the chart stops being the active view right before the transaction, and anything
    /// but a successful delete puts the user back on the view they were on.
    /// </summary>
    public ActionResult Delete(ElementId viewId)
    {
        var exit = new ChartViewExit(_uiapp.ActiveUIDocument, viewId);
        ActionResult result;
        try
        {
            result = Generator.Delete(viewId, exit.Leave);

            // Revit closes a deleted view's windows itself; only if a delete fails while the
            // chart is still open in a window are those windows closed for one retry. Known
            // trade-off: a genuine failure (not caused by the open windows) then also closes
            // them, and Restore reopens the chart in a single window, so a tiled or
            // replicated layout of the chart is not rebuilt.
            if (result.Status == ActionStatus.Failed && exit.ChartOpenInWindow)
            {
                result = exit.CloseChartWindows()
                    ? Generator.Delete(viewId, exit.Leave)
                    : result;
            }
        }
        catch (Exception ex)
        {
            result = new ActionResult(ActionStatus.Failed, $"Delete failed: {ex.Message}");
        }

        if (!result.Succeeded && exit.Restore() is { } notBack)
            result = result with { Message = string.IsNullOrWhiteSpace(result.Message) ? notBack : $"{result.Message} {notBack}" };
        return result;
    }

    public ActionResult RegenerateAll(ProjectSettings settings) => Generator.RegenerateAll(settings);
}
