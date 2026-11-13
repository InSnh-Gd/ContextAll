using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F17")]
public sealed class F17_InProcessCommandsEventsTests
{
    [Fact]
    public async Task InProcess_AcceptPairing_Command_Dispatches_Successfully()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.AcceptPairing, deviceId: "peer-android-1");
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.False(outcome.Value.Ok);
        Assert.Equal("already-decided", outcome.Value.Error);
    }

    [Fact]
    public async Task InProcess_RejectPairing_Command_Dispatches_Successfully()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.RejectPairing, deviceId: "peer-android-1");
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.False(outcome.Value.Ok);
        Assert.Equal("already-decided", outcome.Value.Error);
    }

    [Fact]
    public async Task InProcess_SetClipboardSync_Command_Dispatches_Successfully()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.SetClipboardSync, deviceId: "peer-android-1", enabled: true);
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.False(outcome.Value.Ok);
        Assert.Equal("not-trusted", outcome.Value.Error);
    }

    [Fact]
    public async Task InProcess_SetDisplayName_Command_Updates_Engine_State()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.SetDisplayName, displayName: "AndroidDeviceName");
        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.True(outcome.Value.Ok);
        Assert.Null(outcome.Value.Error);
    }

    [Fact]
    public async Task InProcess_Commands_Fail_When_Service_Not_Started()
    {
        await using var harness = new InProcessServiceHarness();
        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.AcceptPairing, "peer");
        Assert.True(outcome.IsFailure);
        Assert.Equal("not-running", outcome.Reason);
    }
}
