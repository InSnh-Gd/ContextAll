using Avalonia.Controls;
using ContextAll.App.Controls;
using ContextAll.App.Platform;
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
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1160, 760);
            try
            {
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
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void ShellViewModel_RailFabAction_switches_to_transfers_or_emits_snackbar_when_not_ready()
    {
        var client = new FakeLocalDaemonClient();
        var daemon = new FakeStubDaemonController();
        var vm = new ShellViewModel(client, daemon, new ShellPreferences(), _ => { });

        var snackbars = new List<(string Message, string? ActionText, Action? Action)>();
        vm.SnackbarRequested += (msg, act, cb) => snackbars.Add((msg, act, cb));

        // Disconnected -> emits snackbar prompt and stays on Devices
        vm.TriggerRailFab();
        Assert.Single(snackbars);
        Assert.Equal(ShellSection.Devices, vm.SelectedSection);

        // Connected without trusted peers -> emits snackbar with connection action
        client.EmitState(LocalShellState.Empty with
        {
            ConnectionStatus = LocalConnectionStatus.Connected,
            Devices = [new DeviceSnapshotPayload("DISC-1", "发现设备", false, "online", null, null)],
        });
        vm.TriggerRailFab();
        Assert.Equal(2, snackbars.Count);
        Assert.NotNull(snackbars[1].Action);
        snackbars[1].Action?.Invoke();
        Assert.Equal(ShellSection.Connection, vm.SelectedSection);

        // Connected with trusted selected peer -> selects target peer and switches to Transfers
        client.EmitState(LocalShellState.Empty with
        {
            ConnectionStatus = LocalConnectionStatus.Connected,
            Devices = [new DeviceSnapshotPayload("PEER-READY", "主力机", true, "online", true, true)],
        });
        vm.Devices.SelectDevice("PEER-READY");
        vm.TriggerRailFab();
        Assert.Equal(ShellSection.Transfers, vm.SelectedSection);
        Assert.Equal("PEER-READY", vm.Transfers.SelectedTargetPeerId);
    }

    private sealed class FakeStubDaemonController : IDaemonController
    {
        public bool IsRunningFromAppImage => false;
        public Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonStartResult(DaemonStartOutcome.Started));
        public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));
        public Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));
    }
}
