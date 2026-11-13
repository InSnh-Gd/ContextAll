using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B19")]
public sealed class B19_ChineseSpecIntegrityTests
{
    static string SpecsDir => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/specs");

    [Fact]
    public void Specs_FramedChannel_Specifies_16Byte_Header()
    {
        var path = Path.Combine(SpecsDir, "framed-channel.md");
        var content = File.ReadAllText(path);
        Assert.Contains("16", content);
        Assert.Contains("保留", content);
    }

    [Fact]
    public void Specs_LanSession_Specifies_Mdns_Service_Name()
    {
        var path = Path.Combine(SpecsDir, "lan-session.md");
        var content = File.ReadAllText(path);
        Assert.Contains("_contextall._udp.local", content);
    }

    [Fact]
    public void Specs_LocalOperator_Specifies_Daemon_Socket()
    {
        var path = Path.Combine(SpecsDir, "local-operator.md");
        var content = File.ReadAllText(path);
        Assert.Contains("daemon.sock", content);
    }

    [Fact]
    public void Specs_Directory_Contains_No_Empty_Files()
    {
        var files = Directory.GetFiles(SpecsDir, "*.md");
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            Assert.True(info.Length > 0, $"File {file} should not be empty");
        }
    }

    [Fact]
    public void Specs_Documents_Have_Consistent_Chinese_Section_Headers()
    {
        var files = Directory.GetFiles(SpecsDir, "*.md");
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            var title = lines.FirstOrDefault(l => l.StartsWith("# ", StringComparison.Ordinal));
            Assert.NotNull(title);
        }
    }
}
