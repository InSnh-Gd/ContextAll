using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B17")]
public sealed class B17_InProcessThreadingTests
{
    [Fact]
    public async Task InProcess_Rapid_Concurrent_Commands_Do_Not_Throw()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var tasks = Enumerable.Range(0, 20)
            .Select(i => harness.ExecuteInProcessCommandAsync(FrameTypes.SetDisplayName, displayName: $"Device-{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, res =>
        {
            Assert.False(res.IsFailure);
            Assert.NotNull(res.Value);
            Assert.True(res.Value.Ok);
        });
    }

    [Fact]
    public async Task InProcess_Events_Without_Subscribers_Do_Not_Throw()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        // Dispatch a display name update command which generates internal session events
        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.SetDisplayName, displayName: "NewDevice");
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.True(outcome.Value.Ok);
    }

    [Fact]
    public async Task InProcess_Unknown_Command_Type_Returns_Success_Or_Handled()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        const byte unknownCommand = 250;
        var outcome = await harness.ExecuteInProcessCommandAsync(unknownCommand);
        // Either handled with error or timed out cleanly without throwing
        Assert.NotNull(outcome);
    }

    [Fact]
    public async Task InProcess_Service_Disposal_Leaves_AuditLogs_Intact()
    {
        var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();
        await harness.ExecuteInProcessCommandAsync(FrameTypes.AcceptPairing, deviceId: "peer");
        await harness.DisposeAsync();

        Assert.NotEmpty(harness.AuditLogs);
        Assert.False(harness.IsRunning);
    }

    [Fact]
    public async Task InProcess_Multiple_Start_Stop_Cycles_Are_Clean()
    {
        for (var i = 0; i < 3; i++)
        {
            var harness = new InProcessServiceHarness();
            await harness.StartInProcessSessionAsync();
            Assert.True(harness.IsRunning);

            await harness.DisposeAsync();
            Assert.False(harness.IsRunning, $"Harness must report IsRunning=false on cycle {i}");
            Assert.True(InProcessServiceHarness.AuditZeroUnixSockets("contextall-android"));
        }
    }
}
