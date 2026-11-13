using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host.Tests;

[Collection("DiscoverySockets")]
public sealed class AndroidSeamParityTests
{
    [Fact]
    public async Task SessionHost_Runs_Over_Custom_QuicBackend_Discovery_And_InboundCommitSink()
    {
        var previousBackend = QuicLink.Backend;
        var backend = new InMemoryQuicBackend();
        QuicLink.UseBackend(backend);

        using var rootA = new TempDir();
        using var rootB = new TempDir();

        try
        {
            var pathsA = CreatePaths(rootA.Path);
            var pathsB = CreatePaths(rootB.Path);
            var discoveryA = new FakeManagedDiscovery("10.10.0.1");
            var discoveryB = new FakeManagedDiscovery("10.10.0.2");
            var sinkB = new FakeInboundCommitSink();

            await using var hostA = new SessionHost(pathsA, clipboard: null, discoveryA, transferSink: null, quicPort: 44101);
            await using var hostB = new SessionHost(pathsB, clipboard: null, discoveryB, transferSink: sinkB, quicPort: 44102);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            hostA.Start(cts.Token);
            hostB.Start(cts.Token);

            await WaitUntilAsync(() => hostA.ActiveListenerCount == 1 && hostB.ActiveListenerCount == 1, cts.Token);
            Assert.True(discoveryA.Started);
            Assert.True(discoveryB.Started);

            discoveryA.EmitPeer(new DiscoveredPeer(hostB.LocalDeviceId, "10.10.0.2", 44102, "PeerB"));
            discoveryB.EmitPeer(new DiscoveredPeer(hostA.LocalDeviceId, "10.10.0.1", 44101, "PeerA"));

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Apply(new LocalCommandPayload("req-1", hostB.LocalDeviceId, null, null, null, null), FrameTypes.StartPairing));
            }

            await WaitUntilAsync(
                () => ReadSnapshot(hostA).Pairing is { Code.Length: 6 } && ReadSnapshot(hostB).Pairing is { Code.Length: 6 },
                cts.Token);

