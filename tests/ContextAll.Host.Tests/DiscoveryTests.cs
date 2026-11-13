using System.Net;
using System.Net.Sockets;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void An_mdns_result_reaches_the_device_snapshot()
    {
        using var root = new TempRoot();
        using var host = LanHost.CreateIsolated(root.Path);
        var spki = Enumerable.Range(1, 91).Select(index => (byte)index).ToArray();
        var id = DeviceIds.Text(spki);

        host.NoteMdns(new MdnsPeer(id, "phone", "10.0.0.5", 42461));

        var snapshot = Snapshot(host.Snapshot());
        var device = Assert.Single(snapshot.Devices, item => item.DeviceId == id);
        Assert.Equal("phone", device.DisplayName);
        Assert.Equal("reachable", device.Presence);
    }

    [Fact]
    public void The_beacon_interval_is_five_seconds()
    {
        Assert.Equal(5000, LanHost.BeaconIntervalMs);
    }

    [Fact]
    public void The_reconcile_interval_is_five_seconds_and_the_debounce_is_one()
    {
        Assert.Equal(5, LanHost.ReconcileSeconds);
        using var monitor = new NetworkMonitor(() => { });
        Assert.Equal(TimeSpan.FromSeconds(1), monitor.Debounce);
    }

    [Fact]
    public async Task Network_signals_are_debounced_to_one_callback()
    {
        var calls = 0;
        using var monitor = new NetworkMonitor(() => Interlocked.Increment(ref calls), TimeSpan.FromMilliseconds(40));

        monitor.Signal();
        monitor.Signal();
        monitor.Signal();
        await Task.Delay(250);

        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task An_mdns_response_reaches_the_discovery_callback()
    {
        var found = new TaskCompletionSource<MdnsPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery = new MdnsDiscovery("ca1aaaaaaaaaaaaaaaaaaaaaaaaaa", "desk", 42461, peer => found.TrySetResult(peer), _ => { });
        discovery.Start([new OnLinkPrefix("127.0.0.1", 8)]);
        if (!discovery.Active)
        {
            return;
        }

        using var sender = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sender.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var packet = MdnsCodec.EncodeResponse("ca1bbbbbbbbbbbbbbbbbbbbbbbbbb", "phone", 42461, IPAddress.Loopback);
        sender.SendTo(packet, new IPEndPoint(IPAddress.Loopback, MdnsDiscovery.Port));

        var peer = await found.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("ca1bbbbbbbbbbbbbbbbbbbbbbbbbb", peer.DeviceId);
        Assert.Equal("phone", peer.DisplayName);
        Assert.Equal(42461, peer.Port);
        Assert.Equal("127.0.0.1", peer.Address);
    }

    [Fact]
    public void The_mdns_query_interval_is_one_minute_and_link_local_addresses_are_not_advertised()
    {
        Assert.Equal(60, MdnsDiscovery.QuerySeconds);
        var prefixes = new List<OnLinkPrefix>
        {
            new("fe80::1", 64) { InterfaceIndex = 2, LinkLocal = true },
            new("fd00::1", 64) { InterfaceIndex = 2 },
        };

        Assert.Equal(IPAddress.Parse("fd00::1"), MdnsDiscovery.AdvertisedAddress(prefixes, AddressFamily.InterNetworkV6));
        Assert.Null(MdnsDiscovery.AdvertisedAddress([prefixes[0]], AddressFamily.InterNetworkV6));
    }

    [Fact]
    public void Discovery_does_not_throw_when_the_mdns_port_is_busy()
    {
        using var blocker4 = TryBind(AddressFamily.InterNetwork);
        using var blocker6 = TryBind(AddressFamily.InterNetworkV6);
        var logs = new List<string>();
        using var discovery = new MdnsDiscovery("ca1aaaaaaaaaaaaaaaaaaaaaaaaaa", "desk", 42461, _ => { }, logs.Add);

        var thrown = Record.Exception(() => discovery.Start([new OnLinkPrefix("127.0.0.1", 8)]));
        Thread.Sleep(100);

        Assert.Null(thrown);
        Assert.True(logs.Count(message => message.StartsWith("mdns-skipped", StringComparison.Ordinal)) <= 1);
    }

    [Fact]
    public void A_rename_during_pairing_arrives_after_trust_and_paths_can_be_reannounced()
    {
        using var root = new TempRoot();
        using var host = LanHost.CreateIsolated(root.Path);
        var remoteSpki = Enumerable.Range(2, 91).Select(index => (byte)index).ToArray();
        var link = new PeerLink(host, null!, new NullStream(), remoteSpki, initiated: false);
        host.Track(link);
        link.MarkWritable();

        link.Dispatch(FrameMessages.Json(
            FrameTypes.Hello,
            0,
            0,
            FrameTypes.SessionStream,
            new HelloPayload("pairing", "peer"),
            ProtocolJsonContext.Default.HelloPayload));
        link.RenameLocal("renamed");
        Assert.Null(link.AcceptFromUser());
        link.Dispatch(FrameMessages.Json(
            FrameTypes.PairAccept,
            0,
            0,
            FrameTypes.SessionStream,
            new DonePayload(true),
            ProtocolJsonContext.Default.DonePayload));

        Assert.True(link.Pairing.Trusted);
        Assert.Contains(link.Pairing.Frames, frame => frame.Type == FrameTypes.DisplayName);

        link.AnnouncePaths();
        Assert.Contains(link.Pairing.Frames, frame => frame.Type == FrameTypes.PathAnnounce);
    }

    [Fact]
    public async Task A_network_change_sends_a_beacon_and_reannounces_paths()
    {
        using var root = new TempRoot();
        using var host = LanHost.CreateIsolated(root.Path);
        Assert.Equal(0, host.LastBeaconTick);

        await host.RebuildNetworkForTest([new OnLinkPrefix("10.9.0.5", 24)]);
        var first = host.LastBeaconTick;
        Assert.NotEqual(0, first);

        Thread.Sleep(20);
        await host.ReconcileNetworkForTest([new OnLinkPrefix("10.9.0.5", 24)]);
        Assert.Equal(first, host.LastBeaconTick);

        await host.ReconcileNetworkForTest([new OnLinkPrefix("10.9.0.6", 24)]);
        Assert.True(host.LastBeaconTick > first);
    }

    [Fact]
    public async Task A_trusted_peer_receives_the_new_path_set_and_an_empty_one()
    {
        using var root = new TempRoot();
        using var host = LanHost.CreateIsolated(root.Path);
        var remoteSpki = Enumerable.Range(4, 91).Select(index => (byte)index).ToArray();
        using var stream = new RecordingStream();
        var link = new PeerLink(host, null!, stream, remoteSpki, initiated: false);
        host.Track(link);
        var run = link.RunAsync();
        Assert.NotNull(WaitForFrame(stream, FrameTypes.Hello));

        link.Dispatch(FrameMessages.Json(
            FrameTypes.Hello,
            0,
            0,
            FrameTypes.SessionStream,
            new HelloPayload("pairing", "peer"),
            ProtocolJsonContext.Default.HelloPayload));
        Assert.Null(link.AcceptFromUser());
        link.Dispatch(FrameMessages.Json(
            FrameTypes.PairAccept,
            0,
            0,
            FrameTypes.SessionStream,
            new DonePayload(true),
            ProtocolJsonContext.Default.DonePayload));
        Assert.True(link.Pairing.Trusted);

        await host.RebuildNetworkForTest([new OnLinkPrefix("10.9.0.5", 24)]);
        Assert.Equal(["10.9.0.5"], await WaitForPaths(stream, addresses => addresses.SequenceEqual(["10.9.0.5"])));

        await host.RebuildNetworkForTest([]);
        Assert.Empty(await WaitForPaths(stream, addresses => addresses.Length == 0));

        link.SendUnpair();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Unpair_is_written_before_the_link_closes()
    {
        using var root = new TempRoot();
        using var host = LanHost.CreateIsolated(root.Path);
        var remoteSpki = Enumerable.Range(3, 91).Select(index => (byte)index).ToArray();
        using var stream = new RecordingStream();
        var link = new PeerLink(host, null!, stream, remoteSpki, initiated: false);
        host.Track(link);

        var run = link.RunAsync();
        var hello = WaitForFrame(stream, FrameTypes.Hello);
        Assert.NotNull(hello);

        link.SendUnpair();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        var frames = Frames(stream.Written);
        Assert.Contains(frames, frame => frame.Type == FrameTypes.Unpair);
        Assert.Contains(frames, frame => frame.Type == FrameTypes.Hello);
    }

    static async Task<string[]> WaitForPaths(RecordingStream stream, Func<string[], bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var frame in Frames(stream.Written).Where(frame => frame.Type == FrameTypes.PathAnnounce))
            {
                var payload = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.PathAnnouncePayload).Value;
                if (payload is not null && ready(payload.Addresses))
                {
                    return payload.Addresses;
                }
            }

            await Task.Delay(20);
        }

        Assert.Fail("path announce");
        throw new InvalidOperationException("unreachable");
    }

    static Frame? WaitForFrame(RecordingStream stream, byte type)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            var found = Frames(stream.Written).FirstOrDefault(frame => frame.Type == type);
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(20);
        }

        return null;
    }

    static List<Frame> Frames(byte[] bytes)
    {
        var frames = new List<Frame>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = FrameCodec.Read(bytes.AsSpan(offset));
            if (read.Frame is null || read.Consumed == 0)
            {
                break;
            }

            frames.Add(read.Frame);
            offset += read.Consumed;
        }

        return frames;
    }

    static SnapshotPayload Snapshot(Frame frame) =>
        ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value
        ?? throw new InvalidOperationException("snapshot");

    static Socket? TryBind(AddressFamily family)
    {
        try
        {
            var socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(family == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, MdnsDiscovery.Port));
            return socket;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "contextall-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    sealed class NullStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }

    sealed class RecordingStream : Stream
    {
        readonly object gate = new();
        byte[] written = [];

        public byte[] Written
        {
            get
            {
                lock (gate)
                {
                    return written.ToArray();
                }
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written.Length;

        public override long Position
        {
            get => Written.Length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Append(buffer.AsSpan(offset, count));

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Append(buffer.Span);
            return ValueTask.CompletedTask;
        }

        void Append(ReadOnlySpan<byte> data)
        {
            lock (gate)
            {
                var next = new byte[written.Length + data.Length];
                written.CopyTo(next, 0);
                data.CopyTo(next.AsSpan(written.Length));
                written = next;
            }
        }
    }
}
