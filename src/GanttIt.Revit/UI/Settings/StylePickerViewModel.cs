using GanttIt.Core.Model;
using GanttIt.Revit.Services;
using GanttIt.Revit.UI.Mvvm;

namespace GanttIt.Revit.UI.Settings;

/// <summary>
/// One style setting (§2.3). The stored reference is matched by UniqueId,
/// then by name (the stored fallback). A stored style that matches neither no
/// longer exists: it is flagged in red until a replacement is chosen.
/// </summary>
public sealed class StylePickerViewModel : ObservableObject
{
    private readonly StyleRef? _stored;
    private StyleOption? _selected;

    public StylePickerViewModel(string label, string automationId, IReadOnlyList<StyleOption> options, StyleRef? stored, bool reportedMissing)
    {
        Label = label;
        AutomationId = automationId;
        Options = options;
        _stored = stored;
        _selected = stored is null
            ? null
            : options.FirstOrDefault(o => o.UniqueId == stored.UniqueId)
              ?? options.FirstOrDefault(o => string.Equals(o.Name, stored.Name, StringComparison.Ordinal));
        WasMissing = (stored is not null && _selected is null) || reportedMissing && _selected is null;
    }

    public string Label { get; }

    public string AutomationId { get; }

    public IReadOnlyList<StyleOption> Options { get; }

    /// <summary>The stored style could not be found when Settings opened.</summary>
    public bool WasMissing { get; }

    public StyleOption? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            OnPropertyChanged(nameof(IsMissing));
            OnPropertyChanged(nameof(Hint));
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Red: the stored style is gone and nothing replaced it yet.</summary>
    public bool IsMissing => Selected is null && WasMissing;

    /// <summary>Shown under the picker while no style is selected.</summary>
    public string? Hint => Selected is not null
        ? null
        : IsMissing
            ? Resources.Strings.Style_MissingHint(_stored?.Name)
            : Resources.Strings.Style_ChooseHint;

    public event Action? SelectionChanged;

    public StyleRef? ToRef() => Selected is { } s ? new StyleRef(s.UniqueId, s.Name) : null;
}
