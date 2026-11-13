using System.Net;
using ContextAll.Core;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B23")]
public sealed class B23_RegressionUnderStressTests
{
    [Fact]
    public void NetworkSandbox_Resolves_Tool_Gracefully_When_Nonexistent()
    {
        var nonExistent = NetworkSandbox.ResolveTool("definitely_not_a_real_tool_binary_xyz_123");
        Assert.Equal("definitely_not_a_real_tool_binary_xyz_123", nonExistent);
    }

    [Fact]
    public void NetworkSandbox_Unshare_Check_Does_Not_Throw()
    {
        var supported = NetworkSandbox.IsUnshareSupported();
        // Unshare is supported on modern Linux namespaces
        Assert.IsType<bool>(supported);
    }

    [Fact]
    public void NetworkSandbox_GetFreePort_Returns_Non_Zero_Port()
    {
        var port = NetworkSandbox.GetFreePort(IPAddress.Loopback);
        Assert.True(port > 1024);
        Assert.True(port <= 65535);
    }

    [Fact]
    public async Task NetworkSandbox_Disposal_Deletes_Temp_Directory()
    {
        string dir;
        var sandbox = new NetworkSandbox();
        dir = sandbox.TempRoot;
        Assert.True(Directory.Exists(dir));

        await sandbox.DisposeAsync();
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void AndroidPaths_AvoidCollision_Returns_Original_When_No_Collision()
    {
        var result = DownloadNames.AvoidCollision("unique_file.txt", name => false);
        Assert.Equal("unique_file.txt", result);
    }
}
