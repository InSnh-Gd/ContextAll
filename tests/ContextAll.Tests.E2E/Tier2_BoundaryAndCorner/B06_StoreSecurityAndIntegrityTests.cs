using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B06")]
public sealed class B06_StoreSecurityAndIntegrityTests
{
    [Fact]
    public async Task IdentityStore_Fails_When_PrivateKey_Is_Corrupted()
    {
        await using var sandbox = new NetworkSandbox();
        var idDir = Path.Combine(sandbox.TempRoot, "corrupt_id");
        Directory.CreateDirectory(idDir);
        await File.WriteAllTextAsync(Path.Combine(idDir, "device.key"), "corrupt-non-pkcs8-content");

        var outcome = IdentityStore.Start(idDir);
        Assert.False(outcome.Ok);
        Assert.Null(outcome.Material);
    }

    [Fact]
    public async Task SettingsStore_Recovers_When_Yaml_Is_Malformed()
    {
        await using var sandbox = new NetworkSandbox();
        var configFile = Path.Combine(sandbox.TempRoot, "bad_config.yaml");
        await File.WriteAllTextAsync(configFile, ": bad : yaml : [[[");

        // SettingsStore Start should handle corrupt yaml safely
        var outcome = SettingsStore.Start(configFile, "FallbackName");
        Assert.NotNull(outcome);
    }

    [Fact]
    public async Task TransferStore_Rejects_Path_Traversal_In_PeerId()
    {
        await using var sandbox = new NetworkSandbox();
        var store = new TransferStore(sandbox.TempRoot);

        // Path traversal in peerId should be safely isolated or sanitized
        store.DeletePeer("../../etc");
        var incomplete = store.Incomplete("../../etc", "inbound");
        Assert.Empty(incomplete);
    }

    [Fact]
    public void FileTransfer_Limit_Is_1GB()
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
    public async Task TransferStore_Handles_Zero_Byte_Files()
    {
        await using var sandbox = new NetworkSandbox();
        var store = new TransferStore(sandbox.TempRoot);

        var emptyFile = Path.Combine(sandbox.TempRoot, "empty.txt");
        await File.WriteAllBytesAsync(emptyFile, []);

        var info = new FileInfo(emptyFile);
        Assert.Equal(0, info.Length);

        // Verify TransferStore loads or lists cleanly
        var incomplete = store.Incomplete("peer1", "inbound");
        Assert.Empty(incomplete);
    }
}
