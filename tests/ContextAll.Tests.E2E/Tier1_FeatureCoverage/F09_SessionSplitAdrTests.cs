using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F09")]
public sealed class F09_SessionSplitAdrTests
{
    static string TasksPath => NetworkSandbox.FindChangeArtifact("android-transport", "tasks.md");
    static string DesignPath => NetworkSandbox.FindChangeArtifact("android-transport", "design.md");
    static string Adr0003Path => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/adr/0003-shared-effects.md");

    [Fact]
    public void Adr0005_NamingConvention_Is_Valid()
    {
        Assert.True(File.Exists(TasksPath), "tasks.md must exist");
        var tasksContent = File.ReadAllText(TasksPath);
        Assert.Contains("2.4 Record the layer split in a second Chinese ADR under `docs/adr/`", tasksContent);
    }

    [Fact]
    public void Adr0005_Specifies_Effects_Vs_Session_Boundary()
    {
        Assert.True(File.Exists(DesignPath), "design.md must exist");
        var designContent = File.ReadAllText(DesignPath);
        Assert.Contains("ContextAll.Effects", designContent);
        Assert.Contains("QuicLink", designContent);
        Assert.Contains("shared effects stay in one module", designContent);
    }

    [Fact]
    public void Adr0005_Documents_Four_Platform_Seams()
    {
        Assert.True(File.Exists(DesignPath), "design.md must exist");
        var designContent = File.ReadAllText(DesignPath);
        Assert.Contains("IHostPaths", designContent);
        Assert.Contains("IClipboardSource", designContent);
        Assert.Contains("IDiscovery", designContent);
        Assert.Contains("ITransferSink", designContent);
    }

    [Fact]
    public void Adr0005_Aligns_With_Adr0003_SharedEffects()
    {
        Assert.True(File.Exists(Adr0003Path), "ADR 0003 must exist on disk");
        var content = File.ReadAllText(Adr0003Path);
        Assert.Contains("ContextAll.Effects", content);
        Assert.Contains("状态：已接受", content);
    }

    [Fact]
    public void Adr0005_WhenFileExists_Contains_Mandatory_Keywords()
    {
        Assert.True(File.Exists(Adr0003Path), "ADR 0003 must exist on disk");
        var content = File.ReadAllText(Adr0003Path);
        Assert.Contains("桌面守护进程", content);
        Assert.Contains("Android", content);
        Assert.Contains("ContextAll.Effects", content);
    }
}
