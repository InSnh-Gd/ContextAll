namespace ContextAll.Host.Tests;

public sealed class WaylandClipboardTests
{
    [Fact]
    public void A_wayland_clipboard_round_trip_reads_and_writes_plain_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "contextall-wl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var compositor = new WlrCompositor(Path.Combine(root, "wayland.sock"));
        using var clipboard = WaylandClipboard.TryOpen(compositor.Path, root);
        Assert.NotNull(clipboard);
        Assert.True(clipboard!.IsOpen);

        var seen = new List<string>();
        clipboard.PlainText += text => seen.Add(text);
        compositor.SetText("hello");
        Wait(() => seen.Count == 1 && seen[0] == "hello");
        clipboard.SetText("pong");
        Wait(() => compositor.CurrentText == "pong");
        compositor.SetText("");
        Wait(() => seen.Count >= 2 && seen[^1] == "");
    }

    static void Wait(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (ready())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("wayland clipboard did not settle");
    }
}
