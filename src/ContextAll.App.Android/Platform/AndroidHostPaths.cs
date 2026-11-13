using Android.Content;
using ContextAll.Session.Abstractions;

namespace ContextAll.App.Android.Platform;

public sealed class AndroidHostPaths : IHostPaths
{
    public AndroidHostPaths(Context context)
        : this(
            noBackupDirectory: context.NoBackupFilesDir?.AbsolutePath
                ?? Path.Combine("/data/user/0", context.PackageName ?? "app.contextall", "no_backup"),
            filesDirectory: context.FilesDir?.AbsolutePath
                ?? Path.Combine("/data/user/0", context.PackageName ?? "app.contextall", "files"))
    {
    }

    public AndroidHostPaths(string noBackupDirectory, string filesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noBackupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);

        NoBackupDirectory = noBackupDirectory;
        FilesDirectory = filesDirectory;
        IdentityDirectory = Path.Combine(noBackupDirectory, "identity");
        TransfersDirectory = Path.Combine(noBackupDirectory, "transfers");
        StateDirectory = noBackupDirectory;
        SettingsFile = Path.Combine(filesDirectory, "config.yaml");
        DefaultDownloadDirectory = Path.Combine(noBackupDirectory, "downloads-staging");
    }

    public string NoBackupDirectory { get; }

    public string FilesDirectory { get; }

    public string IdentityDirectory { get; }

    public string SettingsFile { get; }

    public string StateDirectory { get; }

    public string TransfersDirectory { get; }

    public string DefaultDownloadDirectory { get; }
}

