using ContextAll.Core;
using ContextAll.Protocol;

namespace ContextAll.Host.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void History_requires_opt_in_and_survives_restart_with_original_source()
    {
        var root = Directory.CreateTempSubdirectory("contextall-history-");
        try
        {
            using (var host = SessionHost.CreateIsolated(root.FullName))
            {
                host.PushClipboardText("not retained");
                Assert.Empty(host.ReadClipboardHistory(0).Entries);
                Assert.Null(host.Apply(new LocalCommandPayload("1", null, null, null, true, null), FrameTypes.ConfigureClipboardHistory));
                host.PushClipboardText("retained");
            }

            using var restarted = SessionHost.CreateIsolated(root.FullName);
            var entry = Assert.Single(restarted.ReadClipboardHistory(0).Entries);
            Assert.Equal("retained", entry.Preview);
            Assert.Equal(restarted.LocalDeviceId, entry.Origin);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Older_completed_copy_remains_in_history_without_replacing_current_copy()
    {
        var root = Directory.CreateTempSubdirectory("contextall-history-");
        try
        {
            using var host = SessionHost.CreateIsolated(root.FullName);
            Assert.Null(host.Apply(new LocalCommandPayload("1", null, null, null, true, null), FrameTypes.ConfigureClipboardHistory));
            var origin = DeviceIds.Text("remote"u8.ToArray());
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            host.Clipboard.ReceiveDelta(origin, new DeltaPayload(origin, 2, now, 0, "newer", null, null, null), true);
            host.Clipboard.ReceiveDelta(origin, new DeltaPayload(origin, 1, now - 1, 0, "older", null, null, null), true);
            host.Clipboard.ReceiveDelta(origin, new DeltaPayload(origin, 1, now - 1, 0, "older", null, null, null), true);

            Assert.Equal("newer", host.Clipboard.Text);
            Assert.Equal(new[] { "newer", "older" }, host.ReadClipboardHistory(0).Entries.Select(entry => entry.Preview));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
