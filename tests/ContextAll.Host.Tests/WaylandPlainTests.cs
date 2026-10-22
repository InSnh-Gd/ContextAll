using ContextAll.Host;

namespace ContextAll.Host.Tests;

public sealed class WaylandPlainTests
{
    [Fact]
    public void Ext_manager_wins_and_charset_plain_text_wins()
    {
        Assert.Equal(
            "ext_data_control_manager_v1",
            WaylandClipboard.PreferredManager(["wl_seat", "zwlr_data_control_manager_v1", "ext_data_control_manager_v1"]));
        Assert.Equal(
            "zwlr_data_control_manager_v1",
            WaylandClipboard.PreferredManager(["wl_seat", "zwlr_data_control_manager_v1"]));
        Assert.Null(WaylandClipboard.PreferredManager(["wl_seat"]));

        Assert.Equal(WaylandClipboard.PlainMime, WaylandClipboard.PreferPlain("text/plain", WaylandClipboard.PlainMime));
        Assert.Equal(WaylandClipboard.PlainMime, WaylandClipboard.PreferPlain(WaylandClipboard.PlainMime, "text/plain"));
        Assert.Equal("text/plain", WaylandClipboard.PreferPlain("", "text/plain"));
        Assert.Equal("", WaylandClipboard.PreferPlain("", "image/png"));
        Assert.True(WaylandClipboard.IsPlainText("text/plain"));
        Assert.True(WaylandClipboard.IsPlainText(WaylandClipboard.PlainMime));
        Assert.False(WaylandClipboard.IsPlainText("image/png"));
    }
}
