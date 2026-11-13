using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F19")]
public sealed class F19_ChineseSpecsSyncTests
{
    static string SpecsDir => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/specs");

    [Fact]
    public void Specs_FramedChannel_Document_Exists()
    {
        var path = Path.Combine(SpecsDir, "framed-channel.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Specs_LanSession_Document_Exists()
    {
        var path = Path.Combine(SpecsDir, "lan-session.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Specs_LocalOperator_Document_Exists()
    {
        var path = Path.Combine(SpecsDir, "local-operator.md");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Specs_Documents_Are_Written_In_Chinese()
    {
        var path = Path.Combine(SpecsDir, "framed-channel.md");
        var content = File.ReadAllText(path);
        Assert.Contains(content, c => c >= 0x4E00 && c <= 0x9FFF);
    }

    [Fact]
    public void Specs_Cite_Port_42461_And_5353()
    {
        var framed = File.ReadAllText(Path.Combine(SpecsDir, "framed-channel.md"));
        var lan = File.ReadAllText(Path.Combine(SpecsDir, "lan-session.md"));

        Assert.Contains("42461", framed);
        Assert.Contains("5353", lan);
    }
}
