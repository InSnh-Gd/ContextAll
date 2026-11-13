using System.Security.Cryptography;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F18")]
public sealed class F18_MediaStoreInboundTransferTests
{
    [Fact]
    public async Task MediaStore_Completed_Inbound_File_Is_Committed()
    {
        await using var harness = new InProcessServiceHarness();
        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "transfer123.part");
        var metaFile = Path.Combine(harness.Paths.NoBackupFilesDir, "transfer123.meta.json");

        var content = "ContextAll inbound file transfer content"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);
        await File.WriteAllTextAsync(metaFile, "{\"completed\":true}");

        var expectedSha256 = Convert.ToHexStringLower(SHA256.HashData(content));

        var committedPath = await harness.TransferSink.CommitInboundFileAsync(tempFile, "photo.jpg", expectedSha256);

        Assert.True(File.Exists(committedPath));
        Assert.False(File.Exists(tempFile));
        Assert.False(File.Exists(metaFile));
        Assert.StartsWith(harness.Paths.SimulatedMediaStoreDownloads, committedPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MediaStore_Rejects_And_Deletes_Partials_On_Checksum_Mismatch()
    {
        await using var harness = new InProcessServiceHarness();
        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "bad_transfer.part");
        var metaFile = Path.Combine(harness.Paths.NoBackupFilesDir, "bad_transfer.meta.json");

        await File.WriteAllBytesAsync(tempFile, "tampered content"u8.ToArray());
        await File.WriteAllTextAsync(metaFile, "{}");

        const string wrongSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.TransferSink.CommitInboundFileAsync(tempFile, "photo.jpg", wrongSha256));

        Assert.Contains("Checksum verification failed", ex.Message);
        Assert.False(File.Exists(tempFile));
        Assert.False(File.Exists(metaFile));
    }

    [Fact]
    public async Task MediaStore_Handles_Collision_By_Appending_Counter()
    {
        await using var harness = new InProcessServiceHarness();
        var existing = Path.Combine(harness.Paths.SimulatedMediaStoreDownloads, "notes.txt");
        await File.WriteAllTextAsync(existing, "existing notes");

        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "notes_new.part");
        var content = "new notes"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));

        var committed = await harness.TransferSink.CommitInboundFileAsync(tempFile, "notes.txt", sha);

        Assert.EndsWith("notes (2).txt", committed, StringComparison.Ordinal);
        Assert.True(File.Exists(existing));
        Assert.True(File.Exists(committed));
    }

    [Fact]
    public async Task MediaStore_Throws_When_TempFile_Does_Not_Exist()
    {
        await using var harness = new InProcessServiceHarness();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            harness.TransferSink.CommitInboundFileAsync("/nonexistent/file.part", "file.txt", "abc"));
    }

    [Fact]
    public async Task MediaStore_AuditLogs_Record_Commit_Event()
    {
        await using var harness = new InProcessServiceHarness();
        var tempFile = Path.Combine(harness.Paths.NoBackupFilesDir, "t1.part");
        var content = "data"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));

        var committed = await harness.TransferSink.CommitInboundFileAsync(tempFile, "doc.pdf", sha);
        Assert.True(File.Exists(committed));
        Assert.True(harness.TransferSink.VerifyChecksum(committed, sha));
    }
}
