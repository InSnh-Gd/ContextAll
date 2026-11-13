using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace ContextAll.Tests.E2E.Harness;

public sealed class NetworkSandbox : IAsyncDisposable, IDisposable
{
    static readonly string[] CandidateDirs =
    [
        "/run/current-system/sw/bin",
        "/usr/bin",
        "/usr/sbin",
        "/bin",
        "/sbin",
        "/etc/profiles/per-user/gdzzc/bin"
    ];

    public static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ContextAll.slnx")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    public static string FindChangeArtifact(string changeName, string relativePath)
    {
        var root = FindRepoRoot();
        var active = Path.Combine(root, "openspec", "changes", changeName, relativePath);
        if (File.Exists(active))
        {
            return active;
        }

        var archiveDir = Path.Combine(root, "openspec", "changes", "archive");
        if (Directory.Exists(archiveDir))
        {
            foreach (var dir in Directory.GetDirectories(archiveDir, $"*-{changeName}"))
            {
                var archived = Path.Combine(dir, relativePath);
                if (File.Exists(archived))
                {
                    return archived;
                }
            }
        }

        return active;
    }

    public static string ResolveTool(string toolName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var searchPaths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(CandidateDirs)
            .Distinct();

        foreach (var dir in searchPaths)
        {
            var fullPath = Path.Combine(dir, toolName);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return toolName;
    }

    public static bool IsUnshareSupported()
    {
        try
        {
            var unshare = ResolveTool("unshare");
            using var proc = Process.Start(new ProcessStartInfo(unshare, "-Urn true")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (proc is null) return false;

            if (!proc.WaitForExit(1000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static int GetFreePort(IPAddress? address = null)
    {
        address ??= IPAddress.Loopback;
        using var socket = new Socket(
            address.AddressFamily,
            SocketType.Dgram,
            ProtocolType.Udp);
        socket.Bind(new IPEndPoint(address, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public static int GetFreeTcpPort(IPAddress? address = null)
    {
        address ??= IPAddress.Loopback;
        using var socket = new Socket(
            address.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(address, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public static bool IsPortAvailable(int port, IPAddress? address = null)
    {
        address ??= IPAddress.Loopback;
        try
        {
            using var s = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string TempRoot { get; }

    public NetworkSandbox()
    {
        TempRoot = Path.Combine(Path.GetTempPath(), "contextall-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempRoot))
            {
                Directory.Delete(TempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
