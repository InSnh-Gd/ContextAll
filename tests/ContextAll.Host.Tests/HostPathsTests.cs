using ContextAll.Host;

namespace ContextAll.Host.Tests;

public sealed class HostPathsTests
{
    [Fact]
    public void ForThisPlatform_matches_the_running_os()
    {
        var paths = HostPaths.ForThisPlatform();
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsHostPaths>(paths);
        }
        else
        {
            Assert.IsType<LinuxHostPaths>(paths);
        }
    }

    [Fact]
    public void Linux_platform_paths_use_xdg_layout()
    {
        var paths = HostPaths.ForPlatform(windows: false);
        Assert.IsType<LinuxHostPaths>(paths);
        Assert.Contains("contextall", paths.IdentityDirectory, StringComparison.Ordinal);
        Assert.Contains("contextall", paths.SettingsFile, StringComparison.Ordinal);
        Assert.Contains("contextall", paths.StateDirectory, StringComparison.Ordinal);
        Assert.EndsWith("transfers", paths.TransfersDirectory);
        Assert.EndsWith("Downloads", paths.DefaultDownloadDirectory);
    }

    [Fact]
    public void Windows_platform_paths_use_local_app_data_and_known_downloads()
    {
        var local = Path.Combine(Path.GetTempPath(), "ca-local-" + Guid.NewGuid().ToString("N"));
        var downloads = Path.Combine(Path.GetTempPath(), "ca-dl-" + Guid.NewGuid().ToString("N"));
        var paths = HostPaths.ForPlatform(windows: true, localAppData: local, downloads: downloads);

        Assert.IsType<WindowsHostPaths>(paths);
        Assert.Equal(Path.Combine(local, "ContextAll", "identity"), paths.IdentityDirectory);
        Assert.Equal(Path.Combine(local, "ContextAll", "config.yaml"), paths.SettingsFile);
        Assert.Equal(Path.Combine(local, "ContextAll"), paths.StateDirectory);
        Assert.Equal(Path.Combine(local, "ContextAll", "transfers"), paths.TransfersDirectory);
        Assert.Equal(downloads, paths.DefaultDownloadDirectory);
    }

    [Fact]
    public void Windows_downloads_use_the_known_folder_when_resolved()
    {
        Assert.Equal("/known/Downloads", WindowsKnownFolders.Downloads(_ => "/known/Downloads"));
    }

    [Fact]
    public void Windows_downloads_fall_back_to_profile_Downloads_when_known_folder_is_missing()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Path.GetTempPath();
        }

        Assert.Equal(Path.Combine(home, "Downloads"), WindowsKnownFolders.Downloads(_ => null));
    }
}
