using System;
using System.Windows.Input;

namespace avalonia_terminal.ViewModels;

public class LocalRelayCommand<T> : ICommand
{
    private readonly Action<T> _execute;
    private readonly Predicate<T>? _canExecute;

    public LocalRelayCommand(Action<T> execute, Predicate<T>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        // 実行可能かどうか判定
        if (_canExecute == null) return true;
        
        if (parameter is T t) return _canExecute(t);
        
        // パラメータがnullでも、Tが参照型ならOKとする場合など
        return false;
    }

    public void Execute(object? parameter)
    {
        // 実行処理
        if (parameter is T t)
        {
            _execute(t);
        }
        else if (parameter != null && typeof(T) == typeof(string))
        {
            // 文字列として無理やりキャストして実行（XAMLからの入力対策）
            _execute((T)(object)parameter.ToString()!);
        }
    }

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
