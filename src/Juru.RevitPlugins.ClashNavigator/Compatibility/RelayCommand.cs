using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace dosymep.WPF.Commands;

/// <summary>
/// Public command adapter used by this plugin. The copied Core implementation
/// is internal to its assembly, so it cannot be consumed through a project
/// reference.
/// </summary>
internal sealed class RelayCommand : ICommand {
    private readonly Action<object> _execute;
    private readonly Func<object, bool> _canExecute;

    private RelayCommand(Action<object> execute, Func<object, bool> canExecute = null) {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool> canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) {
    }

    public event EventHandler CanExecuteChanged {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);

    public static RelayCommand Create(Action execute, Func<bool> canExecute = null) =>
        new(_ => execute(), canExecute is null ? null : _ => canExecute());

    public static RelayCommand Create<T>(Action<T> execute, Func<T, bool> canExecute = null) =>
        new(parameter => execute(parameter is T value ? value : default),
            canExecute is null ? null : parameter => canExecute(parameter is T value ? value : default));

    public static ICommand CreateAsync(Func<Task> execute, Func<bool> canExecute = null) =>
        new RelayCommand(async _ => await execute(), canExecute is null ? null : _ => canExecute());
}
