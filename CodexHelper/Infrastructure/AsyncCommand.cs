using System.Windows.Input;
using CodexHelper.Services;

namespace CodexHelper.Infrastructure;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _exceptionHandler;
    private bool _isExecuting;

    public AsyncCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? exceptionHandler = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _exceptionHandler = exceptionHandler;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isExecuting = true;
            NotifyCanExecuteChanged();
            await _execute();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_exceptionHandler is not null)
            {
                _exceptionHandler(ex);
            }
            else
            {
                DiagnosticLogService.Error("Unhandled command exception.", ex);
            }
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
