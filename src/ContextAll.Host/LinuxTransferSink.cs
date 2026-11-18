using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LinuxTransferSink : ITransferSink
{
    readonly string downloadDirectory;

    public LinuxTransferSink(string downloadDirectory)
    {
        this.downloadDirectory = downloadDirectory;
    }

    public async Task<string> CommitInboundFileAsync(string tempFilePath, string fileName, string rootHashHex, CancellationToken ct = default)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException("Inbound temporary transfer file not found.", tempFilePath);
        }

        string computedHex;
        using (var stream = File.OpenRead(tempFilePath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            computedHex = Convert.ToHexStringLower(hashBytes);
        }

        if (!string.Equals(computedHex, rootHashHex, StringComparison.OrdinalIgnoreCase))
        {
            CleanupPartial(tempFilePath);
            throw new InvalidOperationException($"Checksum verification failed: expected {rootHashHex}, computed {computedHex}.");
        }

        Directory.CreateDirectory(downloadDirectory);
        var sanitizedName = DownloadNames.Sanitize(fileName);
        var uniqueName = DownloadNames.AvoidCollision(sanitizedName, candidate => File.Exists(Path.Combine(downloadDirectory, candidate)));
        var targetPath = Path.Combine(downloadDirectory, uniqueName);

        try
        {
            File.Move(tempFilePath, targetPath, overwrite: false);
        }
        catch (IOException)
        {
            var temporary = Path.Combine(downloadDirectory, uniqueName + ".part-" + Guid.NewGuid().ToString("N"));
            File.Copy(tempFilePath, temporary, overwrite: true);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Flush(true);
            }
            File.Move(temporary, targetPath, overwrite: false);
            File.Delete(tempFilePath);
        }

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
