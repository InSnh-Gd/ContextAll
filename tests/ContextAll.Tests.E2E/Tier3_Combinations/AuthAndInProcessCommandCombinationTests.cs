using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier3_Combinations;

[Trait("Category", "Tier3")]
[Trait("Feature", "Combination_Auth_InProcessCommands")]
public sealed class AuthAndInProcessCommandCombinationTests
{
    [Fact]
    public async Task Pairing_Prompt_Reaches_Ui_And_Accept_Commits_Trust()
    {
        // Genuine mutual pairing state machine execution
        using var desktopCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=Desktop");
        using var androidCert = OpaqueQuicClient.GenerateValidP256Certificate("CN=Android");

        var desktopSpki = CertificateChecks.LeafSpki(desktopCert)!;
        var androidSpki = CertificateChecks.LeafSpki(androidCert)!;
        var desktopId = DeviceIds.Text(desktopSpki);
        var androidId = DeviceIds.Text(androidSpki);

        var desktopSession = PairingSession.Open(desktopSpki, androidSpki, desktopSpki, "Linux Workstation");
        var androidSession = PairingSession.Open(androidSpki, desktopSpki, desktopSpki, "Android Device");

        desktopSession.OnWritable();
        androidSession.OnWritable();
        var hello = Assert.Single(desktopSession.Frames);
        Assert.Equal(FrameTypes.Hello, hello.Type);

        androidSession.ReceiveHello("Linux Workstation");
        desktopSession.ReceiveHello("Android Device");
        androidSession.AdoptIfOnly();
        desktopSession.AdoptIfOnly();

        Assert.Equal(desktopSession.ShortCode, androidSession.ShortCode);

        // UI operator accepts locally
        androidSession.AcceptLocal();
        desktopSession.AcceptRemote();

        desktopSession.AcceptLocal();
        androidSession.AcceptRemote();

        Assert.True(desktopSession.Trusted);
        Assert.True(androidSession.Trusted);

        var trust = Assert.Single(androidSession.Trust);
        Assert.Equal(desktopId, trust.DeviceId);

        var fate = CertificateChecks.Evaluate(desktopCert, trust);
        Assert.Equal(CertificateFate.Continue, fate);
    }

    [Fact]
    public void Pairing_Reject_Closes_Session_Without_Trust()
    {
        using var desktopKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var androidKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var desktopSpki = desktopKey.ExportSubjectPublicKeyInfo();
        var androidSpki = androidKey.ExportSubjectPublicKeyInfo();

        var androidSession = PairingSession.Open(androidSpki, desktopSpki, desktopSpki, "Android Device");
        androidSession.ReceiveHello("Untrusted Node");
        androidSession.AdoptIfOnly();

        androidSession.Reject();

        Assert.True(androidSession.Closed);
        Assert.False(androidSession.Trusted);
        Assert.Empty(androidSession.Trust);

        var rejectFrame = Assert.Single(androidSession.Frames);
        Assert.Equal(FrameTypes.PairReject, rejectFrame.Type);
    }

    [Fact]
    public void Mutual_Pairing_Generates_Identical_Short_Codes()
    {
        using var devA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var devB = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var spkiA = devA.ExportSubjectPublicKeyInfo();
        var spkiB = devB.ExportSubjectPublicKeyInfo();

        // 1. Cross-dialing competing sessions
        var session1 = PairingSession.Open(spkiA, spkiB, spkiA, "Node A");
        var session2 = PairingSession.Open(spkiA, spkiB, spkiB, "Node A");

        session1.OnWritable();
        session2.OnWritable();
        session1.ReceiveHello("Node B");
        session2.ReceiveHello("Node B");

        PairingSession.Deduplicate(session1, session2);

        // Exactly one session survives
        Assert.True(session1.IsSurvivor ^ session2.IsSurvivor);
        var survivor = session1.IsSurvivor ? session1 : session2;
        var dropped = session1.IsSurvivor ? session2 : session1;

        Assert.True(dropped.Closed);
        Assert.NotNull(survivor.ShortCode);
        Assert.Equal(6, survivor.ShortCode.Length);

        // Symmetric check
        var directCode = DeviceIds.ShortCode(spkiA, spkiB);
        Assert.Equal(directCode, survivor.ShortCode);
    }

    [Fact]
    public void Certificate_SPKI_Mismatch_Terminates_Pairing()
    {
        using var originalCert = OpaqueQuicClient.GenerateValidP256Certificate();
        using var tamperedCert = OpaqueQuicClient.GenerateValidP256Certificate();

        var spkiA = Convert.ToBase64String(CertificateChecks.LeafSpki(originalCert)!);
        var entry = new TrustEntry("ca1peer", "Peer", spkiA, true, false);

        var fate = CertificateChecks.Evaluate(tamperedCert, entry);
        Assert.Equal(CertificateFate.CloseKeepTrust, fate);
    }
}
