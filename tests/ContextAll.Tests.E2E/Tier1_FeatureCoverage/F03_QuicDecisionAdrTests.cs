using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F03")]
public sealed class F03_QuicDecisionAdrTests
{
    static string AdrFilePath => Path.Combine(NetworkSandbox.FindRepoRoot(), "docs/adr/0004-quic-android-transport.md");

    [Fact]
    public void Adr0004_NamingConvention_Is_Valid()
    {
        Assert.True(File.Exists(AdrFilePath), $"ADR 0004 must exist on disk at {AdrFilePath}");
        var fileName = Path.GetFileName(AdrFilePath);
        Assert.StartsWith("0004-", fileName, StringComparison.Ordinal);
        Assert.EndsWith(".md", fileName, StringComparison.Ordinal);
    }

    [Fact]
    public void Adr0004_RequiredSections_Contract_Is_Defined()
    {
        Assert.True(File.Exists(AdrFilePath), $"ADR 0004 must exist on disk at {AdrFilePath}");
        var content = File.ReadAllText(AdrFilePath);
        Assert.Contains("# Android 端采用 QUIC 且不设降级传输", content);
        Assert.Contains("状态：已接受", content);
        Assert.Contains("libmsquic.so", content);
        Assert.Contains("DllNotFoundException", content);
        Assert.Contains("协议等价性", content);
    }

    [Fact]
    public void Adr0004_NoFallback_Rule_Mandate_Is_Preserved()
    {
        Assert.True(File.Exists(AdrFilePath), $"ADR 0004 must exist on disk at {AdrFilePath}");
        var content = File.ReadAllText(AdrFilePath);
        Assert.Contains("不为 Android 端提供 TCP、WebSocket、HTTP 或本地套接字等降级传输方案", content);
        Assert.Contains("破坏与桌面守护进程间的协议等价性", content);
    }

    [Fact]
    public void Adr0004_Port_And_Alpn_Are_Documented()
    {
        Assert.True(File.Exists(AdrFilePath), $"ADR 0004 must exist on disk at {AdrFilePath}");
        var content = File.ReadAllText(AdrFilePath);
        Assert.Contains("42461", content);
        Assert.Contains("contextall", content);
    }

    [Fact]
    public void Adr0004_WhenFileExists_Contains_Mandatory_Keywords()
    {
        Assert.True(File.Exists(AdrFilePath), $"ADR 0004 must exist on disk at {AdrFilePath}");
        var content = File.ReadAllText(AdrFilePath);
        Assert.Contains("DllNotFoundException", content);
        Assert.Contains("libSystem.Security.Cryptography.Native.OpenSsl", content);
        Assert.Contains("42461", content);
        Assert.Contains("contextall", content);
        Assert.Contains("QuicListener.IsSupported", content);
    }
}
