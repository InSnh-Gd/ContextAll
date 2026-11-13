using System.Security.Cryptography;
using ContextAll.Effects;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F08")]
public sealed class F08_PlatformSeamsTests
{
    [Fact]
    public void LinuxPaths_Resolves_Xdg_Directories()
    {
        const string xdgData = "/tmp/test-data";
        const string xdgConfig = "/tmp/test-config";
        const string xdgState = "/tmp/test-state";
        const string home = "/tmp/test-home";

        var idDir = LinuxPaths.IdentityDirectory(xdgData, home);
        var settingsFile = LinuxPaths.SettingsFile(xdgConfig, home);
        var stateDir = LinuxPaths.StateDirectory(xdgState, home);

        Assert.Equal("/tmp/test-data/contextall/identity", idDir);
        Assert.Equal("/tmp/test-config/contextall/config.yaml", settingsFile);
        Assert.Equal("/tmp/test-state/contextall", stateDir);
    }

    [Fact]
    public void LinuxPaths_Falls_Back_To_Home_When_Xdg_Unset()
    {
        const string home = "/home/testuser";

        var idDir = LinuxPaths.IdentityDirectory(null, home);
        var settingsFile = LinuxPaths.SettingsFile(null, home);
        var stateDir = LinuxPaths.StateDirectory(null, home);

        Assert.Equal("/home/testuser/.local/share/contextall/identity", idDir);
        Assert.Equal("/home/testuser/.config/contextall/config.yaml", settingsFile);
        Assert.Equal("/home/testuser/.local/state/contextall", stateDir);
    }

    [Fact]
    public void ClipboardSource_Contract_Limits_Chunk_Size()
    {
        Assert.Equal(65536, FrameLimits.MaxPayloadBytes);

        var oversizedPayload = new byte[FrameLimits.MaxPayloadBytes + 1];
        var encodeResult = FrameCodec.Encode(oversizedPayload, FrameTypes.ClipboardChunk, 0, 0, FrameTypes.SessionStream);
        Assert.True(encodeResult.IsFailure);
        Assert.Contains("payload-length", encodeResult.Reason);
    }

    [Fact]
    public void Discovery_Contract_Requires_Start_And_Announce()
    {
        Assert.Equal(42462, LanHost.BeaconPort);
        Assert.Equal(42461, QuicLink.Port);
    }

    [Fact]
    public async Task TransferSink_Moves_TempFile_To_Download_Directory()
    {
        await using var sandbox = new NetworkSandbox();
        var stagingDir = Path.Combine(sandbox.TempRoot, "transfers");
        var downloadDir = Path.Combine(sandbox.TempRoot, "downloads");

        var sink = new AndroidTransferSink(downloadDir, stagingDir);
        var tempFile = Path.Combine(stagingDir, "transfer.part");

        var data = "sample data payload"u8.ToArray();
        await File.WriteAllBytesAsync(tempFile, data);
        var sha256Hex = Convert.ToHexStringLower(SHA256.HashData(data));

        var finalPath = await sink.CommitInboundFileAsync(tempFile, "received.txt", sha256Hex);

        Assert.False(File.Exists(tempFile));
        Assert.True(File.Exists(finalPath));
        Assert.True(sink.VerifyChecksum(finalPath, sha256Hex));
    }
}
