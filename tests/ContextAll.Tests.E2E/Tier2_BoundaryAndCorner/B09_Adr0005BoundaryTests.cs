using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B09")]
public sealed class B09_Adr0005BoundaryTests
{
    static string AdrDir => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/adr");
    static string DesignPath => NetworkSandbox.FindChangeArtifact("android-transport", "design.md");
    static string CoreCsprojPath => Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.Core/ContextAll.Core.csproj");

    [Fact]
    public void Adr0005_Numbering_Strictly_Follows_0004()
    {
        Assert.True(Directory.Exists(AdrDir));
        var adr0003 = Path.Combine(AdrDir, "0003-shared-effects.md");
        var adr0004 = Path.Combine(AdrDir, "0004-quic-android-transport.md");
        Assert.True(File.Exists(adr0003));
        Assert.True(File.Exists(adr0004));

        var seq3 = int.Parse(Path.GetFileName(adr0003).Substring(0, 4));
        var seq4 = int.Parse(Path.GetFileName(adr0004).Substring(0, 4));
        Assert.Equal(seq3 + 1, seq4);
    }

    [Fact]
    public void Adr0005_Explains_Layer_Split_Rationale()
    {
        Assert.True(File.Exists(DesignPath));
        var text = File.ReadAllText(DesignPath);
        Assert.Contains("ContextAll.Effects", text);
        Assert.Contains("ContextAll.Session", text);
        Assert.Contains("shared effects stay in one module, and orchestration is a separate layer", text);
    }

    [Fact]
    public void Adr0005_Specifies_All_Four_Platform_Seams()
    {
        Assert.True(File.Exists(DesignPath));
        var text = File.ReadAllText(DesignPath);
        Assert.Contains("IHostPaths", text);
        Assert.Contains("IClipboardSource", text);
        Assert.Contains("IDiscovery", text);
        Assert.Contains("ITransferSink", text);
    }

    [Fact]
    public void Adr0005_Specifies_IsAotCompatible_Constraint()
    {
        Assert.True(File.Exists(CoreCsprojPath));
        var xml = File.ReadAllText(CoreCsprojPath);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", xml);
    }

    [Fact]
    public void Adr_Directory_Has_No_Duplicate_Sequences()
    {
        Assert.True(Directory.Exists(AdrDir));
        var files = Directory.GetFiles(AdrDir, "*.md");
        var prefixes = files.Select(f => Path.GetFileName(f).Substring(0, 4)).ToList();
        Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
    }
}
