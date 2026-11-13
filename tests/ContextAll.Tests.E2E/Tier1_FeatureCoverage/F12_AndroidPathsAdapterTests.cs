using ContextAll.Core;
using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F12")]
public sealed class F12_AndroidPathsAdapterTests
{
    [Fact]
    public async Task AndroidPaths_Identity_Is_In_NoBackupFilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        Assert.True(AndroidPathsFixture.IsInNoBackup(fixture.IdentityDirectory, fixture.NoBackupFilesDir));
        Assert.False(AndroidPathsFixture.IsInFilesDir(fixture.IdentityDirectory, fixture.FilesDir));
    }

    [Fact]
    public async Task AndroidPaths_Transfers_Is_In_NoBackupFilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        Assert.True(AndroidPathsFixture.IsInNoBackup(fixture.TransfersDirectory, fixture.NoBackupFilesDir));
        Assert.False(AndroidPathsFixture.IsInFilesDir(fixture.TransfersDirectory, fixture.FilesDir));
    }

    [Fact]
    public async Task AndroidPaths_Settings_Is_In_FilesDir()
    {
        await using var fixture = new AndroidPathsFixture();
        Assert.True(AndroidPathsFixture.IsInFilesDir(fixture.SettingsFile, fixture.FilesDir));
        Assert.False(AndroidPathsFixture.IsInNoBackup(fixture.SettingsFile, fixture.NoBackupFilesDir));
    }

    [Fact]
    public void AndroidPaths_Detects_Path_Traversal()
    {
        Assert.True(AndroidPathsFixture.HasPathTraversal("../secret.key"));
        Assert.True(AndroidPathsFixture.HasPathTraversal("sub/dir"));
        Assert.True(AndroidPathsFixture.HasPathTraversal(@"sub\dir"));
        Assert.False(AndroidPathsFixture.HasPathTraversal("valid-transfer-id-123"));
    }

    [Fact]
    public void AndroidPaths_Sanitizes_Illegal_Filename_Characters()
    {
        var sanitized = DownloadNames.Sanitize("report:final*v1?.txt");
        Assert.Equal("reportfinalv1.txt", sanitized);

        var reservedSanitized = DownloadNames.Sanitize("CON.txt");
        Assert.Equal("download", reservedSanitized);
    }
}
