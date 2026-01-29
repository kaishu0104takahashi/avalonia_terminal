using System;
using System.Windows.Input;

namespace avalonia_terminal.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute();
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    // 【修正】警告CS0067を回避するため、明示的に空のadd/removeを定義
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
