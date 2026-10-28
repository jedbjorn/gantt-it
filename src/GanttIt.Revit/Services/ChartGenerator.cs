using Autodesk.Revit.DB;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Serialization;
using GanttIt.Core.Validation;
using RevitLine = Autodesk.Revit.DB.Line;

namespace GanttIt.Revit.Services;

/// <summary>One element Gantt-It drew, with the primitive it came from.</summary>
internal sealed record DrawnElement(ElementId Id, string UniqueId, Primitive Primitive);

/// <summary>What one chart draw produced (also read by the debug Self-Test).</summary>
internal sealed record DrawResult(
    ChartLayout Layout,
    IReadOnlyList<DrawnElement> Elements,
    ResolvedStyles Styles,
    int MeasuredNotes,
    int UnusedCandidatesDeleted,
    int SkippedShortLines,
    int WidenedBars)
{
    public IEnumerable<string> UniqueIds => Elements.Select(e => e.UniqueId);
}

/// <summary>
/// Generate / Update / Delete / Regenerate all (§7.4). Every action is refused
/// before any transaction starts when the chart or settings are invalid, a style
/// is missing, or another user owns what would change (§7.5). Otherwise it runs in
/// one transaction named <c>Gantt-It: &lt;action&gt; &lt;name&gt;</c> (one Undo step)
/// that is rolled back completely on any failure. Update and Delete only ever
/// delete this view's own generated elements (§7.2 OwnerViewId rule).
/// </summary>
internal sealed class ChartGenerator
{
    private readonly Document _doc;

    public ChartGenerator(Document doc)
    {
        _doc = doc;
    }

#if DEBUG
    /// <summary>The last successful draw, for the Self-Test's read-back checks.</summary>
    internal static DrawResult? LastDraw { get; private set; }
#endif

    public const string NoLegendMessage =
        "Revit's API can't create a legend view from scratch, so Gantt-It copies an existing legend. "
        + "This model has none yet. Create one with View › Legends › Legend (any name and scale), then press Generate again. "
        + "Your chart stays in the editor.";

    // ------------------------------------------------------------------ actions

    public ActionResult Generate(Chart chart, ProjectSettings settings)
    {
        chart.Name = chart.Name?.Trim() ?? "";
        if (Refuse(chart, settings, null, out var styles) is { } refused) return refused;

        var source = LegendFactory.FindSource(_doc);
        if (source is null) return new ActionResult(ActionStatus.NoLegend, NoLegendMessage);

        // Defensive: a new chart never shares an Id with a live chart.
        if (ChartStore.Live(_doc).Any(c => ChartJson.Deserialize(c.ChartJson).Value?.Id == chart.Id)) chart.Id = Guid.NewGuid();
        return GanttItTransaction.Run(_doc, GanttItTransaction.Name("Generate", chart.Name), () =>
        {
            var view = LegendFactory.Create(_doc, source, chart.Name);
            var drawn = Draw(view, chart, settings, styles!, new FillTypes(_doc));
            ChartStore.Write(view, chart, drawn.UniqueIds);
            return (view.Id, Note(drawn));
        });
    }

    public ActionResult Update(ElementId viewId, Chart chart, ProjectSettings settings)
    {
        chart.Name = chart.Name?.Trim() ?? "";
        if (LoadChartView(viewId, out var view, out var stored) is { } notChart) return notChart;
        if (ChartJson.Deserialize(stored!.ChartJson).Status == LoadStatus.NewerVersion) return Blocked(NewerChartMessage);
        if (Refuse(chart, settings, view, out var styles) is { } refused) return refused;

        return GanttItTransaction.Run(_doc, GanttItTransaction.Name("Update", chart.Name), () =>
        {
            var drawn = Redraw(view!, stored, chart, settings, styles!, new FillTypes(_doc));
            FillTypes.DeleteUnused(_doc);
            return (view!.Id, Note(drawn));
        });
    }

    public ActionResult Delete(ElementId viewId)
    {
        if (LoadChartView(viewId, out var view, out var stored) is { } notChart) return notChart;
        if (ChartJson.Deserialize(stored!.ChartJson).Status == LoadStatus.NewerVersion)
            return Blocked(NewerChartMessage + " It can still be deleted by hand in Revit.");
        if (EditableProblem(view) is { } owned) return Blocked(owned);

        var name = view!.Name;
        return GanttItTransaction.Run(_doc, GanttItTransaction.Name("Delete", name), () =>
        {
            // Deleting the legend removes its own elements and its viewports (V8).
            _doc.Delete(viewId);
            var freed = FillTypes.DeleteUnused(_doc);
            return (null, freed.Count > 0 ? $"Removed unused fill types: {string.Join(", ", freed)}." : null);
        });
    }

