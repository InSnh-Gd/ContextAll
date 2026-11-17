using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class WindowsHostPaths : IHostPaths
{
    public WindowsHostPaths(string? localAppData = null, string? downloadsDirectory = null)
    {
        var root = localAppData;
        if (string.IsNullOrEmpty(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                home = Path.GetTempPath();
            }

            root = Path.Combine(home, "AppData", "Local");
        }

        IdentityDirectory = Path.Combine(root, "ContextAll", "identity");
        SettingsFile = Path.Combine(root, "ContextAll", "config.yaml");
        StateDirectory = Path.Combine(root, "ContextAll");
        TransfersDirectory = Path.Combine(root, "ContextAll", "transfers");
        DefaultDownloadDirectory = string.IsNullOrEmpty(downloadsDirectory)
            ? WindowsKnownFolders.Downloads()
            : downloadsDirectory;
    }

    public string IdentityDirectory { get; }

    public string SettingsFile { get; }

    public string StateDirectory { get; }

    public string TransfersDirectory { get; }

    public string DefaultDownloadDirectory { get; }
}
