using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B21")]
public sealed class B21_InstrumentedTestStressTests
{
    [Fact]
    public void DeviceIds_Generates_Unique_Ids_For_Different_Keys()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var id1 = DeviceIds.Text(k1.ExportSubjectPublicKeyInfo());
        var id2 = DeviceIds.Text(k2.ExportSubjectPublicKeyInfo());

        Assert.NotEqual(id1, id2);
        Assert.StartsWith("ca1", id1, StringComparison.Ordinal);
        Assert.StartsWith("ca1", id2, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InProcess_Harness_Handles_Multiple_Concurrent_Device_Events()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var frames = new List<Frame>();
        for (var i = 0; i < 50; i++)
        {
            var payload = new byte[] { (byte)i };
            var encoded = FrameCodec.Encode(payload, FrameTypes.DeviceChanged, 0, 0, FrameTypes.SessionStream);
            Assert.False(encoded.IsFailure);

            var decoded = FrameCodec.Read(encoded.Value!);
            Assert.False(decoded.IsFailure);
            Assert.NotNull(decoded.Frame);
            frames.Add(decoded.Frame);
        }

        Assert.Equal(50, frames.Count);
    }

    [Fact]
    public async Task InProcess_Harness_Large_Payload_Command_Succeeds()
    {
        await using var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();

        var largeDisplayName = new string('A', 64);
        var outcome = await harness.ExecuteInProcessCommandAsync(FrameTypes.SetDisplayName, displayName: largeDisplayName);

        Assert.False(outcome.IsFailure);
        Assert.NotNull(outcome.Value);
        Assert.True(outcome.Value.Ok);
    }

    [Fact]
    public void DeviceIds_ShortCode_Is_Symmetric_Regardless_Of_Order()
    {
        using var k1 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var k2 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var spki1 = k1.ExportSubjectPublicKeyInfo();
        var spki2 = k2.ExportSubjectPublicKeyInfo();

        var codeA = DeviceIds.ShortCode(spki1, spki2);
        var codeB = DeviceIds.ShortCode(spki2, spki1);

        // Sorted by digest, so order does not matter!
        Assert.Equal(codeA, codeB);
    }

    [Fact]
    public async Task InProcess_Harness_Clean_Stop_Clears_Running_State()
    {
        var harness = new InProcessServiceHarness();
        await harness.StartInProcessSessionAsync();
        await harness.DisposeAsync();

        var outcome = await harness.ExecuteInProcessCommandAsync(65);
        Assert.True(outcome.IsFailure);
        Assert.Equal("not-running", outcome.Reason);
    }
}
