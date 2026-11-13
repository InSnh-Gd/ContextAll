using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F23")]
public sealed class F23_LinuxSuiteRegressionTests
{
    static string TestsDir => Path.Combine(NetworkSandbox.FindRepoRoot(), "tests");

    [Fact]
    public void Regression_ProtocolTests_Project_Exists()
    {
        var csproj = Path.Combine(TestsDir, "ContextAll.Protocol.Tests/ContextAll.Protocol.Tests.csproj");
        Assert.True(File.Exists(csproj));
    }

    [Fact]
    public void Regression_CoreTests_Project_Exists()
    {
        var csproj = Path.Combine(TestsDir, "ContextAll.Core.Tests/ContextAll.Core.Tests.csproj");
        Assert.True(File.Exists(csproj));
    }

    [Fact]
    public void Regression_EffectsTests_Project_Exists()
    {
        var csproj = Path.Combine(TestsDir, "ContextAll.Effects.Tests/ContextAll.Effects.Tests.csproj");
        Assert.True(File.Exists(csproj));
    }

    [Fact]
    public void Regression_AppTests_Project_Exists()
    {
        var csproj = Path.Combine(TestsDir, "ContextAll.App.Tests/ContextAll.App.Tests.csproj");
        Assert.True(File.Exists(csproj));
    }

    [Fact]
    public void Regression_NetworkSandbox_Resolves_Ip_Tool_From_Path()
    {
        var resolved = NetworkSandbox.ResolveTool("ip");
        Assert.NotEmpty(resolved);
        // On Linux/NixOS, either returns full path or "ip"
        Assert.EndsWith("ip", resolved, StringComparison.Ordinal);
    }
}
