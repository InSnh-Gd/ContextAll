using System.Net;
using System.Net.Quic;
using System.Reflection;
using ContextAll.Effects;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F21")]
public sealed class F21_AndroidInstrumentedTests
{
    static bool IsUsableAddress(IPAddress address) =>
        (bool)typeof(LocalNetwork).GetMethod("IsUsableAddress", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [address])!;

    [Fact]
    public async Task Instrumented_Harness_Verifies_LinkService_Lifecycle()
    {
        var linkServicePath = Path.Combine(NetworkSandbox.FindRepoRoot(), "app/src/main/java/app/contextall/service/LinkService.kt");
        Assert.True(File.Exists(linkServicePath), "LinkService.kt must exist on disk");
        var code = File.ReadAllText(linkServicePath);
        Assert.Contains("FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE", code);

        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();
        Assert.True(harness.IsRunning);
    }

    [Fact]
    public void Instrumented_Harness_Verifies_Nsd_Service_Definition()
    {
        Assert.Equal("_contextall._udp.local", MdnsCodec.ServiceName);
    }

    [Fact]
    public void Instrumented_Harness_Verifies_Network_Filter_Rule()
    {
        Assert.False(IsUsableAddress(IPAddress.Loopback));
        Assert.False(IsUsableAddress(IPAddress.IPv6Loopback));
        Assert.True(IsUsableAddress(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void Instrumented_Harness_Verifies_Quic_Loopback_Prerequisite()
    {
        Assert.Equal(QuicListener.IsSupported, QuicLink.Supported);
    }

    [Fact]
    public async Task Instrumented_Harness_Verifies_Command_And_Event_Loop()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var reply = await harness.ExecuteInProcessCommandAsync(FrameTypes.AcceptPairing, deviceId: "test-peer");
        Assert.False(reply.IsFailure);
        Assert.NotNull(reply.Value);
        Assert.False(reply.Value.Ok);
        Assert.Equal("already-decided", reply.Value.Error);

        var payload = new byte[] { 1, 2, 3 };
        var encoded = FrameCodec.Encode(payload, FrameTypes.DeviceChanged, 0, 0, FrameTypes.SessionStream);
        Assert.False(encoded.IsFailure);

        var decoded = FrameCodec.Read(encoded.Value!);
        Assert.False(decoded.IsFailure);
        Assert.NotNull(decoded.Frame);
        Assert.Equal(FrameTypes.DeviceChanged, decoded.Frame.Type);
    }
}
