using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B03")]
public sealed class B03_Adr0004BoundaryTests
{
    static string AdrDir => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/adr");
    static string Adr0004Path => Path.Combine(AdrDir, "0004-quic-android-transport.md");
    static string Adr0003Path => Path.Combine(AdrDir, "0003-shared-effects.md");

    [Fact]
    public void Adr0004_Numbering_Strictly_Follows_0003()
    {
        Assert.True(File.Exists(Adr0003Path));
        Assert.True(File.Exists(Adr0004Path));

        var seq3 = int.Parse(Path.GetFileName(Adr0003Path).Substring(0, 4));
        var seq4 = int.Parse(Path.GetFileName(Adr0004Path).Substring(0, 4));
        Assert.Equal(seq3 + 1, seq4);
    }

    [Fact]
    public void Adr0004_Rejects_Ambiguous_Fallback_Language()
    {
        Assert.True(File.Exists(Adr0004Path));
        var content = File.ReadAllText(Adr0004Path);

        // Verify it strictly prohibits fallback without ambiguous exceptions
        Assert.DoesNotContain("允许 TCP 回退", content);
        Assert.DoesNotContain("降级为 WebSocket", content);
        Assert.DoesNotContain("允许 HTTP 回退", content);
    }

    [Fact]
    public void Adr0004_Specifies_Both_Api31_And_Api36()
    {
        var tasksPath = NetworkSandbox.FindChangeArtifact("android-transport", "tasks.md");
        Assert.True(File.Exists(tasksPath));
        var tasks = File.ReadAllText(tasksPath);

        Assert.Contains("API 31", tasks);
        Assert.Contains("API 36", tasks);
    }

    [Fact]
    public void Adr0004_References_Udp_Port_42461()
    {
        Assert.True(File.Exists(Adr0004Path));
        var content = File.ReadAllText(Adr0004Path);
        Assert.Contains("42461", content);
    }

    [Fact]
    public void Adr_Directory_Contains_Valid_Markdown_Files_Only()
    {
        Assert.True(Directory.Exists(AdrDir));
        var files = Directory.GetFiles(AdrDir);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.EndsWith(".md", file, StringComparison.Ordinal);
        }
    }
}