            var codeA = ReadSnapshot(hostA).Pairing?.Code;
            var codeB = ReadSnapshot(hostB).Pairing?.Code;
            Assert.Equal(codeA, codeB);

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Apply(new LocalCommandPayload("req-2", hostB.LocalDeviceId, null, null, null, null), FrameTypes.AcceptPairing));
            }

            lock (hostB.Gate)
            {
                Assert.Null(hostB.Apply(new LocalCommandPayload("req-3", hostA.LocalDeviceId, null, null, null, null), FrameTypes.AcceptPairing));
            }

            await WaitUntilAsync(
                () => ReadSnapshot(hostA).Devices.Any(d => d.DeviceId == hostB.LocalDeviceId && d.Trusted && d.Presence == "connected")
                      && ReadSnapshot(hostB).Devices.Any(d => d.DeviceId == hostA.LocalDeviceId && d.Trusted && d.Presence == "connected"),
                cts.Token);

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Apply(new LocalCommandPayload("req-4", null, "RenamedHostA", null, null, null), FrameTypes.SetDisplayName));
            }

            Assert.Equal("RenamedHostA", discoveryA.LastDisplayName);

            lock (hostB.Gate)
            {
                Assert.Null(hostB.Apply(new LocalCommandPayload("req-5", hostA.LocalDeviceId, null, null, false, null), FrameTypes.SetAskBeforeSave));
            }

            var payload = new byte[70_000];
            RandomNumberGenerator.Fill(payload);
            var expectedSha = Convert.ToHexStringLower(SHA256.HashData(payload));
            var sourcePath = Path.Combine(rootA.Path, "android-seam.bin");
            await File.WriteAllBytesAsync(sourcePath, payload, cts.Token);

            lock (hostA.Gate)
            {
                Assert.Null(hostA.Apply(new LocalCommandPayload("req-6", hostB.LocalDeviceId, null, sourcePath, null, null), FrameTypes.SendFile));
            }

            await WaitUntilAsync(() => sinkB.CommittedBytes is not null, cts.Token);
            Assert.Equal(payload, sinkB.CommittedBytes);
            Assert.Equal(expectedSha, sinkB.CommittedRootHash);
            Assert.Equal("android-seam.bin", sinkB.CommittedFileName);
            Assert.Empty(Directory.GetFiles(pathsB.TransfersDirectory));
        }
        finally
        {
            QuicLink.UseBackend(previousBackend);
        }
    }

    static LinuxHostPaths CreatePaths(string root)
    {
        var id = Path.Combine(root, "identity");
        var state = Path.Combine(root, "state");
        var transfers = Path.Combine(root, "transfers");
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(state);
        Directory.CreateDirectory(transfers);
        Directory.CreateDirectory(downloads);
        return new LinuxHostPaths(
            identityDirectory: id,
            settingsFile: Path.Combine(root, "config.yaml"),
            stateDirectory: state,
            transfersDirectory: transfers,
            defaultDownloadDirectory: downloads);
    }

    static SnapshotPayload ReadSnapshot(SessionHost host)
    {
        var frame = host.Snapshot();
        return ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.SnapshotPayload).Value
               ?? throw new InvalidOperationException("Failed to decode snapshot.");
    }

    static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(30, ct);
        }
    }

    sealed class FakeManagedDiscovery(string ip) : IDiscovery
    {
        public bool Started { get; private set; }
        public string? LastDisplayName { get; private set; }
        public bool OwnsNetworkAndSockets => true;
        public event Action<DiscoveredPeer>? PeerDiscovered;
        public event Action? NetworkChanged;

        public IReadOnlyList<OnLinkPrefix>? GetActivePrefixes() =>
            [new OnLinkPrefix(ip, 24) { InterfaceIndex = 1 }];

        public Task StartAsync(CancellationToken ct = default)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task AnnounceAsync(ushort port, CancellationToken ct = default) => Task.CompletedTask;

        public void UpdateDisplayName(string displayName) => LastDisplayName = displayName;

        public void EmitPeer(DiscoveredPeer peer) => PeerDiscovered?.Invoke(peer);

        public void TriggerNetworkChange() => NetworkChanged?.Invoke();

        public void Dispose()
        {
        }
    }

    sealed class FakeInboundCommitSink : ITransferSink
    {
        public bool HandlesCommitInbound => true;
        public byte[]? CommittedBytes { get; private set; }
        public string? CommittedFileName { get; private set; }
        public string? CommittedRootHash { get; private set; }

        public bool TryCommitInbound(
            string tempFilePath,
            string fileName,
            string rootHashHex,
            string downloadDirectory,
            [MaybeNullWhen(false)] out string committedTarget)
        {
            if (!File.Exists(tempFilePath))
            {
                committedTarget = null;
                return false;
            }

            var bytes = File.ReadAllBytes(tempFilePath);
            var actualSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!string.Equals(actualSha, rootHashHex, StringComparison.OrdinalIgnoreCase))
            {
                CleanupPartial(tempFilePath);
                committedTarget = null;
                return false;
            }

            CommittedBytes = bytes;
            CommittedFileName = fileName;
            CommittedRootHash = actualSha;
            CleanupPartial(tempFilePath);
            committedTarget = "content://media/external/downloads/mock-1";
            return true;
        }

        public Task<string> CommitInboundFileAsync(string tempFilePath, string fileName, string rootHashHex, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool VerifyChecksum(string filePath, string expectedSha256Hex) => true;

        public void CleanupPartial(string tempFilePath)
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            var meta = Path.ChangeExtension(tempFilePath, ".meta.json");
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }
    }

    sealed class InMemoryQuicBackend : IQuicBackend
    {
        readonly ConcurrentDictionary<IPEndPoint, Listener> listeners = new();

        public bool IsSupported => true;

        public Task<IQuicListener> ListenMutualAsync(
            IPAddress address,
            X509Certificate2 certificate,
            int port = QuicLink.Port,
            CancellationToken cancellationToken = default)
        {
            var ep = new IPEndPoint(address, port);
            var listener = new Listener(ep, X509CertificateLoader.LoadCertificate(certificate.RawData), () => listeners.TryRemove(ep, out _));
            if (!listeners.TryAdd(ep, listener))
            {
                throw new InvalidOperationException($"Endpoint {ep} already bound.");
            }

            return Task.FromResult<IQuicListener>(listener);
        }

        public Task<(IQuicConnection Connection, X509Certificate2 Peer)> ConnectMutualAsync(
            IPEndPoint remote,
            X509Certificate2 certificate,
            CancellationToken cancellationToken = default)
        {
            if (!listeners.TryGetValue(remote, out var listener))
            {
                throw new InvalidOperationException($"No listener at {remote}.");
            }

            var clientCert = X509CertificateLoader.LoadCertificate(certificate.RawData);
            if (CertificateChecks.Evaluate(clientCert, null) == CertificateFate.RejectBeforeId
                || CertificateChecks.Evaluate(listener.ServerCert, null) == CertificateFate.RejectBeforeId)
            {
                throw new InvalidOperationException("Mutual P-256 check failed.");
            }

            var clientSource = listener.LocalEndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? new IPEndPoint(IPAddress.Parse("10.10.0.1"), 55000)
                : new IPEndPoint(IPAddress.IPv6Loopback, 55000);

            var c2sStreams = Channel.CreateUnbounded<Stream>();
            var s2cStreams = Channel.CreateUnbounded<Stream>();

            var clientConn = new Connection(remote, listener.ServerCert, c2sStreams.Writer, s2cStreams.Reader);
            var serverConn = new Connection(clientSource, clientCert, s2cStreams.Writer, c2sStreams.Reader);
            listener.AcceptQueue.Writer.TryWrite(serverConn);

            return Task.FromResult<(IQuicConnection, X509Certificate2)>((clientConn, listener.ServerCert));
        }

        sealed class Listener(IPEndPoint ep, X509Certificate2 serverCert, Action onDispose) : IQuicListener
        {
            public IPEndPoint LocalEndPoint => ep;
            public X509Certificate2 ServerCert => serverCert;
            public Channel<IQuicConnection> AcceptQueue { get; } = Channel.CreateUnbounded<IQuicConnection>();

            public ValueTask<IQuicConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default) =>
                AcceptQueue.Reader.ReadAsync(cancellationToken);

            public ValueTask DisposeAsync()
            {
                onDispose();
                AcceptQueue.Writer.TryComplete();
                serverCert.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        sealed class Connection(
            IPEndPoint remote,
            X509Certificate2 peerCert,
            ChannelWriter<Stream> outboundWriter,
            ChannelReader<Stream> inboundReader) : IQuicConnection
        {
            public EndPoint? RemoteEndPoint => remote;
            public X509Certificate2? PeerCertificate => peerCert;

            public ValueTask<Stream> OpenOutboundStreamAsync(CancellationToken cancellationToken = default)
            {
                var aToB = new Pipe();
                var bToA = new Pipe();
                var initiator = new DuplexStream(bToA.Reader.AsStream(), aToB.Writer.AsStream());
                var responder = new DuplexStream(aToB.Reader.AsStream(), bToA.Writer.AsStream());
                if (!outboundWriter.TryWrite(responder))
                {
                    throw new IOException("Connection closed.");
                }

                return ValueTask.FromResult<Stream>(initiator);
            }

            public ValueTask<Stream> AcceptInboundStreamAsync(CancellationToken cancellationToken = default) =>
                inboundReader.ReadAsync(cancellationToken);

            public ValueTask DisposeAsync()
            {
                outboundWriter.TryComplete();
                return ValueTask.CompletedTask;
            }
        }

        sealed class DuplexStream(Stream reader, Stream writer) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() => writer.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => reader.Read(buffer, offset, count);
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => reader.ReadAsync(buffer, cancellationToken);
            public override void Write(byte[] buffer, int offset, int count) => writer.Write(buffer, offset, count);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => writer.WriteAsync(buffer, cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    writer.Dispose();
                    reader.Dispose();
                }

                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await writer.DisposeAsync().ConfigureAwait(false);
                await reader.DisposeAsync().ConfigureAwait(false);
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "contextall-android-seam-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

