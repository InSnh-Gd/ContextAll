using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F20")]
public sealed class F20_ReadmeCapabilitiesTableTests
{
    static string ReadmePath => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/README.md");

    [Fact]
    public void Readme_File_Exists()
    {
        Assert.True(File.Exists(ReadmePath));
    }

    [Fact]
    public void Readme_Contains_Capabilities_Table_Header()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("capability", content);
        Assert.Contains("文档", content);
        Assert.Contains("变更", content);
    }

    [Fact]
    public void Readme_Contains_Markdown_Table_Syntax()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("|", content);
        Assert.Contains("---", content);
    }

    [Fact]
    public void Readme_Has_Chinese_Prose()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains(content, c => c >= 0x4E00 && c <= 0x9FFF);
    }

    [Fact]
    public void Readme_References_Specs_Or_Adrs()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.True(content.Contains("specs") || content.Contains("adr"));
    }
}
