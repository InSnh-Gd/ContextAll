using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using ContextAll.Protocol;
using ContextAll.Session;
using Xunit;

namespace ContextAll.Host.Tests;

public sealed class LinuxNotificationTests
{
    [Fact]
    public async Task Pairing_prompt_notification_shows_peer_name_and_code_and_accept_settles_trust()
    {
        using var rootA = new TempDir();
        using var rootB = new TempDir();
        using var sinkB = new FakeNotificationSink();

        var hostA = new LanHost(SessionHost.CreateIsolated(rootA.Path));
        var hostB = new LanHost(SessionHost.CreateIsolated(rootB.Path), sinkB);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var (_, linkB) = ConnectViaPipes(hostA.Engine, hostB.Engine);

            var shown = await WaitForNotificationAsync(sinkB, 1, cts.Token);
            Assert.Contains("ContextAll", shown.Body, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(linkB.Code));
            Assert.Contains(linkB.Code, shown.Body, StringComparison.Ordinal);
            Assert.Contains("accept", shown.Actions);
            Assert.Contains("reject", shown.Actions);
            Assert.Contains("default", shown.Actions);

            sinkB.EmitAction(shown.Id, "accept");

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("a1", hostB.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            await WaitUntilAsync(
                () => SnapshotOf(hostB.Engine).Devices.Any(d => d.DeviceId == hostA.Engine.LocalDeviceId && d.Trusted),
                cts.Token);
            await WaitUntilAsync(() => sinkB.ClosedIds.Contains(shown.Id), cts.Token);
        }
        finally
        {
            hostB.Dispose();
            hostA.Dispose();
        }
    }

    [Fact]
    public async Task Dismissing_notification_leaves_pairing_and_file_prompts_pending()
    {
        using var rootA = new TempDir();
        using var rootB = new TempDir();
        using var sinkB = new FakeNotificationSink();

        var hostA = new LanHost(SessionHost.CreateIsolated(rootA.Path));
        var hostB = new LanHost(SessionHost.CreateIsolated(rootB.Path), sinkB);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ConnectViaPipes(hostA.Engine, hostB.Engine);

            var pairNote = await WaitForNotificationAsync(sinkB, 1, cts.Token);
            // User dismisses banner or lets OS timeout without choosing accept or reject.
            sinkB.EmitClosed(pairNote.Id, reason: 1);

            await Task.Delay(50, cts.Token);
            Assert.NotNull(SnapshotOf(hostB.Engine).Pairing);
            Assert.False(hostB.Engine.IsTrusted(hostA.Engine.LocalDeviceId));

            // Subsequent explicit accept through CLI/GUI path still succeeds.
            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("b1", hostA.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("a1", hostB.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            await WaitUntilAsync(() => SnapshotOf(hostB.Engine).Pairing is null && hostB.Engine.IsTrusted(hostA.Engine.LocalDeviceId), cts.Token);

            // File prompt dismissal also keeps transfer prompt pending until answered.
            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("b2", hostA.Engine.LocalDeviceId, null, null, true, null),
                    FrameTypes.SetAskBeforeSave));
            }

