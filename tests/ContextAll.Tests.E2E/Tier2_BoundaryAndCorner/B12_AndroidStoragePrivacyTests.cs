using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B12")]
public sealed class B12_AndroidStoragePrivacyTests
{
    [Fact]
    public async Task Storage_PrivateKey_Must_Never_Land_In_FilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        var keyPath = Path.Combine(fixture.IdentityDirectory, "device.key");

        Assert.True(AndroidPathsFixture.IsInNoBackup(keyPath, fixture.NoBackupFilesDir));
        Assert.False(AndroidPathsFixture.IsInFilesDir(keyPath, fixture.FilesDir));
    }

    [Fact]
    public async Task Storage_PartialTransfers_Must_Never_Land_In_FilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        var partPath = Path.Combine(fixture.TransfersDirectory, "transfer1.part");

        Assert.True(AndroidPathsFixture.IsInNoBackup(partPath, fixture.NoBackupFilesDir));
        Assert.False(AndroidPathsFixture.IsInFilesDir(partPath, fixture.FilesDir));
    }

    [Fact]
    public async Task Storage_Settings_Are_Safely_In_FilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        Assert.True(AndroidPathsFixture.IsInFilesDir(fixture.SettingsFile, fixture.FilesDir));
    }

    [Fact]
    public void Storage_Rejects_Backslash_Path_Traversal()
    {
        Assert.True(AndroidPathsFixture.HasPathTraversal(@"..\..\windows\system32"));
        Assert.True(AndroidPathsFixture.HasPathTraversal(@"sub\folder"));
    }

    [Fact]
    public async Task Storage_Creation_Is_Idempotent()
    {
        await using var fixture = new AndroidPathsFixture();
        Directory.CreateDirectory(fixture.IdentityDirectory);
        Directory.CreateDirectory(fixture.IdentityDirectory);

        Assert.True(Directory.Exists(fixture.IdentityDirectory));
    }
}
