using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Session;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host.Tests;

[Collection("DiscoverySockets")]
public sealed class PlatformSeamsTests
{
    [Fact]
    public void HostPaths_Uses_Explicit_Directories_When_Provided()
    {
        var paths = new LinuxHostPaths(
            identityDirectory: "/custom/id",
            settingsFile: "/custom/config.yaml",
            stateDirectory: "/custom/state",
            transfersDirectory: "/custom/transfers",
            defaultDownloadDirectory: "/custom/downloads");

        Assert.Equal("/custom/id", paths.IdentityDirectory);
        Assert.Equal("/custom/config.yaml", paths.SettingsFile);
        Assert.Equal("/custom/state", paths.StateDirectory);
        Assert.Equal("/custom/transfers", paths.TransfersDirectory);
        Assert.Equal("/custom/downloads", paths.DefaultDownloadDirectory);
    }

    [Fact]
    public void HostPaths_Falls_Back_To_Home_When_Unset()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Path.GetTempPath();

        var paths = new LinuxHostPaths();

        Assert.Contains("contextall", paths.IdentityDirectory);
        Assert.Contains("contextall", paths.SettingsFile);
        Assert.Contains("contextall", paths.StateDirectory);
        Assert.EndsWith("transfers", paths.TransfersDirectory);
        Assert.EndsWith("Downloads", paths.DefaultDownloadDirectory);
    }

    [Fact]
    public async Task ClipboardSource_Disabled_When_Wayland_Null()
    {
        using var source = new LinuxClipboardSource(null);
        Assert.False(source.IsSupported);

        // Does not throw when unsupported
        source.SetText("test");
        await source.SetClipboardAsync("text/plain", Encoding.UTF8.GetBytes("test"));
        await source.WriteTextAsync("test");
        var read = await source.ReadTextAsync();
        Assert.Null(read);
    }

    [Fact]
    public async Task TransferSink_Commits_Valid_File_To_Download_Directory()
    {
        using var tempRoot = new TempFolder();
        var stagingDir = Path.Combine(tempRoot.Path, "transfers");
        var downloadDir = Path.Combine(tempRoot.Path, "downloads");
        Directory.CreateDirectory(stagingDir);

        var sink = new LinuxTransferSink(downloadDir);
        var tempFile = Path.Combine(stagingDir, "test.part");
        var content = "transfer sink integration test content"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);

        var sha256Hex = Convert.ToHexStringLower(SHA256.HashData(content));
        var targetFile = await sink.CommitInboundFileAsync(tempFile, "sample.txt", sha256Hex);

        Assert.False(File.Exists(tempFile));
        Assert.True(File.Exists(targetFile));
        Assert.True(sink.VerifyChecksum(targetFile, sha256Hex));
        Assert.Equal(content, await File.ReadAllBytesAsync(targetFile));
    }

    [Fact]
    public async Task TransferSink_Throws_And_Deletes_Temp_On_Hash_Mismatch()
    {
        using var tempRoot = new TempFolder();
        var stagingDir = Path.Combine(tempRoot.Path, "transfers");
        var downloadDir = Path.Combine(tempRoot.Path, "downloads");
        Directory.CreateDirectory(stagingDir);

        var sink = new LinuxTransferSink(downloadDir);
        var tempFile = Path.Combine(stagingDir, "corrupt.part");
        var content = "corrupt content"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);

        var bogusSha256 = new string('0', 64);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sink.CommitInboundFileAsync(tempFile, "corrupt.txt", bogusSha256));

        Assert.Contains("Checksum verification failed", ex.Message);
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task TransferSink_Sanitizes_And_Avoids_Collision()
    {
        using var tempRoot = new TempFolder();
        var stagingDir = Path.Combine(tempRoot.Path, "transfers");
        var downloadDir = Path.Combine(tempRoot.Path, "downloads");
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(downloadDir);

        // Pre-create existing file
        var existingPath = Path.Combine(downloadDir, "report.pdf");
        await File.WriteAllTextAsync(existingPath, "original");

        var sink = new LinuxTransferSink(downloadDir);
        var tempFile = Path.Combine(stagingDir, "temp.part");
        var content = "new report content"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, content);

        var sha256Hex = Convert.ToHexStringLower(SHA256.HashData(content));
        var committed = await sink.CommitInboundFileAsync(tempFile, "report.pdf", sha256Hex);

        Assert.Equal(Path.Combine(downloadDir, "report (2).pdf"), committed);
        Assert.True(File.Exists(committed));
        Assert.True(File.Exists(existingPath));
    }

    [Fact]
    public void TransferSink_CleanupPartial_Deletes_Part_And_Meta()
    {
        using var tempRoot = new TempFolder();
        var sink = new LinuxTransferSink(tempRoot.Path);
        var partFile = Path.Combine(tempRoot.Path, "transfer-123.part");
        var metaFile = Path.Combine(tempRoot.Path, "transfer-123.meta.json");

        File.WriteAllText(partFile, "partial data");
        File.WriteAllText(metaFile, "{}");

        Assert.True(File.Exists(partFile));
        Assert.True(File.Exists(metaFile));

        sink.CleanupPartial(partFile);

        Assert.False(File.Exists(partFile));
        Assert.False(File.Exists(metaFile));
    }

    [Fact]
    public async Task Discovery_Start_And_Announce_Does_Not_Throw()
    {
        var digest = Enumerable.Range(10, 32).Select(i => (byte)i).ToArray();
        await using var discovery = new LinuxDiscovery("dev-1", digest, "LinuxHost", 42461, _ => { });

        await discovery.StartAsync();
        Assert.True(discovery.IsActive);

        await discovery.AnnounceAsync(42461);
    }

    [Fact]
    public void Production_linux_discovery_is_linux_discovery_adapter()
    {
        using var folder = new TempFolder();
        var identity = IdentityStore.Start(System.IO.Path.Combine(folder.Path, "identity"));
        Assert.True(identity.Ok);
        Assert.NotNull(identity.Material);

        var discovery = LanHost.CreateLinuxDiscovery(identity.Material, "LinuxHost", 42461);
        if (OperatingSystem.IsWindows())
        {
            Assert.Null(discovery);
            return;
        }

        Assert.IsType<LinuxDiscovery>(discovery);
    }

    [Fact]
    public async Task SessionHost_skips_builtin_beacon_when_discovery_is_injected()
    {
        using var folder = new TempFolder();
        var paths = new LinuxHostPaths(
            identityDirectory: System.IO.Path.Combine(folder.Path, "id"),
            settingsFile: System.IO.Path.Combine(folder.Path, "config.yaml"),
            stateDirectory: System.IO.Path.Combine(folder.Path, "state"),
            transfersDirectory: System.IO.Path.Combine(folder.Path, "transfers"),
            defaultDownloadDirectory: System.IO.Path.Combine(folder.Path, "downloads"));
        var identity = IdentityStore.Start(paths.IdentityDirectory);
        Assert.True(identity.Ok);
        Assert.NotNull(identity.Material);

        await using var discovery = new RecordingDiscovery();
        await using var host = new SessionHost(
            paths,
            clipboard: null,
            discovery,
            transferSink: null,
            quicPort: NextFreeUdpPort(),
            preloadedIdentity: identity.Material);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        host.Start(cts.Token);
        Assert.False(host.UsesBuiltInDiscovery);
        await discovery.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(discovery.IsActive);
    }

    static int NextFreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        if (socket.LocalEndPoint is not IPEndPoint end)
        {
            throw new InvalidOperationException("local-endpoint");
        }

        return end.Port;
    }

    sealed class RecordingDiscovery : IDiscovery
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsActive { get; private set; }

        public event Action<DiscoveredPeer>? PeerDiscovered
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            IsActive = true;
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task AnnounceAsync(ushort port, CancellationToken ct = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "contextall-seams-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
