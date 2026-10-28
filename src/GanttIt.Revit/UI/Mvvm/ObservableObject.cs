using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GanttIt.Revit.UI.Mvvm;

/// <summary>Plain <see cref="INotifyPropertyChanged"/> base for the view-models (§8: no MVVM packages).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Tells WPF that every property may have changed (an empty name refreshes all bindings).</summary>
    protected void OnAllPropertiesChanged() => OnPropertyChanged(string.Empty);

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
