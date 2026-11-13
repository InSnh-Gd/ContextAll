using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B04")]
public sealed class B04_SessionProjectRigorTests
{
    static string RepoRoot => NetworkSandbox.FindRepoRoot();

    [Fact]
    public void DirectoryBuildProps_Enforces_Nullable_As_Error()
    {
        var propsPath = Path.Combine(RepoRoot, "Directory.Build.props");
        Assert.True(File.Exists(propsPath));

        var content = File.ReadAllText(propsPath);
        Assert.Contains("<Nullable>enable</Nullable>", content);
        Assert.Contains("<WarningsAsErrors>nullable;RS0030</WarningsAsErrors>", content);
    }

    [Fact]
    public void BannedSymbols_Bans_Task_Result_And_Wait()
    {
        var bannedPath = Path.Combine(RepoRoot, "BannedSymbols.txt");
        Assert.True(File.Exists(bannedPath));

        var content = File.ReadAllText(bannedPath);
        Assert.Contains("Task.Result", content);
        Assert.Contains("Task.Wait", content);
    }

    [Fact]
    public void SolutionFilter_References_Valid_Solution_File()
    {
        var slnfPath = Path.Combine(RepoRoot, "ContextAll.E2E.slnf");
        Assert.True(File.Exists(slnfPath));

        var content = File.ReadAllText(slnfPath);
        Assert.Contains("ContextAll.slnx", content);
    }

    [Fact]
    public void SolutionFile_Contains_Test_And_Source_Folders()
    {
        var slnxPath = Path.Combine(RepoRoot, "ContextAll.slnx");
        Assert.True(File.Exists(slnxPath));

        var content = File.ReadAllText(slnxPath);
        Assert.Contains("<Folder Name=\"/src/\">", content);
        Assert.Contains("<Folder Name=\"/tests/\">", content);
    }

    [Fact]
    public void SolutionFilter_Contains_ContextAll_Tests_E2E()
    {
        var slnfPath = Path.Combine(RepoRoot, "ContextAll.E2E.slnf");
        var content = File.ReadAllText(slnfPath);
        Assert.Contains("tests/ContextAll.Tests.E2E/ContextAll.Tests.E2E.csproj", content);
    }
}
