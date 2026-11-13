namespace ContextAll.Session.Abstractions;

public interface IHostPaths
{
    string IdentityDirectory { get; }
    string SettingsFile { get; }
    string StateDirectory { get; }
    string TransfersDirectory { get; }
    string DefaultDownloadDirectory { get; }
    string DownloadsDirectory => DefaultDownloadDirectory;
}
