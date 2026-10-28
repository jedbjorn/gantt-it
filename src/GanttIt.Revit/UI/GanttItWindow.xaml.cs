using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GanttIt.Revit.UI.Dialogs;

namespace GanttIt.Revit.UI;

/// <summary>
/// The Gantt-It window (§2). Shown modally by the command, owned by Revit's
/// main window (D7). Hosts Home and the editor; Settings, the color picker and
/// messages open as child dialogs.
/// </summary>
public partial class GanttItWindow : Window
{
    private readonly MainViewModel _model;
    private readonly DialogService _dialogs;

    public GanttItWindow(GanttItSession session)
    {
        InitializeComponent();
        _dialogs = new DialogService(this);
        _model = new MainViewModel(session, _dialogs);
        _model.CloseRequested += () =>
        {
            if (IsVisible) Close();
        };
        DataContext = _model;

        // Fit comfortably on small or high-DPI screens (e.g. 1080p at 150 % = 1280 × 720 DIPs).
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(1180, area.Width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(860, area.Height * 0.92));

        // Our handlers run inside Revit's process: report a stray exception instead of taking Revit down.
        Dispatcher.UnhandledException += OnUnhandledException;
        Closed += (_, _) => Dispatcher.UnhandledException -= OnUnhandledException;

        Loaded += (_, _) => _model.Start();
    }

    /// <summary>The legend the user chose with Open view, for the command to activate after closing.</summary>
    public Autodesk.Revit.DB.ElementId? ViewToOpen => _model.ViewToOpen;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel) _model.OnClosing();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _dialogs.ShowMessage("Gantt-It error",
            $"Gantt-It stopped this step because of an unexpected error:\n\n{e.Exception.Message}");
    }
}
