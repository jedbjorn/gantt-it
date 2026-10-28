using System.Windows.Threading;
using GanttIt.Core.Layout;
using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Preview;

/// <summary>
/// The editor's live preview (§2.2): re-runs the layout engine shortly after
/// each change (debounced, so typing stays smooth) and exposes the result to
/// <see cref="PreviewCanvas"/>.
/// </summary>
public sealed class PreviewViewModel : ObservableObject
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);

    private readonly GanttItSession _session;
    private readonly Func<Chart> _chart;
    private readonly DispatcherTimer _timer;
    private ChartLayout? _layout;
    private IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>? _metrics;
    private string? _message;
    private string? _note;

    public PreviewViewModel(GanttItSession session, Func<Chart> chart)
    {
        _session = session;
        _chart = chart;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Debounce };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            RenderNow();
        };
    }

    public ChartLayout? Layout
    {
        get => _layout;
        private set => SetField(ref _layout, value);
    }

    public IReadOnlyDictionary<TextStyleKey, TextStyleMetrics>? Metrics
    {
        get => _metrics;
        private set => SetField(ref _metrics, value);
    }

    /// <summary>Shown instead of the chart when it can't be laid out.</summary>
    public string? Message
    {
        get => _message;
        private set => SetField(ref _message, value);
    }

    /// <summary>Shown under the chart, e.g. when text metrics are approximate.</summary>
    public string? Note
    {
        get => _note;
        private set => SetField(ref _note, value);
    }

    /// <summary>Schedules a redraw; repeated calls within the debounce window coalesce.</summary>
    public void Request()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void RenderNow()
    {
        var context = _session.Preview;
        var layout = context.TryLayout(_chart(), _session.Settings.Settings, out var message);
        Metrics = context.Metrics;
        Layout = layout;
        Message = message;
        Note = context.Note;
    }

    /// <summary>Stops pending redraws when the editor closes.</summary>
    public void Stop() => _timer.Stop();
}
