using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier3_Combinations;

[Trait("Category", "Tier3")]
[Trait("Feature", "Combination_Transport_MediaStore")]
public sealed class TransportAndMediaStoreCombinationTests
{
    [Fact]
    public async Task Inbound_FileTransfer_Stages_In_NoBackup_And_Commits_To_MediaStore()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        // 1. Data arrives over QUIC bulk stream and stages in NoBackupFilesDir
        var transferId = "xfer_" + Guid.NewGuid().ToString("N");
        var tempPart = Path.Combine(harness.Paths.TransfersDirectory, transferId + ".part");
        var tempMeta = Path.Combine(harness.Paths.TransfersDirectory, transferId + ".meta.json");

        var payload = "Important document contents transferred over QUIC"u8.ToArray();
        await File.WriteAllBytesAsync(tempPart, payload);
        await File.WriteAllTextAsync(tempMeta, "{\"transferId\":\"" + transferId + "\"}");

        Assert.True(File.Exists(tempPart));
        Assert.True(File.Exists(tempMeta));

        // 2. All segments received; commit to MediaStore.Downloads
        var rootHash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var publicFile = await harness.TransferSink.CommitInboundFileAsync(tempPart, "report.pdf", rootHash);

        // 3. Staging files must be removed, committed file must exist in MediaStore
        Assert.True(File.Exists(publicFile));
        Assert.False(File.Exists(tempPart));
        Assert.False(File.Exists(tempMeta));
        Assert.StartsWith(harness.Paths.SimulatedMediaStoreDownloads, publicFile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlidingWindow_With_8_Segments_Respects_Chunk_Limits()
    {
        var book = new TransferBook();
        book.Ready();

        const int totalSegments = 16;
        var slot = book.SeedOutbound("peer-receiver", totalSegments);
        Assert.NotNull(slot);

        book.Release("peer-receiver");

        // Sliding window pumps exactly 8 frames (the window capacity)
        Assert.Equal(8, book.Frames.Count);
        Assert.Equal(8, slot.Flying.Count);

        foreach (var frame in book.Frames)
        {
            Assert.Equal(FrameTypes.FileSegment, frame.Type);
            Assert.Equal(3, frame.Priority);
            Assert.True(frame.Payload.Length <= FrameLimits.MaxPayloadBytes);
        }

        // Acknowledging segment 0 advances sliding window and releases segment 8
        book.Ack(slot.TransferId, 0);
        Assert.Single(book.Frames);
        Assert.Equal(8, slot.Flying.Count);
    }

    [Fact]
    public async Task Transfer_Interrupted_Midway_Preserves_Partials_For_Resumption()
    {
        await using var harness = new InProcessServiceHarness();
        var store = new TransferStore(harness.Paths.TransfersDirectory);

        const int segmentSize = TransferBook.SegmentSize;
        const int totalSegments = 10;
        var totalBytes = segmentSize * totalSegments;

        var transferId = Guid.NewGuid().ToString("N");
        var hexSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var book = new TransferBook();
        book.Ready();
        var offer = new FileOfferPayload(transferId, 2, totalBytes, segmentSize, totalSegments, hexSha, "archive.bin");
        book.ReceiveOffer(offer, "peer1", false, 1000);

        var slot = book.Inbound("peer1");
        Assert.NotNull(slot);
        store.Create(slot, null);

        // Write 4 segments
        for (uint i = 0; i < 4; i++)
        {
            var data = new byte[segmentSize];
            book.ReceiveSegment("peer1", i, SHA256.HashData(data), data, null);
            store.Write(transferId, i * segmentSize, data);
            store.Save(slot);
        }

        Assert.Equal(4, slot.Completed);

        // Process restart: load from fresh TransferStore instance
        var freshStore = new TransferStore(harness.Paths.TransfersDirectory);
        var restoredSlot = freshStore.Load(transferId);

        Assert.NotNull(restoredSlot);
        Assert.Equal(transferId, restoredSlot.TransferId);
        Assert.Equal(4, restoredSlot.Completed);
        Assert.Equal(segmentSize, restoredSlot.SegmentSize);
        Assert.Equal(totalBytes, restoredSlot.TotalSize);
        Assert.Equal(hexSha, restoredSlot.RootSha256);
    }
}
