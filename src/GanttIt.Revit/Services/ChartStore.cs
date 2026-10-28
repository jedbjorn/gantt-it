using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using GanttIt.Core.Model;
using GanttIt.Core.Serialization;

namespace GanttIt.Revit.Services;

/// <summary>A chart entity as stored on its legend view (§7.2).</summary>
internal sealed record StoredChart(View View, string ChartJson, IReadOnlyList<string> GeneratedIds, string AppVersion);

/// <summary>
/// Extensible Storage persistence (§7.2): the <c>GanttIt.Chart</c> v1 entity on each
/// chart legend and the <c>GanttIt.Settings</c> v1 entity on one DataStorage. Both
/// schemas use vendor <c>DSOS</c> with Vendor read/write access (D10); the vendor
/// must match <c>&lt;VendorId&gt;</c> in GanttIt.addin.
/// </summary>
internal static class ChartStore
{
    public const string VendorId = "DSOS";

    /// <summary>GanttIt.Chart v1. Fixed forever: changing it orphans every stored chart.</summary>
    public static readonly Guid ChartSchemaGuid = new("7430cd51-df3c-49b3-8ba0-ef4e5cf65043");

    /// <summary>GanttIt.Settings v1. Fixed forever.</summary>
    public static readonly Guid SettingsSchemaGuid = new("aedc297f-b9b0-4a81-a4ac-1654b4e1dda3");

    private const string ChartJsonField = "ChartJson";
    private const string GeneratedIdsField = "GeneratedIds";
    private const string SettingsJsonField = "SettingsJson";
    private const string AppVersionField = "AppVersion";

    public static Schema ChartSchema() =>
        Schema.Lookup(ChartSchemaGuid) ?? Build(ChartSchemaGuid, "GanttItChart", "Gantt-It chart (GanttIt.Chart v1)", b =>
        {
            b.AddSimpleField(ChartJsonField, typeof(string));
            b.AddArrayField(GeneratedIdsField, typeof(string));
            b.AddSimpleField(AppVersionField, typeof(string));
        });

    public static Schema SettingsSchema() =>
        Schema.Lookup(SettingsSchemaGuid) ?? Build(SettingsSchemaGuid, "GanttItSettings", "Gantt-It project settings (GanttIt.Settings v1)", b =>
        {
            b.AddSimpleField(SettingsJsonField, typeof(string));
            b.AddSimpleField(AppVersionField, typeof(string));
        });

    private static Schema Build(Guid guid, string name, string documentation, Action<SchemaBuilder> fields)
    {
        var builder = new SchemaBuilder(guid);
        builder.SetSchemaName(name);
        builder.SetDocumentation(documentation);
        builder.SetVendorId(VendorId);
        builder.SetReadAccessLevel(AccessLevel.Vendor);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        fields(builder);
        return builder.Finish();
    }

    // ------------------------------------------------------------------ charts

    /// <summary>The chart entity on <paramref name="view"/>, or null when it has none.</summary>
    public static StoredChart? Read(View view)
    {
        var entity = view.GetEntity(ChartSchema());
        if (entity is null || !entity.IsValid()) return null;
        return new StoredChart(
            view,
            entity.Get<string>(ChartJsonField) ?? "",
            entity.Get<IList<string>>(GeneratedIdsField)?.ToList() ?? [],
            entity.Get<string>(AppVersionField) ?? "");
    }

    /// <summary>Every legend with a valid chart entity, in ascending ElementId order (§7.2).</summary>
    public static List<StoredChart> All(Document doc) =>
        RevitUtil.Legends(doc).Select(Read).Where(c => c is not null).Select(c => c!).ToList();

    /// <summary>Writes (or overwrites) the chart entity. Must run inside a transaction.</summary>
    public static void Write(View view, Chart chart, IEnumerable<string> generatedIds)
    {
        var entity = new Entity(ChartSchema());
        entity.Set(ChartJsonField, ChartJson.Serialize(chart));
        entity.Set<IList<string>>(GeneratedIdsField, generatedIds.ToList());
        entity.Set(AppVersionField, RevitUtil.AppVersion);
        view.SetEntity(entity);
    }

    /// <summary>Removes an inherited chart entity (a duplicated chart legend used as a source).</summary>
    public static void Clear(View view)
    {
        if (Read(view) is not null) view.DeleteEntity(ChartSchema());
    }

    /// <summary>
    /// The OwnerViewId hazard (§7.2 v0.8, V6): a copied legend carries its source's
    /// entity verbatim, so its GeneratedIds can name the <i>source</i> chart's live
    /// elements. Only ids whose element exists and is owned by <paramref name="view"/>
    /// are returned; every other id is ignored, never deleted.
    /// </summary>
    public static List<ElementId> OwnedGeneratedIds(Document doc, View view, IEnumerable<string> uniqueIds)
    {
        var result = new List<ElementId>();
        foreach (var uid in uniqueIds)
        {
            if (string.IsNullOrEmpty(uid)) continue;
            Element? e;
            try
            {
                e = doc.GetElement(uid);
            }
            catch (Exception)
            {
                continue;
            }
            if (e is not null && e.OwnerViewId == view.Id) result.Add(e.Id);
        }
        return result;
    }

    // ------------------------------------------------------------------ settings

    /// <summary>The DataStorage holding the settings entity (lowest id if several), or null.</summary>
    public static DataStorage? SettingsStorage(Document doc)
    {
        var schema = SettingsSchema();
        return new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .Where(d => d.GetEntity(schema) is { } e && e.IsValid())
            .OrderBy(d => d.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>The stored settings JSON, or null when the model holds none.</summary>
    public static string? ReadSettingsJson(Document doc)
    {
        var storage = SettingsStorage(doc);
        return storage?.GetEntity(SettingsSchema()).Get<string>(SettingsJsonField);
    }

    /// <summary>Writes the settings entity, creating its DataStorage if needed. Must run inside a transaction.</summary>
    public static DataStorage WriteSettings(Document doc, ProjectSettings settings)
    {
        var storage = SettingsStorage(doc) ?? DataStorage.Create(doc);
        var entity = new Entity(SettingsSchema());
        entity.Set(SettingsJsonField, SettingsJson.Serialize(settings));
        entity.Set(AppVersionField, RevitUtil.AppVersion);
        storage.SetEntity(entity);
        return storage;
    }
}
