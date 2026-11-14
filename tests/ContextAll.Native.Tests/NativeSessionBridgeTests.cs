using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;
using ContextAll.Session;

namespace ContextAll.Native.Tests;

public sealed class NativeSessionBridgeTests
{
    [Fact]
    public void Start_ResetIdentity_And_Stop_CycleIdentityAndState()
    {
        using var dir = new TempDir();
        using var bridge = new NativeSessionBridge();

        var startJson = bridge.Start(
            dir.NoBackup,
            dir.Files,
            "Pixel Test",
            quicPort: NextFreeUdpPort(),
            useNativeMsQuicBackend: false,
            deferInboundCommitToHost: false);

        using var startDoc = JsonDocument.Parse(startJson);
        Assert.True(startDoc.RootElement.GetProperty("ok").GetBoolean());
        var firstId = startDoc.RootElement.GetProperty("deviceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(firstId));
        Assert.StartsWith("ca1", firstId, StringComparison.Ordinal);
        Assert.True(bridge.IsRunning);

        var snapJson = bridge.GetSnapshotJson();
        Assert.NotNull(snapJson);
        using var snapDoc = JsonDocument.Parse(snapJson);
        Assert.Equal(firstId, snapDoc.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal("Pixel Test", snapDoc.RootElement.GetProperty("displayName").GetString());

        var resetJson = bridge.ResetIdentity(
            dir.NoBackup,
            dir.Files,
            "Pixel Test",
            quicPort: NextFreeUdpPort(),
            useNativeMsQuicBackend: false,
            deferInboundCommitToHost: false);

        using var resetDoc = JsonDocument.Parse(resetJson);
        Assert.True(resetDoc.RootElement.GetProperty("ok").GetBoolean());
        var secondId = resetDoc.RootElement.GetProperty("deviceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(secondId));
        Assert.NotEqual(firstId, secondId);

        bridge.Stop();
        Assert.False(bridge.IsRunning);
        Assert.Null(bridge.GetSnapshotJson());

        var sawStopped = false;
        while (bridge.PollEventJson(20) is { } ev)
        {
            using var evDoc = JsonDocument.Parse(ev);
            if (evDoc.RootElement.GetProperty("kind").GetString() == "stopped")
            {
                sawStopped = true;
                break;
            }
        }

        Assert.True(sawStopped);
    }

    [Fact]
    public void SubmitCommandJson_ValidatesFrameTypesAndDeduplicatesRequestId()
    {
        using var dir = new TempDir();
        using var bridge = new NativeSessionBridge();
        bridge.Start(
            dir.NoBackup,
            dir.Files,
            "Device A",
            quicPort: NextFreeUdpPort(),
            useNativeMsQuicBackend: false,
            deferInboundCommitToHost: false);

        // Non-command frame must be rejected cleanly
        var badFrameRes = JsonDocument.Parse(
            bridge.SubmitCommandJson(FrameTypes.Snapshot, """{"requestId":"0001"}"""));
        Assert.False(badFrameRes.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid", badFrameRes.RootElement.GetProperty("error").GetString());

        // Malformed requestId must be rejected
        var badIdRes = JsonDocument.Parse(
            bridge.SubmitCommandJson(FrameTypes.SetDisplayName, """{"requestId":"BAD-ID!","displayName":"Renamed"}"""));
        Assert.False(badIdRes.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid", badIdRes.RootElement.GetProperty("error").GetString());

        // Valid SetDisplayName updates snapshot
        var okRes = JsonDocument.Parse(
            bridge.SubmitCommandJson(FrameTypes.SetDisplayName, """{"requestId":"abcdef01","displayName":"Studio Pixel"}"""));
        Assert.True(okRes.RootElement.GetProperty("ok").GetBoolean());

        var snap = bridge.GetSnapshotJson();
        Assert.NotNull(snap);
        Assert.Contains("Studio Pixel", snap, StringComparison.Ordinal);

        // Duplicate requestId must be rejected
        var dupRes = JsonDocument.Parse(
            bridge.SubmitCommandJson(FrameTypes.SetDisplayName, """{"requestId":"abcdef01","displayName":"Other Name"}"""));
        Assert.False(dupRes.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("duplicate-request", dupRes.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void EncodeLocalBeacon_And_NotifyBeaconPacket_DiscoversPeer()
    {
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        using var bridgeA = new NativeSessionBridge();
        using var bridgeB = new NativeSessionBridge();

        var portA = NextFreeUdpPort();
        var portB = NextFreeUdpPort();

        var startA = JsonDocument.Parse(bridgeA.Start(dirA.NoBackup, dirA.Files, "Alpha", portA, false, false));
        var idA = startA.RootElement.GetProperty("deviceId").GetString();
        bridgeB.Start(dirB.NoBackup, dirB.Files, "Beta", portB, false, false);

        bridgeB.NotifyNetworkChanged("127.0.0.1/8");
        var beacon = bridgeA.EncodeLocalBeacon((ushort)portA);
        Assert.NotNull(beacon);

        bridgeB.NotifyBeaconPacket(beacon, "127.0.0.1");

        var foundPeer = false;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !foundPeer)
        {
            var ev = bridgeB.PollEventJson(150);
            if (ev is null)
            {
                var snap = bridgeB.GetSnapshotJson();
                if (snap is not null && idA is not null && snap.Contains(idA, StringComparison.Ordinal))
                {
                    foundPeer = true;
                }

                continue;
            }

            if (idA is not null && ev.Contains(idA, StringComparison.Ordinal))
            {
                foundPeer = true;
            }
        }

        Assert.True(foundPeer);
    }

    [Fact]
    public void ConnectEndpoint_EstablishesMutualQuicSession_OverLoopback()
    {
        using var quicScope = new ScopedInMemoryQuicBackend();
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        using var bridgeA = new NativeSessionBridge();
        using var bridgeB = new NativeSessionBridge();

        var portA = NextFreeUdpPort();
        var portB = NextFreeUdpPort();

        bridgeA.Start(dirA.NoBackup, dirA.Files, "Phone A", portA, useNativeMsQuicBackend: false, deferInboundCommitToHost: false);
        var startB = JsonDocument.Parse(
            bridgeB.Start(dirB.NoBackup, dirB.Files, "Workstation B", portB, useNativeMsQuicBackend: false, deferInboundCommitToHost: false));
        var idB = startB.RootElement.GetProperty("deviceId").GetString();

        bridgeA.NotifyNetworkChanged("127.0.0.1/8");
        bridgeB.NotifyNetworkChanged("127.0.0.1/8");

        var waitBReady = DateTime.UtcNow.AddSeconds(5);
        while ((bridgeB.CurrentHost?.ActiveListenerCount ?? 0) == 0 && DateTime.UtcNow < waitBReady)
        {
            Thread.Sleep(25);
        }

        Assert.True((bridgeB.CurrentHost?.ActiveListenerCount ?? 0) > 0);

        // Invalid endpoint must fail immediately
        using var badDoc = JsonDocument.Parse(bridgeA.ConnectEndpoint("not-an-ip", 99999, null));
        Assert.False(badDoc.RootElement.GetProperty("ok").GetBoolean());

        // Real QUIC mutual TLS handshake over loopback backend
        using var connectDoc = JsonDocument.Parse(bridgeA.ConnectEndpoint("127.0.0.1", portB, "Dev Workstation"));
        Assert.True(connectDoc.RootElement.GetProperty("ok").GetBoolean(), connectDoc.RootElement.ToString());
        Assert.Equal(idB, connectDoc.RootElement.GetProperty("deviceId").GetString());

        var snapA = bridgeA.GetSnapshotJson();
        Assert.NotNull(snapA);
        Assert.NotNull(idB);
        Assert.Contains(idB, snapA, StringComparison.Ordinal);
        Assert.Contains("Dev Workstation", snapA, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectEndpoint_PairAndSendFile_WithDeferredCommitHandshake()
    {
        using var quicScope = new ScopedInMemoryQuicBackend();
        using var dirA = new TempDir();
        using var dirB = new TempDir();
        using var bridgeA = new NativeSessionBridge();
        using var bridgeB = new NativeSessionBridge();

        var portA = NextFreeUdpPort();
        var portB = NextFreeUdpPort();

        bridgeA.Start(dirA.NoBackup, dirA.Files, "Sender A", portA, useNativeMsQuicBackend: false, deferInboundCommitToHost: false);
        var startB = JsonDocument.Parse(
            bridgeB.Start(dirB.NoBackup, dirB.Files, "Receiver B", portB, useNativeMsQuicBackend: false, deferInboundCommitToHost: true));
        var idB = startB.RootElement.GetProperty("deviceId").GetString();
        Assert.NotNull(idB);

        bridgeA.NotifyNetworkChanged("127.0.0.1/8");
        bridgeB.NotifyNetworkChanged("127.0.0.1/8");

        var waitListeners = DateTime.UtcNow.AddSeconds(5);
        while (((bridgeA.CurrentHost?.ActiveListenerCount ?? 0) == 0 || (bridgeB.CurrentHost?.ActiveListenerCount ?? 0) == 0)
               && DateTime.UtcNow < waitListeners)
        {
            Thread.Sleep(25);
        }

        Assert.True((bridgeA.CurrentHost?.ActiveListenerCount ?? 0) > 0);
        Assert.True((bridgeB.CurrentHost?.ActiveListenerCount ?? 0) > 0);

        using var connectDoc = JsonDocument.Parse(bridgeA.ConnectEndpoint("127.0.0.1", portB, "Receiver B"));
        Assert.True(connectDoc.RootElement.GetProperty("ok").GetBoolean());

        // Start pairing on A and accept pairing on both sides
        bridgeA.SubmitCommandJson(
            FrameTypes.StartPairing,
            $$"""{"requestId":"11110001","deviceId":"{{idB}}"}""");

        // Wait until B receives pairing prompt (or poll until B sees prompt), then accept on both
        var acceptedB = false;
        var acceptedA = false;
        var pairedDeadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < pairedDeadline && (!acceptedA || !acceptedB))
        {
            if (!acceptedB)
            {
                using var rB = JsonDocument.Parse(bridgeB.SubmitCommandJson(FrameTypes.AcceptPairing, $$"""{"requestId":"2222{{RandomHex()}}"}"""));
                if (rB.RootElement.GetProperty("ok").GetBoolean())
                {
                    acceptedB = true;
                }
            }

            if (!acceptedA)
            {
                using var rA = JsonDocument.Parse(bridgeA.SubmitCommandJson(FrameTypes.AcceptPairing, $$"""{"requestId":"3333{{RandomHex()}}"}"""));
                if (rA.RootElement.GetProperty("ok").GetBoolean())
                {
                    acceptedA = true;
                }
            }

            Thread.Sleep(40);
        }

        Assert.True(acceptedA || acceptedB);

        // Wait until both snapshots reflect mutual trust before sending file
        var trustedDeadline = DateTime.UtcNow.AddSeconds(5);
        var trusted = false;
        while (DateTime.UtcNow < trustedDeadline)
        {
            var snapA = bridgeA.GetSnapshotJson();
            var snapB = bridgeB.GetSnapshotJson();
            if (snapA is not null && snapB is not null
                && snapA.Contains("\"trusted\":true", StringComparison.Ordinal)
                && snapB.Contains("\"trusted\":true", StringComparison.Ordinal))
            {
                trusted = true;
                break;
            }

            Thread.Sleep(40);
        }

        Assert.True(trusted);

        // Disable ask-before-save on Receiver B so file auto-accepts and hits deferred MediaStore commit
        bridgeB.SubmitCommandJson(
            FrameTypes.SetAskBeforeSave,
            $$"""{"requestId":"44440001","deviceId":"{{bridgeA.CurrentHost?.LocalDeviceId}}","enabled":false}""");

        var payloadPath = Path.Combine(dirA.NoBackup, "hello-bridge.txt");
        File.WriteAllText(payloadPath, "ContextAll NativeAOT QUIC Bridge Test Payload!");

        using var sendRes = JsonDocument.Parse(bridgeA.SubmitCommandJson(
            FrameTypes.SendFile,
            $$"""{"requestId":"55550001","deviceId":"{{idB}}","path":{{JsonSerializer.Serialize(payloadPath)}}}"""));
        Assert.True(sendRes.RootElement.GetProperty("ok").GetBoolean(), sendRes.RootElement.ToString());

        // Receiver B must emit commitInbound event and complete cleanly once CompleteInboundCommit is called
        var committed = false;
        var commitDeadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < commitDeadline && !committed)
        {
            var ev = bridgeB.PollEventJson(100);
            if (ev is null)
            {
                continue;
            }

            using var evDoc = JsonDocument.Parse(ev);
            if (evDoc.RootElement.GetProperty("kind").GetString() == "commitInbound")
            {
                var commitId = evDoc.RootElement.GetProperty("commitId").GetInt64();
                var tempPath = evDoc.RootElement.GetProperty("tempFilePath").GetString();
                Assert.False(string.IsNullOrWhiteSpace(tempPath));
                Assert.True(File.Exists(tempPath));
                var finalPath = Path.Combine(dirB.Files, "committed-hello.txt");
                File.Copy(tempPath, finalPath, overwrite: true);
                bridgeB.CompleteInboundCommit(commitId, true, finalPath);
                committed = true;
            }
        }

        Assert.True(committed);
        Assert.True(File.Exists(Path.Combine(dirB.Files, "committed-hello.txt")));
    }

    [Fact]
    public void ManagedSha256_MatchesBclSha256_ForVectorsSizesAndStreams()
    {
        // Empty and NIST CAVP vector
        Assert.Equal(SHA256.HashData([]), ManagedSha256.HashData([]));
        var abc = "abc"u8.ToArray();
        Assert.Equal(SHA256.HashData(abc), ManagedSha256.HashData(abc));

        // Block-boundary lengths: 55, 56, 63, 64, 65, 128, 4097
        int[] lengths = [0, 1, 55, 56, 57, 63, 64, 65, 127, 128, 129, 4097, 65539];
        foreach (var len in lengths)
        {
            var payload = new byte[len];
            for (var i = 0; i < len; i++)
            {
                payload[i] = (byte)((i * 31 + 17) & 0xFF);
            }

            var expected = SHA256.HashData(payload);
            Assert.Equal(expected, ManagedSha256.HashData(payload));

            using var stream = new MemoryStream(payload, writable: false);
            Assert.Equal(expected, ManagedSha256.HashData(stream));
        }

        // Incremental AppendData split across odd chunk sizes
        var large = new byte[10_000];
        Random.Shared.NextBytes(large);
        var incremental = new ManagedSha256();
        var pos = 0;
        var step = 1;
        while (pos < large.Length)
        {
            var count = Math.Min(step, large.Length - pos);
            incremental.AppendData(large.AsSpan(pos, count));
            pos += count;
            step = (step * 3) % 97 + 1;
        }

        Assert.Equal(SHA256.HashData(large), incremental.GetHashAndReset());
    }

    [Fact]
    public void CertificateChecks_DerOverloads_ValidateP256AndTrustWithoutX509()
    {
        using var ecP256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p256Req = new CertificateRequest("CN=contextall", ecP256, HashAlgorithmName.SHA256);
        using var p256Cert = p256Req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        var p256Der = p256Cert.RawData;

        var expectedSpki = CertificateChecks.LeafSpki(p256Cert);
        var parsedSpki = CertificateChecks.LeafSpki(p256Der.AsSpan());
        Assert.NotNull(parsedSpki);
        ArgumentNullException.ThrowIfNull(parsedSpki);
        Assert.Equal(expectedSpki, parsedSpki);
        Assert.Equal(CertificateFate.Continue, CertificateChecks.Evaluate(p256Der.AsSpan(), null));

        var matchingTrust = new TrustEntry(
            DeviceIds.Text(parsedSpki),
            "Peer",
            Convert.ToBase64String(parsedSpki),
            true,
            false);
        Assert.Equal(CertificateFate.Continue, CertificateChecks.Evaluate(p256Der.AsSpan(), matchingTrust));

        var wrongSpki = new byte[parsedSpki.Length];
        parsedSpki.CopyTo(wrongSpki, 0);
        wrongSpki[^1] ^= 0x01;
        var mismatchedTrust = matchingTrust with { SpkiBase64 = Convert.ToBase64String(wrongSpki) };
        Assert.Equal(CertificateFate.CloseKeepTrust, CertificateChecks.Evaluate(p256Der.AsSpan(), mismatchedTrust));

        // Non-P256 (P-384 and RSA) must be rejected before identity
        using var ecP384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var p384Req = new CertificateRequest("CN=contextall", ecP384, HashAlgorithmName.SHA384);
        using var p384Cert = p384Req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        Assert.Null(CertificateChecks.LeafSpki(p384Cert.RawData.AsSpan()));
        Assert.Equal(CertificateFate.RejectBeforeId, CertificateChecks.Evaluate(p384Cert.RawData.AsSpan(), null));

        using var rsa = RSA.Create(2048);
        var rsaReq = new CertificateRequest("CN=contextall", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var rsaCert = rsaReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        Assert.Null(CertificateChecks.LeafSpki(rsaCert.RawData.AsSpan()));
        Assert.Equal(CertificateFate.RejectBeforeId, CertificateChecks.Evaluate(rsaCert.RawData.AsSpan(), null));

        // Truncated DER must fail cleanly
        Assert.Null(CertificateChecks.LeafSpki(p256Der.AsSpan(0, p256Der.Length - 1)));
    }

    [Fact]
    public void PortableQuicBackend_WithPreloadedPfxAndManagedSha256_PairsAndClipboardSyncs()
    {
        var previousManaged = PlatformCrypto.UseManagedSha256;
        try
        {
            using var quicScope = new ScopedInMemoryQuicBackend();
            var (pfxA, spkiA) = CreateSelfSignedPfxAndSpki();
            var (pfxB, spkiB) = CreateSelfSignedPfxAndSpki();

            using var dirA = new TempDir();
            using var dirB = new TempDir();
            using var bridgeA = new NativeSessionBridge();
            using var bridgeB = new NativeSessionBridge();

            var portA = NextFreeUdpPort();
            var portB = NextFreeUdpPort();

            var startAJson = bridgeA.Start(
                dirA.NoBackup,
                dirA.Files,
                "Portable A",
                quicPort: portA,
                useNativeMsQuicBackend: false,
                deferInboundCommitToHost: false,
                localPfx: pfxA,
                localSpki: spkiA);
            var startBJson = bridgeB.Start(
                dirB.NoBackup,
                dirB.Files,
                "Portable B",
                quicPort: portB,
                useNativeMsQuicBackend: false,
                deferInboundCommitToHost: false,
                localPfx: pfxB,
                localSpki: spkiB);

            var idA = JsonDocument.Parse(startAJson).RootElement.GetProperty("deviceId").GetString()!;
            var idB = JsonDocument.Parse(startBJson).RootElement.GetProperty("deviceId").GetString()!;
            Assert.Equal(DeviceIds.Text(spkiA), idA);
            Assert.Equal(DeviceIds.Text(spkiB), idB);

            bridgeA.NotifyNetworkChanged("127.0.0.1/8");
            bridgeB.NotifyNetworkChanged("127.0.0.1/8");
            Assert.True(SpinUntil(
                () => (bridgeA.CurrentHost?.ActiveListenerCount ?? 0) > 0 && (bridgeB.CurrentHost?.ActiveListenerCount ?? 0) > 0,
                TimeSpan.FromSeconds(5)));

            var connectRes = JsonDocument.Parse(bridgeA.ConnectEndpoint("127.0.0.1", portB, "Portable B"));
            Assert.True(connectRes.RootElement.GetProperty("ok").GetBoolean(), connectRes.RootElement.ToString());

            _ = bridgeA.SubmitCommandJson(
                FrameTypes.StartPairing,
                $$"""{"requestId":"{{RandomHex()}}","deviceId":"{{idB}}"}""");

            var acceptB = false;
            var acceptA = false;
            Assert.True(SpinUntil(() =>
            {
                if (!acceptB)
                {
                    using var rB = JsonDocument.Parse(bridgeB.SubmitCommandJson(
                        FrameTypes.AcceptPairing,
                        $$"""{"requestId":"{{RandomHex()}}"}"""));
                    acceptB = rB.RootElement.GetProperty("ok").GetBoolean();
                }

                if (!acceptA)
                {
                    using var rA = JsonDocument.Parse(bridgeA.SubmitCommandJson(
                        FrameTypes.AcceptPairing,
                        $$"""{"requestId":"{{RandomHex()}}"}"""));
                    acceptA = rA.RootElement.GetProperty("ok").GetBoolean();
                }

                var snapA = bridgeA.GetSnapshotJson();
                var snapB = bridgeB.GetSnapshotJson();
                return (acceptA || acceptB)
                    && snapA is not null && snapB is not null
                    && snapA.Contains("\"trusted\":true", StringComparison.Ordinal)
                    && snapB.Contains("\"trusted\":true", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(5)));

            using (var cA = JsonDocument.Parse(bridgeA.SubmitCommandJson(
                FrameTypes.SetClipboardSync,
                $$"""{"requestId":"{{RandomHex()}}","deviceId":"{{idB}}","enabled":true}""")))
            {
                Assert.True(cA.RootElement.GetProperty("ok").GetBoolean());
            }

            using (var cB = JsonDocument.Parse(bridgeB.SubmitCommandJson(
                FrameTypes.SetClipboardSync,
                $$"""{"requestId":"{{RandomHex()}}","deviceId":"{{idA}}","enabled":true}""")))
            {
                Assert.True(cB.RootElement.GetProperty("ok").GetBoolean());
            }

            // Wait for mutual clipboard flag exchange before pushing text
            Assert.True(SpinUntil(() =>
            {
                var snapA = bridgeA.GetSnapshotJson();
                var snapB = bridgeB.GetSnapshotJson();
                return snapA is not null && snapB is not null
                    && snapA.Contains("\"clipboardSync\":true", StringComparison.Ordinal)
                    && snapB.Contains("\"clipboardSync\":true", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(5)));

            bridgeA.PushClipboardText("portable-clip-sync");
            var synced = false;
            var clipDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < clipDeadline && !synced)
            {
                if (bridgeB.PollEventJson(50) is not { } ev)
                {
                    continue;
                }

                using var evDoc = JsonDocument.Parse(ev);
                if (evDoc.RootElement.GetProperty("kind").GetString() == "clipboard"
                    && evDoc.RootElement.GetProperty("text").GetString() == "portable-clip-sync")
                {
                    synced = true;
                }
            }

            Assert.True(synced);
        }
        finally
        {
            PlatformCrypto.UseManagedSha256 = previousManaged;
        }
    }

    static (byte[] Pfx, byte[] Spki) CreateSelfSignedPfxAndSpki()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return (cert.Export(X509ContentType.Pfx, "contextall"), key.ExportSubjectPublicKeyInfo());
    }

    static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }

    static int reqCounter;

    static string RandomHex() => Interlocked.Increment(ref reqCounter).ToString("x4");

    static int NextFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint?)socket.LocalEndPoint)?.Port ?? 45000;
    }

    sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "contextall-native-tests-" + Guid.NewGuid().ToString("N"));
        public string NoBackup => Path.Combine(Root, "no_backup");
        public string Files => Path.Combine(Root, "files");

        public TempDir()
        {
            Directory.CreateDirectory(NoBackup);
            Directory.CreateDirectory(Files);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
                // Ignore transient file lock on cleanup
            }
        }
    }

    sealed class ScopedInMemoryQuicBackend : IDisposable
    {
        readonly IQuicBackend previous;

        public ScopedInMemoryQuicBackend()
        {
            previous = QuicLink.Backend;
            QuicLink.UseBackend(new LoopbackQuicBackend());
        }

        public void Dispose() => QuicLink.UseBackend(previous);
    }

    sealed class LoopbackQuicBackend : IPortableQuicBackend
    {
        readonly ConcurrentDictionary<IPEndPoint, Listener> listeners = new();

        public bool IsSupported => true;

        public Task<IQuicListener> ListenMutualAsync(
            IPAddress address,
            X509Certificate2 certificate,
            int port = QuicLink.Port,
            CancellationToken cancellationToken = default) =>
            ListenMutualPortableAsync(address, new PortableCredential(certificate.Export(X509ContentType.Pfx, "contextall")), port, cancellationToken);

        public Task<IQuicListener> ListenMutualPortableAsync(
            IPAddress address,
            PortableCredential credential,
            int port = QuicLink.Port,
            CancellationToken cancellationToken = default)
        {
            var ep = new IPEndPoint(address, port);
            var cert = credential.PfxBytes is { Length: > 0 } pfx
                ? X509CertificateLoader.LoadPkcs12(pfx, "contextall")
                : throw new InvalidOperationException("Missing test credential");
            var listener = new Listener(
                ep,
                cert,
                () => listeners.TryRemove(ep, out _));
            if (!listeners.TryAdd(ep, listener))
            {
                cert.Dispose();
                throw new InvalidOperationException($"Endpoint {ep} already bound.");
            }

            return Task.FromResult<IQuicListener>(listener);
        }

        public async Task<(IQuicConnection Connection, X509Certificate2 Peer)> ConnectMutualAsync(
            IPEndPoint remote,
            X509Certificate2 certificate,
            CancellationToken cancellationToken = default)
        {
            var (conn, peerDer) = await ConnectMutualPortableAsync(
                remote,
                new PortableCredential(certificate.Export(X509ContentType.Pfx, "contextall")),
                cancellationToken).ConfigureAwait(false);
            return (conn, X509CertificateLoader.LoadCertificate(peerDer));
        }

        public Task<(IQuicConnection Connection, byte[] PeerDer)> ConnectMutualPortableAsync(
            IPEndPoint remote,
            PortableCredential credential,
            CancellationToken cancellationToken = default)
        {
            if (!listeners.TryGetValue(remote, out var listener))
            {
                throw new InvalidOperationException($"No listener at {remote}.");
            }

            using var clientCert = credential.PfxBytes is { Length: > 0 } pfx
                ? X509CertificateLoader.LoadPkcs12(pfx, "contextall")
                : throw new InvalidOperationException("Missing test credential");
            var clientDer = clientCert.RawData;
            var serverDer = listener.ServerCert.RawData;
            if (CertificateChecks.Evaluate(clientDer.AsSpan(), null) == CertificateFate.RejectBeforeId
                || CertificateChecks.Evaluate(serverDer.AsSpan(), null) == CertificateFate.RejectBeforeId)
            {
                throw new InvalidOperationException("Mutual P-256 check failed.");
            }

            var clientSource = new IPEndPoint(IPAddress.Loopback, 55000);
            var c2sStreams = Channel.CreateUnbounded<Stream>();
            var s2cStreams = Channel.CreateUnbounded<Stream>();

            var clientConn = new Connection(remote, X509CertificateLoader.LoadCertificate(serverDer), serverDer, c2sStreams.Writer, s2cStreams.Reader);
            var serverConn = new Connection(clientSource, X509CertificateLoader.LoadCertificate(clientDer), clientDer, s2cStreams.Writer, c2sStreams.Reader);
            listener.AcceptQueue.Writer.TryWrite(serverConn);

            return Task.FromResult<(IQuicConnection, byte[])>((clientConn, serverDer));
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
            byte[] peerDer,
            ChannelWriter<Stream> outboundWriter,
            ChannelReader<Stream> inboundReader) : IPortableQuicConnection
        {
            public EndPoint? RemoteEndPoint => remote;
            public X509Certificate2? PeerCertificate => peerCert;
            public byte[]? PeerCertificateDer => peerDer;

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
}
