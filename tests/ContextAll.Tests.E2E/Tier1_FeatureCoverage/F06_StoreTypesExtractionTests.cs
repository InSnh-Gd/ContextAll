using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F06")]
public sealed class F06_StoreTypesExtractionTests
{
    [Fact]
    public async Task IdentityStore_Generates_P256_Keys_When_Missing()
    {
        await using var sandbox = new NetworkSandbox();
        var idDir = Path.Combine(sandbox.TempRoot, "identity");

        var outcome = IdentityStore.Start(idDir);
        Assert.True(outcome.Ok);
        Assert.NotNull(outcome.Material);
        Assert.NotEmpty(outcome.Material.DeviceId);
        Assert.NotEmpty(outcome.Material.Spki);
        Assert.True(File.Exists(Path.Combine(idDir, "device.key")));
        Assert.True(File.Exists(Path.Combine(idDir, "device.spki")));
    }

    [Fact]
    public async Task SettingsStore_Loads_And_Saves_ConfigYaml()
    {
        await using var sandbox = new NetworkSandbox();
        var configFile = Path.Combine(sandbox.TempRoot, "config.yaml");

        var outcome = SettingsStore.Start(configFile, "TestDevice");
        Assert.True(outcome.Ok);
        Assert.NotNull(outcome.Document);
        Assert.Equal("TestDevice", outcome.Document.DisplayName);
        Assert.True(File.Exists(configFile));
    }

    [Fact]
    public async Task TransferStore_Tracks_Active_Partials()
    {
        await using var sandbox = new NetworkSandbox();
        var store = new TransferStore(sandbox.TempRoot);

        Assert.NotNull(store);
        var incomplete = store.Incomplete("peer123", "inbound");
        Assert.NotNull(incomplete);
    }

    [Fact]
    public async Task TransferStore_CleansUp_On_Unpair()
    {
        await using var sandbox = new NetworkSandbox();
        var store = new TransferStore(sandbox.TempRoot);

        store.DeletePeer("peer123");
        var incomplete = store.Incomplete("peer123", "inbound");
        Assert.Empty(incomplete);
    }

    [Fact]
    public async Task TransferStore_Avoids_File_Collision()
    {
        await using var sandbox = new NetworkSandbox();
        var testFile = Path.Combine(sandbox.TempRoot, "document.txt");
        await File.WriteAllTextAsync(testFile, "hello");

        var resolved = DownloadNames.AvoidCollision("document.txt", name => File.Exists(Path.Combine(sandbox.TempRoot, name)));
        Assert.Equal("document (2).txt", resolved);
    }
}
