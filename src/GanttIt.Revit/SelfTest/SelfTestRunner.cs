#if DEBUG
using System.IO;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using GanttIt.Core.Serialization;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// The Gantt-It Self-Test (§8.1). Phases:
/// <list type="bullet">
/// <item><c>1</c>: settings, the fixture charts, read-back geometry (A5), chart
/// correctness (A6), preview calibration (A15), lengths in project units (D11),
/// Update on a sheet with a hand note (A7), detached copies and hand-deleted
/// charts (A9, decision #17), the fill-type purge (§7.3, <c>SelfTestRunner.Purge.cs</c>),
/// fault injection and the warning path (A11), PNG
/// exports, a snapshot for phase 2, save, and a final "GI Undo" chart for the GUI
/// Undo check.</item>
/// <item><c>2</c>: after save, close and reopen, the chart and settings read back
/// identical, the copies are still detached, and updating then deleting the
/// original leaves each copy's elements untouched (A9).</item>
/// <item><c>undo-check</c>: after one GUI Undo, the "GI Undo" chart is gone (A11).</item>
/// <item><c>place-sheets</c> / <c>a8-check</c>: the A8 helpers in <c>SelfTestRunner.Sheets.cs</c>.</item>
/// </list>
/// Every check records PASS/FAIL and its numbers in the report.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const string UndoChartName = "GI Undo";
    private const string FaultChartName = "GI Fault";
    private const string SheetName = "GI Self-Test Sheet";
    private const string CopyWName = "GI Days copy (with detailing)";
    private const string CopyPName = "GI Days copy (plain)";
    private const string AllDeletedName = "GI Hand-deleted";
    private const string PartDeletedName = "GI Partly deleted";
    private static readonly Rgb FaultColor = Rgb.Parse("#13579B");
    private static readonly Rgb UndoColor = Rgb.Parse("#FEDCBA");

    private readonly UIApplication _uiapp;
    private readonly Document _doc;
    private readonly RevitBackend _backend;
    private readonly JsonObject _args;
    private readonly string _stem;
    private readonly string _year;
    private readonly Report _r = new();
    private readonly List<FixtureRun> _runs = [];
    private ProjectSettings _settings = new();
    private ResolvedStyles? _styles;
    private ElementId? _sheetId;
    private ElementId? _viewportId;
    private JsonObject? _copies;

    /// <summary>Per-element geometry read back from Revit (§8.1), so A5 can be re-checked off the VM.</summary>
    private readonly JsonObject _geometry = [];

    private sealed record FixtureRun(Chart Chart, ElementId ViewId, DrawResult Draw);

    public SelfTestRunner(UIApplication uiapp, JsonObject args, string stem)
    {
        _uiapp = uiapp;
        _doc = uiapp.ActiveUIDocument.Document;
        _backend = new RevitBackend(uiapp);
        _args = args;
        _stem = stem;
        _year = uiapp.Application.VersionNumber;
    }

    private string SnapshotPath => Path.Combine(SelfTestHarness.OutDir, $"selftest-snapshot-{_year}-{_stem}.json");

    private string UndoPath => Path.Combine(SelfTestHarness.OutDir, $"selftest-undo-{_year}-{_stem}.json");

    public JsonObject Run(string phase)
    {
        var app = _uiapp.Application;
        _r.Root["meta"] = new JsonObject
        {
            ["phase"] = phase,
            ["at"] = DateTime.Now.ToString("o"),
            ["revit"] = $"{app.VersionNumber} {app.VersionBuild}",
            ["revitYear"] = app.VersionNumber,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["addinVersion"] = RevitUtil.AppVersion,
            ["document"] = _doc.PathName,
            ["documentTitle"] = _doc.Title,
            ["lengthUnit"] = _doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId,
            ["formatLength1_5mm"] = Safely(() => _backend.FormatLength(Core.Model.Units.FromMillimeters(1.5))),
            ["formatLength180mm"] = Safely(() => _backend.FormatLength(Core.Model.Units.FromMillimeters(180))),
            ["shortCurveToleranceMm"] = Report.Mm(app.ShortCurveTolerance),
            ["workshared"] = _doc.IsWorkshared,
        };

        _r.Section("geometry", _geometry);
        switch (phase)
        {
            case "1":
                Phase1();
                break;
            case "2":
                Phase2();
                break;
            case "undo-check":
                UndoCheck();
                break;
            case "place-sheets":
                _r.Step("A8", "place on sheets", PlaceOnSheets);
                break;
            case "a8-check":
                _r.Step("A8", "after Delete", CheckAfterDelete);
                break;
            default:
                _r.Check("setup", $"unknown phase '{phase}'", false);
                break;
        }
        return _r.Finish();
    }

    // ================================================================== phase 1

    private void Phase1()
    {
        var dialogsAtStart = SelfTestHarness.DialogLog.Count;

        _r.Step("setup", "cleanup", Cleanup);
        _r.Step("A9", "settings", SetupSettings);
        if (_styles is null) return;

        foreach (var chart in Fixtures.All())
            _r.Step("A6", $"generate {chart.Name}", () => GenerateFixture(chart));

        _r.Step("A15", "calibrated preview vs Revit", CheckCalibration);
        _r.Step("D11", "lengths in project units", CheckLengths);
        _r.Step("A7", "update on a sheet", CheckUpdateOnSheet);
        _r.Step("A9", "duplicated legends are detached", CheckCopiesDetached);
        _r.Step("§7.3", "fill-type purge", CheckFillPurge);
        _r.Step("A9", "hand-deleted charts", CheckHandDeleted);
        _r.Step("A11", "fault injection", CheckFaults);
        _r.Step("A11", "warning preprocessing", CheckWarningPath);
        _r.Step("A6", "list charts", CheckListCharts);
        _r.Step("evidence", "export images", ExportImages);
        _r.Step("A9", "snapshot", WriteSnapshot);

        var dialogs = SelfTestHarness.DialogLog.Count - dialogsAtStart;
        _r.Check("A11", "no Revit dialog or warning popup during any Gantt-It action", dialogs == 0,
            new JsonObject { ["dialogsDuringRun"] = dialogs });

        if (_args["save"]?.GetValue<bool>() != false) _r.Step("A9", "save", Save);
        _r.Step("A11", "undo preparation", PrepareUndo);
        Activate(_args["activate"]?.GetValue<string>() ?? Fixtures.Months);
    }

    /// <summary>Removes what an earlier Self-Test run left in this model, so a rerun starts clean.</summary>
    private void Cleanup()
    {
        var removed = new JsonArray();
        foreach (var entry in _backend.ListCharts().Where(e => e.ViewName.StartsWith("GI ", StringComparison.Ordinal)))
        {
            var r = _backend.Delete(entry.ViewId);
            removed.Add($"{entry.ViewName}: {r.Status}");
        }
        // Detached "GI " legends (copies, hand-deleted charts) are plain legends that the
        // backend refuses to delete; remove them directly so a rerun's names don't collide.
        var leftovers = RevitUtil.Legends(_doc).Where(v => v.Name.StartsWith("GI ", StringComparison.Ordinal)).Select(v => v.Id).ToList();
        if (leftovers.Count > 0) SelfTestTx("remove leftover legends", () => _doc.Delete(leftovers));
        var sheets = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.Name == SheetName).Select(s => s.Id).ToList();
        if (sheets.Count > 0) SelfTestTx("remove sheets", () => _doc.Delete(sheets));
        // What an aborted fill-type purge check left: its drafting view, group types, and
        // a Filled Region default still pointing at a probe type.
        var probes = 0;
        var defaultRestored = false;
        SelfTestTx("remove fill-type purge probes", () =>
        {
            defaultRestored = RestoreDefaultFillType(null);
            probes = RemovePurgeProbes();
        });
        _r.Section("cleanup", new JsonObject
        {
            ["charts"] = removed,
            ["detachedLegends"] = leftovers.Count,
            ["sheets"] = sheets.Count,
            ["purgeProbes"] = probes,
            ["purgeDefaultRestored"] = defaultRestored,
        });
    }

    /// <summary>
    /// Settings from the model's own styles: the three smallest distinct text types
    /// (body, header, title) and the built-in Thin / Medium line styles, found by
    /// category (never by name).
    /// </summary>
    private void SetupSettings()
    {
        var textTypes = StyleReader.TextTypes(_doc)
            .OrderBy(t => StyleReader.Font(t).SizeFt)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        if (textTypes.Count == 0) throw new InvalidOperationException("The model has no text types.");
        TextNoteType Pick(int i) => textTypes[Math.Min(i, textTypes.Count - 1)];

        var lines = _backend.LineStyles();
        StyleRef LineRef(BuiltInCategory category, int fallback)
        {
            var gs = StyleReader.BuiltInLineStyle(_doc, category);
            var option = gs is null ? null : lines.FirstOrDefault(o => o.UniqueId == gs.UniqueId);
            option ??= lines[Math.Min(fallback, lines.Count - 1)];
            return new StyleRef(option.UniqueId, option.Name);
        }

        var before = _backend.LoadSettings();
        _settings = new ProjectSettings
        {
            BodyTextStyle = StyleReader.RefOf(Pick(0)),
            HeaderTextStyle = StyleReader.RefOf(Pick(1)),
            TitleTextStyle = StyleReader.RefOf(Pick(2)),
            ChartLineStyle = LineRef(BuiltInCategory.OST_CurvesThinLines, 0),
            HeaderLineStyle = LineRef(BuiltInCategory.OST_CurvesMediumLines, 1),
        };

        var saved = _backend.SaveSettings(_settings);
        var after = _backend.LoadSettings();
        _styles = StyleReader.Resolve(_doc, _settings, out var problem);

        var same = SettingsJson.Serialize(after.Settings) == SettingsJson.Serialize(_settings);
        _r.Check("A9", "settings save and read back identical in session",
            saved.Succeeded && after.Stored && after.Error is null && after.MissingStyles.Count == 0 && same && _styles is not null,
            new JsonObject
            {
                ["storedBefore"] = before.Stored,
                ["saveStatus"] = saved.Status.ToString(),
                ["storedAfter"] = after.Stored,
                ["missingStyles"] = after.MissingStyles.Count,
                ["identical"] = same,
            },
            saved.Message ?? problem);

        _r.Section("settings", new JsonObject
        {
            ["json"] = SettingsJson.Serialize(_settings),
            ["textTypes"] = new JsonArray(textTypes.Select(t => (JsonNode)TypeInfo(t)).ToArray()),
            ["lineStyles"] = new JsonArray(lines.Select(o => (JsonNode)o.Name).ToArray()),
        });
    }

    private void GenerateFixture(Chart chart)
    {
        var result = _backend.Generate(chart, _settings);
        var ok = _r.Check("A6", $"{chart.Name}: Generate succeeds", result.Succeeded && result.ViewId is not null,
            new JsonObject { ["status"] = result.Status.ToString(), ["sections"] = chart.Sections.Count }, result.Message);
        if (!ok || ChartGenerator.LastDraw is not { } draw) return;

        var run = new FixtureRun(chart, result.ViewId!, draw);
        _runs.Add(run);
        CheckChart(run, "after Generate", []);
    }

    /// <summary>A5 (read-back geometry) and A6 (counts, styles, scale, fill types) for one chart.</summary>
    private void CheckChart(FixtureRun run, string when, HashSet<ElementId> handAdded)
    {
        var view = (View)_doc.GetElement(run.ViewId);
        var stored = ChartStore.Read(view)!;
        var byUid = run.Draw.Elements.ToDictionary(e => e.UniqueId, e => e.Primitive);
        CheckGeometry(view, stored.GeneratedIds, run.Draw.Layout.Grid.GridXs, run.Draw.Layout.Grid.GridYs, byUid, $"{run.Chart.Name} {when}");
        CheckContent(run, view, stored, byUid, handAdded, when);
    }

    // ------------------------------------------------------------------ A5

    private void CheckGeometry(
        View view, IReadOnlyList<string> generatedIds, IReadOnlyList<double> gridXs, IReadOnlyList<double> gridYs,
        IReadOnlyDictionary<string, Primitive>? primitives, string label)
    {
        var boxes = new List<Box>();
        var coordBoxes = new List<Box>();
        var segments = new List<(Point2, Point2)>();
        double agreement = 0, anchorError = 0, sizeDiff = 0;
        var missing = 0;

        foreach (var uid in generatedIds)
        {
            var e = _doc.GetElement(uid);
            switch (e)
            {
                case null:
                    missing++;
                    break;
                case TextNote note:
                {
                    var bbox = BoxFromBoundingBox(note, view);
                    var coord = BoxFromCoord(note);
                    boxes.Add(bbox);
                    coordBoxes.Add(coord);
                    agreement = Math.Max(agreement, MaxSideDiff(bbox, coord));
                    if (primitives?.GetValueOrDefault(uid) is TextPrimitive p)
                    {
                        var m = p.MeasuredBox;
                        var anchorX = p.Align == TextAlign.Right ? Math.Abs(bbox.Right - m.Right) : Math.Abs(bbox.Left - m.Left);
                        anchorError = Math.Max(anchorError, Math.Max(anchorX, Math.Abs(bbox.Top - m.Top)));
                        sizeDiff = Math.Max(sizeDiff, Math.Max(Math.Abs(bbox.Width - m.Width), Math.Abs(bbox.Height - m.Height)));
                    }
                    break;
                }
                case DetailCurve curve:
                {
                    var c = curve.GeometryCurve;
                    var a = c.GetEndPoint(0);
                    var b = c.GetEndPoint(1);
                    segments.Add((new Point2(a.X, a.Y), new Point2(b.X, b.Y)));
                    break;
                }
            }
        }

        _geometry[label] = new JsonObject
        {
            ["viewId"] = view.Id.Value,
            ["viewUniqueId"] = view.UniqueId,
            ["units"] = "feet, legend coordinates (Y up)",
            ["gridXs"] = new JsonArray(gridXs.Select(x => (JsonNode)x).ToArray()),
            ["gridYs"] = new JsonArray(gridYs.Select(y => (JsonNode)y).ToArray()),
            ["textBoxes"] = new JsonArray(boxes.Select(b => (JsonNode)new JsonArray(b.Left, b.Bottom, b.Right, b.Top)).ToArray()),
            ["textBoxesFromCoord"] = new JsonArray(coordBoxes.Select(b => (JsonNode)new JsonArray(b.Left, b.Bottom, b.Right, b.Top)).ToArray()),
            ["lines"] = new JsonArray(segments.Select(l => (JsonNode)new JsonArray(l.Item1.X, l.Item1.Y, l.Item2.X, l.Item2.Y)).ToArray()),
        };

        var report = GeometryChecker.Check(boxes, segments, gridXs, gridYs);
        var coordReport = GeometryChecker.Check(coordBoxes, segments, gridXs, gridYs);
        var violations = report.Violations.Concat(coordReport.Violations).ToList();
        _r.Check("A5", $"{label}: clean lines on Revit read-back (no text box touches a line, endpoints on grid ≤ 1e-6 ft, no collinear overlap)",
            report.IsClean && coordReport.IsClean && missing == 0 && boxes.Count > 0 && segments.Count > 0,
            new JsonObject
            {
                ["textBoxes"] = boxes.Count,
                ["lines"] = segments.Count,
                ["gridXs"] = gridXs.Count,
                ["gridYs"] = gridYs.Count,
                ["missingElements"] = missing,
                ["violationsBoundingBox"] = report.Violations.Count,
                ["violationsCoordWidth"] = coordReport.Violations.Count,
                ["textCrossesLine"] = violations.Count(v => v.Kind == ViolationKind.TextCrossesLine),
                ["endpointOffGrid"] = violations.Count(v => v.Kind == ViolationKind.EndpointOffGrid),
                ["collinearOverlap"] = violations.Count(v => v.Kind == ViolationKind.CollinearOverlap),
                ["maxBoxAgreementMm"] = Report.Mm(agreement),
                ["maxAnchorErrorMm"] = Report.Mm(anchorError),
                ["maxSizeDiffVsLayoutMm"] = Report.Mm(sizeDiff),
                ["minTextToLineClearanceMm"] = Report.Mm(MinClearance(boxes, segments)),
            },
            violations.Count == 0 ? null : string.Join("\n", violations.Take(12).Select(v => $"{v.Kind}: {v.Detail}")));
    }

    /// <summary>The text area: Revit's view bounding box minus the type's Leader/Border Offset on every side (V9).</summary>
    private static Box BoxFromBoundingBox(TextNote note, View view)
    {
        var bb = note.get_BoundingBox(view);
        var offset = note.TextNoteType.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET)?.AsDouble() ?? 0;
        return new Box(bb.Min.X + offset, bb.Min.Y + offset, bb.Max.X - offset, bb.Max.Y - offset);
    }

    /// <summary>The text area from Coord, Width, Height and the horizontal alignment (V9).</summary>
    private static Box BoxFromCoord(TextNote note)
    {
        var c = note.Coord;
        var left = note.HorizontalAlignment == HorizontalTextAlignment.Right ? c.X - note.Width : c.X;
        return new Box(left, c.Y - note.Height, left + note.Width, c.Y);
    }

    private static double MaxSideDiff(Box a, Box b) =>
        new[] { a.Left - b.Left, a.Right - b.Right, a.Top - b.Top, a.Bottom - b.Bottom }.Max(Math.Abs);

    /// <summary>Smallest gap between any text box and any line (axis-aligned lines only).</summary>
    private static double MinClearance(List<Box> boxes, List<(Point2 A, Point2 B)> lines)
    {
        var min = double.PositiveInfinity;
        foreach (var (a, b) in lines)
        {
            var x0 = Math.Min(a.X, b.X);
            var x1 = Math.Max(a.X, b.X);
            var y0 = Math.Min(a.Y, b.Y);
            var y1 = Math.Max(a.Y, b.Y);
            foreach (var box in boxes)
            {
                var dx = Math.Max(0, Math.Max(box.Left - x1, x0 - box.Right));
                var dy = Math.Max(0, Math.Max(box.Bottom - y1, y0 - box.Top));
                min = Math.Min(min, Math.Sqrt(dx * dx + dy * dy));
            }
        }
        return min;
    }

    // ------------------------------------------------------------------ A6

    private void CheckContent(
        FixtureRun run, View view, StoredChart stored, IReadOnlyDictionary<string, Primitive> byUid,
        HashSet<ElementId> handAdded, string when)
    {
        var label = $"{run.Chart.Name} {when}";
        var prims = run.Draw.Layout.Primitives;
        var expectedTexts = prims.OfType<TextPrimitive>().Count();
        var expectedLines = prims.OfType<LinePrimitive>().Count() - run.Draw.SkippedShortLines;
        var expectedBars = prims.OfType<FilledRectPrimitive>().Count();

        var elements = stored.GeneratedIds.Select(uid => _doc.GetElement(uid)).ToList();
        var notes = elements.OfType<TextNote>().ToList();
        var curves = elements.OfType<DetailCurve>().ToList();
        var regions = elements.OfType<FilledRegion>().ToList();
        var other = elements.Count(e => e is not (TextNote or DetailCurve or FilledRegion));

        // Strays: notes, curves or regions in the view that Gantt-It neither listed nor
        // the test added by hand (e.g. a measuring note that was never cleaned up).
        var generated = elements.Where(e => e is not null).Select(e => e!.Id).ToHashSet();
        var owned = new FilteredElementCollector(_doc, view.Id).WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == view.Id).ToList();
        var boundary = owned.OfType<FilledRegion>()
            .SelectMany(fr => fr.GetDependentElements(new ElementClassFilter(typeof(CurveElement)))).ToHashSet();
        var strays = owned.Count(e => e is TextNote or DetailCurve or FilledRegion
                                      && !generated.Contains(e.Id) && !handAdded.Contains(e.Id) && !boundary.Contains(e.Id));

        _r.Check("A6", $"{label}: legend view at 1:1",
            view.ViewType == ViewType.Legend && view.Scale == 1,
            new JsonObject { ["viewType"] = view.ViewType.ToString(), ["scale"] = view.Scale });

        CheckFixtureCounts(run.Chart, notes, regions, label);

        _r.Check("A6", $"{label}: element counts match the layout",
            notes.Count == expectedTexts && curves.Count == expectedLines && regions.Count == expectedBars
            && regions.Count == run.Chart.Sections.Count && other == 0 && strays == 0 && run.Draw.SkippedShortLines == 0,
            new JsonObject
            {
                ["sections"] = run.Chart.Sections.Count,
                ["textNotes"] = notes.Count,
                ["expectedTextNotes"] = expectedTexts,
                ["detailCurves"] = curves.Count,
                ["expectedDetailCurves"] = expectedLines,
                ["filledRegions"] = regions.Count,
                ["expectedFilledRegions"] = expectedBars,
                ["otherGenerated"] = other,
                ["straysInView"] = strays,
                ["skippedShortLines"] = run.Draw.SkippedShortLines,
                ["widenedBars"] = run.Draw.WidenedBars,
                ["measuredNotes"] = run.Draw.MeasuredNotes,
                ["unusedCandidatesDeleted"] = run.Draw.UnusedCandidatesDeleted,
            });

        // Styles against the settings, resolved independently of the generator.
        var styles = _styles!;
        int wrongType = 0, wrongText = 0, wrongAlign = 0, wrongLine = 0;
        foreach (var note in notes)
        {
            if (byUid.GetValueOrDefault(note.UniqueId) is not TextPrimitive p) { wrongType++; continue; }
            if (note.GetTypeId() != styles.Text(p.Style)) wrongType++;
            if (note.Text.TrimEnd('\r') != RevitTextMeasurer.RevitText(p.Text)) wrongText++;
            var align = p.Align == TextAlign.Right ? HorizontalTextAlignment.Right : HorizontalTextAlignment.Left;
            if (note.HorizontalAlignment != align || note.VerticalAlignment != VerticalTextAlignment.Top) wrongAlign++;
        }
        foreach (var curve in curves)
        {
            if (byUid.GetValueOrDefault(curve.UniqueId) is not LinePrimitive p || curve.LineStyle?.Id != styles.Line(p.Style)) wrongLine++;
        }
        _r.Check("A6", $"{label}: every text and line uses its configured style",
            wrongType == 0 && wrongText == 0 && wrongAlign == 0 && wrongLine == 0,
            new JsonObject
            {
                ["wrongTextType"] = wrongType,
                ["wrongTextContent"] = wrongText,
                ["wrongAlignment"] = wrongAlign,
                ["wrongLineStyle"] = wrongLine,
                ["titleNotes"] = notes.Count(n => n.GetTypeId() == styles.TitleText),
                ["headerNotes"] = notes.Count(n => n.GetTypeId() == styles.HeaderText),
                ["bodyNotes"] = notes.Count(n => n.GetTypeId() == styles.BodyText),
                ["headerLines"] = curves.Count(c => c.LineStyle?.Id == styles.HeaderLine),
                ["chartLines"] = curves.Count(c => c.LineStyle?.Id == styles.ChartLine),
                ["rightAlignedNotes"] = notes.Count(n => n.HorizontalAlignment == HorizontalTextAlignment.Right),
            });

        // Bars: FilledRegions of type "Gantt-It Fill #RRGGBB", solid, unmasked, invisible boundary.
        var solid = FillTypes.FindSolidDrafting(_doc);
        var invisible = FillTypes.FindInvisibleLines(_doc);
        int wrongFill = 0, visibleBoundary = 0;
        var typeNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            var type = (FilledRegionType)_doc.GetElement(region.GetTypeId());
            typeNames.Add(type.Name);
            var color = byUid.GetValueOrDefault(region.UniqueId) is FilledRectPrimitive p ? p.Color : (Rgb?)null;
            var fg = type.ForegroundPatternColor;
            if (color is null || type.Name != FillTypes.NameFor(color.Value)
                || type.ForegroundPatternId != solid || type.BackgroundPatternId != ElementId.InvalidElementId || type.IsMasking
                || fg.Red != color.Value.R || fg.Green != color.Value.G || fg.Blue != color.Value.B)
                wrongFill++;
            var edges = region.GetDependentElements(new ElementClassFilter(typeof(CurveElement)))
                .Select(id => _doc.GetElement(id)).OfType<CurveElement>().ToList();
            if (edges.Count == 0 || edges.Any(c => c.LineStyle?.Id != invisible)) visibleBoundary++;
        }
        _r.Check("A6", $"{label}: bars are 'Gantt-It Fill #RRGGBB' solid regions with invisible boundaries",
            wrongFill == 0 && visibleBoundary == 0 && regions.Count > 0,
            new JsonObject
            {
                ["regions"] = regions.Count,
                ["wrongFillType"] = wrongFill,
                ["regionsWithVisibleBoundary"] = visibleBoundary,
                ["fillTypes"] = new JsonArray(typeNames.Select(n => (JsonNode)n).ToArray()),
            });
    }

    /// <summary>
    /// Counts derived from the fixture definition alone, independent of the layout:
    /// one bar per section, one body note per section and visible column, one header
    /// per column, one title, and every section name drawn in the body.
    /// </summary>
    private void CheckFixtureCounts(Chart chart, List<TextNote> notes, List<FilledRegion> regions, string label)
    {
        var styles = _styles!;
        var columns = 1 + new[] { ChartColumns.Start, ChartColumns.End, ChartColumns.Duration }.Count(c => chart.Columns.HasFlag(c));
        var headerTexts = new HashSet<string>(StringComparer.Ordinal) { "Section", "Start", "End", "Dur" };
        var body = notes.Where(n => n.GetTypeId() == styles.BodyText).ToList();
        var bodyTexts = body.Select(n => n.Text.TrimEnd('\r')).ToHashSet(StringComparer.Ordinal);
        var titles = notes.Count(n => n.GetTypeId() == styles.TitleText);
        var columnHeaders = notes.Count(n => n.GetTypeId() == styles.HeaderText && headerTexts.Contains(n.Text.TrimEnd('\r')));
        var namesDrawn = chart.Sections.Count(s => bodyTexts.Contains(RevitTextMeasurer.RevitText(s.Name)));
        var expectedTitles = string.IsNullOrWhiteSpace(chart.Title) ? 0 : 1;
        _r.Check("A6", $"{label}: element counts match the fixture definition (independent of the layout)",
            regions.Count == chart.Sections.Count && body.Count == chart.Sections.Count * columns
            && titles == expectedTitles && columnHeaders == columns && namesDrawn == chart.Sections.Count,
            new JsonObject
            {
                ["sections"] = chart.Sections.Count,
                ["visibleColumns"] = columns,
                ["bars"] = regions.Count,
                ["bodyNotes"] = body.Count,
                ["expectedBodyNotes"] = chart.Sections.Count * columns,
                ["titleNotes"] = titles,
                ["expectedTitleNotes"] = expectedTitles,
                ["columnHeaderNotes"] = columnHeaders,
                ["sectionNamesDrawn"] = namesDrawn,
            });
    }

    // ------------------------------------------------------------------ A15

    private void CheckCalibration()
    {
        var run = Fixture(Fixtures.Months);
        var view = (View)_doc.GetElement(run.ViewId);
        var metrics = _backend.CalibrateTextStyles(_settings);
        var requests = LayoutEngine.BuildCells(run.Chart, _settings).AllRequests()
            .Where(r => r.Style != TextStyleKey.Title && r.WrapWidth is null)
            .Distinct()
            .ToList();

        var revit = RevitUtil.RolledBack(_doc, "Gantt-It Self-Test: measure",
            () => new RevitTextMeasurer(_doc, view, _styles!.TextTypes).Measure(requests));
        var wpf = new WpfTextMeasurer(metrics).Measure(requests);

        var rows = new JsonArray();
        double maxWidth = 0, maxHeight = 0, sum = 0;
        for (var i = 0; i < requests.Count; i++)
        {
            var dw = Math.Abs(wpf[i].Width - revit[i].Width);
            var dh = Math.Abs(wpf[i].Height - revit[i].Height);
            maxWidth = Math.Max(maxWidth, dw);
            maxHeight = Math.Max(maxHeight, dh);
            sum += dw;
            rows.Add(new JsonObject
            {
                ["text"] = requests[i].Text,
                ["style"] = requests[i].Style.ToString(),
                ["revitWidthMm"] = Report.Mm(revit[i].Width),
                ["wpfWidthMm"] = Report.Mm(wpf[i].Width),
                ["diffMm"] = Report.Mm(wpf[i].Width - revit[i].Width),
            });
        }

        _r.Check("A15", $"{run.Chart.Name}: calibrated WPF width within 0.5 mm of Revit for every header/body string",
            requests.Count > 0 && maxWidth * 304.8 <= 0.5,
            new JsonObject
            {
                ["strings"] = requests.Count,
                ["maxWidthDiffMm"] = Report.Mm(maxWidth),
                ["meanWidthDiffMm"] = Report.Mm(requests.Count == 0 ? 0 : sum / requests.Count),
                ["within0_5mm"] = rows.Count(r => Math.Abs(r!["diffMm"]!.GetValue<double>()) <= 0.5),
                ["maxHeightDiffMm"] = Report.Mm(maxHeight),
            });
        _r.Section("calibration", new JsonObject
        {
            ["metrics"] = new JsonObject(metrics.Select(kv => KeyValuePair.Create(kv.Key.ToString(), (JsonNode?)new JsonObject
            {
                ["font"] = kv.Value.FontName,
                ["sizeMm"] = Report.Mm(kv.Value.TextSizeFt),
                ["bold"] = kv.Value.Bold,
                ["italic"] = kv.Value.Italic,
                ["widthFactor"] = Report.N(kv.Value.WidthFactor),
                ["k"] = Report.N(kv.Value.WidthCalibration, 6),
                ["linePitchMm"] = Report.Mm(kv.Value.LinePitchFt),
            }))),
            ["rows"] = rows,
        });
    }

    // ------------------------------------------------------------------ A7

    private void CheckUpdateOnSheet()
    {
        var run = Fixture(Fixtures.Months);
        var view = (View)_doc.GetElement(run.ViewId);
        var viewUid = view.UniqueId;
        var oldIds = ChartStore.Read(view)!.GeneratedIds.ToHashSet();

        ElementId viewportId = ElementId.InvalidElementId, noteId = ElementId.InvalidElementId;
        SelfTestTx("place on a sheet and add a hand note", () =>
        {
            var titleBlock = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().FirstElementId() ?? ElementId.InvalidElementId;
            var sheet = ViewSheet.Create(_doc, titleBlock);
            sheet.Name = SheetName;
            _sheetId = sheet.Id;
            viewportId = Viewport.Create(_doc, sheet.Id, view.Id, new XYZ(0.6, 0.6, 0)).Id;
            _viewportId = viewportId;
            noteId = TextNote.Create(_doc, view.Id, new XYZ(0, Core.Model.Units.FromMillimeters(15), 0),
                "Hand-added note (must survive Update)", _settings.BodyTextStyle is { } s ? StyleReader.ResolveText(_doc, s)!.Id
                    : _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)).Id;
        });

        // Edit the chart the way the editor would: move an end date and rename a section.
        var chart = run.Chart;
        Scheduler.SetEnd(chart, chart.Sections[0].Id, chart.Sections[0].End.AddDays(9));
        chart.Sections[2].Name = "Tender (re-issued)";
        var result = _backend.Update(run.ViewId, chart, _settings);

        var after = _doc.GetElement(run.ViewId) as View;
        var newIds = after is null ? [] : ChartStore.Read(after)!.GeneratedIds.ToHashSet();
        var viewport = _doc.GetElement(viewportId) as Viewport;
        var note = _doc.GetElement(noteId) as TextNote;
        var oldRemaining = oldIds.Count(uid => _doc.GetElement(uid) is not null);
        var newOwned = newIds.Count(uid => _doc.GetElement(uid) is { } e && e.OwnerViewId == run.ViewId);
        var sheets = _backend.ListCharts().FirstOrDefault(e => e.ViewId == run.ViewId)?.Sheets ?? [];

        _r.Check("A7", $"{chart.Name}: Update keeps the view, its viewport and the hand note; GeneratedIds fully replaced",
            result.Succeeded && after?.UniqueId == viewUid && viewport?.ViewId == run.ViewId
            && note is not null && note.OwnerViewId == run.ViewId
            && newIds.Count > 0 && !newIds.Overlaps(oldIds) && oldRemaining == 0 && newOwned == newIds.Count
            && sheets.Count == 1,
            new JsonObject
            {
                ["status"] = result.Status.ToString(),
                ["viewId"] = run.ViewId.Value,
                ["viewportId"] = viewportId.Value,
                ["sheetId"] = _sheetId?.Value,
                ["sameViewUniqueId"] = after?.UniqueId == viewUid,
                ["viewportExists"] = viewport is not null,
                ["viewportShowsChart"] = viewport?.ViewId == run.ViewId,
                ["handNoteExists"] = note is not null,
                ["oldGeneratedIds"] = oldIds.Count,
                ["oldStillExisting"] = oldRemaining,
                ["newGeneratedIds"] = newIds.Count,
                ["overlap"] = newIds.Intersect(oldIds).Count(),
                ["newOwnedByView"] = newOwned,
                ["listedSheets"] = new JsonArray(sheets.Select(s => (JsonNode)s).ToArray()),
            },
            result.Message);

        if (result.Succeeded && ChartGenerator.LastDraw is { } draw)
        {
            var updated = run with { Draw = draw };
            _runs[_runs.IndexOf(run)] = updated;
            CheckChart(updated, "after Update", [noteId]);
        }
    }

    /// <summary>D11: a paper length formats in project units and parses back within 0.05 mm.</summary>
    private void CheckLengths()
    {
        var rows = new JsonObject();
        var pass = true;
        foreach (var mm in new[] { 1.5, 180.0 })
        {
            var feet = Core.Model.Units.FromMillimeters(mm);
            var text = _backend.FormatLength(feet);
            var parsed = _backend.TryParseLength(text, out var back);
            var errorMm = parsed ? Math.Abs(back - feet) * 304.8 : double.NaN;
            pass &= parsed && errorMm <= 0.05;
            rows[$"{mm}mm"] = new JsonObject { ["text"] = text, ["parsed"] = parsed, ["roundTripErrorMm"] = Report.N(errorMm, 5) };
        }
        _r.Check("D11", "paper lengths show in project units and round-trip within 0.05 mm (1.5 mm, 180 mm)", pass, rows);
    }

    /// <summary>
    /// Decision #17 (§7.2 v1.1): legends duplicated in Revit, plain or with detailing,
    /// are detached plain legends. They are not listed; Update and Delete refuse them;
    /// updating the original and Regenerate all leave their elements untouched.
    /// </summary>
    private void CheckCopiesDetached()
    {
        var run = Fixture(Fixtures.Days);
        var source = (View)_doc.GetElement(run.ViewId);
        var sourceStored = ChartStore.Read(source)!;

        ElementId copyW = ElementId.InvalidElementId, copyP = ElementId.InvalidElementId;
        SelfTestTx("duplicate a chart legend (with detailing, and plain)", () =>
        {
            copyW = source.Duplicate(ViewDuplicateOption.WithDetailing);
            _doc.GetElement(copyW).Name = CopyWName;
            copyP = source.Duplicate(ViewDuplicateOption.Duplicate);
            _doc.GetElement(copyP).Name = CopyPName;
        });
        var w0 = CopyState(copyW);
        var p0 = CopyState(copyP);

        var listed = _backend.ListCharts();
        var copyListed = listed.Any(e => e.ViewId == copyW || e.ViewId == copyP);
        var sourceListed = listed.Any(e => e.ViewId == run.ViewId);
        _r.Check("A9", "duplicated legends (with detailing, plain) are detached: not listed, entity copied verbatim, no owned GeneratedIds",
            !copyListed && sourceListed && w0.Entity == sourceStored.ChartJson && p0.Entity == sourceStored.ChartJson
            && !w0.Live && !p0.Live && w0.Owned.Count > 0 && p0.Owned.Count == 0,
            new JsonObject
            {
                ["copiesListed"] = copyListed,
                ["sourceListed"] = sourceListed,
                ["entityCopiedVerbatim"] = w0.Entity == sourceStored.ChartJson && p0.Entity == sourceStored.ChartJson,
                ["withDetailingOwnedElements"] = w0.Owned.Count,
                ["plainOwnedElements"] = p0.Owned.Count,
                ["withDetailingLive"] = w0.Live,
                ["plainLive"] = p0.Live,
            });

        var results = new List<(string What, ActionResult Result)>();
        foreach (var (id, name) in new[] { (copyW, CopyWName), (copyP, CopyPName) })
        {
            var chart = ChartJson.Deserialize(ChartStore.Read((View)_doc.GetElement(id))!.ChartJson).Value!;
            chart.Name = name;
            results.Add(($"Update {name}", _backend.Update(id, chart, _settings)));
            results.Add(($"Delete {name}", _backend.Delete(id)));
        }
        var sourceIds = ChartStore.Read(source)!.GeneratedIds.ToList();
        _r.Check("A9", "Update and Delete refuse both detached copies (Blocked); copies and source unchanged",
            results.All(r => r.Result.Status == ActionStatus.Blocked)
            && CopyState(copyW).SameAs(w0) && CopyState(copyP).SameAs(p0)
            && sourceIds.SequenceEqual(sourceStored.GeneratedIds),
            new JsonObject(results.Select(r => KeyValuePair.Create(r.What, (JsonNode?)r.Result.Status.ToString()))),
            results.FirstOrDefault(r => r.Result.Status == ActionStatus.Blocked).Result?.Message);

        // The original is still a chart: update it.
        var original = run.Chart;
        original.Sections[1].Name = "Survey (revised)";
        var update = _backend.Update(run.ViewId, original, _settings);
        _r.Check("A9", "updating the original leaves both copies' elements and entities untouched",
            update.Succeeded && CopyState(copyW).SameAs(w0) && CopyState(copyP).SameAs(p0)
            && !_backend.ListCharts().Any(e => e.ViewId == copyW || e.ViewId == copyP),
            new JsonObject
            {
                ["status"] = update.Status.ToString(),
                ["withDetailingOwnedElements"] = CopyState(copyW).Owned.Count,
            },
            update.Message);
        if (update.Succeeded && ChartGenerator.LastDraw is { } draw)
        {
            var updated = run with { Draw = draw };
            _runs[_runs.IndexOf(run)] = updated;
            CheckChart(updated, "after Update (copies present)", []);
        }

        var regen = _backend.RegenerateAll(_settings);
        var live = _backend.ListCharts().Count;
        _r.Check("A9", "Regenerate all redraws live charts only; the detached copies are untouched",
            regen.Succeeded && CopyState(copyW).SameAs(w0) && CopyState(copyP).SameAs(p0)
            && (regen.Message ?? "").Contains($"Regenerated {live} chart", StringComparison.Ordinal),
            new JsonObject { ["status"] = regen.Status.ToString(), ["liveCharts"] = live },
            regen.Message);

        _copies = new JsonObject
        {
            ["sourceViewUniqueId"] = source.UniqueId,
            ["withDetailing"] = w0.ToJson(_doc, copyW),
            ["plain"] = p0.ToJson(_doc, copyP),
        };
    }

    /// <summary>The state of a (detached) legend: its entity and the elements it owns.</summary>
    private sealed record LegendState(bool Exists, bool Live, string? Entity, string GeneratedIds, HashSet<string> Owned)
    {
        public bool SameAs(LegendState o) =>
            Exists == o.Exists && Live == o.Live && Entity == o.Entity && GeneratedIds == o.GeneratedIds && Owned.SetEquals(o.Owned);

        public JsonObject ToJson(Document doc, ElementId id) => new()
        {
            ["viewUniqueId"] = doc.GetElement(id)?.UniqueId,
            ["entity"] = Entity,
            ["generatedIdsJoined"] = GeneratedIds,
            ["ownedUniqueIds"] = new JsonArray(Owned.Order(StringComparer.Ordinal).Select(u => (JsonNode)u).ToArray()),
        };
    }

    private LegendState CopyState(ElementId viewId) =>
        _doc.GetElement(viewId) is View v ? StateOf(v) : new LegendState(false, false, null, "", []);

    private LegendState StateOf(View v)
    {
        var stored = ChartStore.Read(v);
        var owned = new FilteredElementCollector(_doc, v.Id).WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == v.Id && e is TextNote or DetailCurve or FilledRegion)
            .Select(e => e.UniqueId).ToHashSet(StringComparer.Ordinal);
        return new LegendState(true, stored is not null && ChartStore.IsLive(_doc, stored), stored?.ChartJson,
            stored is null ? "" : string.Join(",", stored.GeneratedIds), owned);
    }

    /// <summary>
    /// A chart whose generated elements were all deleted by hand is detached; one with
    /// only some deleted stays live and Update redraws it cleanly (Beyond Intention).
    /// </summary>
    private void CheckHandDeleted()
    {
        // All deleted.
        var all = Fixtures.BuildDays();
        all.Name = AllDeletedName;
        var gen = _backend.Generate(all, _settings);
        if (gen.ViewId is null)
        {
            _r.Check("A9", "hand-deleted: chart generated", false, detail: gen.Message);
            return;
        }
        var view = (View)_doc.GetElement(gen.ViewId);
        var ids = ChartStore.OwnedGeneratedIds(_doc, view, ChartStore.Read(view)!.GeneratedIds);
        SelfTestTx("delete every generated element by hand", () => _doc.Delete(ids));
        var listed = _backend.ListCharts().Any(e => e.ViewId == view.Id);
        var update = _backend.Update(view.Id, all, _settings);
        var delete = _backend.Delete(view.Id);
        _r.Check("A9", "every generated element deleted by hand: the chart is detached (not listed; Update and Delete Blocked)",
            !listed && update.Status == ActionStatus.Blocked && delete.Status == ActionStatus.Blocked && _doc.GetElement(view.Id) is View,
            new JsonObject
            {
                ["deletedByHand"] = ids.Count,
                ["listed"] = listed,
                ["update"] = update.Status.ToString(),
                ["delete"] = delete.Status.ToString(),
                ["viewStillExists"] = _doc.GetElement(view.Id) is View,
            },
            update.Message);
        SelfTestTx("remove the detached legend", () => _doc.Delete(view.Id));

        // Some deleted.
        var part = Fixtures.BuildMonths();
        part.Name = PartDeletedName;
        gen = _backend.Generate(part, _settings);
        if (gen.ViewId is null || ChartGenerator.LastDraw is null)
        {
            _r.Check("Beyond Intention", "partly hand-deleted: chart generated", false, detail: gen.Message);
            return;
        }
        view = (View)_doc.GetElement(gen.ViewId);
        var stored = ChartStore.Read(view)!;
        var elements = stored.GeneratedIds.Select(u => _doc.GetElement(u)).ToList();
        var victims = elements.OfType<TextNote>().Where((_, i) => i % 2 == 0).Select(e => e.Id)
            .Concat(elements.OfType<DetailCurve>().Take(5).Select(e => e.Id))
            .Concat(elements.OfType<FilledRegion>().Take(1).Select(e => e.Id))
            .ToList();
        SelfTestTx("delete some generated elements by hand", () => _doc.Delete(victims));
        listed = _backend.ListCharts().Any(e => e.ViewId == view.Id);
        update = _backend.Update(view.Id, part, _settings);
        var oldLeft = stored.GeneratedIds.Count(u => _doc.GetElement(u) is not null);
        _r.Check("Beyond Intention", "some generated elements deleted by hand: still listed, and Update redraws it completely",
            listed && update.Succeeded && oldLeft == 0,
            new JsonObject
            {
                ["deletedByHand"] = victims.Count,
                ["listed"] = listed,
                ["update"] = update.Status.ToString(),
                ["oldElementsLeft"] = oldLeft,
            },
            update.Message);
        if (update.Succeeded && ChartGenerator.LastDraw is { } draw)
            CheckChart(new FixtureRun(part, view.Id, draw), "after partial hand delete and Update", []);
        var partId = view.Id;
        var removed = _backend.Delete(partId);
        _r.Check("Beyond Intention", "the partly hand-deleted chart deletes cleanly", removed.Succeeded && _doc.GetElement(partId) is null,
            new JsonObject { ["status"] = removed.Status.ToString() }, removed.Message);
    }

    private void WriteSnapshot()
    {
        var charts = new JsonArray();
        foreach (var run in _runs)
        {
            var view = (View)_doc.GetElement(run.ViewId);
            var stored = ChartStore.Read(view)!;
            charts.Add(new JsonObject
            {
                ["name"] = run.Chart.Name,
                ["viewUniqueId"] = view.UniqueId,
                ["chartJson"] = stored.ChartJson,
                ["generatedIds"] = new JsonArray(stored.GeneratedIds.Select(u => (JsonNode)u).ToArray()),
                ["gridXs"] = new JsonArray(run.Draw.Layout.Grid.GridXs.Select(x => (JsonNode)x).ToArray()),
                ["gridYs"] = new JsonArray(run.Draw.Layout.Grid.GridYs.Select(y => (JsonNode)y).ToArray()),
            });
        }
        var snapshot = new JsonObject
        {
            ["document"] = _doc.PathName,
            ["settingsJson"] = ChartStore.ReadSettingsJson(_doc),
            ["charts"] = charts,
            ["copies"] = _copies?.DeepClone(),
        };
        Report.Write(SnapshotPath, snapshot);
        _r.Check("A9", "snapshot written for the reopen phase", File.Exists(SnapshotPath),
            new JsonObject { ["charts"] = charts.Count, ["path"] = SnapshotPath });
    }

    // ------------------------------------------------------------------ A11

    private void CheckFaults()
    {
        // 1. A failure in the middle of Generate leaves nothing behind.
        var before = CountAll();
        var legendsBefore = RevitUtil.Legends(_doc).Count;
        var fillsBefore = GanttItFillNames();
        var chart = Chart.Create(FaultChartName);
        var start = new DateOnly(2026, 11, 2);
        chart.Sections.Add(new Section { Id = Guid.NewGuid(), Name = "Fault A", Start = start, End = start.AddDays(9), Color = FaultColor });
        chart.Sections.Add(new Section { Id = Guid.NewGuid(), Name = "Fault B", Start = start.AddDays(10), End = start.AddDays(19), Color = Rgb.Parse("#2468AC") });

        DebugFaults.Arm(DebugFaults.MidGenerate);
        ActionResult result;
        try
        {
            result = _backend.Generate(chart, _settings);
        }
        finally
        {
            DebugFaults.Disarm();
        }
        var after = CountAll();
        var fillsAfter = GanttItFillNames();
        var viewLeft = RevitUtil.Legends(_doc).Any(v => v.Name == FaultChartName);
        _r.Check("A11", "fault mid-Generate: rolled back, no Gantt-It element or fill type remains",
            result.Status == ActionStatus.Failed && (result.Message ?? "").Contains("debug fault", StringComparison.Ordinal)
            && !viewLeft && after == before && RevitUtil.Legends(_doc).Count == legendsBefore
            && fillsAfter.SetEquals(fillsBefore) && !fillsAfter.Contains(FillTypes.NameFor(FaultColor)),
            new JsonObject
            {
                ["status"] = result.Status.ToString(),
                ["elementsBefore"] = before,
                ["elementsAfter"] = after,
                ["legendsBefore"] = legendsBefore,
                ["legendsAfter"] = RevitUtil.Legends(_doc).Count,
                ["faultViewLeft"] = viewLeft,
                ["ganttItFillTypesBefore"] = fillsBefore.Count,
                ["ganttItFillTypesAfter"] = fillsAfter.Count,
            },
            result.Message);

        // 2. A failure in the middle of Update restores the chart exactly.
        var run = Fixture(Fixtures.Months);
        var view = (View)_doc.GetElement(run.ViewId);
        var stored = ChartStore.Read(view)!;
        var edited = ChartJson.Deserialize(stored.ChartJson).Value!;
        edited.Name = view.Name;
        edited.Sections[1].Name = "Should never be drawn";
        var beforeUpdate = CountAll();
        DebugFaults.Arm(DebugFaults.MidGenerate);
        try
        {
            result = _backend.Update(run.ViewId, edited, _settings);
        }
        finally
        {
            DebugFaults.Disarm();
        }
        var storedAfter = ChartStore.Read(view)!;
        var intact = stored.GeneratedIds.Count(uid => _doc.GetElement(uid) is { } e && e.OwnerViewId == run.ViewId);
        _r.Check("A11", "fault mid-Update: rolled back, the chart and its elements are unchanged",
            result.Status == ActionStatus.Failed && storedAfter.ChartJson == stored.ChartJson
            && storedAfter.GeneratedIds.SequenceEqual(stored.GeneratedIds) && intact == stored.GeneratedIds.Count
            && CountAll() == beforeUpdate,
            new JsonObject
            {
                ["status"] = result.Status.ToString(),
                ["elementsBefore"] = beforeUpdate,
                ["elementsAfter"] = CountAll(),
                ["generatedIds"] = stored.GeneratedIds.Count,
                ["generatedIntact"] = intact,
            },
            result.Message);
    }

    /// <summary>
    /// The failure preprocessor's warning path: two coincident detail lines in a plain
    /// legend, in a transaction using <see cref="GanttItFailures"/>. If Revit raises a
    /// warning, it must be recorded and deleted with no dialog. The lines are removed
    /// afterwards. If no warning is raised, that is recorded as observed, not as a check.
    /// </summary>
    private void CheckWarningPath()
    {
        var legend = RevitUtil.Legends(_doc).FirstOrDefault(v => ChartStore.Read(v) is null);
        if (legend is null)
        {
            _r.Section("warningPath", "no plain legend to draw in");
            return;
        }
        var dialogs = SelfTestHarness.DialogLog.Count;
        var failures = new GanttItFailures();
        var made = new List<ElementId>();
        using (var t = new Transaction(_doc, "Gantt-It Self-Test: provoke a warning"))
        {
            var options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(failures);
            options.SetClearAfterRollback(true);
            t.SetFailureHandlingOptions(options);
            t.Start();
            var a = new XYZ(0, -3, 0);
            var b = new XYZ(0.3, -3, 0);
            for (var i = 0; i < 2; i++) made.Add(_doc.Create.NewDetailCurve(legend, Autodesk.Revit.DB.Line.CreateBound(a, b)).Id);
            t.Commit();
        }
        var dialogDelta = SelfTestHarness.DialogLog.Count - dialogs;
        var survivors = made.Where(id => _doc.GetElement(id) is not null).ToList();
        if (survivors.Count > 0) SelfTestTx("remove the warning probe lines", () => _doc.Delete(survivors));

        if (failures.Warnings.Count > 0)
            _r.Check("A11", "a Revit warning is recorded and deleted by the failure preprocessor, with no dialog",
                dialogDelta == 0,
                new JsonObject
                {
                    ["warnings"] = failures.Warnings.Count,
                    ["firstWarning"] = failures.Warnings[0],
                    ["dialogs"] = dialogDelta,
                });
        else
            _r.Section("warningPath", $"observed only: two coincident detail lines raised no warning (dialogs {dialogDelta})");
    }

    /// <summary>Generates "GI Undo" as the last transaction, for the GUI Undo check (undo-check phase).</summary>
    private void PrepareUndo()
    {
        var before = CountAll();
        var chart = Chart.Create(UndoChartName);
        var start = new DateOnly(2026, 11, 2);
        chart.Sections.Add(new Section { Id = Guid.NewGuid(), Name = "Undo me", Start = start, End = start.AddDays(13), Color = UndoColor });
        var result = _backend.Generate(chart, _settings);
        var view = result.ViewId is null ? null : _doc.GetElement(result.ViewId) as View;
        var record = new JsonObject
        {
            ["viewUniqueId"] = view?.UniqueId,
            ["name"] = UndoChartName,
            ["elementsBefore"] = before,
            ["elementsAfter"] = CountAll(),
            ["fillType"] = FillTypes.NameFor(UndoColor),
            ["transaction"] = GanttItTransaction.Name("Generate", UndoChartName),
        };
        Report.Write(UndoPath, record);
        _r.Check("A11", "undo preparation: 'GI Undo' generated as the last transaction",
            result.Succeeded && view is not null && GanttItFillNames().Contains(FillTypes.NameFor(UndoColor)),
            record.DeepClone().AsObject(), result.Message);
    }

    private void UndoCheck()
    {
        if (!File.Exists(UndoPath))
        {
            _r.Check("A11", "undo-check: undo record exists", false, detail: UndoPath);
            return;
        }
        var record = JsonNode.Parse(File.ReadAllText(UndoPath))!.AsObject();
        var uid = record["viewUniqueId"]?.GetValue<string>();
        var before = record["elementsBefore"]!.GetValue<int>();
        var gone = uid is null || _doc.GetElement(uid) is null;
        var count = CountAll();
        var fillGone = !GanttItFillNames().Contains(record["fillType"]!.GetValue<string>());
        var listed = _backend.ListCharts().Any(e => e.ViewName == UndoChartName);
        _r.Check("A11", "one GUI Undo fully reverses a Generate",
            gone && count == before && fillGone && !listed,
            new JsonObject
            {
                ["viewGone"] = gone,
                ["elementsBeforeGenerate"] = before,
                ["elementsAfterGenerate"] = record["elementsAfter"]?.GetValue<int>(),
                ["elementsNow"] = count,
                ["fillTypeGone"] = fillGone,
                ["stillListed"] = listed,
            });
    }

    // ------------------------------------------------------------------ list, export, save

    private void CheckListCharts()
    {
        var entries = _backend.ListCharts();
        var names = entries.Select(e => e.ViewName).ToHashSet(StringComparer.Ordinal);
        var all = _runs.All(r => names.Contains(r.Chart.Name));
        _r.Check("A6", "every generated chart is listed with an editable load",
            all && entries.Where(e => _runs.Any(r => r.ViewId == e.ViewId)).All(e => e.Load.IsEditable),
            new JsonObject
            {
                ["listed"] = entries.Count,
                ["fixtures"] = _runs.Count,
                ["charts"] = new JsonArray(entries.Select(e => (JsonNode)new JsonObject
                {
                    ["name"] = e.ViewName,
                    ["status"] = e.Load.Status.ToString(),
                    ["sections"] = e.Load.Value?.Sections.Count,
                    ["sheets"] = e.Sheets.Count,
                }).ToArray()),
            });
    }

    /// <summary>A PNG of every fixture chart and of the sheet, for the FnB's visual sign-off (A16).</summary>
    private void ExportImages()
    {
        var files = new JsonArray();
        var targets = _runs.Select(r => (r.ViewId, r.Chart.Name)).ToList();
        if (_sheetId is not null) targets.Add((_sheetId, "sheet"));
        foreach (var (id, name) in targets)
        {
            var prefix = Path.Combine(SelfTestHarness.OutDir, $"chart-{_year}-{_stem}-{Safe(name)}");
            var options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = prefix,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_300,
                ZoomType = ZoomFitType.FitToPage,
                PixelSize = name == Fixtures.Large ? 1600 : 2400,
                FitDirection = FitDirectionType.Horizontal,
            };
            options.SetViewsAndSheets([id]);
            _doc.ExportImage(options);
            foreach (var f in Directory.GetFiles(SelfTestHarness.OutDir, Path.GetFileName(prefix) + "*.png")) files.Add(Path.GetFileName(f));
        }
        _r.Section("images", files);
    }

    private void Save()
    {
        var path = _doc.PathName;
        if (string.IsNullOrEmpty(path) || path.Contains(@"\fixtures\", StringComparison.OrdinalIgnoreCase))
        {
            _r.Check("A9", "save refused: not a work copy", false, detail: path);
            return;
        }
        string how;
        try
        {
            _doc.Save();
            how = "Save";
        }
        catch (Exception)
        {
            _doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
            how = "SaveAs(overwrite)";
        }
        _r.Check("A9", "work copy saved for the reopen phase", !_doc.IsModified, new JsonObject { ["how"] = how, ["path"] = path });
    }

    private void Activate(string chartName)
    {
        var view = RevitUtil.Legends(_doc).FirstOrDefault(v => v.Name == chartName);
        if (view is null) return;
        try
        {
            _uiapp.ActiveUIDocument.RequestViewChange(view);
            ZoomOnce.Schedule(_uiapp, view.Id);
        }
        catch (Exception ex)
        {
            _r.Section("activate", ex.Message);
        }
    }

    // ================================================================== phase 2 (A9)

    private void Phase2()
    {
        if (!File.Exists(SnapshotPath))
        {
            _r.Check("A9", "phase 2: the phase-1 snapshot exists", false, detail: SnapshotPath);
            return;
        }
        var snapshot = JsonNode.Parse(File.ReadAllText(SnapshotPath))!.AsObject();

        _r.Step("A9", "settings read back", () =>
        {
            var state = _backend.LoadSettings();
            _settings = state.Settings;
            _styles = StyleReader.Resolve(_doc, _settings, out _);
            var expected = snapshot["settingsJson"]?.GetValue<string>();
            var stored = ChartStore.ReadSettingsJson(_doc);
            _r.Check("A9", "settings read back identical after save, close and reopen",
                state.Stored && state.Error is null && state.MissingStyles.Count == 0 && stored == expected
                && SettingsJson.Serialize(state.Settings) == expected && _styles is not null,
                new JsonObject
                {
                    ["stored"] = state.Stored,
                    ["missingStyles"] = state.MissingStyles.Count,
                    ["storedJsonIdentical"] = stored == expected,
                    ["loadedSettingsIdentical"] = SettingsJson.Serialize(state.Settings) == expected,
                });
        });

        foreach (var node in snapshot["charts"]!.AsArray())
        {
            var c = node!.AsObject();
            var name = c["name"]!.GetValue<string>();
            _r.Step("A9", $"reopen {name}", () => CheckReopenedChart(c));
        }

        if (snapshot["copies"] is JsonObject copies) _r.Step("A9", "copies after reopen", () => CheckCopiesAfterReopen(copies));
    }

    private void CheckReopenedChart(JsonObject c)
    {
        var name = c["name"]!.GetValue<string>();
        var view = _doc.GetElement(c["viewUniqueId"]!.GetValue<string>()) as View;
        var stored = view is null ? null : ChartStore.Read(view);
        var ids = c["generatedIds"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var json = c["chartJson"]!.GetValue<string>();
        var load = stored is null ? null : ChartJson.Deserialize(stored.ChartJson);
        var original = ChartJson.Deserialize(json).Value;
        var existing = ids.Count(uid => _doc.GetElement(uid) is { } e && e.OwnerViewId == view?.Id);
        _r.Check("A9", $"{name}: chart reads back identical after save, close and reopen",
            stored is not null && stored.ChartJson == json && stored.GeneratedIds.SequenceEqual(ids)
            && load?.IsEditable == true && original is not null
            && ChartJson.Serialize(load.Value!) == ChartJson.Serialize(original)
            && existing == ids.Count && view!.Scale == 1,
            new JsonObject
            {
                ["viewFound"] = view is not null,
                ["chartJsonIdentical"] = stored?.ChartJson == json,
                ["generatedIdsIdentical"] = stored?.GeneratedIds.SequenceEqual(ids),
                ["generatedElementsPresent"] = existing,
                ["generatedIds"] = ids.Count,
                ["loadStatus"] = load?.Status.ToString(),
            });

        if (view is null) return;
        var xs = c["gridXs"]!.AsArray().Select(n => n!.GetValue<double>()).ToList();
        var ys = c["gridYs"]!.AsArray().Select(n => n!.GetValue<double>()).ToList();
        CheckGeometry(view, ids, xs, ys, null, $"{name} after reopen");
    }

    /// <summary>
    /// After reopen the copies are still detached. Updating and then deleting the
    /// original (through the backend) leaves each copy's elements and entity untouched.
    /// </summary>
    private void CheckCopiesAfterReopen(JsonObject copies)
    {
        var source = _doc.GetElement(copies["sourceViewUniqueId"]!.GetValue<string>()) as View;
        var copyW = _doc.GetElement(copies["withDetailing"]!["viewUniqueId"]!.GetValue<string>()) as View;
        var copyP = _doc.GetElement(copies["plain"]!["viewUniqueId"]!.GetValue<string>()) as View;
        if (source is null || copyW is null || copyP is null)
        {
            _r.Check("A9", "copies after reopen: the source and both copies exist", false);
            return;
        }

        bool Unchanged(View v, JsonObject snap)
        {
            var st = StateOf(v);
            var owned = snap["ownedUniqueIds"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            return st.Exists && !st.Live && st.Entity == snap["entity"]?.GetValue<string>()
                   && st.GeneratedIds == snap["generatedIdsJoined"]!.GetValue<string>() && st.Owned.SetEquals(owned);
        }
        var w = copies["withDetailing"]!.AsObject();
        var p = copies["plain"]!.AsObject();

        var listed = _backend.ListCharts();
        var sourceEntry = listed.FirstOrDefault(e => e.ViewId == source.Id);
        var copiesListed = listed.Any(e => e.ViewId == copyW.Id || e.ViewId == copyP.Id);
        var blocked = new[] { copyW, copyP }.SelectMany(v =>
        {
            var chart = ChartJson.Deserialize(ChartStore.Read(v)!.ChartJson).Value!;
            chart.Name = v.Name;
            return new[] { _backend.Update(v.Id, chart, _settings), _backend.Delete(v.Id) };
        }).ToList();
        _r.Check("A9", "after reopen: copies still detached (not listed, Update and Delete Blocked), elements unchanged; source listed",
            !copiesListed && sourceEntry?.Load.IsEditable == true && blocked.All(r => r.Status == ActionStatus.Blocked)
            && Unchanged(copyW, w) && Unchanged(copyP, p),
            new JsonObject
            {
                ["copiesListed"] = copiesListed,
                ["sourceListed"] = sourceEntry is not null,
                ["blocked"] = blocked.Count(r => r.Status == ActionStatus.Blocked),
                ["withDetailingOwnedElements"] = StateOf(copyW).Owned.Count,
            });

        var original = sourceEntry?.Load.Value;
        if (original is null) return;
        original.Sections[0].Name = "Kickoff (after reopen)";
        var sourceId = source.Id; // the View object is invalid once deleted
        var update = _backend.Update(sourceId, original, _settings);
        var afterUpdate = Unchanged(copyW, w) && Unchanged(copyP, p);
        var delete = _backend.Delete(sourceId);
        var sourceGone = _doc.GetElement(sourceId) is null;
        _r.Check("A9", "after reopen: updating then deleting the original leaves each copy's elements untouched",
            update.Succeeded && afterUpdate && delete.Succeeded && sourceGone && Unchanged(copyW, w) && Unchanged(copyP, p)
            && !_backend.ListCharts().Any(e => e.ViewId == copyW.Id || e.ViewId == copyP.Id),
            new JsonObject
            {
                ["update"] = update.Status.ToString(),
                ["copiesUnchangedAfterUpdate"] = afterUpdate,
                ["delete"] = delete.Status.ToString(),
                ["sourceGone"] = sourceGone,
                ["withDetailingOwnedElements"] = StateOf(copyW).Owned.Count,
            },
            update.Message ?? delete.Message);
    }

    // ================================================================== helpers

    private FixtureRun Fixture(string name) =>
        _runs.FirstOrDefault(r => r.Chart.Name == name)
        ?? throw new InvalidOperationException($"Fixture '{name}' was not generated.");

    /// <summary>Every element and element type in the model.</summary>
    private int CountAll() =>
        new FilteredElementCollector(_doc).WhereElementIsNotElementType().GetElementCount()
        + new FilteredElementCollector(_doc).WhereElementIsElementType().GetElementCount();

    private HashSet<string> GanttItFillNames() =>
        new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType))
            .Select(t => t.Name).Where(FillTypes.IsGanttItName).ToHashSet(StringComparer.Ordinal);

    /// <summary>A test-only edit (sheet, hand note, a duplicate made the way a user would).</summary>
    private void SelfTestTx(string what, Action body)
    {
        using var t = new Transaction(_doc, $"Gantt-It Self-Test: {what}");
        var options = t.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new GanttItFailures());
        options.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(options);
        t.Start();
        body();
        if (t.Commit() != TransactionStatus.Committed) throw new InvalidOperationException($"Self-Test transaction '{what}' did not commit.");
    }

    private static JsonObject TypeInfo(TextNoteType t)
    {
        var (font, size, bold, italic, wf, offset) = StyleReader.Font(t);
        return new JsonObject
        {
            ["name"] = t.Name,
            ["font"] = font,
            ["sizeMm"] = Report.Mm(size),
            ["bold"] = bold,
            ["italic"] = italic,
            ["widthFactor"] = Report.N(wf),
            ["leaderOffsetMm"] = Report.Mm(offset),
        };
    }

    private static string Safely(Func<string> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex)
        {
            return $"error: {ex.Message}";
        }
    }

    private static string Safe(string name) =>
        new(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
}

/// <summary>
/// Zooms the newly activated chart view to fit once it is active (view changes are
/// asynchronous). The Idling handler is subscribed once at startup: Revit refuses a
/// subscription made from inside an Idling handler, which is where autorun runs.
/// </summary>
internal static class ZoomOnce
{
    private static ElementId? _target;
    private static int _ticks;

    public static void Schedule(UIApplication uiapp, ElementId viewId)
    {
        _target = viewId;
        _ticks = 0;
    }

    public static void OnIdling(object? sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
    {
        if (_target is null || sender is not UIApplication uiapp) return;
        var uidoc = uiapp.ActiveUIDocument;
        if (++_ticks > 200 || uidoc is null)
        {
            _target = null;
            return;
        }
        if (uidoc.ActiveView?.Id != _target) return;
        var target = _target;
        _target = null;
        uidoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == target)?.ZoomToFit();
    }
}
#endif
