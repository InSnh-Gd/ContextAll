using System.Text;
using ContextAll.Ipc;
using ContextAll.Protocol;
using Xunit;

namespace ContextAll.Host.Tests;

public sealed class LocalDaemonConnectionTests
{
    [Fact]
    public async Task Connects_correlates_commands_pushes_clipboard_and_reconnects_without_spawning_serve()
    {
        var root = Directory.CreateTempSubdirectory("ca-ipc-conn-").FullName;
        var runtimeDir = Path.Combine(root, "run");
        var host1Dir = Path.Combine(root, "host1");
        var host2Dir = Path.Combine(root, "host2");
        var downloadsDir = Path.Combine(root, "custom-downloads");
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(host1Dir);
        Directory.CreateDirectory(host2Dir);
        Directory.CreateDirectory(downloadsDir);

        try
        {
            await using var client = new LocalDaemonConnection(
                runtimeDirectory: runtimeDir,
                initialReconnectDelay: TimeSpan.FromMilliseconds(40),
                maxReconnectDelay: TimeSpan.FromMilliseconds(120));

            await client.StartAsync();
            await Task.Delay(120);
            Assert.False(client.CurrentState.Connected);
            Assert.False(File.Exists(LocalChannelClient.LinuxSocket(runtimeDir)));
            var offlineCmd = await client.SetDisplayNameAsync("OfflineCheck");
            Assert.False(offlineCmd.Ok);
            Assert.Equal("not-connected", offlineCmd.Error);

            var connected1 = new TaskCompletionSource<LocalShellState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnected1 = new TaskCompletionSource<LocalShellState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reconnected2 = new TaskCompletionSource<LocalShellState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stage = 0;

            client.StateChanged += state =>
            {
                if (stage == 0 && state.Connected && !string.IsNullOrEmpty(state.LocalDeviceId))
                {
                    connected1.TrySetResult(state);
                }
                else if (stage == 1 && !state.Connected && state.ConnectionStatus == LocalConnectionStatus.Disconnected)
                {
                    disconnected1.TrySetResult(state);
                }
                else if (stage == 2 && state.Connected && state.LocalDisplayName == "Reborn Host")
                {
                    reconnected2.TrySetResult(state);
                }
            };

            using var cts1 = new CancellationTokenSource();
            var listening1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var serve1 = DaemonProcess.Serve(
                runtimeDir,
                () => LanHost.CreateIsolated(host1Dir),
                _ =>
                {
                    listening1.TrySetResult();
                    return Task.Delay(Timeout.Infinite, cts1.Token);
                },
                cts1.Token);

            await listening1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            client.RetryNow();
            var initialSnapshot = await connected1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(initialSnapshot.Connected);
            Assert.False(string.IsNullOrEmpty(initialSnapshot.LocalDeviceId));
            Assert.False(string.IsNullOrEmpty(initialSnapshot.ResolvedDownloadDirectory));

            var renameTask = client.SetDisplayNameAsync("Studio Box");
            var dirTask = client.SetDownloadDirectoryAsync(downloadsDir);
            var results = await Task.WhenAll(renameTask, dirTask);
            Assert.All(results, r => Assert.True(r.Ok, r.Error));
            Assert.NotEqual(results[0].RequestId, results[1].RequestId);

            var inlinePush = await client.PushClipboardTextAsync("short-snippet");
            Assert.True(inlinePush.Ok, inlinePush.Error);

            var bigText = new string('B', 200 * 1024);
            var chunkedPush = await client.PushClipboardTextAsync(bigText);
            Assert.True(chunkedPush.Ok, chunkedPush.Error);

            var oversized = new string('X', FrameLimits.MaxClipboardBytes + 64);
            Assert.True(Encoding.UTF8.GetByteCount(oversized) > FrameLimits.MaxClipboardBytes);
            var tooLarge = await client.PushClipboardTextAsync(oversized);
            Assert.False(tooLarge.Ok);
            Assert.Equal("too-large", tooLarge.Error);

            stage = 1;
            cts1.Cancel();
            await serve1.WaitAsync(TimeSpan.FromSeconds(5));
            var afterDrop = await disconnected1.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(afterDrop.Connected);
            Assert.Equal(LocalConnectionStatus.Disconnected, afterDrop.ConnectionStatus);

            stage = 2;
            using var cts2 = new CancellationTokenSource();
            var listening2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var host2 = LanHost.CreateIsolated(host2Dir);
            host2.Engine.Apply(new LocalCommandPayload("init1", null, "Reborn Host", null, null, null), FrameTypes.SetDisplayName);
            var serve2 = DaemonProcess.Serve(
                runtimeDir,
                () => host2,
                _ =>
                {
                    listening2.TrySetResult();
                    return Task.Delay(Timeout.Infinite, cts2.Token);
                },
                cts2.Token);

            await listening2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reconnected = await reconnected2.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(reconnected.Connected);
            Assert.Equal("Reborn Host", reconnected.LocalDisplayName);

            cts2.Cancel();
            await serve2.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
