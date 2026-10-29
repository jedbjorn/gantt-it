#if DEBUG
using System.IO;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using GanttIt.Revit.Services;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// Helpers for the MCP-driven Delete check (A8), run from the ribbon button with a
/// request file (<c>revit.ps1 -Action request -Phase … -Chart …</c>):
/// <list type="bullet">
/// <item><c>place-sheets</c>: places the named chart on <c>count</c> new sheets
/// (default 2) and records the view, viewports and fill types it uses;</item>
/// <item><c>a8-check</c>: read-only. After the Delete through the UI, the view and
/// each recorded viewport are gone and no Gantt-It fill type is left unused.</item>
/// </list>
/// </summary>
internal sealed partial class SelfTestRunner
{
    private const string A8SheetPrefix = "GI A8 Sheet";

    private string A8Path => Path.Combine(SelfTestHarness.OutDir, $"selftest-a8-{_year}-{_stem}.json");

    private void PlaceOnSheets()
    {
        var name = _args["chart"]?.GetValue<string>();
        var count = _args["count"]?.GetValue<int>() ?? 2;
        var view = RevitUtil.Legends(_doc).FirstOrDefault(v => v.Name == name);
        if (view is null || ChartStore.ReadLive(view) is not { } stored)
        {
            _r.Check("A8", $"place-sheets: a live chart named '{name}' exists", false);
            return;
        }

        var viewports = new JsonArray();
        var sheets = new JsonArray();
        SelfTestTx($"place {name} on {count} sheets", () =>
        {
            var titleBlock = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().FirstElementId() ?? ElementId.InvalidElementId;
            for (var i = 1; i <= count; i++)
            {
                var sheet = ViewSheet.Create(_doc, titleBlock);
                sheet.Name = $"{A8SheetPrefix} {i}";
                var vp = Viewport.Create(_doc, sheet.Id, view.Id, new XYZ(0.6, 0.6, 0));
                viewports.Add(vp.Id.Value);
                sheets.Add($"{sheet.SheetNumber} - {sheet.Name}");
            }
        });

        var fills = stored.GeneratedIds
            .Select(uid => _doc.GetElement(uid))
            .OfType<FilledRegion>()
            .Select(f => _doc.GetElement(f.GetTypeId())?.Name)
            .Where(n => n is not null)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (JsonNode)n!)
            .ToArray();
        var record = new JsonObject
        {
            ["chart"] = name,
            ["viewId"] = view.Id.Value,
            ["viewUniqueId"] = view.UniqueId,
            ["viewportIds"] = viewports,
            ["sheets"] = sheets,
            ["chartFillTypes"] = new JsonArray(fills),
            ["fillInventory"] = FillInventory(),
        };
        Report.Write(A8Path, record);
        var listed = RevitUtil.SheetsShowing(_doc, view.Id);
        _r.Check("A8", $"place-sheets: '{name}' placed on {count} sheet{(count == 1 ? "" : "s")}", listed.Count == count,
            record.DeepClone().AsObject());
    }

    private void CheckAfterDelete()
    {
        if (!File.Exists(A8Path))
        {
            _r.Check("A8", "a8-check: place-sheets record exists", false, detail: A8Path);
            return;
        }
        var record = JsonNode.Parse(File.ReadAllText(A8Path))!.AsObject();
        var uid = record["viewUniqueId"]!.GetValue<string>();
        var name = record["chart"]!.GetValue<string>();
        var viewGone = _doc.GetElement(uid) is null && RevitUtil.Legends(_doc).All(v => v.Name != name);
        var viewportsLeft = record["viewportIds"]!.AsArray()
            .Select(n => n!.GetValue<long>())
            .Count(id => _doc.GetElement(new ElementId(id)) is not null);
        var inventory = FillInventory();
        var unused = inventory.Select(n => n!.AsObject()).Count(o => o["regions"]!.GetValue<int>() == 0);
        var sheetsLeft = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.Name.StartsWith(A8SheetPrefix, StringComparison.Ordinal))
            .Select(s => (JsonNode)$"{s.SheetNumber} - {s.Name} ({new FilteredElementCollector(_doc, s.Id).OfClass(typeof(Viewport)).GetElementCount()} viewports)")
            .ToArray();

        _r.Check("A8", $"a8-check: '{name}' view is gone", viewGone);
        var recorded = record["viewportIds"]!.AsArray().Count;
        _r.Check("A8", recorded == 1 ? "a8-check: the 1 recorded viewport is gone" : $"a8-check: all {recorded} recorded viewports are gone",
            viewportsLeft == 0,
            new JsonObject { ["recorded"] = recorded, ["stillExisting"] = viewportsLeft });
        _r.Check("A8", "a8-check: no unused Gantt-It fill type remains", unused == 0,
            new JsonObject { ["unused"] = unused, ["fillInventory"] = inventory, ["before"] = record["fillInventory"]?.DeepClone() });
        _r.Section("a8", new JsonObject
        {
            ["record"] = record.DeepClone(),
            ["sheetsAfter"] = new JsonArray(sheetsLeft),
            ["liveCharts"] = new JsonArray(ChartStore.Live(_doc).Select(c => (JsonNode)c.View.Name).ToArray()),
        });
    }

    /// <summary>Every Gantt-It fill type and how many filled regions use it.</summary>
    private JsonArray FillInventory()
    {
        var uses = new FilteredElementCollector(_doc).OfClass(typeof(FilledRegion))
            .GroupBy(e => e.GetTypeId())
            .ToDictionary(g => g.Key, g => g.Count());
        return new JsonArray(new FilteredElementCollector(_doc).OfClass(typeof(FilledRegionType))
            .Where(t => FillTypes.IsGanttItName(t.Name))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["regions"] = uses.TryGetValue(t.Id, out var n) ? n : 0,
            })
            .ToArray());
    }
}
#endif
