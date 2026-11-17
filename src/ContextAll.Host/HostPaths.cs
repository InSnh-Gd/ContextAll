using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

public static class HostPaths
{
    public static IHostPaths ForThisPlatform() => ForPlatform(OperatingSystem.IsWindows());

    public static IHostPaths ForPlatform(bool windows, string? localAppData = null, string? downloads = null) =>
        windows ? new WindowsHostPaths(localAppData, downloads) : new LinuxHostPaths();
}
