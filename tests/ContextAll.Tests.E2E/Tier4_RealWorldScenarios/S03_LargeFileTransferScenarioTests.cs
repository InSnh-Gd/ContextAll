using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier4_RealWorldScenarios;

[Trait("Category", "Tier4")]
[Trait("Feature", "Scenario3_LargeFileTransferWindowing")]
public sealed class S03_LargeFileTransferScenarioTests
{
    [Fact]
    public async Task Scenario3_Sliding_Window_Flow_Control_And_Verification()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "s03_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            // 1. Generate real 4,192,000-byte test file (64 segments of 65,500 bytes)
            const int segmentSize = TransferBook.SegmentSize;
            const int segmentCount = 64;
            var totalBytes = segmentSize * segmentCount;

            var sourceData = new byte[totalBytes];
            Random.Shared.NextBytes(sourceData);
            var expectedRootSha256 = Convert.ToHexStringLower(SHA256.HashData(sourceData));

            var sinkPath = Path.Combine(testDir, "received_large_file.iso");
            var (senderStream, receiverStream) = DuplexStreamPair.Create();

            // 2. Sliding window coordinator
            const int windowCapacity = 8;
            var inFlight = new SemaphoreSlim(windowCapacity, windowCapacity);
            var acksReceived = 0;

            // 3. Launch Receiver Pipeline
            var receiverTask = Task.Run(async () =>
            {
                await using var fs = new FileStream(sinkPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
                for (var i = 0; i < segmentCount; i++)
                {
                    var frameResult = await FrameStreamReader.ReadFrameAsync(receiverStream);
                    Assert.False(frameResult.IsFailure);
                    var frame = frameResult.Value!;

                    Assert.Equal(FrameTypes.FileSegment, frame.Type);
                    Assert.Equal(3, frame.Priority);

                    // Extract 4-byte segment index + payload
                    var segIndex = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.Payload.Span[..4]);

                    // Verify chunk length and write to exact disk offset
                    fs.Position = (long)segIndex * segmentSize;
                    await fs.WriteAsync(frame.Payload[4..]);

                    // Emit FileAck back to sender
                    var ackPayload = new byte[4];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(ackPayload, segIndex);
                    var ackWire = FrameCodec.Encode(ackPayload, FrameTypes.FileAck, 2, 0, frame.StreamId);
                    Assert.False(ackWire.IsFailure);
                    await receiverStream.WriteAsync(ackWire.Value!);
                }
                await fs.FlushAsync();
            });

            // 4. Launch Sender Pipeline with Windowing
            var senderTask = Task.Run(async () =>
            {
                // Background ACK reader
                var ackReader = Task.Run(async () =>
                {
                    while (acksReceived < segmentCount)
                    {
                        var ackFrameResult = await FrameStreamReader.ReadFrameAsync(senderStream);
                        Assert.False(ackFrameResult.IsFailure);
                        Assert.Equal(FrameTypes.FileAck, ackFrameResult.Value!.Type);
                        Interlocked.Increment(ref acksReceived);
                        inFlight.Release();
                    }
                });

                for (uint seg = 0; seg < segmentCount; seg++)
                {
                    await inFlight.WaitAsync(); // Block if windowCapacity frames are in flight

                    var chunkMemory = sourceData.AsMemory((int)(seg * segmentSize), segmentSize);
                    var framePayload = new byte[4 + segmentSize];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(framePayload.AsSpan(0, 4), seg);
                    chunkMemory.CopyTo(framePayload.AsMemory(4));

                    var encoded = FrameCodec.Encode(framePayload, FrameTypes.FileSegment, 3, 0, 2);
                    Assert.False(encoded.IsFailure);
                    await senderStream.WriteAsync(encoded.Value!);
                }

                await ackReader;
            });

            await Task.WhenAll(senderTask, receiverTask);

            // 5. Cryptographic hash verification of destination file
            Assert.Equal(segmentCount, acksReceived);
            Assert.True(File.Exists(sinkPath));

            var sinkBytes = await File.ReadAllBytesAsync(sinkPath);
            Assert.Equal(totalBytes, sinkBytes.Length);
            var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(sinkBytes));

            Assert.Equal(expectedRootSha256, actualSha256);
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
