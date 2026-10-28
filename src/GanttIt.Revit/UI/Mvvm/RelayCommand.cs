using System.Windows.Input;

namespace GanttIt.Revit.UI.Mvvm;

/// <summary>An <see cref="ICommand"/> over delegates. CanExecute is re-queried through <see cref="CommandManager"/>.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute();
    }

    /// <summary>Asks every command to re-evaluate CanExecute (after validation changes).</summary>
    public static void Refresh() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>A command whose handler takes the bound <c>CommandParameter</c>.</summary>
public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    // A null parameter answers as if no predicate existed: WPF may query CanExecute
    // before the CommandParameter binding resolves, and doesn't always ask again.
    public bool CanExecute(object? parameter) =>
        parameter is T value ? canExecute?.Invoke(value) ?? true : canExecute is null;

    public void Execute(object? parameter)
    {
        if (parameter is T value && CanExecute(value)) execute(value);
    }
}
