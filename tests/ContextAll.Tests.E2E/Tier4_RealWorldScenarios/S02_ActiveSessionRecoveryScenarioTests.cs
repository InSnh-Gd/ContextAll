using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Tier4_RealWorldScenarios;

[Trait("Category", "Tier4")]
[Trait("Feature", "Scenario2_ActiveSessionRecovery")]
public sealed class S02_ActiveSessionRecoveryScenarioTests
{
    [Fact]
    public async Task Scenario2_Network_Disconnect_And_Partial_File_Resumption()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "s02_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var stagingDir = Path.Combine(testDir, "transfers");
            var downloadsDir = Path.Combine(testDir, "downloads");
            Directory.CreateDirectory(stagingDir);
            Directory.CreateDirectory(downloadsDir);

            var store = new TransferStore(stagingDir);

            const int segmentSize = TransferBook.SegmentSize; // 65,500 bytes
            const int totalSegments = 10;
            var totalBytes = segmentSize * totalSegments;

            var payload = new byte[totalBytes];
            Random.Shared.NextBytes(payload);
            var rootSha256 = Convert.ToHexStringLower(SHA256.HashData(payload));

            var peerId = "ca1receivernode";
            var transferId = Guid.NewGuid().ToString("N");

            var senderBook = new TransferBook();
            var receiverBook = new TransferBook();
            senderBook.Ready();
            receiverBook.Ready();

            // Phase 1: Initial Offer and Accept
            var offer = new FileOfferPayload(transferId, 2, totalBytes, segmentSize, totalSegments, rootSha256, "archive.bin");
            var offerResult = receiverBook.ReceiveOffer(offer, peerId, askBeforeSave: false, nowMs: 1000);
            Assert.Null(offerResult);

            var inSlot = receiverBook.Inbound(peerId);
            Assert.NotNull(inSlot);
            store.Create(inSlot, null);

            // Phase 2: Stream first 5 segments, then simulate transport crash/abort
            for (uint i = 0; i < 5; i++)
            {
                var segmentData = payload.AsMemory((int)(i * segmentSize), segmentSize);
                var hash = SHA256.HashData(segmentData.Span);

                receiverBook.ReceiveSegment(peerId, i, hash, segmentData, null);
                store.Write(transferId, i * segmentSize, segmentData.Span);
                store.Save(inSlot);
            }

            Assert.Equal(5, inSlot.Completed);

            // Transport tear down simulation: stream closed abruptly
            // Partial file on disk retains data
            var partBytes = store.Read(transferId);
            Assert.Equal(totalBytes, partBytes.Length);

            // Phase 3: Reconnection and Resumption Negotiation
            // Reload slot from disk into a fresh TransferBook to verify persistent recovery
            var restoredSlot = store.Load(transferId);
            Assert.NotNull(restoredSlot);
            var newReceiverBook = new TransferBook();
            newReceiverBook.Ready();
            newReceiverBook.RestoreInbound(restoredSlot);

            // Sender receives bitmask from receiver
            var resumeBits = restoredSlot.Bits;
            senderBook.ReceiveBitmap(transferId, 0, resumeBits, last: true);

            var outSlot = senderBook.SeedOutbound(peerId, totalSegments);
            Assert.NotNull(outSlot);
            outSlot.Bits = resumeBits;
            outSlot.BitmapComplete = true;

            // Phase 4: Stream only remaining segments 5..9
            for (uint i = 5; i < 10; i++)
            {
                var segmentData = payload.AsMemory((int)(i * segmentSize), segmentSize);
                var hash = SHA256.HashData(segmentData.Span);

                newReceiverBook.ReceiveSegment(peerId, i, hash, segmentData, null);
                store.Write(transferId, i * segmentSize, segmentData.Span);
                store.Save(restoredSlot);
            }

            // Phase 5: Verify Completion and Commit
            Assert.Equal(totalSegments, restoredSlot.Completed);
            var placed = store.TryPlace(restoredSlot, downloadsDir);
            Assert.True(placed);

            // Verify final file SHA-256 and cleanup of partial files
            var finalFile = Path.Combine(downloadsDir, "archive.bin");
            Assert.True(File.Exists(finalFile));
            var finalHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(finalFile)));
            Assert.Equal(rootSha256, finalHash);

            Assert.False(File.Exists(Path.Combine(stagingDir, transferId + ".part")));
            Assert.False(File.Exists(Path.Combine(stagingDir, transferId + ".meta.json")));
        }
        finally
        {
            try
            {
                Directory.Delete(testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
