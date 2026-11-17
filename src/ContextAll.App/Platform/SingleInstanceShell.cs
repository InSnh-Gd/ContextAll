using System.Net.Sockets;
using System.Text;

namespace ContextAll.App.Platform;

public sealed record SingleInstanceAcquisition(bool Acquired, SingleInstanceShell? Instance);

public sealed class SingleInstanceShell : IAsyncDisposable, IDisposable
{
    readonly string socketPath;
    readonly Socket listener;
    readonly CancellationTokenSource cts;
    readonly Action<string?> onActivate;
    readonly Task acceptTask;
    int disposed;

    SingleInstanceShell(string socketPath, Socket listener, Action<string?> onActivate)
    {
        this.socketPath = socketPath;
        this.listener = listener;
        this.onActivate = onActivate;
        cts = new CancellationTokenSource();
        acceptTask = Task.Run(() => AcceptLoopAsync(cts.Token));
    }

    public string SocketPathValue => socketPath;

    public static string SocketPath(string? runtimeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            return Path.Combine(runtimeDirectory, "contextall", "shell.sock");
        }

        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
        {
            user = "user";
        }

        return Path.Combine(Path.GetTempPath(), $"contextall-{user}", "shell.sock");
    }

    public static async Task<SingleInstanceAcquisition> TryAcquireOrActivateAsync(
        string socketPath,
        string? activationToken,
        Action<string?> onActivate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        ArgumentNullException.ThrowIfNull(onActivate);

        var directory = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            SetDirectoryPrivateMode(directory);
        }

        if (await TryActivateExistingAsync(socketPath, activationToken, cancellationToken).ConfigureAwait(false))
        {
            return new SingleInstanceAcquisition(false, null);
        }

        TryDeleteSocketFile(socketPath);

        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            SetSocketPrivateMode(socketPath);
            listener.Listen(8);
            return new SingleInstanceAcquisition(true, new SingleInstanceShell(socketPath, listener, onActivate));
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listener.Dispose();
            if (await TryActivateExistingAsync(socketPath, activationToken, cancellationToken).ConfigureAwait(false))
            {
                return new SingleInstanceAcquisition(false, null);
            }

            throw;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    public static async Task<bool> TryActivateExistingAsync(
        string socketPath,
        string? activationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (!Path.Exists(socketPath))
        {
            return false;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(750));

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeoutCts.Token).ConfigureAwait(false);
            var cleanToken = (activationToken ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            var line = string.IsNullOrEmpty(cleanToken) ? "ACTIVATE\n" : $"ACTIVATE {cleanToken}\n";
            await client.SendAsync(Encoding.UTF8.GetBytes(line), SocketFlags.None, timeoutCts.Token).ConfigureAwait(false);

            var ack = new byte[16];
            _ = await client.ReceiveAsync(ack, SocketFlags.None, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket? accepted = null;
            try
            {
                accepted = await listener.AcceptAsync(ct).ConfigureAwait(false);
                await HandleClientAsync(accepted, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                accepted?.Dispose();
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                accepted?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                accepted?.Dispose();
                break;
            }
            catch
            {
                accepted?.Dispose();
            }
        }
    }

    async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        using (client)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(1));

            var buffer = new byte[1024];
            var received = 0;
            while (received < buffer.Length)
            {
                var n = await client.ReceiveAsync(buffer.AsMemory(received), SocketFlags.None, readCts.Token).ConfigureAwait(false);
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
                    onActivate(token);
                }
                catch
                {
                    // Activation callback errors must not break the single-instance listener.
                }

                await client.SendAsync("OK\n"u8.ToArray(), SocketFlags.None, readCts.Token).ConfigureAwait(false);
            }
        }
    }

    static void SetDirectoryPrivateMode(string directory)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // Ignore filesystems that reject chmod.
            }
        }
    }

    static void SetSocketPrivateMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Ignore filesystems that reject chmod.
            }
        }
    }

    static void TryDeleteSocketFile(string path)
    {
        try
        {
            if (Path.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup race.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        cts.Cancel();
        listener.Dispose();
        TryDeleteSocketFile(socketPath);
        cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await acceptTask.ConfigureAwait(false);
        }
        catch
        {
            // Accept loop already terminated on disposal.
        }
    }
}
