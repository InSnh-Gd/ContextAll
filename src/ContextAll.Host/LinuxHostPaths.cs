using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public sealed class LinuxHostPaths : IHostPaths
{
    public LinuxHostPaths(
        string? identityDirectory = null,
        string? settingsFile = null,
        string? stateDirectory = null,
        string? transfersDirectory = null,
        string? defaultDownloadDirectory = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();

        IdentityDirectory = identityDirectory ?? LinuxPaths.IdentityDirectory(Environment.GetEnvironmentVariable("XDG_DATA_HOME"), home);
        SettingsFile = settingsFile ?? LinuxPaths.SettingsFile(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), home);
        StateDirectory = stateDirectory ?? LinuxPaths.StateDirectory(Environment.GetEnvironmentVariable("XDG_STATE_HOME"), home);
        TransfersDirectory = transfersDirectory ?? Path.Combine(StateDirectory, "transfers");
        DefaultDownloadDirectory = defaultDownloadDirectory ?? LinuxPaths.Downloads(home);
    }

    public string IdentityDirectory { get; }
    public string SettingsFile { get; }
    public string StateDirectory { get; }
    public string TransfersDirectory { get; }
    public string DefaultDownloadDirectory { get; }
}
