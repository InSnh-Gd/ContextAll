using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Android.Content;
using Android.Provider;
using ContextAll.Core;
using ContextAll.Session.Abstractions;

namespace ContextAll.App.Android.Platform;

public sealed class AndroidTransferSink : ITransferSink
{
    readonly ContentResolver resolver;
    readonly object gate = new();

    public AndroidTransferSink(Context context)
        : this(context.ContentResolver ?? throw new InvalidOperationException("ContentResolver is unavailable."))
    {
    }

    public AndroidTransferSink(ContentResolver resolver)
    {
        this.resolver = resolver;
    }

    public bool HandlesCommitInbound => true;

    public global::Android.Net.Uri? LastCommittedUri { get; private set; }

    public string? LastCommittedDisplayName { get; private set; }

    public bool TryCommitInbound(
        string tempFilePath,
        string fileName,
        string rootHashHex,
        string downloadDirectory,
        [MaybeNullWhen(false)] out string committedTarget)
    {
        _ = downloadDirectory;
        committedTarget = null;
        if (!File.Exists(tempFilePath))
        {
            return false;
        }

        if (!VerifyChecksum(tempFilePath, rootHashHex))
        {
            return false;
        }

        var collection = MediaStore.Downloads.ExternalContentUri;
        if (collection is null)
        {
            return false;
        }

        var sanitized = DownloadNames.Sanitize(fileName);
        string uniqueName;
        lock (gate)
        {
            uniqueName = DownloadNames.AvoidCollision(sanitized, ExistsInDownloads);
        }

        using var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, uniqueName);
        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        global::Android.Net.Uri? itemUri = null;
        try
        {
            itemUri = resolver.Insert(collection, values);
            if (itemUri is null)
            {
                return false;
            }

            using (var output = resolver.OpenOutputStream(itemUri))
            {
                if (output is null)
                {
                    resolver.Delete(itemUri, null, null);
                    return false;
                }

                using var input = File.OpenRead(tempFilePath);
                input.CopyTo(output);
                output.Flush();
            }

            using var publish = new ContentValues();
            publish.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(itemUri, publish, null, null);

            LastCommittedUri = itemUri;
            LastCommittedDisplayName = uniqueName;
            committedTarget = itemUri.ToString() ?? uniqueName;
            CleanupPartial(tempFilePath);
            return true;
        }
        catch
        {
            if (itemUri is not null)
            {
                try
                {
                    resolver.Delete(itemUri, null, null);
                }
                catch
                {
                }
            }

            return false;
        }
    }

    public async Task<string> CommitInboundFileAsync(
        string tempFilePath,
        string fileName,
        string rootHashHex,
        CancellationToken ct = default)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new FileNotFoundException("Inbound temporary transfer file not found.", tempFilePath);
        }

        await using (var stream = File.OpenRead(tempFilePath))
        {
            var hashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
            var computedHex = Convert.ToHexStringLower(hashBytes);
            if (!string.Equals(computedHex, rootHashHex, StringComparison.OrdinalIgnoreCase))
            {
                CleanupPartial(tempFilePath);
                throw new InvalidOperationException($"Checksum verification failed: expected {rootHashHex}, computed {computedHex}.");
            }
        }

        if (!TryCommitInbound(tempFilePath, fileName, rootHashHex, "", out var committed))
        {
            throw new IOException("Failed to insert completed file into MediaStore.Downloads.");
        }

        return committed;
    }

    public bool VerifyChecksum(string filePath, string expectedSha256Hex)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

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
        }
    }

    bool ExistsInDownloads(string candidateDisplayName)
    {
        var collection = MediaStore.Downloads.ExternalContentUri;
        if (collection is null)
        {
            return false;
        }

        try
        {
            string[] projection = [MediaStore.IMediaColumns.DisplayName];
            const string selection = $"{MediaStore.IMediaColumns.DisplayName} = ?";
            string[] selectionArgs = [candidateDisplayName];
            using var cursor = resolver.Query(collection, projection, selection, selectionArgs, null);
            return cursor is not null && cursor.MoveToFirst();
        }
        catch
        {
            return false;
        }
    }
}

