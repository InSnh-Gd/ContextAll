using ContextAll.Protocol;

namespace ContextAll.Core.Tests;

public sealed class LocalOperatorTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("0", true)]
    [InlineData("abcdef", true)]
    [InlineData("ABCDEF", false)]
    [InlineData("g", false)]
    [InlineData("76-bad", false)]
    public void Request_id_is_lowercase_hex(string id, bool ok) =>
        Assert.Equal(ok, LocalOperator.RequestIdOk(id));

    [Fact]
    public void Request_id_rejects_more_than_64_hex_chars()
    {
        Assert.True(LocalOperator.RequestIdOk(new string('a', 64)));
        Assert.False(LocalOperator.RequestIdOk(new string('a', 65)));
    }

    [Fact]
    public void Admit_rejects_bad_then_duplicate_ids()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Assert.Equal("invalid", LocalOperator.Admit("BAD!", seen));
        Assert.Empty(seen);
        Assert.Null(LocalOperator.Admit("aa01", seen));
        Assert.Equal("duplicate-request", LocalOperator.Admit("aa01", seen));
        Assert.Equal("duplicate-request", LocalOperator.Remember(seen, "aa01"));
        Assert.Single(seen);
    }

    [Fact]
    public void Validate_connect_clipboard_name_path_and_send_file()
    {
        Assert.Equal("invalid", LocalOperator.Validate(
            FrameTypes.Snapshot,
            new LocalCommandPayload("aa", null, null, null, null, null)));

        Assert.Equal(
            "invalid-endpoint",
            LocalOperator.Validate(
                FrameTypes.ConnectEndpoint,
                new LocalCommandPayload("aa", null, null, null, null, null, Host: "", Port: 1)));
        Assert.Null(LocalOperator.Validate(
            FrameTypes.ConnectEndpoint,
            new LocalCommandPayload("aa", null, null, null, null, null, Host: "192.168.1.50", Port: 4242)));

        Assert.Equal(
            "too-large",
            LocalOperator.Validate(
                FrameTypes.PushClipboardText,
                new LocalCommandPayload("aa", null, null, null, null, null, TotalBytes: FrameLimits.MaxClipboardBytes + 1)));
        Assert.Null(LocalOperator.Validate(
            FrameTypes.PushClipboardText,
            new LocalCommandPayload("aa", null, null, null, null, null, Text: "hi")));

        Assert.Equal(
            "invalid-name",
            LocalOperator.Validate(
                FrameTypes.SetDisplayName,
                new LocalCommandPayload("aa", null, "", null, null, null)));
        Assert.Null(LocalOperator.Validate(
            FrameTypes.SetDisplayName,
            new LocalCommandPayload("aa", null, "Desk", null, null, null)));

        var missing = Path.Combine(Path.GetTempPath(), "contextall-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(
            "invalid-path",
            LocalOperator.Validate(
                FrameTypes.SetDownloadDirectory,
                new LocalCommandPayload("aa", null, null, missing, null, null)));

        Assert.Equal(
            "not-a-file",
            LocalOperator.Validate(
                FrameTypes.SendFile,
                new LocalCommandPayload("aa", "ca1peer", null, null, null, null)));

        var dir = Directory.CreateTempSubdirectory("ca-op-");
        try
        {
            Assert.Equal(
                "not-a-file",
                LocalOperator.Validate(
                    FrameTypes.SendFile,
                    new LocalCommandPayload("aa", "ca1peer", null, dir.FullName, null, null)));
        }
        finally
        {
            dir.Delete(true);
        }

        Assert.Null(LocalOperator.Validate(
            FrameTypes.StartPairing,
            new LocalCommandPayload("aa", "ca1peer", null, null, null, null)));
    }
}
