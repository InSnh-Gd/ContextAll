using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Effects;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F22")]
public sealed class F22_CrossPlatformIntegrationTests
{
    [Fact]
    public void CrossPlatform_Discovery_Beacon_Digest_Is_16Bytes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rawKey = key.ExportSubjectPublicKeyInfo();
        var digest = DeviceIds.Digest(rawKey);
        Assert.Equal(16, digest.Length);
    }

    [Fact]
    public void CrossPlatform_ShortCode_Generates_6_Base32_Chars()
    {
        using var key1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var key2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki1 = key1.ExportSubjectPublicKeyInfo();
        var spki2 = key2.ExportSubjectPublicKeyInfo();

        var code = DeviceIds.ShortCode(spki1, spki2);
        Assert.Equal(6, code.Length);
        Assert.True(code.All(c => "abcdefghijklmnopqrstuvwxyz234567".Contains(c)));
    }

    [Fact]
    public void CrossPlatform_Quic_Parameters_Match_Both_Platforms()
    {
        Assert.Equal(42461, QuicLink.Port);
        Assert.Equal("contextall", QuicLink.Alpn);
    }

    [Fact]
    public void CrossPlatform_FileTransfer_SegmentSize_Is_65500_Bytes()
    {
        Assert.Equal(65500, TransferBook.SegmentSize);
        Assert.True(TransferBook.SegmentSize <= FrameLimits.MaxPayloadBytes);
    }

    [Fact]
    public void CrossPlatform_SlidingWindow_Is_8_Segments()
    {
        var book = new TransferBook();
        book.Ready();
        var slot = book.SeedOutbound("peer-dest", 16);
        Assert.NotNull(slot);
        book.Release("peer-dest");

        // Max frames pumped in sliding window is bounded to 8
        Assert.Equal(8, book.Frames.Count);
        foreach (var frame in book.Frames)
        {
            Assert.Equal(FrameTypes.FileSegment, frame.Type);
            Assert.Equal(3, frame.Priority);
        }
    }
}