            var filePath = Path.Combine(rootA.Path, "report.txt");
            await File.WriteAllTextAsync(filePath, "important file bytes", cts.Token);
            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("a2", hostB.Engine.LocalDeviceId, null, filePath, null, null),
                    FrameTypes.SendFile));
            }

            var fileNote = await WaitForNotificationAsync(sinkB, 2, cts.Token);
            Assert.Contains("report.txt", fileNote.Body, StringComparison.Ordinal);

            sinkB.EmitClosed(fileNote.Id, reason: 2);
            await Task.Delay(50, cts.Token);
            var promptSnapshot = SnapshotOf(hostB.Engine);
            var pendingFile = Assert.Single(promptSnapshot.FilePrompts);
            Assert.Equal("report.txt", pendingFile.FileName);

            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("b3", null, null, null, null, pendingFile.TransferId),
                    FrameTypes.AcceptFile));
            }

            await WaitUntilAsync(() => SnapshotOf(hostB.Engine).FilePrompts.Length == 0, cts.Token);
        }
        finally
        {
            hostB.Dispose();
            hostA.Dispose();
        }
    }

    [Fact]
    public async Task First_decision_wins_across_gui_and_notification_for_pairing_and_file_prompts()
    {
        using var rootA = new TempDir();
        using var rootB = new TempDir();
        using var sinkB = new FakeNotificationSink();

        var hostA = new LanHost(SessionHost.CreateIsolated(rootA.Path));
        var hostB = new LanHost(SessionHost.CreateIsolated(rootB.Path), sinkB);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ConnectViaPipes(hostA.Engine, hostB.Engine);

            var pairNote = await WaitForNotificationAsync(sinkB, 1, cts.Token);

            // GUI/CLI accepts first; coordinator closes notification and late "reject" cannot undo acceptance.
            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("gui-pair", hostA.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("peer-pair", hostB.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            await WaitUntilAsync(() => sinkB.ClosedIds.Contains(pairNote.Id), cts.Token);
            sinkB.EmitAction(pairNote.Id, "reject");
            await Task.Delay(50, cts.Token);
            Assert.True(hostB.Engine.IsTrusted(hostA.Engine.LocalDeviceId));

            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("ask", hostA.Engine.LocalDeviceId, null, null, true, null),
                    FrameTypes.SetAskBeforeSave));
            }

            var path = Path.Combine(rootA.Path, "reject-then-late-accept.bin");
            await File.WriteAllBytesAsync(path, new byte[256], cts.Token);
            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("send-1", hostB.Engine.LocalDeviceId, null, path, null, null),
                    FrameTypes.SendFile));
            }

            var fileNote = await WaitForNotificationAsync(sinkB, 2, cts.Token);
            sinkB.EmitAction(fileNote.Id, "reject");

            await WaitUntilAsync(() => SnapshotOf(hostB.Engine).FilePrompts.Length == 0, cts.Token);
            var promptCountAfterReject = SnapshotOf(hostB.Engine).FilePrompts.Length;
            Assert.Equal(0, promptCountAfterReject);
            Assert.Empty(SnapshotOf(hostB.Engine).Transfers);

            // Late CLI accept loses to the already-recorded reject decision.
            lock (hostB.Gate)
            {
                var err = hostB.Engine.Apply(
                    new LocalCommandPayload("late-accept", null, null, null, null, "nonexistent"),
                    FrameTypes.AcceptFile);
                Assert.NotNull(err);
            }
        }
        finally
        {
            hostB.Dispose();
            hostA.Dispose();
        }
    }

    [Fact]
    public async Task Notification_file_accept_and_pairing_reject_actions_apply_and_clipboard_updates_stay_silent()
    {
        using var rootA = new TempDir();
        using var rootB = new TempDir();
        using var sinkB = new FakeNotificationSink();

        var hostA = new LanHost(SessionHost.CreateIsolated(rootA.Path));
        var hostB = new LanHost(SessionHost.CreateIsolated(rootB.Path), sinkB);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ConnectViaPipes(hostA.Engine, hostB.Engine);

            var pairNote = await WaitForNotificationAsync(sinkB, 1, cts.Token);
            sinkB.EmitAction(pairNote.Id, "reject");
            await WaitUntilAsync(() => SnapshotOf(hostB.Engine).Pairing is null, cts.Token);
            Assert.False(hostB.Engine.IsTrusted(hostA.Engine.LocalDeviceId));

            // Re-open pairing link to establish trust and verify file accept + clipboard silence.
            ConnectViaPipes(hostA.Engine, hostB.Engine);
            var secondPairNote = await WaitForNotificationAsync(sinkB, 2, cts.Token);
            sinkB.EmitAction(secondPairNote.Id, "accept");
            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("a-acc", hostB.Engine.LocalDeviceId, null, null, null, null),
                    FrameTypes.AcceptPairing));
            }

            await WaitUntilAsync(() => hostB.Engine.IsTrusted(hostA.Engine.LocalDeviceId), cts.Token);

            lock (hostB.Gate)
            {
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("cb-b", hostA.Engine.LocalDeviceId, null, null, true, null),
                    FrameTypes.SetClipboardSync));
                Assert.Null(hostB.Engine.Apply(
                    new LocalCommandPayload("ask-b", hostA.Engine.LocalDeviceId, null, null, true, null),
                    FrameTypes.SetAskBeforeSave));
            }

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("cb-a", hostB.Engine.LocalDeviceId, null, null, true, null),
                    FrameTypes.SetClipboardSync));
            }

            var path = Path.Combine(rootA.Path, "accepted-from-toast.txt");
            await File.WriteAllTextAsync(path, "payload", cts.Token);
            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.Apply(
                    new LocalCommandPayload("send-2", hostB.Engine.LocalDeviceId, null, path, null, null),
                    FrameTypes.SendFile));
            }

            var fileNote = await WaitForNotificationAsync(sinkB, 3, cts.Token);
            sinkB.EmitAction(fileNote.Id, "accept");
            await WaitUntilAsync(() => SnapshotOf(hostB.Engine).Transfers.Length > 0 || SnapshotOf(hostB.Engine).FilePrompts.Length == 0, cts.Token);

            var notificationsBeforeClipboard = sinkB.Published.Count;

            // Push clipboard text locally and sync across peers: zero notifications permitted.
            lock (hostA.Gate)
            {
                Assert.Null(hostA.Engine.PushClipboardText("clipboard update from host A"));
                Assert.Null(hostB.Engine.PushClipboardText("clipboard update from host B"));
            }

            await Task.Delay(80, cts.Token);
            Assert.Equal(notificationsBeforeClipboard, sinkB.Published.Count);
        }
        finally
        {
            hostB.Dispose();
            hostA.Dispose();
        }
    }

    [Fact]
    public async Task Default_action_activates_single_instance_shell_socket_without_deciding_prompt()
    {
        using var rootA = new TempDir();
        using var rootB = new TempDir();
        using var runtimeDir = new TempDir();
        using var sinkB = new FakeNotificationSink();

        var socketDir = Path.Combine(runtimeDir.Path, "contextall");
        Directory.CreateDirectory(socketDir);
        var shellSocket = Path.Combine(socketDir, "shell.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(shellSocket));
        listener.Listen(4);

        var hostA = new LanHost(SessionHost.CreateIsolated(rootA.Path));
        var hostB = new LanHost(SessionHost.CreateIsolated(rootB.Path), sinkB, runtimeDir.Path);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ConnectViaPipes(hostA.Engine, hostB.Engine);

            var pairNote = await WaitForNotificationAsync(sinkB, 1, cts.Token);
            sinkB.EmitAction(pairNote.Id, "default");

            using var accepted = await listener.AcceptAsync(cts.Token);
            var buf = new byte[64];
            var n = await accepted.ReceiveAsync(buf, SocketFlags.None, cts.Token);
            var line = Encoding.UTF8.GetString(buf, 0, n);
            Assert.StartsWith("ACTIVATE", line, StringComparison.Ordinal);

            // Opening the shell via notification click must keep the prompt pending.
            Assert.NotNull(SnapshotOf(hostB.Engine).Pairing);
        }
        finally
        {
            hostB.Dispose();
            hostA.Dispose();
        }
    }

    static (PeerLink Left, PeerLink Right) ConnectViaPipes(SessionHost hostA, SessionHost hostB) =>
        ConnectViaPipes(hostA, hostB, out _);

    internal static (PeerLink Left, PeerLink Right) ConnectViaPipes(SessionHost hostA, SessionHost hostB, out Func<Task> disconnect)
    {
        var aToB = new Pipe();
        var bToA = new Pipe();
        var streamA = new DuplexPipeStream(bToA.Reader.AsStream(), aToB.Writer.AsStream());
        var streamB = new DuplexPipeStream(aToB.Reader.AsStream(), bToA.Writer.AsStream());
        var linkA = new PeerLink(hostA, null, streamA, hostB.LocalSpki, initiated: true);
        var linkB = new PeerLink(hostB, null, streamB, hostA.LocalSpki, initiated: false);
        hostA.Track(linkA);
        hostB.Track(linkB);
        var runLeft = linkA.RunAsync();
        var runRight = linkB.RunAsync();
        disconnect = async () =>
        {
            aToB.Reader.CancelPendingRead();
            bToA.Reader.CancelPendingRead();
            await Task.WhenAll(runLeft, runRight).WaitAsync(TimeSpan.FromSeconds(5));
        };
        return (linkA, linkB);
    }

    static SnapshotPayload SnapshotOf(SessionHost host)
    {
        var frame = host.Snapshot();
        return ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value
               ?? throw new InvalidOperationException("Invalid snapshot payload");
    }

    static async Task<RecordedNotification> WaitForNotificationAsync(FakeNotificationSink sink, int count, CancellationToken ct)
    {
        while (true)
        {
            var item = sink.GetAtOrDefault(count - 1);
            if (item is not null)
            {
                return item;
            }

            await Task.Delay(20, ct);
        }
    }

    static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
        {
            await Task.Delay(20, ct);
        }
    }

    sealed record RecordedNotification(uint Id, string Summary, string Body, string[] Actions);

    sealed class FakeNotificationSink : ILinuxNotificationSink
    {
        readonly object gate = new();
        uint nextId = 100;

        public List<RecordedNotification> Published { get; } = [];
        public List<uint> ClosedIds { get; } = [];

        public event Action<uint, string>? ActionInvoked;
        public event Action<uint, uint>? NotificationClosed;

        public Task<uint> NotifyAsync(
            uint replacesId,
            string summary,
            string body,
            string[] actions,
            CancellationToken cancellationToken = default)
        {
            uint assigned;
            lock (gate)
            {
                assigned = replacesId != 0 ? replacesId : nextId++;
                Published.Add(new RecordedNotification(assigned, summary, body, actions));
            }

            return Task.FromResult(assigned);
        }

        public Task CloseNotificationAsync(uint notificationId, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                ClosedIds.Add(notificationId);
            }

            return Task.CompletedTask;
        }

        public RecordedNotification? GetAtOrDefault(int index)
        {
            lock (gate)
            {
                return index >= 0 && index < Published.Count ? Published[index] : null;
            }
        }

        public void EmitAction(uint id, string actionKey) => ActionInvoked?.Invoke(id, actionKey);

        public void EmitClosed(uint id, uint reason) => NotificationClosed?.Invoke(id, reason);

        public void Dispose()
        {
        }
    }

    sealed class DuplexPipeStream(Stream readStream, Stream writeStream) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => writeStream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => writeStream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => readStream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            readStream.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            readStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => writeStream.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            writeStream.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            writeStream.WriteAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                readStream.Dispose();
                writeStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "contextall-notify-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }
}
