using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GanttIt.Core.Editing;

namespace GanttIt.Revit.UI.Editor;

/// <summary>
/// The chart editor (§2.2). Behaviour lives in <see cref="EditorViewModel"/>;
/// this code-behind only translates drag-and-drop on the row handles (and
/// Alt+Up/Alt+Down on a focused handle) into <see cref="EditorViewModel.Move"/>.
/// </summary>
public partial class EditorView : UserControl
{
    private const string RowFormat = "GanttIt.SectionRow";
    private Point _dragStart;
    private SectionRowViewModel? _dragRow;

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is EditorViewModel old) old.Rows.CollectionChanged -= OnRowsChanged;
            if (e.NewValue is EditorViewModel now) now.Rows.CollectionChanged += OnRowsChanged;
        };
    }

    private EditorViewModel? Editor => DataContext as EditorViewModel;

    /// <summary>Add section appends a row: scroll it into view once it is laid out.</summary>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (SectionsList.Template?.FindName("PART_Scroller", SectionsList) is ScrollViewer scroller)
                scroller.ScrollToEnd();
        });
    }

    private void OnHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SectionRowViewModel row } handle) return;
        _dragRow = row;
        _dragStart = e.GetPosition(this);
        handle.Focus();
    }

    private void OnHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragRow is null || e.LeftButton != MouseButtonState.Pressed || sender is not DependencyObject handle) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var row = _dragRow;
        _dragRow = null;
        DragDrop.DoDragDrop(handle, new DataObject(RowFormat, row), DragDropEffects.Move);
        ClearMarkers();
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDrop(sender, e, out var target, out var after))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        ClearMarkers(except: target);
        target.DropMarker = after ? DropMarker.Below : DropMarker.Above;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SectionRowViewModel row }) row.DropMarker = DropMarker.None;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        var editor = Editor;
        if (editor is not null
            && e.Data.GetData(RowFormat) is SectionRowViewModel dragged
            && TryGetDrop(sender, e, out var target, out var after))
        {
            var count = editor.Rows.Count;
            editor.Move(dragged.Index, EditorInput.DropIndex(dragged.Index, target.Index, after, count));
        }
        ClearMarkers();
        e.Handled = true;
    }

    private void OnHandleKeyDown(object sender, KeyEventArgs e)
    {
        if (Editor is not { } editor || sender is not FrameworkElement { DataContext: SectionRowViewModel row }) return;
        if (Keyboard.Modifiers != ModifierKeys.Alt) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var to = key switch
        {
            Key.Up => row.Index - 1,
            Key.Down => row.Index + 1,
            _ => -1,
        };
        if (to < 0 || to >= editor.Rows.Count) return;
        editor.Move(row.Index, to);
        e.Handled = true;
        // Keep focus on the moved row's handle so repeated presses keep moving it.
        Dispatcher.BeginInvoke(() => FocusHandleOf(row));
    }

    private static bool TryGetDrop(object sender, DragEventArgs e, out SectionRowViewModel target, out bool after)
    {
        target = null!;
        after = false;
        if (!e.Data.GetDataPresent(RowFormat) || sender is not FrameworkElement { DataContext: SectionRowViewModel row } element)
            return false;
        target = row;
        after = e.GetPosition(element).Y > element.ActualHeight / 2;
        return true;
    }

    private void ClearMarkers(SectionRowViewModel? except = null)
    {
        if (Editor is not { } editor) return;
        foreach (var row in editor.Rows)
            if (!ReferenceEquals(row, except)) row.DropMarker = DropMarker.None;
    }

    private void FocusHandleOf(SectionRowViewModel row)
    {
        var name = $"Section{row.Number}.DragHandle";
        foreach (var element in Descendants(this))
            if (element is FrameworkElement fe && AutomationIdOf(fe) == name)
            {
                fe.Focus();
                return;
            }
    }

    private static string AutomationIdOf(DependencyObject d) =>
        System.Windows.Automation.AutomationProperties.GetAutomationId(d);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
