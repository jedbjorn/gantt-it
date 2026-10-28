using System.Reflection;
using System.Text.Json.Nodes;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace GanttIt.Spike;

/// <summary>
/// Phase 0 spike application (throwaway).
///
/// Ribbon: one "Gantt-It" panel on the built-in Add-Ins tab (CreateRibbonPanel without a tab name).
///
/// Autorun harness (reusable pattern for the later Self-Test):
///   If %TEMP%\GanttIt\autorun.json exists when Revit starts, the add-in subscribes to Idling and,
///   on the first Idling tick that has an ActiveUIDocument (the model passed on Revit's command
///   line), consumes (deletes) the request, runs the named action and writes its JSON report plus
///   a "&lt;report&gt;.done" marker. The driver script waits for the marker and then stops Revit.
///   Request shape: { "action": "spike" | "verify" | "hello", "args": { ... } }
/// </summary>
public class SpikeApp : IExternalApplication
{
    // NOT Path.GetTempPath(): inside Revit, TEMP points at a per-session GUID subfolder
    // (%TEMP%\<guid>\), which a driver outside Revit cannot predict. Use the user's real temp dir.
    internal static string OutDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "GanttIt");
    internal static string AutorunPath => Path.Combine(OutDir, "autorun.json");
    internal static readonly JsonArray DialogLog = new();
    internal static bool AutorunActive;
    private static JsonObject _request;

    public Result OnStartup(UIControlledApplication app)
    {
        Directory.CreateDirectory(OutDir);
        var panel = app.CreateRibbonPanel("Gantt-It");
        var asm = Assembly.GetExecutingAssembly().Location;
        panel.AddItem(new PushButtonData("GanttItHello", "Hello", asm, typeof(HelloCommand).FullName));
        panel.AddItem(new PushButtonData("GanttItSpike", "Run\nSpike", asm, typeof(SpikeCommand).FullName));
        panel.AddItem(new PushButtonData("GanttItV7", "V7\nDialog", asm, typeof(V7Command).FullName));
        panel.AddItem(new PushButtonData("GanttItV7Probe", "V7\nProbe", asm, typeof(V7ProbeCommand).FullName));
        panel.AddItem(new PushButtonData("GanttItVerify", "Verify", asm, typeof(VerifyCommand).FullName));

        app.DialogBoxShowing += OnDialog;

        if (File.Exists(AutorunPath))
        {
            try
            {
                _request = JsonNode.Parse(File.ReadAllText(AutorunPath))!.AsObject();
                AutorunActive = true;
                app.Idling += OnIdling;
                File.WriteAllText(Path.Combine(OutDir, "autorun.armed"), DateTime.Now.ToString("o"));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(OutDir, "autorun.error.txt"), ex.ToString());
            }
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    private static void OnDialog(object sender, DialogBoxShowingEventArgs e)
    {
        var o = new JsonObject { ["at"] = DateTime.Now.ToString("o"), ["dialogId"] = e.DialogId, ["type"] = e.GetType().Name };
        if (e is TaskDialogShowingEventArgs td) o["message"] = td.Message;
        if (e is MessageBoxShowingEventArgs mb) o["message"] = mb.Message;
        // While an unattended autorun is in flight, auto-accept Revit dialogs so nothing blocks;
        // every one is recorded in the report either way.
        if (AutorunActive)
        {
            try { o["overridden"] = e.OverrideResult(1); } catch (Exception ex) { o["overrideError"] = ex.Message; }
        }
        DialogLog.Add(o);
    }

    private static void OnIdling(object sender, IdlingEventArgs e)
    {
        var uiapp = sender as UIApplication;
        if (uiapp?.ActiveUIDocument == null) return;
        ((UIApplication)sender).Idling -= OnIdling;
        try { File.Delete(AutorunPath); } catch { /* best effort */ }

        var action = _request?["action"]?.GetValue<string>() ?? "spike";
        var args = _request?["args"] as JsonObject ?? new JsonObject();
        var year = uiapp.Application.VersionNumber;
        var outPath = Path.Combine(OutDir, $"{action}-{year}.json");
        JsonObject report;
        try
        {
            report = action switch
            {
                "verify" => Verify.Run(uiapp, args),
                "hello" => HelloCommand.Report(uiapp),
                _ => Spike.Run(uiapp, args),
            };
        }
        catch (Exception ex)
        {
            report = new JsonObject { ["fatal"] = ex.ToString() };
        }
        report["autorun"] = new JsonObject { ["action"] = action, ["args"] = args.DeepClone() };
        report["dialogs"] = DialogLog.DeepClone();
        Util.WriteJson(outPath, report);
        File.WriteAllText(outPath + ".done", DateTime.Now.ToString("o"));
        AutorunActive = false;
    }
}

[Transaction(TransactionMode.Manual)]
public class HelloCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var r = Report(data.Application);
        Util.WriteJson(Path.Combine(SpikeApp.OutDir, $"hello-{data.Application.Application.VersionNumber}.json"), r);
        TaskDialog.Show("Gantt-It", $"Hello from Gantt-It (spike)\n\nRevit {r["revitVersion"]} build {r["revitBuild"]}\n.NET {r["dotnet"]}\nBuilt for {r["builtFor"]}");
        return Result.Succeeded;
    }

    internal static JsonObject Report(UIApplication uiapp) => new()
    {
        ["revitVersion"] = uiapp.Application.VersionNumber,
        ["revitBuild"] = uiapp.Application.VersionBuild,
        ["subVersion"] = uiapp.Application.SubVersionNumber,
        ["dotnet"] = Environment.Version.ToString(),
        ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["builtFor"] = typeof(HelloCommand).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName,
        ["assembly"] = typeof(HelloCommand).Assembly.Location,
        ["at"] = DateTime.Now.ToString("o"),
    };
}

[Transaction(TransactionMode.Manual)]
public class SpikeCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var report = Spike.Run(data.Application, new JsonObject());
        report["dialogs"] = SpikeApp.DialogLog.DeepClone();
        var path = Path.Combine(SpikeApp.OutDir, $"spike-{data.Application.Application.VersionNumber}.json");
        Util.WriteJson(path, report);
        TaskDialog.Show("Gantt-It Spike", "Report written to\n" + path);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class VerifyCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var report = Verify.Run(data.Application, new JsonObject());
        var path = Path.Combine(SpikeApp.OutDir, $"verify-{data.Application.Application.VersionNumber}.json");
        Util.WriteJson(path, report);
        TaskDialog.Show("Gantt-It Spike", "Verify report written to\n" + path);
        return Result.Succeeded;
    }
}
