using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

internal sealed class FakeLocalDaemonClient : ILocalDaemonClient
{
    public List<(byte FrameType, LocalCommandPayload Payload)> SentCommands { get; } = [];
    public Func<byte, LocalCommandPayload, CommandResultPayload>? CommandResponder { get; set; }
    public int RetryCount { get; private set; }

    public LocalShellState CurrentState { get; private set; } = LocalShellState.Empty;

    public event Action<LocalShellState>? StateChanged;

    public void EmitState(LocalShellState next)
    {
        CurrentState = next;
        StateChanged?.Invoke(next);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RetryNow() => RetryCount++;

    public Task<CommandResultPayload> SendCommandAsync(
        byte frameType,
        LocalCommandPayload payload,
        CancellationToken cancellationToken = default)
    {
        SentCommands.Add((frameType, payload));
        var result = CommandResponder is not null
            ? CommandResponder(frameType, payload)
            : new CommandResultPayload(payload.RequestId, true, null);
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
