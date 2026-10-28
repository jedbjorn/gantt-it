using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using RDB = Autodesk.Revit.DB;
using RUI = Autodesk.Revit.UI;

namespace GanttIt.Spike;

/// <summary>V7: modal WPF window owned by the Revit main window; a button handler runs a transaction
/// while the dialog is open (no ExternalEvent). Writes %TEMP%\GanttIt\v7-&lt;year&gt;.json.</summary>
[Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
public class V7Command : RUI.IExternalCommand
{
    public RUI.Result Execute(RUI.ExternalCommandData data, ref string message, RDB.ElementSet elements)
    {
        var uiapp = data.Application;
        var doc = uiapp.ActiveUIDocument.Document;
        var log = new JsonObject
        {
            ["revitMainWindowHandle"] = uiapp.MainWindowHandle.ToInt64(),
            ["docIsModifiableAtCommandStart"] = doc.IsModifiable,
            ["clicks"] = new JsonArray(),
        };
        var path = Path.Combine(SpikeApp.OutDir, $"v7-{uiapp.Application.VersionNumber}.json");
        var w = new V7Window(uiapp, log, path);
        var helper = new WindowInteropHelper(w) { Owner = uiapp.MainWindowHandle };
        log["ownerHandleSet"] = helper.Owner.ToInt64();
        var result = w.ShowDialog();
        log["dialogResult"] = result?.ToString();
        log["closedAt"] = DateTime.Now.ToString("o");
        Util.WriteJson(path, log);
        return RUI.Result.Succeeded;
    }
}

internal class V7Window : Window
{
    private readonly RUI.UIApplication _uiapp; private readonly JsonObject _log; private readonly string _path;
    private readonly TextBlock _status;

    public V7Window(RUI.UIApplication uiapp, JsonObject log, string path)
    {
        _uiapp = uiapp; _log = log; _path = path;
        Title = "Gantt-It Spike V7"; Width = 460; Height = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var sp = new StackPanel { Margin = new Thickness(14) };
        _status = new TextBlock { Text = "Ready. Click Create to add a TextNote inside a transaction.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetAutomationId(_status, "V7Status");
        var create = new Button { Content = "Create element", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetAutomationId(create, "V7Create");
        create.Click += OnCreate;
        var close = new Button { Content = "Close", Padding = new Thickness(10, 4, 10, 4) };
        AutomationProperties.SetAutomationId(close, "V7Close");
        close.Click += (_, _) => { DialogResult = true; };
        sp.Children.Add(_status); sp.Children.Add(create); sp.Children.Add(close);
        Content = sp;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        var doc = _uiapp.ActiveUIDocument.Document;
        var click = new JsonObject { ["at"] = DateTime.Now.ToString("o"), ["docIsModifiableBefore"] = doc.IsModifiable };
        try
        {
            var view = doc.ActiveView is RDB.View av && av.ViewType == RDB.ViewType.Legend ? av : Util.Legends(doc).First();
            var n = ((JsonArray)_log["clicks"]).Count + 1;
            RDB.ElementId created = null;
            var fails = new JsonArray();
            var st = Util.Tx(doc, $"Gantt-It: V7 create {n}", () =>
            {
                var note = RDB.TextNote.Create(doc, view.Id, new RDB.XYZ(Util.Ft(20 * n), Util.Ft(-200), 0), $"V7 note {n} created from a modal WPF button",
                    doc.GetDefaultElementTypeId(RDB.ElementTypeGroup.TextNoteType));
                created = note.Id;
            }, fails);
            click["view"] = view.Name; click["viewId"] = Util.Id(view.Id);
            click["createdId"] = Util.Id(created); click["txStatus"] = st.ToString(); click["failures"] = fails;
            click["existsAfterCommit"] = created != null && doc.GetElement(created) != null;
            _status.Text = $"Click {n}: created TextNote id {Util.Id(created)} in '{view.Name}' (tx {st}).";
        }
        catch (Exception ex)
        {
            click["exception"] = Util.Err(ex);
            _status.Text = "Error: " + ex.Message;
        }
        ((JsonArray)_log["clicks"]).Add(click);
        Util.WriteJson(_path, _log);
    }
}

/// <summary>Run after closing the V7 dialog and pressing Undo: reports which V7 ids still exist.</summary>
[Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
public class V7ProbeCommand : RUI.IExternalCommand
{
    public RUI.Result Execute(RUI.ExternalCommandData data, ref string message, RDB.ElementSet elements)
    {
        var uiapp = data.Application;
        var doc = uiapp.ActiveUIDocument.Document;
        var year = uiapp.Application.VersionNumber;
        var v7 = JsonNode.Parse(File.ReadAllText(Path.Combine(SpikeApp.OutDir, $"v7-{year}.json")))!.AsObject();
        var probe = new JsonObject { ["at"] = DateTime.Now.ToString("o") };
        var arr = new JsonArray();
        foreach (var c in (JsonArray)v7["clicks"])
        {
            var id = c?["createdId"]?.GetValue<long>() ?? -1;
            arr.Add(new JsonObject { ["id"] = id, ["exists"] = id > 0 && doc.GetElement(new RDB.ElementId(id)) != null });
        }
        probe["ids"] = arr;
        var probePath = Path.Combine(SpikeApp.OutDir, $"v7-probe-{year}-{DateTime.Now:HHmmss}.json");
        Util.WriteJson(probePath, probe);
        RUI.TaskDialog.Show("Gantt-It V7 probe", probe.ToJsonString());
        return RUI.Result.Succeeded;
    }
}
