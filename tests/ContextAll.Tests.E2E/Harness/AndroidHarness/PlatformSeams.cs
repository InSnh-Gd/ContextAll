using System.Security.Cryptography;
using ContextAll.Core;

namespace ContextAll.Tests.E2E.Harness.AndroidHarness;

public interface IHostPaths
{
    string IdentityDirectory { get; }
    string SettingsFile { get; }
    string TransfersDirectory { get; }
    string DownloadsDirectory { get; }
    string StateDirectory { get; }
}

public interface IClipboardSource : IAsyncDisposable
{
    bool IsSupported { get; }
    ValueTask<string?> ReadTextAsync(CancellationToken ct = default);
    ValueTask WriteTextAsync(string text, CancellationToken ct = default);
}

public interface ITransferSink
{
    Task<string> CommitInboundFileAsync(string tempFilePath, string fileName, string expectedSha256Hex, CancellationToken ct = default);
    bool VerifyChecksum(string filePath, string expectedSha256Hex);
    void CleanupPartial(string tempFilePath);
}

public sealed class AndroidHostPaths : IHostPaths
{
    readonly AndroidPathsFixture paths;

    public AndroidHostPaths(AndroidPathsFixture paths) => this.paths = paths;

    public string IdentityDirectory => paths.IdentityDirectory;
    public string SettingsFile => paths.SettingsFile;
    public string TransfersDirectory => paths.TransfersDirectory;
    public string DownloadsDirectory => paths.SimulatedMediaStoreDownloads;
    public string StateDirectory => Path.Combine(paths.NoBackupFilesDir, "state");
}

public sealed class AndroidDisabledClipboardSource : IClipboardSource
{
    public bool IsSupported => false;
    public ValueTask<string?> ReadTextAsync(CancellationToken ct = default) => ValueTask.FromResult<string?>(null);
    public ValueTask WriteTextAsync(string text, CancellationToken ct = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Genuine implementation of ITransferSink for Android MediaStore inbound transfers.
/// Uses production ContextAll.Core.DownloadNames for sanitization and collision avoidance.
/// Performs full SHA-256 integrity verification and cleans up temporary .part and .meta.json files.
/// </summary>
public sealed class AndroidTransferSink : ITransferSink
{
    public string MediaStoreDownloadsDir { get; }
    public string StagingDir { get; }

    public AndroidTransferSink(string mediaStoreDownloadsDir, string stagingDir)
    {
        MediaStoreDownloadsDir = mediaStoreDownloadsDir;
        StagingDir = stagingDir;
        Directory.CreateDirectory(MediaStoreDownloadsDir);
        Directory.CreateDirectory(StagingDir);
    }

    public async Task<string> CommitInboundFileAsync(
        string tempFilePath,
        string fileName,
        string expectedSha256Hex,
        CancellationToken ct = default)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException("Inbound temporary transfer file not found.", tempFilePath);
        }

        // 1. Verify SHA-256 integrity against expected hex digest
        using (var stream = File.OpenRead(tempFilePath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            var computedHex = Convert.ToHexStringLower(hashBytes);

            if (!string.Equals(computedHex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                CleanupPartial(tempFilePath);
                throw new InvalidOperationException($"Checksum verification failed: expected {expectedSha256Hex}, computed {computedHex}.");
            }
        }

        // 2. Apply PRODUCTION DownloadNames.Sanitize from ContextAll.Core
        var sanitizedName = DownloadNames.Sanitize(fileName);

        // 3. Apply PRODUCTION DownloadNames.AvoidCollision from ContextAll.Core
        var uniqueName = DownloadNames.AvoidCollision(sanitizedName, candidate =>
            File.Exists(Path.Combine(MediaStoreDownloadsDir, candidate)));

        var targetPath = Path.Combine(MediaStoreDownloadsDir, uniqueName);

        // 4. Commit to MediaStore.Downloads (atomic move)
        File.Move(tempFilePath, targetPath);

        // 5. Clean up associated metadata (.meta.json) and partial files
        CleanupPartial(tempFilePath);

        return targetPath;
    }

    public bool VerifyChecksum(string filePath, string expectedSha256Hex)
    {
        if (!File.Exists(filePath)) return false;
        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        var hex = Convert.ToHexStringLower(hashBytes);
        return string.Equals(hex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    public void CleanupPartial(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            var metaFile = Path.ChangeExtension(tempFilePath, ".meta.json");
            if (File.Exists(metaFile))
            {
                File.Delete(metaFile);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
