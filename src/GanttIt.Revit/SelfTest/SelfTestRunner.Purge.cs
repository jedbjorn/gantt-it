#if DEBUG
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using RevitLine = Autodesk.Revit.DB.Line;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// The fill-type purge (§7.3): every Gantt-It action ends by deleting the Gantt-It
/// fill types nothing uses, and never one that something still uses; a purge that
/// makes the commit fail is dropped and the action retried without it. Runs after
/// the A9 copies exist, so a detached copy's types are among those that must stay.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const string PurgeChartName = "GI Purge";
    private const string DeferredChartName = "GI Purge deferred";
    private const string PurgeViewName = "GI Purge probe";
    private const string PurgeGroupPlaced = "GI Purge group (placed)";
    private const string PurgeGroupUnplaced = "GI Purge group (unplaced)";
    private static readonly Rgb OrphanColor = Rgb.Parse("#0A0B0C");
    private static readonly Rgb GroupedColor = Rgb.Parse("#0D0E0F");
    private static readonly Rgb UnplacedColor = Rgb.Parse("#101112");
    private static readonly Rgb DefaultColor = Rgb.Parse("#131415");
    private static readonly Rgb PurgeChartColor = Rgb.Parse("#161718");
    private static readonly Rgb DeferredChartColor = Rgb.Parse("#191A1B");

    private static IEnumerable<string> PurgeProbeTypeNames =>
        new[] { OrphanColor, GroupedColor, UnplacedColor, DefaultColor, PurgeChartColor, DeferredChartColor }.Select(FillTypes.NameFor);

    /// <summary>
    /// Seeds an orphan Gantt-It fill type (no region), an unused one made the Filled
    /// Region tool's default, and types held only by the user's own regions in a placed
    /// detail group and in a group type with no placed instance. Then:
    /// <list type="number">
    /// <item>Generate with a simulated commit failure after the purge: the action still
    /// succeeds (retried once without the purge), draws one chart, and the purge is
    /// deferred.</item>
    /// <item>Generate: the orphan and the default type are deleted; the new chart's
    /// types, the detached copy's types and the held types all remain.</item>
    /// <item>With the probes removed by hand, deleting the two charts purges every
    /// probe type.</item>
    /// </list>
    /// The probes and the default type are restored whatever fails.
    /// </summary>
    private void CheckFillPurge()
    {
        var copy = RevitUtil.Legends(_doc).FirstOrDefault(v => v.Name == CopyWName);
        var copyTypes = copy is null ? [] : RegionTypeNames(copy.Id);
        var copyRegions = copy is null ? 0 : RegionCount(copy.Id);
        var previousDefault = _doc.GetDefaultElementTypeId(ElementTypeGroup.FilledRegionType);
        var unplacedGroupType = ElementId.InvalidElementId;
        var defaultSet = false;
        ActionResult? deferred = null, gen = null;

        try
        {
            // Seed, the way a user (or an aborted session) would leave the model.
            SelfTestTx("seed fill-type purge probes", () =>
            {
                var fills = new FillTypes(_doc);
                fills.Get(OrphanColor);
                var view = RevitUtil.CreateProbeDraftingView(_doc);
                view.Name = PurgeViewName;

                var grouped = FilledRegion.Create(_doc, fills.Get(GroupedColor), view.Id, [Square(0)]);
                _doc.Create.NewGroup([grouped.Id]).GroupType.Name = PurgeGroupPlaced;

                var unplaced = FilledRegion.Create(_doc, fills.Get(UnplacedColor), view.Id, [Square(1)]);
                var group = _doc.Create.NewGroup([unplaced.Id]);
                group.GroupType.Name = PurgeGroupUnplaced;
                unplacedGroupType = group.GroupType.Id;

                var defaultType = fills.Get(DefaultColor);
                if (_doc.IsDefaultElementTypeIdValid(ElementTypeGroup.FilledRegionType, defaultType))
                {
                    _doc.SetDefaultElementTypeId(ElementTypeGroup.FilledRegionType, defaultType);
                    defaultSet = true;
                }
            });
            var unplacedInstance = new FilteredElementCollector(_doc).OfClass(typeof(Group)).Cast<Group>()
                .FirstOrDefault(g => g.GetTypeId() == unplacedGroupType);
            if (unplacedInstance is not null) SelfTestTx("delete the only instance of a group", () => _doc.Delete(unplacedInstance.Id));
            var unplacedTypeKept = _doc.GetElement(unplacedGroupType) is GroupType;
            var unplacedCollected = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegion))
                .Any(e => _doc.GetElement(e.GetTypeId())?.Name == FillTypes.NameFor(UnplacedColor));

            // 1. A commit the purge makes fail: retried once without the purge.
            var beforeDeferred = GanttItFillNames();
            var legendsBefore = RevitUtil.Legends(_doc).Count;
            bool faultFired;
            DebugFaults.Arm(DebugFaults.AfterPurge);
            try
            {
                deferred = _backend.Generate(PurgeChart(DeferredChartName, DeferredChartColor), _settings);
                faultFired = !DebugFaults.IsArmed(DebugFaults.AfterPurge); // Hit disarms
            }
            finally
            {
                DebugFaults.Disarm();
            }
            var afterDeferred = GanttItFillNames();
            var deferredLegends = RevitUtil.Legends(_doc).Count(v => v.Name == DeferredChartName);
            var deferredListed = _backend.ListCharts().Any(e => e.ViewName == DeferredChartName);
            var expectedDeferred = new HashSet<string>(beforeDeferred, StringComparer.Ordinal) { FillTypes.NameFor(DeferredChartColor) };
            _r.Check("§7.3", "a commit failed by the purge is retried once without it: Generate succeeds, draws one chart, and the purge is deferred",
                faultFired && deferred.Succeeded && deferredLegends == 1 && deferredListed && RevitUtil.Legends(_doc).Count == legendsBefore + 1
                && afterDeferred.SetEquals(expectedDeferred) && afterDeferred.Contains(FillTypes.NameFor(OrphanColor)),
                new JsonObject
                {
                    ["status"] = deferred.Status.ToString(),
                    ["simulatedCommitFailureFired"] = faultFired,
                    ["legendsNamedLikeTheChart"] = deferredLegends,
                    ["listed"] = deferredListed,
                    ["legendsAdded"] = RevitUtil.Legends(_doc).Count - legendsBefore,
                    ["orphanStillThere"] = afterDeferred.Contains(FillTypes.NameFor(OrphanColor)),
                    ["unexpectedlyDeleted"] = Names(expectedDeferred.Except(afterDeferred)),
                    ["unexpectedlyAdded"] = Names(afterDeferred.Except(expectedDeferred)),
                },
                deferred.Message);

            // 2. The purge itself.
            var before = GanttItFillNames();
            gen = _backend.Generate(PurgeChart(PurgeChartName, PurgeChartColor), _settings);
            var after = GanttItFillNames();
            var purge = FillTypes.LastPurge;
            var defaultNow = _doc.GetElement(_doc.GetDefaultElementTypeId(ElementTypeGroup.FilledRegionType));

            var chartTypes = gen.ViewId is null ? [] : RegionTypeNames(gen.ViewId);
            var expected = new HashSet<string>(before, StringComparer.Ordinal) { FillTypes.NameFor(PurgeChartColor) };
            expected.Remove(FillTypes.NameFor(OrphanColor));
            expected.Remove(FillTypes.NameFor(DefaultColor)); // being the default is not a use (planner decision)
            // A group type Revit dropped with its last instance holds nothing: its type is an orphan too.
            if (!unplacedTypeKept && !unplacedCollected) expected.Remove(FillTypes.NameFor(UnplacedColor));

            _r.Check("§7.3", "Generate purges the orphan and the unused default Gantt-It fill type, and keeps every type still used (chart, detached copy, grouped and unplaced-group regions)",
                gen.Succeeded && after.SetEquals(expected)
                && !after.Contains(FillTypes.NameFor(OrphanColor)) && !after.Contains(FillTypes.NameFor(DefaultColor))
                && chartTypes.Count > 0 && chartTypes.IsSubsetOf(after)
                && copy is not null && copyTypes.Count > 0 && copyTypes.IsSubsetOf(after) && RegionCount(copy.Id) == copyRegions
                && after.Contains(FillTypes.NameFor(GroupedColor))
                && purge is not null && purge.Deleted.Contains(FillTypes.NameFor(OrphanColor)),
                new JsonObject
                {
                    ["status"] = gen.Status.ToString(),
                    ["orphanGone"] = !after.Contains(FillTypes.NameFor(OrphanColor)),
                    ["defaultTypeSet"] = defaultSet,
                    ["defaultTypeGone"] = !after.Contains(FillTypes.NameFor(DefaultColor)),
                    ["defaultAfterPurge"] = defaultNow is null ? "(none)" : $"{defaultNow.Name} ({defaultNow.Id.Value})",
                    ["unexpectedlyDeleted"] = Names(expected.Except(after)),
                    ["unexpectedlyKept"] = Names(after.Except(expected)),
                    ["chartTypes"] = Names(chartTypes),
                    ["copyTypes"] = Names(copyTypes),
                    ["copyRegionsBefore"] = copyRegions,
                    ["copyRegionsAfter"] = copy is null ? 0 : RegionCount(copy.Id),
                    ["unplacedGroupTypeKeptByRevit"] = unplacedTypeKept,
                    ["unplacedRegionSeenByCollector"] = unplacedCollected,
                    ["purgeDeleted"] = Names(purge?.Deleted ?? []),
                    ["purgeKept"] = Kept(purge),
                },
                gen.Message);
        }
        finally
        {
            // Remove the probes by hand and give the user's default back, whatever failed above.
            SelfTestTx("remove fill-type purge probes", () =>
            {
                if (defaultSet) RestoreDefaultFillType(previousDefault, force: true);
                RemovePurgeProbes();
            });
        }

        // 3. The next Gantt-It actions purge what the hand removal orphaned.
        var removed = new[] { deferred, gen }.Where(r => r?.ViewId is not null).Select(r => _backend.Delete(r!.ViewId!)).ToList();
        var left = GanttItFillNames().Where(PurgeProbeTypeNames.Contains).ToList();
        _r.Check("§7.3", "after the probes are removed by hand, deleting the charts purges every probe fill type and reports it",
            removed.Count == 2 && removed.All(r => r.Succeeded) && left.Count == 0
            && removed.Any(r => (r.Message ?? "").Contains("Removed unused fill types", StringComparison.Ordinal)),
            new JsonObject
            {
                ["deletes"] = new JsonArray(removed.Select(r => (JsonNode)r.Status.ToString()).ToArray()),
                ["probeTypesLeft"] = Names(left),
                ["purgeKept"] = Kept(FillTypes.LastPurge),
            },
            string.Join(" ", removed.Select(r => r.Message).Where(m => m is not null)));
    }

    private static Chart PurgeChart(string name, Rgb color)
    {
        var chart = Chart.Create(name);
        var start = new DateOnly(2026, 11, 2);
        chart.Sections.Add(new Section { Id = Guid.NewGuid(), Name = "Purge", Start = start, End = start.AddDays(9), Color = color });
        return chart;
    }

    private static JsonArray Kept(FillPurge? purge) =>
        new((purge?.Kept ?? []).Select(k => (JsonNode)$"{k.Name}: {k.Reason}").ToArray());

    /// <summary>
    /// Points the Filled Region default back at <paramref name="preferred"/>, or at any
    /// non-Gantt-It type when that is gone or is itself a Gantt-It type. Without
    /// <paramref name="force"/> it only acts when the default is missing or is a purge
    /// probe type (an aborted earlier run). Must run inside a transaction.
    /// </summary>
    private bool RestoreDefaultFillType(ElementId? preferred, bool force = false)
    {
        const ElementTypeGroup group = ElementTypeGroup.FilledRegionType;
        var current = _doc.GetElement(_doc.GetDefaultElementTypeId(group));
        if (!force && current is not null && !PurgeProbeTypeNames.Contains(current.Name)) return false;
        var target = preferred is not null && _doc.IsDefaultElementTypeIdValid(group, preferred)
                                           && !FillTypes.IsGanttItName(_doc.GetElement(preferred)?.Name)
            ? preferred
            : new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType))
                .Where(t => !FillTypes.IsGanttItName(t.Name)).Select(t => t.Id).FirstOrDefault();
        if (target is null) return false;
        _doc.SetDefaultElementTypeId(group, target);
        return true;
    }

    /// <summary>Deletes the probe drafting view (with its group instance), then the probe group types. Returns how many were found.</summary>
    private int RemovePurgeProbes()
    {
        var views = new FilteredElementCollector(_doc).OfClass(typeof(ViewDrafting)).Cast<View>()
            .Where(v => v.Name == PurgeViewName).Select(v => v.Id).ToList();
        if (views.Count > 0) _doc.Delete(views);
        var groupTypes = new FilteredElementCollector(_doc).OfClass(typeof(GroupType))
            .Where(t => t.Name is PurgeGroupPlaced or PurgeGroupUnplaced).Select(t => t.Id).ToList();
        if (groupTypes.Count > 0) _doc.Delete(groupTypes);
        return views.Count + groupTypes.Count;
    }

    private HashSet<string> RegionTypeNames(ElementId viewId) =>
        new FilteredElementCollector(_doc, viewId).OfClass(typeof(FilledRegion))
            .Select(e => _doc.GetElement(e.GetTypeId())?.Name)
            .Where(FillTypes.IsGanttItName)
            .Select(n => n!)
            .ToHashSet(StringComparer.Ordinal);

    private int RegionCount(ElementId viewId) =>
        new FilteredElementCollector(_doc, viewId).OfClass(typeof(FilledRegion)).GetElementCount();

    private static JsonArray Names(IEnumerable<string> names) =>
        new(names.Order(StringComparer.Ordinal).Select(n => (JsonNode)n).ToArray());

    /// <summary>A 0.1 ft square, <paramref name="slot"/> squares to the right of the origin.</summary>
    private static CurveLoop Square(int slot)
    {
        var x = slot * 0.2;
        var p0 = new XYZ(x, 0, 0);
        var p1 = new XYZ(x + 0.1, 0, 0);
        var p2 = new XYZ(x + 0.1, 0.1, 0);
        var p3 = new XYZ(x, 0.1, 0);
        return CurveLoop.Create(new List<Curve>
        {
            RevitLine.CreateBound(p0, p1),
            RevitLine.CreateBound(p1, p2),
            RevitLine.CreateBound(p2, p3),
            RevitLine.CreateBound(p3, p0),
        });
    }
}
#endif
