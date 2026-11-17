using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ContextAll.Host;

public static class WindowsKnownFolders
{
    public static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string Downloads(Func<Guid, string?>? resolve = null)
    {
        var resolved = (resolve ?? TryNative)(DownloadsId);
        if (!string.IsNullOrEmpty(resolved))
        {
            return resolved;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Path.GetTempPath();
        }

        return Path.Combine(home, "Downloads");
    }

    static string? TryNative(Guid folderId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return Native(folderId);
    }

    [SupportedOSPlatform("windows")]
    static string? Native(Guid folderId)
    {
        var hr = SHGetKnownFolderPath(in folderId, 0, 0, out var ptr);
        if (hr != 0 || ptr == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