    public ActionResult RegenerateAll(ProjectSettings settings)
    {
        if (settings.SchemaVersion > SchemaVersions.Settings)
            return Blocked("The settings were written by a newer version of Gantt-It and are read-only here.");
        var styles = StyleReader.Resolve(_doc, settings, out var styleProblem);
        if (styles is null) return Blocked(styleProblem!);
        if (ChartValidator.ValidateSettings(settings) is { Count: > 0 } settingIssues)
            return Blocked(string.Join(" ", settingIssues.Select(i => i.Message)));

        var skipped = new List<string>();
        var work = new List<(View View, StoredChart Stored, Chart Chart)>();
        foreach (var stored in ChartStore.Live(_doc))
        {
            var load = ChartJson.Deserialize(stored.ChartJson);
            if (!load.IsEditable || load.Value is null)
            {
                // Newer-version charts are read-only (§7.2); unreadable ones are reported.
                skipped.Add($"\"{stored.View.Name}\" ({load.Error})");
                continue;
            }
            load.Value.Name = stored.View.Name;
            work.Add((stored.View, stored, load.Value));
        }

        var ready = new List<(View View, StoredChart Stored, Chart Chart)>();
        foreach (var w in work)
        {
            var issues = ChartValidator.Validate(w.Chart, settings, RevitUtil.OtherViewNames(_doc, w.View.Id));
            if (issues.Count > 0) skipped.Add($"\"{w.View.Name}\" ({issues[0].Message})");
            else ready.Add(w);
        }
        if (ready.Count == 0)
            return new ActionResult(ActionStatus.Ok, skipped.Count > 0 ? "No chart could be regenerated: " + string.Join("; ", skipped) : "There are no charts to regenerate.");

        foreach (var w in ready)
            if (EditableProblem(w.View) is { } owned) return Blocked(owned);

        return GanttItTransaction.Run(_doc, GanttItTransaction.Name("Regenerate all charts", null), () =>
        {
            var fills = new FillTypes(_doc);
            foreach (var w in ready) Redraw(w.View, w.Stored, w.Chart, settings, styles, fills);
            FillTypes.DeleteUnused(_doc);
            var note = $"Regenerated {ready.Count} chart{(ready.Count == 1 ? "" : "s")}.";
            if (skipped.Count > 0) note += " Skipped: " + string.Join("; ", skipped) + ".";
            return (null, note);
        });
    }

    // ------------------------------------------------------------------ checks

    /// <summary>Null when the chart can be drawn; otherwise the Blocked result (§2.2, §2.3, §7.5).</summary>
    private ActionResult? Refuse(Chart chart, ProjectSettings settings, View? self, out ResolvedStyles? styles)
    {
        styles = null;
        if (chart.SchemaVersion > SchemaVersions.Chart)
            return Blocked("This chart was written by a newer version of Gantt-It and is read-only here.");
        if (settings.SchemaVersion > SchemaVersions.Settings)
            return Blocked("The settings were written by a newer version of Gantt-It and are read-only here.");

        var issues = ChartValidator.Validate(chart, settings, RevitUtil.OtherViewNames(_doc, self?.Id));
        if (issues.Count > 0) return Blocked(string.Join(" ", issues.Select(i => i.Message).Distinct()));

        styles = StyleReader.Resolve(_doc, settings, out var problem);
        if (styles is null) return Blocked(problem!);

        return EditableProblem(self) is { } owned ? Blocked(owned) : null;
    }

    /// <summary>
    /// §7.5: the settings DataStorage, and for an existing chart its legend view and
    /// the elements Gantt-It drew in it, must not be owned by another user.
    /// </summary>
    public string? EditableProblem(View? view)
    {
        if (!_doc.IsWorkshared) return null;
        var ids = new List<ElementId>();
        if (ChartStore.SettingsStorage(_doc) is { } storage) ids.Add(storage.Id);
        if (view is not null)
        {
            ids.Add(view.Id);
            if (ChartStore.Read(view) is { } stored) ids.AddRange(ChartStore.OwnedGeneratedIds(_doc, view, stored.GeneratedIds));
        }
        return Worksharing.CheckEditable(_doc, ids);
    }

    /// <summary>
    /// Update and Delete act on live charts only (§7.2 v1.1, decision #17). A detached
    /// legend (a Revit duplicate, a legend pasted from another model, or a chart whose
    /// generated elements were all deleted by hand) is a plain legend: Blocked, even when
    /// the caller still holds its ViewId.
    /// </summary>
    private ActionResult? LoadChartView(ElementId viewId, out View? view, out StoredChart? stored)
    {
        view = _doc.GetElement(viewId) as View;
        stored = view is null ? null : ChartStore.Read(view);
        if (stored is null) return Blocked("That view is not a Gantt-It chart (or it no longer exists).");
        if (!ChartStore.IsLive(_doc, stored))
            return Blocked($"\"{view!.Name}\" is a plain legend, not a live Gantt-It chart: it holds none of the elements "
                           + "Gantt-It drew (a copy made in Revit, or a chart whose elements were deleted). Use New chart instead.");
        return null;
    }

