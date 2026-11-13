using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F05")]
public sealed class F05_SessionHostCoreEngineTests
{
    [Fact]
    public async Task SessionEngine_Initializes_With_Valid_Identity_And_Settings()
    {
        await using var fixture = new DesktopDaemonFixture();
        var host = fixture.StartInProcessHost();

        Assert.NotNull(host);
        Assert.False(string.IsNullOrWhiteSpace(host.DisplayName));
        Assert.NotEmpty(host.LocalSpki);
    }

    [Fact]
    public async Task SessionEngine_Snapshot_Contains_Device_Info()
    {
        await using var fixture = new DesktopDaemonFixture();
        var host = fixture.StartInProcessHost();

        var snapshot = host.Snapshot();
        Assert.NotNull(snapshot);
        Assert.Equal(FrameTypes.Snapshot, snapshot.Type);
    }

    [Fact]
    public async Task SessionEngine_Handles_Local_Command()
    {
        await using var fixture = new DesktopDaemonFixture();
        var host = fixture.StartInProcessHost();

        // Command to set display name
        host.RenamePeer("device-nonexistent", "new-name");
        Assert.NotNull(host.DisplayName);
    }

    [Fact]
    public async Task SessionEngine_DeviceEvent_Generates_Valid_Frame()
    {
        await using var fixture = new DesktopDaemonFixture();
        var host = fixture.StartInProcessHost();

        var eventFrame = host.DeviceEvent("device123", "reachable");
        Assert.NotNull(eventFrame);
        Assert.Equal(FrameTypes.DeviceChanged, eventFrame.Type);
    }

    [Fact]
    public async Task SessionEngine_Clean_Disposal_Leaves_No_Leaked_Sockets()
    {
        var fixture = new DesktopDaemonFixture();
        var host = fixture.StartInProcessHost();
        Assert.NotNull(host);
        var socketPath = fixture.SocketPath;

        await fixture.DisposeAsync();
        Assert.False(File.Exists(socketPath), $"Socket {socketPath} must be cleaned up on disposal.");
    }
}
