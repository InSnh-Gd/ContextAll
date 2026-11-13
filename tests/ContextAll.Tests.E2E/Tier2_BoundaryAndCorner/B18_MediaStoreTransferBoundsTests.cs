using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B18")]
public sealed class B18_MediaStoreTransferBoundsTests
{
    [Fact]
    public async Task MediaStore_Rejects_Corrupted_Hash_And_Removes_All_Partials()
    {
        await using var harness = new InProcessServiceHarness();
        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "corrupted.part");
        var metaFile = Path.Combine(harness.Paths.NoBackupFilesDir, "corrupted.meta.json");

        await File.WriteAllTextAsync(tempFile, "real data");
        await File.WriteAllTextAsync(metaFile, "{}");

        const string badHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TransferSink.CommitInboundFileAsync(tempFile, "target.txt", badHash));

        Assert.False(File.Exists(tempFile));
        Assert.False(File.Exists(metaFile));
    }

    [Fact]
    public async Task MediaStore_Commit_Replaces_Reserved_Windows_Names()
    {
        await using var harness = new InProcessServiceHarness();
        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "aux.part");
        var content = "aux content"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));

        var path = await harness.TransferSink.CommitInboundFileAsync(tempFile, "AUX.txt", sha);
        Assert.EndsWith("download", path, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaStore_Rejects_Transfers_Over_1GB()
    {
        Assert.Equal(1_000_000_000L, TransferBook.MaxFileBytes);

        var book = new TransferBook();
        book.Ready();

        const string transferId = "0123456789abcdef0123456789abcdef";
        const string hexSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var overLimitOffer = new FileOfferPayload(
            transferId,
            2,
            TransferBook.MaxFileBytes + 1,
            TransferBook.SegmentSize,
            (TransferBook.MaxFileBytes + 1) / TransferBook.SegmentSize + 1,
            hexSha,
            "huge.iso");

        var rejectReason = book.ReceiveOffer(overLimitOffer, "peer1", false, 1000);
        Assert.Equal("invalid", rejectReason);
        Assert.Single(book.Frames);
        Assert.Equal(FrameTypes.FileReject, book.Frames[0].Type);
    }

    [Fact]
    public async Task MediaStore_Commit_Throws_When_Source_Is_Missing()
    {
        await using var harness = new InProcessServiceHarness();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            harness.TransferSink.CommitInboundFileAsync("/nonexistent/file.part", "file.bin", "sha"));
    }

    [Fact]
    public async Task MediaStore_Multiple_Sequential_Commits_Succeed()
    {
        await using var harness = new InProcessServiceHarness();

        for (var i = 0; i < 3; i++)
        {
            var temp = Path.Combine(harness.Paths.NoBackupFilesDir, $"t_{i}.part");
            var data = new byte[] { (byte)i };
            await File.WriteAllBytesAsync(temp, data);
            var sha = Convert.ToHexStringLower(SHA256.HashData(data));

            var committed = await harness.TransferSink.CommitInboundFileAsync(temp, $"file_{i}.bin", sha);
            Assert.True(File.Exists(committed));
            Assert.True(harness.TransferSink.VerifyChecksum(committed, sha));
        }
    }
}
