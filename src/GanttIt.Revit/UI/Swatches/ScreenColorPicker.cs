using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GanttIt.Core.Model;
using GanttIt.Core.Picking;

namespace GanttIt.Revit.UI.Swatches;

/// <summary>
/// "Pick from screen" (the eyedropper): picks the color of any pixel on any monitor.
/// <list type="number">
/// <item>One overlay window per monitor is created off-screen; the first is activated so
/// Gantt-It keeps the foreground while its own windows are out of the way.</item>
/// <item>The color picker and every Gantt-It window that owns it are hidden at the Win32
/// level (see <see cref="NativeScreen.Hide"/>) with the DWM animation off.</item>
/// <item>The whole virtual screen is captured once, in physical pixels, and each overlay
/// shows its monitor's part full screen: snapshot-then-pick, so the magnifier and the
/// result both come from the same frozen bitmap.</item>
/// <item>Left click picks the pixel under the pointer's hotspot; Esc, right click or
/// switching to another app cancels. Arrow keys move the pointer one pixel (Shift: ten),
/// Enter or Space picks.</item>
/// </list>
/// Every path, exceptions included, closes the overlays and shows the hidden windows
/// again where they were, with the color picker active.
/// </summary>
internal sealed class ScreenColorPicker
{
    private static readonly TimeSpan HideSettle = TimeSpan.FromMilliseconds(150);

    private readonly Window _popup;
    private readonly List<Window> _hidden = [];
    private readonly List<ScreenPickOverlay> _overlays = [];
    private ScreenSnapshot? _snapshot;
    private DispatcherFrame? _frame;
    private (int X, int Y)? _pressedAt;
    private Rgb? _result;
    private Exception? _error;

    private ScreenColorPicker(Window popup)
    {
        _popup = popup;
    }

    /// <summary>
    /// Runs a pick over everything on screen. <paramref name="popup"/> is the color picker;
    /// it and its owner windows are hidden meanwhile. Null when cancelled; throws when the
    /// screen couldn't be read (the windows are back by then).
    /// </summary>
    public static Rgb? Pick(Window popup) => new ScreenColorPicker(popup).Run();

