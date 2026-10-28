#if DEBUG
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace GanttIt.Revit.SelfTest;

/// <summary>
/// Debug-only Self-Test plumbing (§8.1), registered from <c>App.OnStartup</c>:
/// <list type="bullet">
/// <item>a <b>Gantt-It Self-Test</b> button on the Gantt-It panel;</item>
/// <item>the autorun harness: when <c>%LOCALAPPDATA%\Temp\GanttIt\autorun.json</c>
/// exists at startup, the request is consumed and the Self-Test runs on the first
/// idle tick that has a document open, then writes its report and a
/// <c>.done</c> marker;</item>
/// <item>a <c>DialogBoxShowing</c> handler that records every Revit dialog and,
/// during unattended runs only, dismisses it so nothing blocks.</item>
/// </list>
/// Reports go to <c>%LOCALAPPDATA%\Temp\GanttIt\</c>, not <c>Path.GetTempPath()</c>,
/// which inside Revit is a per-session subfolder (V9).
/// </summary>
internal static class SelfTestHarness
{
    public static string OutDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "GanttIt");

    private static string AutorunPath => Path.Combine(OutDir, "autorun.json");

    /// <summary>Optional arguments for the next ribbon-button run (e.g. the GUI Undo check).</summary>
    private static string RequestPath => Path.Combine(OutDir, "selftest-request.json");

    /// <summary>Every dialog Revit tried to show since startup.</summary>
    public static JsonArray DialogLog { get; } = [];

    private static bool _unattended;
    private static JsonObject? _autorun;

    public static void Register(UIControlledApplication application, RibbonPanel panel)
    {
        Directory.CreateDirectory(OutDir);
        panel.AddItem(new PushButtonData(
            "GanttIt.SelfTest",
            "Gantt-It\nSelf-Test",
            Assembly.GetExecutingAssembly().Location,
            typeof(SelfTestCommand).FullName)
        {
            ToolTip = "Debug build only: generates the fixture charts and writes selftest.json.",
        });

        application.DialogBoxShowing += OnDialog;

        if (!File.Exists(AutorunPath)) return;
        try
        {
            _autorun = JsonNode.Parse(File.ReadAllText(AutorunPath))?.AsObject() ?? [];
            _unattended = true;
            application.Idling += OnIdling;
            File.WriteAllText(Path.Combine(OutDir, "autorun.armed"), DateTime.Now.ToString("o"));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir, "autorun.error.txt"), ex.ToString());
        }
    }

    private static void OnDialog(object? sender, DialogBoxShowingEventArgs e)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTime.Now.ToString("o"),
            ["dialogId"] = e.DialogId,
            ["type"] = e.GetType().Name,
        };
        if (e is TaskDialogShowingEventArgs td) entry["message"] = td.Message;
        if (e is MessageBoxShowingEventArgs mb) entry["message"] = mb.Message;
        if (_unattended)
        {
            try
            {
                entry["dismissed"] = e.OverrideResult(1);
            }
            catch (Exception ex)
            {
                entry["dismissError"] = ex.Message;
            }
        }
        DialogLog.Add(entry);
    }

    private static void OnIdling(object? sender, IdlingEventArgs e)
    {
        if (sender is not UIApplication uiapp || uiapp.ActiveUIDocument is null) return;
        uiapp.Idling -= OnIdling;
        try
        {
            File.Delete(AutorunPath);
        }
        catch (Exception)
        {
            // Best effort: the request is consumed either way.
        }

        var args = _autorun?["args"] as JsonObject ?? [];
        try
        {
            Run(uiapp, args, "autorun");
        }
        finally
        {
            _unattended = false;
        }
    }

    /// <summary>Runs the Self-Test, writes the report and its <c>.done</c> marker, and returns the report path.</summary>
    public static string Run(UIApplication uiapp, JsonObject args, string trigger)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        var phase = args["phase"]?.GetValue<string>() ?? "1";
        var stem = Path.GetFileNameWithoutExtension(doc.PathName);
        if (string.IsNullOrEmpty(stem)) stem = doc.Title;
        var year = uiapp.Application.VersionNumber;
        var path = Path.Combine(OutDir, $"selftest-{year}-{stem}-{phase}.json");

        JsonObject report;
        try
        {
            report = new SelfTestRunner(uiapp, args, stem).Run(phase);
        }
        catch (Exception ex)
        {
            report = new JsonObject { ["overall"] = "FAIL", ["fatal"] = ex.ToString() };
        }

        try
        {
            report["trigger"] = trigger;
            report["request"] = args.DeepClone();
            report["dialogs"] = DialogLog.DeepClone();
            Report.Write(path, report);
            Report.Write(Path.Combine(OutDir, "selftest.json"), report);
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, $"{{\"overall\":\"FAIL\",\"fatal\":\"report could not be written\"}}");
            File.WriteAllText(path + ".error.txt", ex.ToString());
        }
        File.WriteAllText(path + ".done", DateTime.Now.ToString("o"));
        return path;
    }

    /// <summary>The arguments for a ribbon-button run: a pending request file (consumed), or phase 1.</summary>
    public static JsonObject TakeRequest()
    {
        try
        {
            if (!File.Exists(RequestPath)) return [];
            var request = JsonNode.Parse(File.ReadAllText(RequestPath))?.AsObject() ?? [];
            File.Delete(RequestPath);
            return request["args"] as JsonObject ?? request;
        }
        catch (Exception ex)
        {
            return new JsonObject { ["requestError"] = ex.Message };
        }
    }
}

/// <summary>The debug-only "Gantt-It Self-Test" ribbon command.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class SelfTestCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (commandData.Application.ActiveUIDocument is null)
        {
            message = "Open a project first.";
            return Result.Failed;
        }
        var args = SelfTestHarness.TakeRequest();
        var quiet = args["quiet"]?.GetValue<bool>() == true;
        var path = SelfTestHarness.Run(commandData.Application, args, "ribbon");
        if (!quiet) TaskDialog.Show("Gantt-It Self-Test", "Report written to\n" + path);
        return Result.Succeeded;
    }
}
#endif
