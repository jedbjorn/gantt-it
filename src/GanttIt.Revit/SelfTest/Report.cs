#if DEBUG
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// The Self-Test's JSON report: every check records PASS/FAIL plus its numbers,
/// and the report carries an overall result (§8.1).
/// </summary>
internal sealed class Report
{
    private readonly JsonArray _checks = [];
    private readonly JsonObject _sections = [];

    public JsonObject Root { get; } = [];

    public int Failed { get; private set; }

    public int Passed { get; private set; }

    /// <summary>Records one check. <paramref name="criterion"/> is the acceptance id (A5, A6 …).</summary>
    public bool Check(string criterion, string name, bool pass, JsonObject? numbers = null, string? detail = null)
    {
        var o = new JsonObject
        {
            ["criterion"] = criterion,
            ["name"] = name,
            ["result"] = pass ? "PASS" : "FAIL",
        };
        if (numbers is not null) o["numbers"] = numbers;
        if (detail is not null) o["detail"] = detail;
        _checks.Add(o);
        if (pass) Passed++;
        else Failed++;
        return pass;
    }

    /// <summary>Extra data under <c>sections.&lt;name&gt;</c>.</summary>
    public void Section(string name, JsonNode? data) => _sections[name] = data;

    /// <summary>
    /// Runs one step; an exception becomes a FAIL check instead of ending the run.
    /// </summary>
    public void Step(string criterion, string name, Action body)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Check(criterion, $"{name}: completes without an exception", false,
                detail: $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        Timings[name] = sw.ElapsedMilliseconds;
    }

    public JsonObject Timings { get; } = [];

    public JsonObject Finish()
    {
        Root["overall"] = Failed == 0 && Passed > 0 ? "PASS" : "FAIL";
        Root["summary"] = new JsonObject
        {
            ["passed"] = Passed,
            ["failed"] = Failed,
            ["byCriterion"] = ByCriterion(),
        };
        Root["checks"] = _checks;
        Root["sections"] = _sections;
        Root["timingsMs"] = Timings;
        return Root;
    }

    private JsonObject ByCriterion()
    {
        var result = new JsonObject();
        foreach (var group in _checks.Select(c => c!.AsObject()).GroupBy(c => c["criterion"]!.GetValue<string>()).OrderBy(g => g.Key))
        {
            var failed = group.Count(c => c["result"]!.GetValue<string>() != "PASS");
            result[group.Key] = new JsonObject
            {
                ["result"] = failed == 0 ? "PASS" : "FAIL",
                ["checks"] = group.Count(),
                ["failed"] = failed,
            };
        }
        return result;
    }

    public static JsonValue? N(double v, int digits = 4) =>
        double.IsFinite(v) ? JsonValue.Create(Math.Round(v, digits)) : null;

    /// <summary>Feet to millimetres, rounded.</summary>
    public static JsonValue? Mm(double feet, int digits = 4) => N(feet * 304.8, digits);

    public static void Write(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
#endif
