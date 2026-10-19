using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace GeometryGrid.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void Raise(params string[] names)
    {
        foreach (var name in names) Raise(name);
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayParameterCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
