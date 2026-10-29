#if DEBUG
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using RevitLine = Autodesk.Revit.DB.Line;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// The fill-type purge (§7.3): every Gantt-It action ends by deleting the Gantt-It
/// fill types nothing uses, and never one that something still uses. Runs after
/// the A9 copies exist, so a detached copy's types are among those that must stay.
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const string PurgeChartName = "GI Purge";
    private const string PurgeViewName = "GI Purge probe";
    private const string PurgeGroupPlaced = "GI Purge group (placed)";
    private const string PurgeGroupUnplaced = "GI Purge group (unplaced)";
    private static readonly Rgb OrphanColor = Rgb.Parse("#0A0B0C");
    private static readonly Rgb GroupedColor = Rgb.Parse("#0D0E0F");
    private static readonly Rgb UnplacedColor = Rgb.Parse("#101112");
    private static readonly Rgb DefaultColor = Rgb.Parse("#131415");
    private static readonly Rgb PurgeChartColor = Rgb.Parse("#161718");

    /// <summary>
    /// Seeds an orphan Gantt-It fill type (no region) plus types held only by the
    /// user's own regions in a placed detail group and in a group type with no placed
    /// instance, and one made the Filled Region tool's default. Generate must delete
    /// the orphan only: the new chart's types, the detached copy's types and the held
    /// types all remain. When the probes are then removed by hand, the next action
    /// (Delete) purges their types.
    /// </summary>
    private void CheckFillPurge()
    {
        var copy = RevitUtil.Legends(_doc).FirstOrDefault(v => v.Name == CopyWName);
        var copyTypes = copy is null ? [] : RegionTypeNames(copy.Id);
        var copyRegions = copy is null ? 0 : RegionCount(copy.Id);

        // Seed, the way a user (or an aborted session) would leave the model.
        var unplacedGroupType = ElementId.InvalidElementId;
        var previousDefault = _doc.GetDefaultElementTypeId(ElementTypeGroup.FilledRegionType);
        var defaultSet = false;
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

        var before = GanttItFillNames();
        var chart = Chart.Create(PurgeChartName);
        var start = new DateOnly(2026, 11, 2);
        chart.Sections.Add(new Section { Id = Guid.NewGuid(), Name = "Purge", Start = start, End = start.AddDays(9), Color = PurgeChartColor });
        var gen = _backend.Generate(chart, _settings);
        var after = GanttItFillNames();
        var purge = FillTypes.LastPurge;

        var chartTypes = gen.ViewId is null ? [] : RegionTypeNames(gen.ViewId);
        var expected = new HashSet<string>(before, StringComparer.Ordinal) { FillTypes.NameFor(PurgeChartColor) };
        expected.Remove(FillTypes.NameFor(OrphanColor));
        // A group type Revit dropped with its last instance holds nothing: its type is an orphan too.
        if (!unplacedTypeKept && !unplacedCollected) expected.Remove(FillTypes.NameFor(UnplacedColor));
        if (!defaultSet) expected.Remove(FillTypes.NameFor(DefaultColor));

        _r.Check("§7.3", "Generate purges an orphan Gantt-It fill type and keeps every type still used (chart, detached copy, grouped and unplaced-group regions, Filled Region default)",
            gen.Succeeded && !after.Contains(FillTypes.NameFor(OrphanColor)) && after.SetEquals(expected)
            && chartTypes.Count > 0 && chartTypes.IsSubsetOf(after)
            && copy is not null && copyTypes.Count > 0 && copyTypes.IsSubsetOf(after) && RegionCount(copy.Id) == copyRegions
            && after.Contains(FillTypes.NameFor(GroupedColor))
            && purge is not null && purge.Deleted.Contains(FillTypes.NameFor(OrphanColor)),
            new JsonObject
            {
                ["status"] = gen.Status.ToString(),
                ["orphanGone"] = !after.Contains(FillTypes.NameFor(OrphanColor)),
                ["unexpectedlyDeleted"] = Names(expected.Except(after)),
                ["unexpectedlyKept"] = Names(after.Except(expected)),
                ["chartTypes"] = Names(chartTypes),
                ["copyTypes"] = Names(copyTypes),
                ["copyRegionsBefore"] = copyRegions,
                ["copyRegionsAfter"] = copy is null ? 0 : RegionCount(copy.Id),
                ["unplacedGroupTypeKeptByRevit"] = unplacedTypeKept,
                ["unplacedRegionSeenByCollector"] = unplacedCollected,
                ["defaultTypeSet"] = defaultSet,
                ["purgeDeleted"] = Names(purge?.Deleted ?? []),
                ["purgeKept"] = new JsonArray((purge?.Kept ?? []).Select(k => (JsonNode)$"{k.Name}: {k.Reason}").ToArray()),
            },
            gen.Message);

        // Remove the probes by hand; the next Gantt-It action (Delete) purges their types.
        SelfTestTx("remove fill-type purge probes", () =>
        {
            if (defaultSet)
            {
                // Back to the user's default (or any non-Gantt-It type, after an aborted run).
                var restore = _doc.IsDefaultElementTypeIdValid(ElementTypeGroup.FilledRegionType, previousDefault)
                              && !FillTypes.IsGanttItName(_doc.GetElement(previousDefault)?.Name)
                    ? previousDefault
                    : new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType))
                        .Where(t => !FillTypes.IsGanttItName(t.Name)).Select(t => t.Id).FirstOrDefault();
                if (restore is not null) _doc.SetDefaultElementTypeId(ElementTypeGroup.FilledRegionType, restore);
            }
            RemovePurgeProbes();
        });
        var removed = gen.ViewId is null ? null : _backend.Delete(gen.ViewId);
        var probeColors = new[] { OrphanColor, GroupedColor, UnplacedColor, DefaultColor, PurgeChartColor }.Select(FillTypes.NameFor).ToList();
        var left = GanttItFillNames().Where(probeColors.Contains).ToList();
        _r.Check("§7.3", "after the probes are removed by hand, Delete purges their fill types and reports them",
            removed is { Succeeded: true } && left.Count == 0
            && (removed.Message ?? "").Contains("Removed unused fill types", StringComparison.Ordinal),
            new JsonObject
            {
                ["status"] = removed?.Status.ToString(),
                ["probeTypesLeft"] = Names(left),
                ["purgeKept"] = new JsonArray((FillTypes.LastPurge?.Kept ?? []).Select(k => (JsonNode)$"{k.Name}: {k.Reason}").ToArray()),
            },
            removed?.Message);
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
