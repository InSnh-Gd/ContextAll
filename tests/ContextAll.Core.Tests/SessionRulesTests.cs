using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class SessionRulesTests
{
    static readonly byte[] Spki = Enumerable.Range(0, 91).Select(i => (byte)i).ToArray();
    static readonly byte[] SpkiGreater = Enumerable.Range(0, 80).Select(i => (byte)((i * 3) % 256)).ToArray();

    [Fact]
    public void A_malformed_frame_closes_the_session_without_changing_trust()
    {
        var trust = ImmutableArray.Create(new TrustEntry("ca1example", "desk", Convert.ToBase64String(Spki), false, true));
        var state = new ChannelState(true, trust);
        var malformed = new byte[FrameLimits.HeaderBytes];
        malformed[15] = 1;

        var thrown = Record.Exception(() => ChannelRules.OnQuicFrame(state, malformed));
        var next = ChannelRules.OnQuicFrame(state, malformed);

        Assert.Null(thrown);
        Assert.False(next.Open);
        Assert.Equal(trust, next.Trust);
        Assert.Equal("ca1example", next.Trust[0].DeviceId);
    }

    [Fact]
    public void Device_id_rejects_a_non_p256_leaf_and_keeps_trust_when_spki_bytes_differ()
    {
        Assert.Equal("ca1ldjjiwnscmfc4fisklkarok6nu", DeviceIds.Text(Spki));

        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var first = Certificate(p256, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
        var second = Certificate(p256, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var spki = p256.ExportSubjectPublicKeyInfo();
        Assert.Equal(DeviceIds.Text(spki), DeviceIds.Text(CertificateChecks.SpkiOfLeaf(first).Match(value => value, _ => [])));
        Assert.Equal(DeviceIds.Text(CertificateChecks.SpkiOfLeaf(first).Match(value => value, _ => [])), DeviceIds.Text(CertificateChecks.SpkiOfLeaf(second).Match(value => value, _ => [])));

        using var rsa = RSA.Create(2048);
        var foreign = new CertificateRequest("CN=foreign", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));
        var trust = new TrustEntry(DeviceIds.Text(spki), "desk", Convert.ToBase64String(spki), false, true);
        Assert.Equal(CertificateFate.RejectBeforeId, CertificateChecks.Evaluate(foreign, trust));
        Assert.Equal(Convert.ToBase64String(spki), trust.SpkiBase64);

        var expired = Certificate(p256, DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow.AddDays(-1));
        Assert.True(expired.NotAfter.ToUniversalTime() < DateTime.UtcNow);
        Assert.Equal(CertificateFate.Continue, CertificateChecks.Evaluate(expired, trust));

        var collided = trust with { SpkiBase64 = Convert.ToBase64String(Spki) };
        Assert.Equal(CertificateFate.CloseKeepTrust, CertificateChecks.Evaluate(expired, collided));
        Assert.Equal(Convert.ToBase64String(Spki), collided.SpkiBase64);
    }

    [Fact]
    public void Pairing_keeps_the_session_opened_by_the_greater_digest_and_hides_the_short_code()
    {
        var low = PairingSession.Open(Spki, SpkiGreater, Spki, "low");
        var high = PairingSession.Open(SpkiGreater, Spki, SpkiGreater, "high");
        low.OnWritable();
        high.OnWritable();

        Assert.False(low.RemoteHelloSeen);
        Assert.False(high.RemoteHelloSeen);
        Assert.Contains(low.Frames, frame => frame.Type == FrameTypes.Hello);
        Assert.Contains(high.Frames, frame => frame.Type == FrameTypes.Hello);

        low.ReceiveHello("high");
        high.ReceiveHello("low");
        PairingSession.Deduplicate(low, high);

        Assert.True(high.IsSurvivor);
        Assert.False(low.IsSurvivor);
        Assert.Equal("lhmshi", high.ShortCode);
        Assert.Null(low.ShortCode);
        Assert.Contains(low.Frames, frame => frame.Type == FrameTypes.SessionDuplicateClose);
        Assert.DoesNotContain(Encoded(low), text => text.Contains("lhmshi", StringComparison.Ordinal));
        Assert.DoesNotContain(Encoded(high), text => text.Contains("lhmshi", StringComparison.Ordinal));

        high.OnClock(60_000);
        Assert.True(high.Closed);
        Assert.Empty(high.Trust);

        var rejected = PairingSession.Open(SpkiGreater, Spki, SpkiGreater, "high");
        rejected.OnWritable();
        rejected.ReceiveHello("low");
        var other = PairingSession.Open(Spki, SpkiGreater, Spki, "low");
        other.OnWritable();
        other.ReceiveHello("high");
        PairingSession.Deduplicate(rejected, other);
        rejected.Reject();
        Assert.True(rejected.Closed);
        Assert.Empty(rejected.Trust);
    }

    [Fact]
    public void Trust_is_stored_only_after_both_accepts_and_a_rename_is_not_a_second_hello()
    {
        var session = Survivor();
        session.Rename("renamed");

        Assert.Equal(1, session.Frames.Count(frame => frame.Type == FrameTypes.Hello));
        Assert.False(session.TryAnnounce(["10.0.0.8"]));
        Assert.DoesNotContain(session.Frames, frame => frame.Type == FrameTypes.PathAnnounce);

        session.AcceptLocal();
        Assert.Empty(session.Trust);
        session.AcceptRemote();

        Assert.Single(session.Trust);
        Assert.False(session.Trust[0].ClipboardSync);
        Assert.True(session.Trust[0].AskBeforeSave);
        Assert.Contains(session.Frames, frame => frame.Type == FrameTypes.DisplayName);
        Assert.Equal(1, session.Frames.Count(frame => frame.Type == FrameTypes.Hello));
        Assert.True(session.TryAnnounce(["10.0.0.8"]));
        Assert.Contains(session.Frames, frame => frame.Type == FrameTypes.PathAnnounce);
    }

    static PairingSession Survivor()
    {
        var session = PairingSession.Open(SpkiGreater, Spki, SpkiGreater, "high");
        var other = PairingSession.Open(Spki, SpkiGreater, Spki, "low");
        session.OnWritable();
        other.OnWritable();
        session.ReceiveHello("low");
        other.ReceiveHello("high");
        PairingSession.Deduplicate(session, other);
        return session;
    }

    static IEnumerable<string> Encoded(PairingSession session) =>
        session.Frames.Select(frame => Encoding.UTF8.GetString(frame.Payload.Span));

    static X509Certificate2 Certificate(ECDsa key, DateTimeOffset from, DateTimeOffset to) =>
        new CertificateRequest("CN=contextall", key, HashAlgorithmName.SHA256).CreateSelfSigned(from, to);
}
