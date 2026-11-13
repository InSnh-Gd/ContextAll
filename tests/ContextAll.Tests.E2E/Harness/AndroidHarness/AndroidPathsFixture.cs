namespace ContextAll.Tests.E2E.Harness.AndroidHarness;

/// <summary>
/// Fixture modeling Android internal app storage layout and storage isolation policies.
/// Does NOT contain filename sanitization or collision logic (which are tested directly in ContextAll.Core.DownloadNames).
/// </summary>
public sealed class AndroidPathsFixture : IAsyncDisposable, IDisposable
{
    public string RootDir { get; }
    public string NoBackupFilesDir { get; }
    public string FilesDir { get; }
    public string SimulatedMediaStoreDownloads { get; }

    public string IdentityDirectory => Path.Combine(NoBackupFilesDir, "identity");
    public string TransfersDirectory => Path.Combine(NoBackupFilesDir, "transfers");
    public string SettingsFile => Path.Combine(FilesDir, "config.yaml");

    public AndroidPathsFixture()
    {
        RootDir = Path.Combine(Path.GetTempPath(), "contextall-android-paths-" + Guid.NewGuid().ToString("N"));
        NoBackupFilesDir = Path.Combine(RootDir, "no_backup");
        FilesDir = Path.Combine(RootDir, "files");
        SimulatedMediaStoreDownloads = Path.Combine(RootDir, "media_store_downloads");

        Directory.CreateDirectory(NoBackupFilesDir);
        Directory.CreateDirectory(FilesDir);
        Directory.CreateDirectory(IdentityDirectory);
        Directory.CreateDirectory(TransfersDirectory);
        Directory.CreateDirectory(SimulatedMediaStoreDownloads);
    }

    public static bool IsInNoBackup(string path, string noBackupDir)
    {
        var fullPath = Path.GetFullPath(path);
        var fullNoBackup = Path.GetFullPath(noBackupDir);
        return fullPath.StartsWith(fullNoBackup, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsInFilesDir(string path, string filesDir)
    {
        var fullPath = Path.GetFullPath(path);
        var fullFiles = Path.GetFullPath(filesDir);
        return fullPath.StartsWith(fullFiles, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPathTraversal(string input)
    {
        return input.Contains("..", StringComparison.Ordinal) ||
               input.Contains('/', StringComparison.Ordinal) ||
               input.Contains('\\', StringComparison.Ordinal);
    }

    public void AssertStorageIsolation(string identityKeyPath, string transferPartPath, string settingsConfigPath)
    {
        if (!IsInNoBackup(identityKeyPath, NoBackupFilesDir))
            throw new InvalidOperationException($"Identity path '{identityKeyPath}' violates NoBackupFilesDir containment.");

        if (!IsInNoBackup(transferPartPath, NoBackupFilesDir))
            throw new InvalidOperationException($"Transfer partial '{transferPartPath}' violates NoBackupFilesDir containment.");

        if (!IsInFilesDir(settingsConfigPath, FilesDir))
            throw new InvalidOperationException($"Settings file '{settingsConfigPath}' violates FilesDir containment.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDir))
            {
                Directory.Delete(RootDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup on test completion
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
