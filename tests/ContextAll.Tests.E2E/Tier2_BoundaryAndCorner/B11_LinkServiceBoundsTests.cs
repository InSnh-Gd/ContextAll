using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B11")]
public sealed class B11_LinkServiceBoundsTests
{
    [Fact]
    public async Task LinkService_Double_Start_Throws_InvalidOperationException()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.StartInProcessSessionAsync());
    }

    [Fact]
    public async Task LinkService_Command_Fails_When_Not_Running()
    {
        await using var harness = new InProcessServiceHarness();
        var outcome = await harness.ExecuteInProcessCommandAsync(65);
        Assert.True(outcome.IsFailure);
        Assert.Equal("not-running", outcome.Reason);
    }

    [Fact]
    public async Task LinkService_Clean_Dispose_Allows_Subsequent_Session()
    {
        var harness1 = new InProcessServiceHarness();
        await harness1.StartInProcessSessionAsync();
        await harness1.DisposeAsync();

        var harness2 = new InProcessServiceHarness();
        await harness2.StartInProcessSessionAsync();
        Assert.Contains(harness2.AuditLogs, l => l.Contains("SessionHost initialized in-process with zero local sockets."));
        await harness2.DisposeAsync();
    }

    [Fact]
    public void LinkService_Audits_No_Unix_Sockets_With_Specific_Pattern()
    {
        var isClean = InProcessServiceHarness.AuditZeroUnixSockets("contextall-android-specific");
        Assert.True(isClean);
    }

    [Fact]
    public async Task LinkService_AuditLogs_Record_Command_Executions()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        await harness.ExecuteInProcessCommandAsync(65, "device-abc");
        Assert.Contains(harness.AuditLogs, l => l.Contains("Dispatched wire command 65"));
    }
}
