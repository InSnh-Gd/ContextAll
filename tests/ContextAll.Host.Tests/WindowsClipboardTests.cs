using System.Threading.Channels;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class WindowsClipboardTests
{
    [Fact]
    public async Task An_external_copy_is_delivered_after_a_busy_read_but_a_local_write_is_not_echoed()
    {
        var desktop = new DesktopClipboard();
        using var source = new WindowsClipboardSource(desktop);
        var changes = Channel.CreateUnbounded<string>();
        source.PlainText += text => changes.Writer.TryWrite(text);

        desktop.Copy("外部文本", busy: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal("外部文本", await changes.Reader.ReadAsync(timeout.Token));

        Assert.True(await source.ApplyTextAsync("远端文本", timeout.Token));
        Assert.Equal("远端文本", desktop.Text);
        await Task.Delay(250, timeout.Token);
        desktop.Copy("下一条", busy: false);
        Assert.Equal("下一条", await changes.Reader.ReadAsync(timeout.Token));
        Assert.False(changes.Reader.TryRead(out _));
    }

    [Fact]
    public async Task A_write_that_cannot_be_represented_does_not_replace_the_clipboard()
    {
        var desktop = new DesktopClipboard();
        desktop.Copy("original", busy: false);
        using var source = new WindowsClipboardSource(desktop);

        Assert.False(await source.ApplyTextAsync("truncated\0content"));
        Assert.False(await source.ApplyTextAsync(new string('x', FrameLimits.MaxClipboardBytes + 1)));
        Assert.Equal("original", desktop.Text);
    }

    sealed class DesktopClipboard : IWindowsClipboard
    {
        readonly object gate = new();
        uint sequence = 1;
        string? text;
        bool busy;

        public uint Sequence { get { lock (gate) { return sequence; } } }
        public string? Text { get { lock (gate) { return text; } } }

        public void Copy(string value, bool busy)
        {
            lock (gate)
            {
                text = value;
                sequence++;
                this.busy = busy;
            }
        }

        public bool TryRead(out uint capturedSequence, out string? value)
        {
            lock (gate)
            {
                capturedSequence = sequence;
                value = text;
                var wasBusy = busy;
                busy = false;
                return !wasBusy;
            }
        }

        public bool TryWrite(string value, out uint writtenSequence)
        {
            lock (gate)
            {
                text = value;
                writtenSequence = ++sequence;
                return true;
            }
        }
    }
}
