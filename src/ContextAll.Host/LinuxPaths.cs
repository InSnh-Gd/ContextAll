namespace ContextAll.Host;

public static class LinuxPaths
{
    public static string IdentityDirectory(string? xdgDataHome, string home)
    {
        var root = string.IsNullOrEmpty(xdgDataHome) ? Path.Combine(home, ".local", "share") : xdgDataHome;
        return Path.Combine(root, "contextall", "identity");
    }

    public static string SettingsFile(string? xdgConfigHome, string home)
    {
        var root = string.IsNullOrEmpty(xdgConfigHome) ? Path.Combine(home, ".config") : xdgConfigHome;
        return Path.Combine(root, "contextall", "config.yaml");
    }

    public static string StateDirectory(string? xdgStateHome, string home)
    {
        var root = string.IsNullOrEmpty(xdgStateHome) ? Path.Combine(home, ".local", "state") : xdgStateHome;
        return Path.Combine(root, "contextall");
    }

    public static string Downloads(string home) => Path.Combine(home, "Downloads");
}