    private const string NewerChartMessage = "This chart was written by a newer version of Gantt-It and is read-only here.";

    private static ActionResult Blocked(string message) => new(ActionStatus.Blocked, message);

    private static string? Note(DrawResult drawn) =>
        drawn.WidenedBars > 0
            ? $"{drawn.WidenedBars} very short bar(s) were widened to Revit's minimum line length."
            : null;

    // ------------------------------------------------------------------ drawing

    /// <summary>Update in place: delete this view's own generated elements, redraw, store (D8).</summary>
    private DrawResult Redraw(View view, StoredChart stored, Chart chart, ProjectSettings settings, ResolvedStyles styles, FillTypes fills)
    {
        if (!string.Equals(view.Name, chart.Name, StringComparison.Ordinal)) view.Name = chart.Name;
        LegendFactory.EnsureScaleOne(view);

        var own = ChartStore.OwnedGeneratedIds(_doc, view, stored.GeneratedIds);
        if (own.Count > 0) _doc.Delete(own);

        var drawn = Draw(view, chart, settings, styles, fills);
        ChartStore.Write(view, chart, drawn.UniqueIds);
        return drawn;
    }

    /// <summary>
    /// Measures, lays out and draws the chart into <paramref name="view"/> (§6.1).
    /// Must run inside the action's transaction.
    /// </summary>
    internal DrawResult Draw(View view, Chart chart, ProjectSettings settings, ResolvedStyles styles, FillTypes fills)
    {
        var measurer = new RevitTextMeasurer(_doc, view, styles.TextTypes);
        var layout = LayoutEngine.Layout(chart, settings, measurer);
        var measured = measurer.NotesCreated;
        var tolerance = _doc.Application.ShortCurveTolerance;
        var elements = new List<DrawnElement>(layout.Primitives.Count);
        int skipped = 0, widened = 0;

        foreach (var bar in layout.Primitives.OfType<FilledRectPrimitive>())
        {
            var rect = bar.Rect;
            if (rect.Width < tolerance * 1.01 || rect.Height < tolerance * 1.01)
            {
                rect = Widen(rect, tolerance * 1.01, layout.Grid);
                widened++;
            }
            var region = FilledRegion.Create(_doc, fills.Get(bar.Color), view.Id, [Rectangle(rect)]);
            region.SetLineStyleId(fills.InvisibleLines);
            elements.Add(new DrawnElement(region.Id, region.UniqueId, bar));
        }

        foreach (var line in layout.Primitives.OfType<LinePrimitive>())
        {
            var a = new XYZ(line.Start.X, line.Start.Y, 0);
            var b = new XYZ(line.End.X, line.End.Y, 0);
            if (a.DistanceTo(b) < tolerance)
            {
                skipped++; // §6.3 says this can't occur; never fail a chart over it.
                continue;
            }
            var curve = _doc.Create.NewDetailCurve(view, RevitLine.CreateBound(a, b));
            curve.LineStyle = _doc.GetElement(styles.Line(line.Style));
            elements.Add(new DrawnElement(curve.Id, curve.UniqueId, line));
        }

#if DEBUG
        SelfTest.DebugFaults.Hit(SelfTest.DebugFaults.MidGenerate);
#endif

        foreach (var text in layout.Primitives.OfType<TextPrimitive>())
        {
            var note = measurer.Place(text);
            elements.Add(new DrawnElement(note.Id, note.UniqueId, text));
        }
        var unused = measurer.DeleteUnused();

        var result = new DrawResult(layout, elements, styles, measured, unused, skipped, widened);
#if DEBUG
        LastDraw = result;
#endif
        return result;
    }

    private static CurveLoop Rectangle(Box r)
    {
        var p0 = new XYZ(r.Left, r.Bottom, 0);
        var p1 = new XYZ(r.Right, r.Bottom, 0);
        var p2 = new XYZ(r.Right, r.Top, 0);
        var p3 = new XYZ(r.Left, r.Top, 0);
        return CurveLoop.Create(new List<Curve>
        {
            RevitLine.CreateBound(p0, p1),
            RevitLine.CreateBound(p1, p2),
            RevitLine.CreateBound(p2, p3),
            RevitLine.CreateBound(p3, p0),
        });
    }

    /// <summary>
    /// A bar narrower than Revit's short-curve tolerance can't be drawn (a one-day
    /// section on a decades-long axis). It is widened to the minimum, kept inside
    /// the timeline.
    /// </summary>
    private static Box Widen(Box r, double min, GridLayout grid)
    {
        var left = r.Left;
        var right = r.Right;
        if (right - left < min)
        {
            right = Math.Min(left + min, grid.Width);
            left = Math.Max(grid.TimelineLeft, right - min);
        }
        var bottom = r.Bottom;
        var top = r.Top;
        if (top - bottom < min)
        {
            var mid = (top + bottom) / 2;
            bottom = mid - min / 2;
            top = mid + min / 2;
        }
        return new Box(left, bottom, right, top);
    }
}
