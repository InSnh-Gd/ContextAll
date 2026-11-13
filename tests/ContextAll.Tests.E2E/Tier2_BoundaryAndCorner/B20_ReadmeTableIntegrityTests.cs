using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B20")]
public sealed class B20_ReadmeTableIntegrityTests
{
    static string ReadmePath => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/README.md");

    [Fact]
    public void Readme_Table_Has_Uniform_Column_Count()
    {
        var lines = File.ReadAllLines(ReadmePath);
        var tableLines = lines.Where(l => l.StartsWith('|') && l.EndsWith('|')).ToList();
        Assert.NotEmpty(tableLines);

        var expectedColumns = tableLines[0].Split('|').Length;
        foreach (var line in tableLines)
        {
            Assert.Equal(expectedColumns, line.Split('|').Length);
        }
    }

    [Fact]
    public void Readme_Table_Contains_LanSession_Row()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("lan-session", content);
    }

    [Fact]
    public void Readme_Table_Contains_FramedChannel_Row()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("framed-channel", content);
    }

    [Fact]
    public void Readme_Links_To_Adr_Directory()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("adr/", content);
    }

    [Fact]
    public void Readme_Links_To_Specs_Directory()
    {
        var content = File.ReadAllText(ReadmePath);
        Assert.Contains("specs/", content);
    }
}
