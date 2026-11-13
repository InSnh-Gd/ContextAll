using System.Diagnostics.CodeAnalysis;

namespace ContextAll.Session.Abstractions;

public interface ITransferSink
{
    bool HandlesCommitInbound => false;

    bool TryCommitInbound(
        string tempFilePath,
        string fileName,
        string rootHashHex,
        string downloadDirectory,
        [MaybeNullWhen(false)] out string committedTarget)
    {
        committedTarget = null;
        return false;
    }

    Task<string> CommitInboundFileAsync(string tempFilePath, string fileName, string rootHashHex, CancellationToken ct = default);
    bool VerifyChecksum(string filePath, string expectedSha256Hex);
    void CleanupPartial(string tempFilePath);
}
