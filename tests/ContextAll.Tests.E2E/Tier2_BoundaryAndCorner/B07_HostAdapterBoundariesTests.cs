using ContextAll.Host;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B07")]
public sealed class B07_HostAdapterBoundariesTests
{
    [Fact]
    public async Task DaemonProcess_Serve_Fails_When_RuntimeDir_Is_Empty()
    {
        var result = await DaemonProcess.Serve("", null, CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task DaemonProcess_Serve_Fails_When_RuntimeDir_Is_A_File()
    {
        await using var sandbox = new NetworkSandbox();
        var filePath = Path.Combine(sandbox.TempRoot, "not_a_directory");
        await File.WriteAllTextAsync(filePath, "regular file");

        var result = await DaemonProcess.Serve(filePath, null, CancellationToken.None);
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task DaemonProcess_Cleans_Up_Socket_File_On_Clean_Exit()
    {
        var fixture = new DesktopDaemonFixture();
        var tcs = new TaskCompletionSource<bool>();
        fixture.StartDaemonServer(onListening: ct =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAny(tcs.Task, Task.Delay(2000));
        var socket = fixture.SocketPath;
        await fixture.DisposeAsync();

        await Task.Delay(100);
        Assert.False(File.Exists(socket));
    }

    [Fact]
    public void LocalChannel_LinuxSocket_Requires_ContextAll_Subfolder()
    {
        var path = ContextAll.Ipc.LocalChannelClient.LinuxSocket("/run/user/1000");
        Assert.Contains("/contextall/", path);
        Assert.EndsWith("daemon.sock", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DaemonProcess_Rejects_Duplicate_Daemon_Instance()
    {
        await using var fixture = new DesktopDaemonFixture();
        var tcs = new TaskCompletionSource<bool>();
        fixture.StartDaemonServer(onListening: ct =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        });

        await Task.WhenAny(tcs.Task, Task.Delay(2000));
        var second = await DaemonProcess.Serve(fixture.RuntimeDir, null, CancellationToken.None);
        Assert.Equal(1, second);
    }
}
