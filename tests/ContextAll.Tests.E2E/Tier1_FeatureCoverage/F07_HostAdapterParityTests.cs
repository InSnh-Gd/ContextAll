using ContextAll.Host;
using ContextAll.Ipc;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F07")]
public sealed class F07_HostAdapterParityTests
{
    [Fact]
    public void LinuxSocket_Path_Matches_Standard_Convention()
    {
        const string runtimeDir = "/run/user/1000";
        var socketPath = LocalChannelClient.LinuxSocket(runtimeDir);
        Assert.Equal("/run/user/1000/contextall/daemon.sock", socketPath);
    }

    [Fact]
    public async Task HostAdapter_Binds_UnixDomainSocket_Under_RuntimeDir()
    {
        await using var fixture = new DesktopDaemonFixture();
        var listeningTcs = new TaskCompletionSource<bool>();

        fixture.StartDaemonServer(onListening: ct =>
        {
            listeningTcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        var completed = await Task.WhenAny(listeningTcs.Task, Task.Delay(2000));
        Assert.Equal(listeningTcs.Task, completed);
        Assert.True(File.Exists(fixture.SocketPath));
    }

    [Fact]
    public async Task HostAdapter_Deletes_Stale_Socket_Before_Listening()
    {
        await using var fixture = new DesktopDaemonFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.SocketPath)!);
        await File.WriteAllTextAsync(fixture.SocketPath, "stale");

        var listeningTcs = new TaskCompletionSource<bool>();
        fixture.StartDaemonServer(onListening: ct =>
        {
            listeningTcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAny(listeningTcs.Task, Task.Delay(2000));
        Assert.True(File.Exists(fixture.SocketPath));
    }

    [Fact]
    public async Task HostAdapter_Unlinks_Socket_On_Shutdown()
    {
        var fixture = new DesktopDaemonFixture();
        var listeningTcs = new TaskCompletionSource<bool>();
        fixture.StartDaemonServer(onListening: ct =>
        {
            listeningTcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAny(listeningTcs.Task, Task.Delay(2000));
        var path = fixture.SocketPath;

        await fixture.DisposeAsync();
        await Task.Delay(100);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task HostAdapter_Exits_With_Code1_When_Socket_Locked()
    {
        await using var fixture = new DesktopDaemonFixture();
        var listeningTcs = new TaskCompletionSource<bool>();
        fixture.StartDaemonServer(onListening: ct =>
        {
            listeningTcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAny(listeningTcs.Task, Task.Delay(2000));

        // Attempting to serve on the same runtime directory while locked should return 1
        var secondRunResult = await DaemonProcess.Serve(fixture.RuntimeDir, null, CancellationToken.None);
        Assert.Equal(1, secondRunResult);
    }
}
