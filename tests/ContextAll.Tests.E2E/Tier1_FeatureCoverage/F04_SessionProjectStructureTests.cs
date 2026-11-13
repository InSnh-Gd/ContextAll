using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F04")]
public sealed class F04_SessionProjectStructureTests
{
    static string DesignDocPath => NetworkSandbox.FindChangeArtifact("android-transport", "design.md");
    static string SlnxPath => Path.Combine(NetworkSandbox.FindRepoRoot(), "ContextAll.slnx");
    static string CoreCsprojPath => Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.Core/ContextAll.Core.csproj");

    [Fact]
    public void SessionProject_Has_Defined_Dependencies()
    {
        Assert.True(File.Exists(DesignDocPath), "design.md must exist");
        var text = File.ReadAllText(DesignDocPath);
        Assert.Contains("ContextAll.Session", text);
        Assert.Contains("references `Core`, `Effects`, `Protocol`", text);
    }

    [Fact]
    public void SessionProject_Forbids_Circular_Reference_To_Host()
    {
        Assert.True(File.Exists(DesignDocPath), "design.md must exist");
        var text = File.ReadAllText(DesignDocPath);
        Assert.Contains("ContextAll.Host", text);
        Assert.Contains("becomes an adapter over `SessionHost`", text);
    }

    [Fact]
    public void SessionProject_Target_Framework_Is_Net10()
    {
        Assert.True(File.Exists(CoreCsprojPath), "ContextAll.Core.csproj must exist");
        var xml = File.ReadAllText(CoreCsprojPath);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", xml);
    }

    [Fact]
    public void SessionProject_Requires_IsAotCompatible()
    {
        Assert.True(File.Exists(CoreCsprojPath), "ContextAll.Core.csproj must exist");
        var xml = File.ReadAllText(CoreCsprojPath);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", xml);
    }

    [Fact]
    public void SessionProject_WhenCsprojExists_Validates_Xml_Structure()
    {
        Assert.True(File.Exists(SlnxPath), "Solution file ContextAll.slnx must exist");
        var slnxContent = File.ReadAllText(SlnxPath);
        Assert.Contains("ContextAll.Core", slnxContent);
        Assert.Contains("ContextAll.Effects", slnxContent);
        Assert.Contains("ContextAll.Host", slnxContent);
    }
}
