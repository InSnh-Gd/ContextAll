using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class TransfersAndQueueTests
{
    [Fact]
    public async Task Enqueueing_three_regular_files_dispatches_one_by_one_per_peer()
    {
        var tempDir = Directory.CreateTempSubdirectory("ca-queue-seq-");
        try
        {
            var f1 = Path.Combine(tempDir.FullName, "one.txt");
            var f2 = Path.Combine(tempDir.FullName, "two.txt");
            var f3 = Path.Combine(tempDir.FullName, "three.txt");
            await File.WriteAllTextAsync(f1, "1");
            await File.WriteAllTextAsync(f2, "2");
            await File.WriteAllTextAsync(f3, "3");

            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("PEER-1", "台式机", true, "online", true, true);
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [peer],
            });

            var vm = new TransfersViewModel(client);
            var enqueued = vm.EnqueueFilesForPeer("PEER-1", [f1, f2, f3]);
            Assert.Equal(3, enqueued);

            await Task.Delay(30);
            Assert.Single(client.SentCommands, c => c.FrameType == FrameTypes.SendFile);
            Assert.Equal(f1, client.SentCommands.Single().Payload.Path);
            Assert.Equal(3, vm.PendingQueueItems.Count);

            // File 1 becomes active then ends
            client.EmitState(client.CurrentState with
            {
                ActiveTransfers = [new TransferSnapshotPayload("t1", "PEER-1", "send", "one.txt", 1, 2, false, 10)],
            });
            await Task.Delay(20);
            Assert.Single(client.SentCommands);

            client.EmitState(client.CurrentState with
            {
                ActiveTransfers = [],
                History = [new LocalTransferHistoryEntry("t1", "PEER-1", "台式机", "send", "one.txt", 10, "done", DateTimeOffset.UtcNow, "")],
            });
            await Task.Delay(30);
            Assert.Equal(2, client.SentCommands.Count(c => c.FrameType == FrameTypes.SendFile));
            Assert.Equal(f2, client.SentCommands[1].Payload.Path);

            // File 2 finishes via history entry
            client.EmitState(client.CurrentState with
            {
                History =
                [
                    new LocalTransferHistoryEntry("t2", "PEER-1", "台式机", "send", "two.txt", 10, "done", DateTimeOffset.UtcNow.AddSeconds(1), ""),
                    new LocalTransferHistoryEntry("t1", "PEER-1", "台式机", "send", "one.txt", 10, "done", DateTimeOffset.UtcNow, ""),
                ],
            });
            await Task.Delay(30);
            Assert.Equal(3, client.SentCommands.Count(c => c.FrameType == FrameTypes.SendFile));
            Assert.Equal(f3, client.SentCommands[2].Payload.Path);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public Task Mixed_drop_with_directory_skips_folder_with_explanation_and_sends_regular_file() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var tempDir = Directory.CreateTempSubdirectory("ca-queue-dir-");
            try
            {
                var folder = Directory.CreateDirectory(Path.Combine(tempDir.FullName, "PhotosFolder")).FullName;
                var file = Path.Combine(tempDir.FullName, "report.pdf");
                await File.WriteAllTextAsync(file, "pdf");

                var client = new FakeLocalDaemonClient();
                client.EmitState(LocalShellState.Empty with
                {
                    ConnectionStatus = LocalConnectionStatus.Connected,
                    Devices = [new DeviceSnapshotPayload("PEER-A", "A", true, "online", false, true)],
                });

                var vm = new TransfersViewModel(client);
                var view = new TransfersView(vm);

                var count = vm.EnqueueFilesForPeer("PEER-A", [folder, file]);
                Assert.Equal(1, count);
                Assert.True(view.IsNoticeVisible);
                Assert.Contains("PhotosFolder", view.NoticeText);

                await Task.Delay(25);
                Assert.Single(client.SentCommands);
                Assert.Equal(file, client.SentCommands[0].Payload.Path);
            }
            finally
            {
                tempDir.Delete(recursive: true);
            }
        });

    [Fact]
    public Task TransfersView_supports_prompt_decision_active_cancel_and_history_folder_open() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var prompt = new FilePromptSnapshotPayload("tx-prompt", "PEER-1", "notes.md", 2048, 25000);
            var active = new TransferSnapshotPayload("tx-active", "PEER-1", "receive", "archive.tar", 3, 10, false, 10240);
            var history = new LocalTransferHistoryEntry("tx-done", "PEER-1", "台式机", "receive", "photo.png", 4096, "done", DateTimeOffset.UtcNow, "/home/user/Downloads");

            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [new DeviceSnapshotPayload("PEER-1", "台式机", true, "online", true, true)],
                FilePrompts = [prompt],
                ActiveTransfers = [active],
                History = [history],
            });

            string? openedFolder = null;
            var vm = new TransfersViewModel(client, openFolder: dir => openedFolder = dir);
            var view = new TransfersView(vm);

            Assert.Equal(1, view.RenderedActiveTransferCount);
            Assert.Equal(1, view.RenderedHistoryCount);

            Assert.True(await vm.AcceptFileAsync("tx-prompt"));
            Assert.True(await vm.RejectFileAsync("tx-prompt"));
            Assert.True(await vm.CancelTransferAsync("tx-active"));
            Assert.True(vm.OpenContainingFolder(history));
            Assert.Equal("/home/user/Downloads", openedFolder);

            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.AcceptFile && c.Payload.TransferId == "tx-prompt");
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.RejectFile && c.Payload.TransferId == "tx-prompt");
            Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.CancelTransfer && c.Payload.TransferId == "tx-active");
        });
}
