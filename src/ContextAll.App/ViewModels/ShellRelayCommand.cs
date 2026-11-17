using System.Windows.Input;

namespace ContextAll.App.ViewModels;

public sealed class ShellRelayCommand : ICommand
{
    readonly Func<object?, Task> execute;
    bool running;

    public ShellRelayCommand(Action execute)
        : this(_ =>
        {
            execute();
            return Task.CompletedTask;
        })
    {
    }

    public ShellRelayCommand(Action<object?> execute)
        : this(parameter =>
        {
            execute(parameter);
            return Task.CompletedTask;
        })
    {
    }

    public ShellRelayCommand(Func<Task> execute)
        : this(_ => execute())
    {
    }

    public ShellRelayCommand(Func<object?, Task> execute)
    {
        this.execute = execute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running;

    public async void Execute(object? parameter)
    {
        if (running)
        {
            return;
        }

        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute(parameter).ConfigureAwait(true);
        }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
