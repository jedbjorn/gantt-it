using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace GanttIt.Spike;

internal static class Util
{
    public const double MmPerFt = 304.8;
    public static double Ft(double mm) => mm / MmPerFt;
    public static double Mm(double ft) => ft * MmPerFt;

    public static JsonNode N(double v, int digits = 5) =>
        double.IsNaN(v) || double.IsInfinity(v) ? null : JsonValue.Create(Math.Round(v, digits));

    public static JsonArray P(XYZ p) => p == null ? null : new JsonArray(N(Mm(p.X)), N(Mm(p.Y)), N(Mm(p.Z)));
    public static JsonArray Pft(XYZ p) => p == null ? null : new JsonArray(N(p.X, 9), N(p.Y, 9), N(p.Z, 9));

    public static JsonObject Bb(BoundingBoxXYZ bb) => bb == null ? null : new JsonObject
    {
        ["minMm"] = P(bb.Min), ["maxMm"] = P(bb.Max),
        ["wMm"] = N(Mm(bb.Max.X - bb.Min.X)), ["hMm"] = N(Mm(bb.Max.Y - bb.Min.Y)),
        ["transformIsIdentity"] = bb.Transform?.IsIdentity,
    };

    public static long Id(ElementId id) => id?.Value ?? -1;

    public static JsonObject Err(Exception ex) => new()
    {
        ["error"] = ex.GetType().FullName, ["message"] = ex.Message, ["stack"] = ex.StackTrace,
    };

    public static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Runs one named step; exceptions are captured into the step object instead of aborting the run.</summary>
    public static JsonObject Step(string name, Func<JsonObject> body)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        JsonObject o;
        try { o = body() ?? new JsonObject(); }
        catch (Exception ex) { o = new JsonObject { ["exception"] = Err(ex) }; }
        o["elapsedMs"] = sw.ElapsedMilliseconds;
        return o;
    }

    /// <summary>One transaction with a failure preprocessor that records every failure, deletes warnings
    /// (so no modal warning pops up) and rolls back on errors.</summary>
    public static TransactionStatus Tx(Document doc, string name, Action body, JsonArray failLog)
    {
        using var t = new Transaction(doc, name);
        t.Start();
        var fho = t.GetFailureHandlingOptions();
        fho.SetFailuresPreprocessor(new RecordingPreprocessor(failLog, name));
        fho.SetClearAfterRollback(true);
        t.SetFailureHandlingOptions(fho);
        try
        {
            body();
            return t.Commit();
        }
        catch
        {
            if (t.HasStarted() && !t.HasEnded()) t.RollBack();
            throw;
        }
    }

    public static IEnumerable<View> Legends(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => v.ViewType == ViewType.Legend && !v.IsTemplate);

    public static List<Element> OwnedBy(Document doc, ElementId viewId) =>
        new FilteredElementCollector(doc).WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == viewId).ToList();

    public static JsonArray Describe(IEnumerable<Element> els) =>
        new(els.Select(e => (JsonNode)new JsonObject
        {
            ["id"] = Id(e.Id), ["class"] = e.GetType().Name, ["category"] = e.Category?.Name, ["name"] = SafeName(e),
        }).ToArray());

    public static string SafeName(Element e) { try { return e.Name; } catch { return null; } }
}

internal class RecordingPreprocessor : IFailuresPreprocessor
{
    private readonly JsonArray _log; private readonly string _tx;
    public RecordingPreprocessor(JsonArray log, string tx) { _log = log; _tx = tx; }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
    {
        var hasError = false;
        foreach (var m in fa.GetFailureMessages())
        {
            var sev = m.GetSeverity();
            _log?.Add(new JsonObject
            {
                ["transaction"] = _tx, ["severity"] = sev.ToString(), ["description"] = m.GetDescriptionText(),
                ["failureDefinition"] = m.GetFailureDefinitionId()?.Guid.ToString(),
                ["failingIds"] = new JsonArray(m.GetFailingElementIds().Select(i => (JsonNode)Util.Id(i)).ToArray()),
            });
            if (sev == FailureSeverity.Warning) fa.DeleteWarning(m);
            else hasError = true;
        }
        return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