    private Rgb? Run()
    {
        try
        {
            List<PixelRect> monitors;
            using (NativeScreen.PerMonitorDpi())
            {
                monitors = NativeScreen.Monitors();
                if (monitors.Count == 0) throw new InvalidOperationException("Windows reported no monitors.");
                foreach (var monitor in monitors)
                {
                    var overlay = new ScreenPickOverlay(monitor);
                    overlay.KeyDown += OnKeyDown;
                    overlay.MouseMove += (_, _) => Guard(Track);
                    overlay.MouseLeftButtonDown += OnLeftDown;
                    overlay.MouseLeftButtonUp += OnLeftUp;
                    overlay.MouseRightButtonDown += (_, e) => e.Handled = true;
                    overlay.MouseRightButtonUp += (_, e) =>
                    {
                        e.Handled = true;
                        Finish(null);
                    };
                    overlay.Deactivated += (_, _) => _popup.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(CancelIfInactive));
                    overlay.Closed += (_, _) => Finish(null); // closed from outside (Alt+F4): cancel
                    _overlays.Add(overlay);
                    overlay.Show(); // off-screen and tiny until Cover()
                }
            }
            _overlays[0].Activate();

            HideGanttItWindows();
            Wait(HideSettle);
            NativeScreen.FlushCompositor();
            NativeScreen.FlushCompositor();

            using (NativeScreen.PerMonitorDpi())
            {
                _snapshot = NativeScreen.Capture(PixelRect.Union(monitors));
                var image = BitmapSource.Create(_snapshot.Bounds.Width, _snapshot.Bounds.Height, 96, 96,
                    PixelFormats.Bgr32, null, _snapshot.Pixels, _snapshot.Stride);
                image.Freeze();
                foreach (var overlay in _overlays)
                {
                    var m = overlay.Monitor;
                    var part = new CroppedBitmap(image,
                        new Int32Rect(m.Left - _snapshot.Bounds.Left, m.Top - _snapshot.Bounds.Top, m.Width, m.Height));
                    part.Freeze();
                    overlay.ShowScreenshot(part);
                    overlay.Cover();
                }
            }

            ActivateOverlayUnderPointer();
            Track();

            _frame = new DispatcherFrame();
            Dispatcher.PushFrame(_frame);
            if (_error is not null) throw new InvalidOperationException(_error.Message, _error);
            return _result;
        }
        finally
        {
            Restore();
        }
    }

    // ---- input ---------------------------------------------------------------------------

    /// <summary>Moves the magnifier to the pointer, on whichever monitor it is.</summary>
    private void Track()
    {
        if (_snapshot is null) return;
        var (x, y) = Pointer();
        var index = PixelRect.IndexAt(_overlays.Select(o => o.Monitor).ToList(), x, y);
        for (var i = 0; i < _overlays.Count; i++)
        {
            if (i == index && _overlays[i].Monitor.Contains(x, y)) _overlays[i].ShowLoupe(_snapshot, x, y);
            else _overlays[i].HideLoupe();
        }
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Guard(() =>
        {
            _pressedAt = Pointer();
            ((UIElement)sender).CaptureMouse();
        });
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Guard(() =>
        {
            ((UIElement)sender).ReleaseMouseCapture();
            if (_pressedAt is not { } at) return; // the press started before the overlay was up
            PickAt(at.X, at.Y);
        });
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        Guard(() =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Finish(null);
                    break;
                case Key.Enter:
                case Key.Space:
                    var (x, y) = Pointer();
                    PickAt(x, y);
                    break;
                case Key.Left: Nudge(-step, 0); break;
                case Key.Right: Nudge(step, 0); break;
                case Key.Up: Nudge(0, -step); break;
                case Key.Down: Nudge(0, step); break;
            }
        });
    }

    private void Nudge(int dx, int dy)
    {
        if (_snapshot is null) return;
        var (x, y) = Pointer();
        var (nx, ny) = _snapshot.Bounds.Clamp(x + dx, y + dy);
        using (NativeScreen.PerMonitorDpi()) NativeScreen.SetCursorPosition(nx, ny);
        Track();
    }

    private void PickAt(int x, int y)
    {
        // The pointer can sit in a gap between monitors of different sizes: nothing to pick there.
        if (_snapshot?.ColorAt(x, y) is { } color) Finish(color);
    }

    /// <summary>Another app took the foreground (Alt+Tab, Win key): cancel instead of leaving overlays up.</summary>
    private void CancelIfInactive()
    {
        if (_frame is null || _overlays.Any(o => o.IsActive)) return;
        Finish(null);
    }

    private void Finish(Rgb? color)
    {
        if (_frame is null) return;
        _result = color;
        _frame.Continue = false;
        _frame = null;
    }

    /// <summary>Input handlers run inside Revit: any failure ends the pick and is reported by the caller.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _error ??= ex;
            Finish(null);
        }
    }

    private static (int X, int Y) Pointer()
    {
        using (NativeScreen.PerMonitorDpi()) return NativeScreen.CursorPosition();
    }

    private void ActivateOverlayUnderPointer()
    {
        var (x, y) = Pointer();
        var index = PixelRect.IndexAt(_overlays.Select(o => o.Monitor).ToList(), x, y);
        var overlay = _overlays[Math.Max(0, index)];
        overlay.Activate();
        overlay.Focus();
    }

    // ---- hiding and restoring the Gantt-It windows ---------------------------------------

    /// <summary>The color picker first, then each window that owns it (the Gantt-It window).</summary>
    private void HideGanttItWindows()
    {
        for (var w = _popup; w is not null; w = w.Owner)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) continue;
            NativeScreen.SetTransitionsDisabled(hwnd, true);
            NativeScreen.Hide(hwnd);
            _hidden.Add(w);
        }
    }

    /// <summary>Never throws: shows the hidden windows (owners first), closes the overlays, activates the picker.</summary>
    private void Restore()
    {
        _frame = null;
        for (var i = _hidden.Count - 1; i >= 0; i--)
        {
            Try(() =>
            {
                var hwnd = new WindowInteropHelper(_hidden[i]).Handle;
                NativeScreen.Show(hwnd, activate: _hidden[i] == _popup);
                NativeScreen.SetTransitionsDisabled(hwnd, false);
            });
        }
        _hidden.Clear();

        foreach (var overlay in _overlays) Try(overlay.Close);
        _overlays.Clear();
        _snapshot = null;

        Try(() =>
        {
            _popup.Activate();
            _popup.Focus();
        });
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Gantt-It: Pick from screen cleanup: {ex.Message}");
        }
    }

    /// <summary>Keeps the message loop running for <paramref name="duration"/> (windows repaint, DWM composes).</summary>
    private static void Wait(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
