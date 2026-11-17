using ContextAll.Protocol;

namespace ContextAll.Ipc;

public sealed class IdleLocalChannel : ILocalDaemonClient
{
    public static IdleLocalChannel Shared { get; } = new();

    public LocalShellState CurrentState => LocalShellState.Empty;

    public event Action<LocalShellState>? StateChanged
    {
        add { }
        remove { }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RetryNow()
    {
    }

    public Task<CommandResultPayload> SendCommandAsync(
        byte frameType,
        LocalCommandPayload payload,
        CancellationToken cancellationToken = default)
    {
        _ = frameType;
        _ = cancellationToken;
        return Task.FromResult(new CommandResultPayload(payload.RequestId, false, "idle"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
