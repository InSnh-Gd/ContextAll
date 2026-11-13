using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F11")]
public sealed class F11_ForegroundLinkServiceTests
{
    static string LinkServiceSourcePath =>
        Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.App.Android/LinkService.cs");

    [Fact]
    public void LinkService_Must_Be_Foreground_ConnectedDevice()
    {
        Assert.True(File.Exists(LinkServiceSourcePath), "LinkService.cs must exist on disk");
        var source = File.ReadAllText(LinkServiceSourcePath);
        Assert.Contains("TypeConnectedDevice", source);
        Assert.Contains("Exported = false", source);
    }

    [Fact]
    public async Task LinkService_Starts_SessionHost_InProcess()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        Assert.True(harness.IsRunning);
        Assert.Contains(harness.AuditLogs, l => l.Contains("SessionHost initialized in-process with zero local sockets."));
    }

    [Fact]
    public async Task LinkService_Stops_SessionHost_On_Service_Destroy()
    {
        var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        await harness.DisposeAsync();
        Assert.False(harness.IsRunning);

        // Subsequent command fails because service stopped
        var outcome = await harness.ExecuteInProcessCommandAsync(1);
        Assert.True(outcome.IsFailure);
        Assert.Equal("not-running", outcome.Reason);
    }

    [Fact]
    public void LinkService_Notification_Channel_Contract()
    {
        var designPath = NetworkSandbox.FindChangeArtifact("android-transport", "design.md");
        Assert.True(File.Exists(designPath));
        var design = File.ReadAllText(designPath);
        Assert.Contains("LinkService", design);
        Assert.Contains("connectedDevice", design);
    }

    [Fact]
    public async Task LinkService_Audits_Zero_Local_Sockets()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var isClean = InProcessServiceHarness.AuditZeroUnixSockets("contextall-android");
        Assert.True(isClean);
    }
}
