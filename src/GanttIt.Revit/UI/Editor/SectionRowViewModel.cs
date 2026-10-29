using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using GanttIt.Core.Model;
using GanttIt.Core.Scheduling;
using GanttIt.Revit.UI.Swatches;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Editor;

/// <summary>Where a dragged row would land relative to this row.</summary>
public enum DropMarker
{
    None,
    Above,
    Below,
}

/// <summary>
/// One row of the sections grid (§2.2), a thin view over a <see cref="Section"/>
/// of the editor's working chart. Every edit goes through the editor, which
/// applies the scheduling rules (§5) and refreshes all rows, since a Start or
/// End edit can move other sections of the chain.
/// </summary>
public sealed class SectionRowViewModel : ObservableObject
{
    private readonly EditorViewModel _editor;
    private string? _error;
    private DropMarker _dropMarker;

    public SectionRowViewModel(EditorViewModel editor, Section section)
    {
        _editor = editor;
        Section = section;
        PickColorCommand = new RelayCommand(() => _editor.PickColor(this));
        RemoveCommand = new RelayCommand(() => _editor.RemoveSection(this));
    }

    public Section Section { get; }

    public Guid Id => Section.Id;

    /// <summary>Position in the list, 0-based.</summary>
    public int Index => _editor.Chart.IndexOf(Id);

    /// <summary>1-based row number, shown in the grid and used in AutomationIds ("Section2.Start").</summary>
    public int Number => Index + 1;

    public bool IsFirst => Index == 0;

    public bool IsLast => Index == _editor.Chart.Sections.Count - 1;

    public string Name
    {
        get => Section.Name;
        set
        {
            if (Section.Name == value) return;
            Section.Name = value;
            _editor.OnEdited();
        }
    }

    /// <summary>The Start date picker. Editing it shifts the whole chain (§5.2).</summary>
    public DateTime? StartDate
    {
        get => Section.Start.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } v) _editor.SetStart(this, DateOnly.FromDateTime(v));
            else OnPropertyChanged(); // a cleared picker snaps back to the current date
        }
    }

    /// <summary>The End date picker. Later linked sections follow (§5.2).</summary>
    public DateTime? EndDate
    {
        get => Section.End.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (value is { } v) _editor.SetEnd(this, DateOnly.FromDateTime(v));
            else OnPropertyChanged();
        }
    }

    /// <summary>Duration in days (End − Start + 1). Same effect as an End edit (§5.2).</summary>
    public string DurationText
    {
        get => Section.Duration.ToString(CultureInfo.InvariantCulture);
        set
        {
            _editor.SetDuration(this, value);
            OnPropertyChanged();
        }
    }

    /// <summary>"Relates to previous": the same link as the previous row's "following" (D5).</summary>
    public bool LinkPrevious
    {
        get => !IsFirst && Scheduler.IsLinkedToPrevious(_editor.Chart, Id);
        set
        {
            if (!IsFirst) _editor.SetLink(Index - 1, value);
        }
    }

    /// <summary>"Relates to following": the same link as the next row's "previous" (D5).</summary>
    public bool LinkFollowing
    {
        get => !IsLast && Scheduler.IsLinkedToFollowing(_editor.Chart, Id);
        set
        {
            if (!IsLast) _editor.SetLink(Index, value);
        }
    }

    public Brush ColorBrush => SwatchViewModel.BrushFor(Section.Color);

    public string ColorHex => Section.Color.ToHex();

    /// <summary>Validation messages for this row (blank name, end before start, …); null when valid.</summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (SetField(ref _error, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    public bool HasNameError { get; private set; }

    public bool HasDateError { get; private set; }

    public void SetIssues(string? error, bool nameError, bool dateError)
    {
        Error = error;
        if (HasNameError != nameError)
        {
            HasNameError = nameError;
            OnPropertyChanged(nameof(HasNameError));
        }
        if (HasDateError != dateError)
        {
            HasDateError = dateError;
            OnPropertyChanged(nameof(HasDateError));
        }
    }

    public DropMarker DropMarker
    {
        get => _dropMarker;
        set => SetField(ref _dropMarker, value);
    }

    public ICommand PickColorCommand { get; }

    public ICommand RemoveCommand { get; }

    /// <summary>Re-reads everything from the section and the chart's links (after any scheduling change).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Index));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(IsFirst));
        OnPropertyChanged(nameof(IsLast));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(LinkPrevious));
        OnPropertyChanged(nameof(LinkFollowing));
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(ColorHex));
    }
}
