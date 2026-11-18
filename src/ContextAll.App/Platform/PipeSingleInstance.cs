using System.IO.Pipes;
using System.Text;

namespace ContextAll.App.Platform;

public sealed record PipeSingleInstanceAcquisition(bool Acquired, PipeSingleInstance? Instance);

/// <summary>Single-instance lock using a named mutex plus a named pipe for ACTIVATE.</summary>
public sealed class PipeSingleInstance : IAsyncDisposable, IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly Action<string?> _onActivate;
    private readonly CancellationTokenSource _cts;
    private readonly Task _acceptTask;
    private NamedPipeServerStream? _listening;
    private int _disposed;

    private PipeSingleInstance(Mutex mutex, string pipeName, Action<string?> onActivate)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _onActivate = onActivate;
        _cts = new CancellationTokenSource();
        _listening = CreateServer(pipeName);
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public static async Task<PipeSingleInstanceAcquisition> TryAcquireOrActivateAsync(
        string mutexName,
        string pipeName,
        string? activationToken,
        Action<string?> onActivate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(onActivate);

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                mutex = null;
                await TryActivateExistingAsync(pipeName, activationToken, cancellationToken).ConfigureAwait(false);
                return new PipeSingleInstanceAcquisition(false, null);
            }

            var instance = new PipeSingleInstance(mutex, pipeName, onActivate);
            mutex = null;
            return new PipeSingleInstanceAcquisition(true, instance);
        }
        catch
        {
            mutex?.Dispose();
            throw;
        }
    }

    public static async Task<bool> TryActivateExistingAsync(
        string pipeName,
        string? activationToken = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(750));

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            var cleanToken = (activationToken ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            var line = string.IsNullOrEmpty(cleanToken) ? "ACTIVATE\n" : $"ACTIVATE {cleanToken}\n";
            await client.WriteAsync(Encoding.UTF8.GetBytes(line), timeoutCts.Token).ConfigureAwait(false);
            await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var ack = new byte[16];
            _ = await client.ReadAsync(ack, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = Interlocked.Exchange(ref _listening, null) ?? CreateServer(_pipeName);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await HandleClientAsync(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Keep listening after a bad client.
            }
            finally
            {
                if (server is not null)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream client, CancellationToken ct)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readCts.CancelAfter(TimeSpan.FromSeconds(1));

        var buffer = new byte[1024];
        var received = 0;
        while (received < buffer.Length)
        {
            var n = await client.ReadAsync(buffer.AsMemory(received), readCts.Token).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            received += n;
            if (Array.IndexOf(buffer, (byte)'\n', 0, received) >= 0)
            {
                break;
            }
        }

        var text = Encoding.UTF8.GetString(buffer, 0, received).Trim();
        if (text.StartsWith("ACTIVATE", StringComparison.Ordinal))
        {
            var token = text.Length > "ACTIVATE".Length ? text["ACTIVATE".Length..].Trim() : null;
            if (string.IsNullOrEmpty(token))
            {
                token = null;
            }

            try
            {
                _onActivate(token);
            }
            catch
            {
                // Activation callback errors must not break the listener.
            }

            await client.WriteAsync("OK\n"u8.ToArray(), readCts.Token).ConfigureAwait(false);
            await client.FlushAsync(readCts.Token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        var listening = Interlocked.Exchange(ref _listening, null);
        listening?.Dispose();
        _cts.Dispose();
        _mutex.Dispose();
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _acceptTask.ConfigureAwait(false);
        }
        catch
        {
            // Accept loop already terminated on disposal.
        }
    }
}
