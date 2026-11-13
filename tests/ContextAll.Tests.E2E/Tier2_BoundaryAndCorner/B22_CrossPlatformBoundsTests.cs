using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B22")]
public sealed class B22_CrossPlatformBoundsTests
{
    [Fact]
    public void Certificate_Changed_Spki_Returns_CloseKeepTrust()
    {
        using var certA = OpaqueQuicClient.GenerateValidP256Certificate();
        using var certB = OpaqueQuicClient.GenerateValidP256Certificate();

        var spkiA = Convert.ToBase64String(CertificateChecks.LeafSpki(certA)!);
        var trustedEntry = new TrustEntry("ca1testdevice123456789012345", "Alice", spkiA, true, false);

        // Peer B presents certB which has a different SPKI than trustedEntry
        var fate = CertificateChecks.Evaluate(certB, trustedEntry);
        Assert.Equal(CertificateFate.CloseKeepTrust, fate);
    }

    [Fact]
    public void Certificate_Matching_Spki_Returns_Continue()
    {
        using var certA = OpaqueQuicClient.GenerateValidP256Certificate();
        var spkiA = Convert.ToBase64String(CertificateChecks.LeafSpki(certA)!);
        var trustedEntry = new TrustEntry("ca1testdevice123456789012345", "Alice", spkiA, true, false);

        var fate = CertificateChecks.Evaluate(certA, trustedEntry);
        Assert.Equal(CertificateFate.Continue, fate);
    }

    [Fact]
    public void Beacon_Encodes_And_Decodes_Chinese_And_Emoji_DisplayNames()
    {
        var digest = new byte[16];
        const string unicodeName = "测试手机 🚀📱";

        var outcome = BeaconCodec.Encode(digest, 42461, unicodeName);
        Assert.False(outcome.IsFailure);

        var decoded = BeaconCodec.Decode(outcome.Value!);
        Assert.False(decoded.IsFailure);
        Assert.Equal(unicodeName, decoded.Value!.Name);
    }

    [Fact]
    public void Transfer_Segment_Offset_Calculation_Is_Exact()
    {
        const int segmentIndex = 320;
        Assert.Equal(65500, TransferBook.SegmentSize);
        var offset = (long)segmentIndex * TransferBook.SegmentSize;
        Assert.Equal((long)segmentIndex * (FrameLimits.MaxPayloadBytes - 36), offset);
    }

    [Fact]
    public void Transfer_Bitmask_Tracks_Received_Segments()
    {
        var book = new TransferBook();
        book.Ready();

        const string transferId = "0123456789abcdef0123456789abcdef";
        const string hexSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var offer = new FileOfferPayload(transferId, 2, TransferBook.SegmentSize * 2, TransferBook.SegmentSize, 2, hexSha, "f.bin");
        book.ReceiveOffer(offer, "peer", false, 1000);

        var inSlot = book.Inbound("peer");
        Assert.NotNull(inSlot);
        Assert.Equal(0, inSlot.Completed);

        var segmentData = new byte[TransferBook.SegmentSize];
        var hash = SHA256.HashData(segmentData);
        book.ReceiveSegment("peer", 0, hash, segmentData, null);

        Assert.Equal(1, inSlot.Completed);
    }
}
