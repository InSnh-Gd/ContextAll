using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier4_RealWorldScenarios;

[Trait("Category", "Tier4")]
[Trait("Feature", "Scenario1_DesktopToAndroidPairing")]
public sealed class S01_DesktopToAndroidPairingScenarioTests
{
    [Fact]
    public async Task Scenario1_Full_Desktop_To_Android_Pairing_Flow()
    {
        // 1. Generate real ECDSA P-256 certificates for both endpoints
        using var desktopCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=Desktop");
        using var androidCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=Android");

        var desktopSpki = CertificateChecks.LeafSpki(desktopCert)!;
        var androidSpki = CertificateChecks.LeafSpki(androidCert)!;
        var desktopId = DeviceIds.Text(desktopSpki);
        var androidId = DeviceIds.Text(androidSpki);

        // Verify mutual certificates satisfy production cryptographic rules
        Assert.True(CertificateChecks.IsP256(desktopCert));
        Assert.True(CertificateChecks.IsP256(androidCert));

        // 2. Initialize genuine protocol pairing state machines
        var desktopSession = PairingSession.Open(desktopSpki, androidSpki, desktopSpki, "Linux Workstation");
        var androidSession = PairingSession.Open(androidSpki, desktopSpki, desktopSpki, "Android Device");

        // 3. Connect via in-memory duplex stream simulating the transport channel
        var (desktopStream, androidStream) = DuplexStreamPair.Create();
        await using (desktopStream)
        await using (androidStream)
        {
            // 4. Exchange Hello frames over the wire
            desktopSession.OnWritable();
            var helloFrame = Assert.Single(desktopSession.Frames);
            var helloWireBytes = FrameCodec.Encode(helloFrame.Payload, helloFrame.Type, helloFrame.Priority, helloFrame.Flags, helloFrame.StreamId);
            Assert.False(helloWireBytes.IsFailure);

            // Write to desktop stream, read from android stream
            await desktopStream.WriteAsync(helloWireBytes.Value!);
            var receivedFrameResult = await FrameStreamReader.ReadFrameAsync(androidStream);
            Assert.False(receivedFrameResult.IsFailure);
            var decodedHello = ControlJson.Decode(receivedFrameResult.Value!.Payload.Span, ProtocolJsonContext.Default.HelloPayload);
            Assert.False(decodedHello.IsFailure);
            androidSession.ReceiveHello(decodedHello.Value!.DisplayName);

            // Android emits Hello back to desktop
            androidSession.OnWritable();
            var androidHelloFrame = Assert.Single(androidSession.Frames);
            var androidWireBytes = FrameCodec.Encode(androidHelloFrame.Payload, androidHelloFrame.Type, androidHelloFrame.Priority, androidHelloFrame.Flags, androidHelloFrame.StreamId);
            await androidStream.WriteAsync(androidWireBytes.Value!);
            var desktopReceivedResult = await FrameStreamReader.ReadFrameAsync(desktopStream);
            Assert.False(desktopReceivedResult.IsFailure);
            var desktopDecodedHello = ControlJson.Decode(desktopReceivedResult.Value!.Payload.Span, ProtocolJsonContext.Default.HelloPayload);
            Assert.False(desktopDecodedHello.IsFailure);
            desktopSession.ReceiveHello(desktopDecodedHello.Value!.DisplayName);

            // 5. Symmetric Short Code verification
            desktopSession.AdoptIfOnly();
            androidSession.AdoptIfOnly();

            Assert.NotNull(desktopSession.ShortCode);
            Assert.NotNull(androidSession.ShortCode);
            Assert.Equal(desktopSession.ShortCode, androidSession.ShortCode);
            Assert.Equal(6, desktopSession.ShortCode.Length);

            // 6. Mutual Acceptance over the wire
            androidSession.AcceptLocal();
            var acceptWire = FrameCodec.Encode(ReadOnlyMemory<byte>.Empty, FrameTypes.PairAccept, 0, 0, FrameTypes.SessionStream);
            Assert.False(acceptWire.IsFailure);
            await androidStream.WriteAsync(acceptWire.Value!);

            var acceptRead = await FrameStreamReader.ReadFrameAsync(desktopStream);
            Assert.False(acceptRead.IsFailure);
            Assert.Equal(FrameTypes.PairAccept, acceptRead.Value!.Type);
            desktopSession.AcceptRemote();

            desktopSession.AcceptLocal();
            var desktopAcceptWire = FrameCodec.Encode(ReadOnlyMemory<byte>.Empty, FrameTypes.PairAccept, 0, 0, FrameTypes.SessionStream);
            Assert.False(desktopAcceptWire.IsFailure);
            await desktopStream.WriteAsync(desktopAcceptWire.Value!);

            var androidAcceptRead = await FrameStreamReader.ReadFrameAsync(androidStream);
            Assert.False(androidAcceptRead.IsFailure);
            Assert.Equal(FrameTypes.PairAccept, androidAcceptRead.Value!.Type);
            androidSession.AcceptRemote();

            // 7. Verify mutual trust established and identity persisted to disk
            Assert.True(desktopSession.Trusted);
            Assert.True(androidSession.Trusted);

            var trustEntry = Assert.Single(androidSession.Trust);
            Assert.Equal(desktopId, trustEntry.DeviceId);
            Assert.Equal(Convert.ToBase64String(desktopSpki), trustEntry.SpkiBase64);

            // Verify certificate SPKI fingerprint matches stored trust
            var fate = CertificateChecks.Evaluate(desktopCert, trustEntry);
            Assert.Equal(CertificateFate.Continue, fate);
        }
    }
}
