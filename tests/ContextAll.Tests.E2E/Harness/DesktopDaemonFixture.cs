using System.Net.Sockets;
using ContextAll.Host;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Harness;

public sealed class DesktopDaemonFixture : IAsyncDisposable
{
    public string RootDir { get; }
    public string DataDir { get; }
    public string ConfigDir { get; }
    public string StateDir { get; }
    public string RuntimeDir { get; }

    public LanHost? Host { get; private set; }
    public string SocketPath => LocalChannelClient.LinuxSocket(RuntimeDir);

    readonly CancellationTokenSource cts = new();
    Task? serveTask;

    public DesktopDaemonFixture(string? baseDir = null)
    {
        RootDir = baseDir ?? Path.Combine(Path.GetTempPath(), "contextall-daemon-" + Guid.NewGuid().ToString("N"));
        DataDir = Path.Combine(RootDir, "data");
        ConfigDir = Path.Combine(RootDir, "config");
        StateDir = Path.Combine(RootDir, "state");
        RuntimeDir = Path.Combine(RootDir, "runtime");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(RuntimeDir);
    }

    public void SetEnvironment()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", DataDir);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", ConfigDir);
        Environment.SetEnvironmentVariable("XDG_STATE_HOME", StateDir);
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", RuntimeDir);
    }

    public LanHost StartInProcessHost()
    {
        SetEnvironment();
        Host = LanHost.TryCreate() ?? throw new InvalidOperationException("Failed to create LanHost");
        Host.Start(cts.Token);
        return Host;
    }

    public void StartDaemonServer(Func<CancellationToken, Task>? onListening = null)
    {
        SetEnvironment();
        serveTask = Task.Run(async () =>
        {
            await DaemonProcess.Serve(RuntimeDir, onListening, cts.Token).ConfigureAwait(false);
        });
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        cts.Dispose();

        if (Host is not null)
        {
            Host.Dispose();
        }

        if (serveTask is not null)
        {
            try
            {
                await serveTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore cancel/timeout on teardown
            }
        }

        try
        {
            if (Directory.Exists(RootDir))
            {
                Directory.Delete(RootDir, recursive: true);
            }
        }
        catch
        {
            // Best effort directory cleanup
        }
    }
}
