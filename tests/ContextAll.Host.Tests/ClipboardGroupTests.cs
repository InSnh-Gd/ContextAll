using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host.Tests;

public sealed class ClipboardGroupTests
{
    [Theory]
    [InlineData(true, FrameLimits.MaxPayloadBytes + 321)]
    [InlineData(false, FrameLimits.MaxPayloadBytes + 321)]
    [InlineData(true, FrameLimits.MaxClipboardAttachmentBytes)]
    public async Task Reconnect_sends_only_the_missing_attachment_bytes(bool ownerSends, int size)
    {
        var root = Directory.CreateTempSubdirectory("contextall-clipboard-resume-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var owner = SessionHost.CreateIsolated(Path.Combine(root.FullName, "owner"));
            using var member = SessionHost.CreateIsolated(Path.Combine(root.FullName, "member"));
            Trust(owner, member);
            Trust(member, owner);
            GroupCommand(owner, owner: "self");
            GroupCommand(owner, member: member.LocalDeviceId, invited: true);
            GroupCommand(member, owner: owner.LocalDeviceId);
            var (sender, receiver) = ownerSends ? (owner, member) : (member, owner);
            var chunks = 0;
            var (link, senderLink) = LinuxNotificationTests.ConnectViaPipes(receiver, sender, out var disconnect,
                buffer => buffer.Span[4] != FrameTypes.ClipboardGroupChunk || Interlocked.Increment(ref chunks) == 1);
            try
            {
                await WaitUntil(() => Ready(receiver, link) && Ready(sender, senderLink), timeout.Token);
                var payload = new byte[size];
                new Random(73).NextBytes(payload);
                Assert.Null(sender.PushClipboardAttachment(new ClipboardAttachment(ClipboardAttachment.FileType, "partial.bin", payload)));
                var incoming = Path.Combine(receiver.ClipboardStateDirectory, "clipboard-incoming");
                await WaitUntil(() => Directory.Exists(incoming) && Directory.EnumerateFiles(incoming)
                    .Any(path => new FileInfo(path).Length == FrameLimits.MaxPayloadBytes), timeout.Token);
                await disconnect();

                var replayBytes = 0;
                (link, _) = LinuxNotificationTests.ConnectViaPipes(receiver, sender, out disconnect, buffer =>
                {
                    if (buffer.Span[4] == FrameTypes.ClipboardGroupChunk)
                    {
                        Interlocked.Add(ref replayBytes, buffer.Length - FrameLimits.HeaderBytes);
                    }
                    return true;
                });
                var entry = await HistoryEntry(receiver, "partial.bin", timeout.Token);
                Assert.Equal(payload, File.ReadAllBytes(Path.Combine(receiver.ClipboardStateDirectory, "clipboard-history", entry.Id + ".txt")));
                Assert.Equal(size - FrameLimits.MaxPayloadBytes, Volatile.Read(ref replayBytes));
            }
            finally
            {
                await disconnect();
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_group_relays_a_file_and_catches_up_after_reconnect_without_applying_old_content()
    {
        var root = Directory.CreateTempSubdirectory("contextall-group-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var source = SessionHost.CreateIsolated(Path.Combine(root.FullName, "source"));
            using var owner = SessionHost.CreateIsolated(Path.Combine(root.FullName, "owner"));
            using var receiver = SessionHost.CreateIsolated(Path.Combine(root.FullName, "receiver"));
            using var outsider = SessionHost.CreateIsolated(Path.Combine(root.FullName, "outsider"));
            foreach (var peer in new[] { source, receiver, outsider })
            {
                Trust(owner, peer);
                Trust(peer, owner);
            }

            GroupCommand(owner, owner: "self");
            GroupCommand(owner, member: source.LocalDeviceId, invited: true);
            GroupCommand(owner, member: receiver.LocalDeviceId, invited: true);
            GroupCommand(source, owner: owner.LocalDeviceId);
            GroupCommand(receiver, owner: owner.LocalDeviceId);
            GroupCommand(outsider, owner: owner.LocalDeviceId);

            var (fromSource, _) = LinuxNotificationTests.ConnectViaPipes(source, owner, out var disconnectSource);
            var (fromReceiver, _) = LinuxNotificationTests.ConnectViaPipes(receiver, owner, out var disconnectReceiver);
            var (fromOutsider, _) = LinuxNotificationTests.ConnectViaPipes(outsider, owner, out var disconnectOutsider);
            try
            {
                await WaitUntil(() => Ready(source, fromSource) && Ready(receiver, fromReceiver) && fromOutsider.Entered, timeout.Token);
                var payload = Enumerable.Repeat((byte)42, 90_000).ToArray();
                Assert.Null(source.PushClipboardAttachment(new ClipboardAttachment(ClipboardAttachment.FileType, "report.bin", payload)));
                var file = await HistoryEntry(receiver, "report.bin", timeout.Token);
                Assert.Equal(source.LocalDeviceId, file.Origin);
                Assert.Empty(outsider.ReadClipboardHistory(0).Entries);
                Assert.False(Ready(outsider, fromOutsider));

                await disconnectReceiver();
                Assert.Null(source.PushClipboardText("copied while disconnected"));
                await HistoryEntry(owner, "copied while disconnected", timeout.Token);
                (fromReceiver, _) = LinuxNotificationTests.ConnectViaPipes(receiver, owner, out disconnectReceiver);
                var replay = await HistoryEntry(receiver, "copied while disconnected", timeout.Token);
                Assert.Equal(source.LocalDeviceId, replay.Origin);
                Assert.Empty(receiver.Clipboard.Text);

                GroupCommand(owner, member: receiver.LocalDeviceId, invited: false);
                await WaitUntil(() => !Ready(receiver, fromReceiver), timeout.Token);
                Assert.Null(source.PushClipboardText("after removal"));
                await HistoryEntry(owner, "after removal", timeout.Token);
                Assert.DoesNotContain(receiver.ReadClipboardHistory(0).Entries, entry => entry.Preview == "after removal");
            }
            finally
            {
                await Task.WhenAll(disconnectSource(), disconnectReceiver(), disconnectOutsider());
            }
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_group_owner_does_not_accept_an_origin_claimed_by_another_peer()
    {
        var root = Directory.CreateTempSubdirectory("contextall-group-");
        try
        {
            using var host = SessionHost.CreateIsolated(root.FullName);
            var member = DeviceIds.Text("member"u8.ToArray());
            var other = DeviceIds.Text("other member"u8.ToArray());
            Assert.Null(host.ClipboardGroup.Configure(host.LocalDeviceId));
            var permit = host.ClipboardGroup.Admit(member, true);
            Assert.NotNull(permit);
            Assert.NotNull(permit.Group);
            var copiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var claimed = new ClipboardGroupItem(permit.Group, other, 1, copiedAt, 0, 4, new string('0', 64), false);
            Assert.False(host.ClipboardGroup.Allows(member, claimed, true));
            Assert.True(host.ClipboardGroup.Allows(member, claimed with { Origin = member }, true));
            Assert.False(host.ClipboardGroup.Allows(member, claimed with { Origin = member }, false));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    static void Trust(SessionHost host, SessionHost peer) => host.SaveTrust(new TrustEntry(
        peer.LocalDeviceId, peer.DisplayName, Convert.ToBase64String(peer.LocalSpki), false, true));

    static bool Ready(SessionHost host, PeerLink link)
    {
        lock (host.Gate) return link.GroupReady;
    }

    static void GroupCommand(SessionHost host, string? owner = null, string? member = null, bool? invited = null) =>
        Assert.Null(host.Apply(new LocalCommandPayload("1", member, owner, null, invited, null), FrameTypes.ConfigureClipboardGroup));

    static async Task WaitUntil(Func<bool> condition, CancellationToken ct)
    {
        while (!condition()) await Task.Delay(10, ct);
    }

    static async Task<ClipboardHistoryEntry> HistoryEntry(SessionHost host, string preview, CancellationToken ct)
    {
        while (true)
        {
            var entry = host.ReadClipboardHistory(0).Entries.FirstOrDefault(row => row.Preview == preview);
            if (entry is not null) return entry;
            await Task.Delay(10, ct);
        }
    }
}
