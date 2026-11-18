using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextAll.App.Chrome;
using ContextAll.App.Controls;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eAxamlViewsTests
{
    [Fact]
    public Task ShellRootView_axaml_arranges_wide_and_narrow_chrome_corrupt_dialog_and_snackbar() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var daemon = new FakeDaemonController
            {
                NextStartResult = new DaemonStartResult(DaemonStartOutcome.CorruptState, "配置损坏"),
            };
            var vm = new ShellViewModel(client, daemon, new ShellPreferences(), _ => { });
            var root = new ShellRootView(vm);

            // Wide window (1280x800 >= 840) shows rail, hides bottom floating toolbar
            vm.ApplyWidth(1280);
            root.Measure(new Size(1280, 800));
            root.Arrange(new Rect(0, 0, 1280, 800));
            Assert.True(root.IsRailVisible);
            Assert.False(root.IsDockVisible);

            // Narrow window (520x720 < 840) shows HorizontalFloatingToolbar, hides rail
            vm.ApplyWidth(520);
            root.Measure(new Size(520, 720));
            root.Arrange(new Rect(0, 0, 520, 720));
            Assert.False(root.IsRailVisible);
            Assert.True(root.IsDockVisible);

            // Snackbar integration via ViewModel
            vm.RequestSnackbar("已复制端点地址");
            Assert.True(root.SnackbarHost.IsOpen);
            Assert.Equal("已复制端点地址", root.SnackbarHost.CurrentMessage);

            // Corrupt state opens MaterialDialog defaulting focus to Cancel
            Assert.False(await vm.StartDaemonExplicitAsync());
            Assert.True(root.CorruptDialog.IsOpen);
            Assert.True(root.CorruptDialog.DefaultFocusCancel);
            root.CorruptDialog.InvokeCancel();
            Assert.False(vm.IsCorruptStateDialogOpen);
            Assert.Equal(0, daemon.ResetCalls);
        });

    [Fact]
    public void ShellRootView_shows_only_the_selected_section_on_first_layout() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var vm = new ShellViewModel(
                new FakeLocalDaemonClient(),
                new FakeDaemonController(),
                new ShellPreferences(),
                _ => { });
            var root = new ShellRootView(vm);
            vm.ApplyWidth(1280);
            root.Measure(new Size(1280, 800));
            root.Arrange(new Rect(0, 0, 1280, 800));

            var devices = root.FindControl<ContentControl>("DevicesSlot");
            var connection = root.FindControl<ContentControl>("ConnectionSlot");
            var transfers = root.FindControl<ContentControl>("TransfersSlot");
            var thisDevice = root.FindControl<ContentControl>("ThisDeviceSlot");
            Assert.NotNull(devices);
            Assert.NotNull(connection);
            Assert.NotNull(transfers);
            Assert.NotNull(thisDevice);
            Assert.True(devices.IsVisible);
            Assert.False(connection.IsVisible);
            Assert.False(transfers.IsVisible);
            Assert.False(thisDevice.IsVisible);
        });

    [Fact]
    public void ShellRootView_native_ssd_chrome_hides_header_and_drops_card_border() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var root = new ShellRootView(new ShellViewModel(
                new FakeLocalDaemonClient(),
                new FakeDaemonController(),
                new ShellPreferences(),
                _ => { }));

            root.ApplyChrome(ChromeModeState.Compute(
                useNativeTitleBar: true,
                WindowState.Normal,
                systemTitleBarActive: true));
            Assert.False(root.IsHeaderVisible);
            Assert.Equal(new Thickness(0), root.ChromeBorderThickness);

            root.ApplyChrome(ChromeModeState.Compute(useNativeTitleBar: false, WindowState.Normal));
            Assert.True(root.IsHeaderVisible);
            Assert.Equal(new Thickness(1), root.ChromeBorderThickness);
        });

    [Fact]
    public Task Title_bar_feedback_tracks_runtime_negotiation_without_persisting_the_result() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var directory = Directory.CreateTempSubdirectory("contextall-chrome-");
            var path = Path.Combine(directory.FullName, "shell.json");
            var window = new MainWindow(new ShellPreferences(), path);
            try
            {
                var root = Assert.IsType<ShellRootView>(window.FindControl<ContentControl>("ShellHost")?.Content);
                var vm = Assert.IsType<ShellViewModel>(root.DataContext);
                var description = Assert.IsType<TextBlock>(root.ThisDeviceView.FindControl<TextBlock>("TitleBarDescription"));
                var toggle = Assert.IsType<ExpressiveSwitch>(root.ThisDeviceView.FindControl<ExpressiveSwitch>("NativeTitleBarSwitch"));

                Assert.Equal("正在使用应用标题栏。", description.Text);
                vm.ThisDevice.SetUseNativeTitleBar(true);
                Assert.True(toggle.IsChecked);
                Assert.Equal("正在请求系统标题栏…", description.Text);
                Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
                Assert.True(ShellPreferencesStore.Load(path).UseNativeTitleBar);
                var saved = File.ReadAllText(path);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal(root.IsHeaderVisible ? "当前桌面未提供系统标题栏，正在使用应用标题栏。" : "正在使用系统标题栏。", description.Text);

                root.ApplyChrome(ChromeModeState.Compute(useNativeTitleBar: true, WindowState.Normal, systemTitleBarActive: true));
                Assert.Equal("正在使用系统标题栏。", description.Text);
                Assert.False(root.IsHeaderVisible);
                root.ApplyChrome(ChromeModeState.Compute(useNativeTitleBar: true, WindowState.Normal));
                Assert.Equal("当前桌面未提供系统标题栏，正在使用应用标题栏。", description.Text);
                Assert.True(root.IsHeaderVisible);
                Assert.True(toggle.IsChecked);
                Assert.Equal(saved, File.ReadAllText(path));

                vm.ThisDevice.SetUseNativeTitleBar(false);
                Assert.False(toggle.IsChecked);
                Assert.Equal("正在使用应用标题栏。", description.Text);
                Assert.False(ShellPreferencesStore.Load(path).UseNativeTitleBar);

                var startupWindow = new MainWindow(new ShellPreferences(UseNativeTitleBar: true), path);
                try { Assert.Equal(WindowDecorations.Full, startupWindow.WindowDecorations); }
                finally { startupWindow.ForceQuit(); }
            }
            finally
            {
                window.ForceQuit();
                directory.Delete(recursive: true);
            }
        });

    [Fact]
    public void DevicesView_tracks_live_theme_variant_changes_on_detail_hero_card() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var app = Assert.IsType<App>(Application.Current);
            app.ApplyAppearanceOverrides(AppearanceModeOverride.Light, AccentSourceOverride.BrandBlue);

            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-T", "测试设备", true, "online", true, true, true, "10.0.0.1:42461");
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices = [peer],
            });

            var vm = new DevicesViewModel(client);
            vm.SelectDevice("DEV-T");
            var view = new DevicesView(vm);
            var window = new Window { Width = 1100, Height = 720, Content = view };
            window.Measure(new Size(1100, 720));
            window.Arrange(new Rect(0, 0, 1100, 720));

            var heroBefore = view.GetLogicalDescendants()
                .OfType<Border>()
                .First(b => b.CornerRadius == new CornerRadius(24) && b.Background is ISolidColorBrush);
            var lightHeroColor = Assert.IsAssignableFrom<ISolidColorBrush>(heroBefore.Background).Color;

            app.ApplyAppearanceOverrides(AppearanceModeOverride.Dark, AccentSourceOverride.ForestTeal);

            var heroAfter = view.GetLogicalDescendants()
                .OfType<Border>()
                .First(b => b.CornerRadius == new CornerRadius(24) && b.Background is ISolidColorBrush);
            var darkHeroColor = Assert.IsAssignableFrom<ISolidColorBrush>(heroAfter.Background).Color;

            Assert.NotEqual(lightHeroColor, darkHeroColor);

            app.ApplyAppearanceOverrides(AppearanceModeOverride.FollowSystem, AccentSourceOverride.FollowSystem);
        });

    [Fact]
    public void DevicesView_g1_expressive_studio_validates_dual_sheet_empty_state_real_telemetry_and_broadcast_dialog() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1160, 740);
            try
            {
                // Empty state: shows unified empty panel with ContainedLoadingIndicator, hides section headers
                vm.ApplyWidth(1280);
                AvaloniaUiTestHost.Layout(window, 1160, 740);
                Assert.False(view.IsEmptyStateVisible);
                var ready = LocalShellState.Empty with { ConnectionStatus = LocalConnectionStatus.Connected, LocalAddresses = ["10.0.0.2"] };
                client.EmitState(ready);
                Assert.True(view.IsEmptyStateVisible);
                Assert.Null(view.FindControl<Button>("QuickClipboardOpenButton"));
                Assert.NotNull(view.FindControl<Button>("GlobalPushClipboardButton"));
                var manual = view.GetVisualDescendants().OfType<Button>().Where(button => Equals(button.Content, "手动连接")).ToArray();
                var navigate = false;
                view.NavigateToConnectionRequested += () => navigate = true;
                foreach (var width in new[] { 1280, 480 })
                {
                    vm.ApplyWidth(width);
                    var link = Assert.Single(manual, button => button.IsEffectivelyVisible);
                    link.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Assert.True(navigate);
                    navigate = false;
                }
                client.EmitState(ready with { PairingPrompt = new PairingSnapshotPayload("DEV-B", "新手机", "482913", 30_000) });
                Assert.True(view.IsPairingBannerVisible);
                Assert.DoesNotContain(manual, button => button.IsEffectivelyVisible);
                vm.ApplyWidth(1280);

                // Populate trusted (ClipboardSync = false) + discovered + real transfer history
                var syncOffPeer = new DeviceSnapshotPayload("DEV-A", "工作站", true, "online", false, true, true, "192.168.1.5:42461");
                var discoveredPeer = new DeviceSnapshotPayload("DEV-B", "新手机", false, "online", null, null, null, "192.168.1.9:42461");
                var history = new LocalTransferHistoryEntry("t1", "DEV-A", "工作站", "receive", "notes.txt", 512, "done", DateTimeOffset.UtcNow, "/tmp");

                client.EmitState(LocalShellState.Empty with
                {
                    ConnectionStatus = LocalConnectionStatus.Connected,
                    Devices = [syncOffPeer, discoveredPeer],
                    History = [history],
                });

                AvaloniaUiTestHost.Layout(window, 1160, 740);
                Assert.False(view.IsEmptyStateVisible);
                Assert.True(view.FindControl<StackPanel>("EmptyDetailPlaceholder")?.IsEffectivelyVisible);
                Assert.True(view.IsListPaneVisible);
                Assert.True(view.IsDetailPaneVisible);

                // Narrow (< 840) drill-in
                vm.ApplyWidth(520);
                Assert.True(view.IsListPaneVisible);
                Assert.False(view.IsDetailPaneVisible);

                vm.SelectDevice("DEV-A");
                Assert.False(view.IsListPaneVisible);
                Assert.True(view.IsDetailPaneVisible);

                AvaloniaUiTestHost.Layout(window, 500, 680);

                var renderedTexts = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Empty)
                    .ToArray();

                // Zero fabricated telemetry anywhere in rendered tree
                Assert.DoesNotContain(renderedTexts, t => t.Contains("RTT", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(renderedTexts, t => t.Contains("最近活跃", StringComparison.Ordinal));
                Assert.DoesNotContain(renderedTexts, t => t.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));

                // Real history and endpoint appear
                Assert.Contains(renderedTexts, t => t.Contains("notes.txt", StringComparison.Ordinal));
                Assert.Contains(renderedTexts, t => t.Contains("192.168.1.5:42461", StringComparison.Ordinal));

                // 1.A clipboard broadcast MaterialDialog clarifies broadcast + disabled peer notice
                vm.OpenClipboardModal();
                Assert.True(view.ClipboardDialog.IsOpen);
                Assert.Contains("广播", view.ClipboardDialog.Message);
                Assert.Contains("关闭剪贴板同步", view.ClipboardDialog.Message);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void ConnectionView_axaml_binds_endpoints_backend_and_material_guide_dialog() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalAddresses = ["10.0.0.8"],
                QuicPort = 42461,
                ClipboardBackend = "wayland",
            });

            var guideSeen = false;
            var vm = new ConnectionViewModel(client, seen => guideSeen = seen);
            var view = new ConnectionView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1100, 720);
            Assert.Contains("Wayland 原生", view.ClipboardStatusText);

            foreach (var (field, label) in new[] { ("EndpointBox", "对方地址"), ("AliasBox", "设备备注（选填）") })
            {
                var input = Assert.IsType<TextBox>(view.FindControl<TextBox>(field));
                Assert.Equal(new CornerRadius(20), input.CornerRadius);
                Assert.Equal(new Thickness(1), input.BorderThickness);
                Assert.Equal(56, input.MinHeight);
                Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == label && text.IsVisible);
                var outline = Assert.Single(input.GetVisualDescendants().OfType<Border>(), border => border.Name == "PART_BorderElement");
                Assert.True(input.Focus());
                Assert.Equal(new Thickness(2), outline.BorderThickness);
                Assert.Equal(new CornerRadius(20), outline.CornerRadius);
            }

            vm.OpenGuide();
            Assert.True(view.GuideDialog.IsOpen);
            view.GuideDialog.InvokePrimaryAction();
            Assert.False(view.IsGuideVisible);
            Assert.True(guideSeen);
            window.Close();
        });

    [Fact]
    public void Connection_workspace_follows_shell_width_without_losing_inputs() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var vm = new ShellViewModel(new FakeLocalDaemonClient(), new FakeDaemonController(), new ShellPreferences(), _ => { });
            var root = new ShellRootView(vm);
            vm.SelectedSection = ShellSection.Connection;
            var view = Assert.IsType<ConnectionView>(root.FindControl<ContentControl>("ConnectionSlot")?.Content);
            var endpoint = Assert.IsType<TextBox>(view.FindControl<TextBox>("EndpointBox"));
            var alias = Assert.IsType<TextBox>(view.FindControl<TextBox>("AliasBox"));
            var actions = Assert.IsAssignableFrom<Control>(view.FindControl<Control>("HeaderActions"));
            endpoint.Text = "10.0.0.1";
            alias.Text = "工作机";
            foreach (var width in new[] { 839, 840, 480, 1280 })
            {
                vm.ApplyWidth(width);
                root.Measure(new Size(width, 640));
                root.Arrange(new Rect(0, 0, width, 640));
                Assert.Equal(width >= 840, view.IsSplitLayout);
                Assert.Equal(width >= 840 ? 0 : 1, Grid.GetRow(actions));
                Assert.Equal("10.0.0.1", endpoint.Text);
                Assert.Equal("工作机", alias.Text);
            }
        });

    [Fact]
    public void Settings_categories_share_editors_and_preserve_the_open_rename_draft() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var vm = new ShellViewModel(new FakeLocalDaemonClient(), new FakeDaemonController(), new ShellPreferences(), _ => { });
            var root = new ShellRootView(vm);
            vm.SelectedSection = ShellSection.ThisDevice;
            var view = root.ThisDeviceView;
            var window = AvaloniaUiTestHost.ShowAndLayout(root, 840, 640);
            try
            {
                var nav = Assert.IsType<ListBox>(view.FindControl<ListBox>("Categories"));
                var sections = new[] { "IdentitySection", "AppearanceSection", "ServiceSection", "WindowSection", "HelpSection" }
                    .Select(name => Assert.IsAssignableFrom<Control>(view.FindControl<Control>(name))).ToArray();
                for (var index = 0; index < sections.Length; index++)
                {
                    nav.SelectedIndex = index;
                    Assert.Equal(sections[index], Assert.Single(sections, section => section.IsEffectivelyVisible));
                }
                nav.SelectedIndex = 0;
                view.FindControl<Button>("EditNameButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                var input = Assert.IsType<TextBox>(view.FindControl<TextBox>("NameInput"));
                input.Text = "尚未保存的名称";
                vm.ApplyWidth(839);
                Assert.False(nav.IsVisible);
                Assert.All(sections, section => Assert.True(section.IsEffectivelyVisible));
                vm.ApplyWidth(840);
                var rename = Assert.IsType<MaterialDialog>(view.FindControl<MaterialDialog>("RenameDialogControl"));
                Assert.True(rename.IsOpen);
                Assert.Equal("尚未保存的名称", input.Text);
                rename.InvokeCancel();

                vm.ApplyWidth(839);
                AvaloniaUiTestHost.Layout(window, 839, 640);
                var windowOption = Assert.IsType<ExpressiveGroupTile>(view.FindControl<Control>("NativeTitleBarTile"));
                Assert.True(windowOption.Focus());
                vm.ApplyWidth(840);
                Assert.Equal(3, nav.SelectedIndex);
                Assert.True(windowOption.IsFocused);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void Settings_rows_grow_for_long_text_and_keep_group_corners() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var view = new ThisDeviceView();
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 480, 640);
            try
            {
                var tile = Assert.IsType<ExpressiveGroupTile>(view.FindControl<Control>("NameTile"));
                var label = Assert.IsType<TextBlock>(view.FindControl<TextBlock>("DisplayNameLabel"));
                Assert.Equal(16, tile.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Text == "本机名称").FontSize);
                Assert.Equal(14, label.FontSize);
                Assert.Equal(72, tile.MinHeight);
                Assert.Equal(2, Assert.IsType<StackPanel>(tile.Parent).Spacing);
                Assert.Equal(new Thickness(0), tile.BorderThickness);
                label.Text = new string('名', 240);
                AvaloniaUiTestHost.Layout(window, 480, 640);
                Assert.True(tile.Bounds.Height > 72);
                foreach (var (position, corners) in new[] { (GroupPosition.First, new CornerRadius(24,24,6,6)), (GroupPosition.Middle, new CornerRadius(6)), (GroupPosition.Last, new CornerRadius(6,6,24,24)), (GroupPosition.Single, new CornerRadius(24)) })
                {
                    tile.Transitions = null;
                    tile.Position = position;
                    Assert.Equal(corners, tile.CornerRadius);
                }
            }
            finally { window.Close(); }
        });

    [Fact]
    public Task TransfersView_g2_split_workbench_switches_wide_split_and_narrow_stack_and_renders_wavy_progress() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-1", "主力笔记本", true, "online", true, true, true, "192.168.1.20:42461");
            var prompt = new FilePromptSnapshotPayload("t-in-1", "DEV-1", "photo.png", 4096, 30_000);
            var active = new TransferSnapshotPayload("t-act-1", "DEV-1", "send", "movie.mp4", 4, 10, false, 10_000);
            var history = new LocalTransferHistoryEntry("t-old-1", "DEV-1", "主力笔记本", "receive", "doc.pdf", 2048, "done", DateTimeOffset.UtcNow, "/tmp");

            var vm = new TransfersViewModel(client);
            var view = new TransfersView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1160, 760);
            try
            {
                void AssertTotalsFollowHistory()
                {
                    var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
                    var historyTitle = texts.Single(text => text.Text == "传输记录");
                    foreach (var name in new[] { "KpiSentValue", "KpiReceivedValue", "KpiCompletedValue" })
                    {
                        var total = Assert.Single(texts, text => text.Name == name);
                        Assert.True(total.TranslatePoint(default, view)?.Y > historyTitle.TranslatePoint(default, view)?.Y);
                    }
                }
                AssertTotalsFollowHistory();
                // Wide (>= 840) enables g2-split-workbench dual-sheet split; narrow (< 840) stacks vertically
                view.ApplyResponsiveLayout(1160);
                AvaloniaUiTestHost.Layout(window, 1160, 760);
                Assert.True(view.IsSplitLayout);

                view.ApplyResponsiveLayout(520);
                AvaloniaUiTestHost.Layout(window, 520, 720);
                Assert.False(view.IsSplitLayout);

                view.ApplyResponsiveLayout(1160);
                AvaloniaUiTestHost.Layout(window, 1160, 760);

                client.EmitState(LocalShellState.Empty with
                {
                    ConnectionStatus = LocalConnectionStatus.Connected,
                    Devices = [peer],
                    FilePrompts = [prompt],
                    ActiveTransfers = [active],
                    History = [history],
                });

                AvaloniaUiTestHost.Layout(window, 1160, 760);
                Assert.Equal(1, view.RenderedActiveTransferCount);
                Assert.Equal(1, view.RenderedHistoryCount);
                AssertTotalsFollowHistory();

                var wavy = Assert.Single(view.GetVisualDescendants().OfType<LinearWavyProgressIndicator>());
                Assert.Equal(10, wavy.Maximum);
                Assert.Equal(4, wavy.Value);

                var renderedTexts = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text ?? string.Empty)
                    .ToArray();
                Assert.Contains(renderedTexts, t => t.Contains("photo.png", StringComparison.Ordinal));
                Assert.Contains(renderedTexts, t => t.Contains("movie.mp4", StringComparison.Ordinal));
                Assert.Contains(renderedTexts, t => t.Contains("doc.pdf", StringComparison.Ordinal));

                Assert.True(await vm.AcceptFileAsync("t-in-1"));
                Assert.Contains(client.SentCommands, c => c.FrameType == FrameTypes.AcceptFile && c.Payload.TransferId == "t-in-1");
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void ThisDeviceView_axaml_exposes_appearance_and_accent_overrides_with_zero_reset_controls() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDeviceId = "DEV-LOCAL-01",
                LocalDisplayName = "办公台式机",
                ResolvedDownloadDirectory = "/home/user/Downloads",
            });

            var daemon = new FakeDaemonController();
            var saved = new List<ShellPreferences>();
            var vm = new ThisDeviceViewModel(client, daemon, new ShellPreferences(), saved.Add);
            var view = new ThisDeviceView(vm);

            view.Measure(new Size(960, 760));
            view.Arrange(new Rect(0, 0, 960, 760));

            Assert.Equal(AppearanceModeOverride.FollowSystem, view.SelectedAppearanceMode);
            Assert.Equal(AccentSourceOverride.FollowSystem, view.SelectedAccentSource);

            view.SelectAppearanceMode(AppearanceModeOverride.Dark);
            view.SelectAccentSource(AccentSourceOverride.ForestTeal);

            Assert.Equal(AppearanceModeOverride.Dark, view.SelectedAppearanceMode);
            Assert.Equal(AccentSourceOverride.ForestTeal, view.SelectedAccentSource);
            Assert.True(saved.Count >= 2);
            Assert.Equal(AppearanceModeOverride.Dark, saved[^1].AppearanceModeOverride);
            Assert.Equal(AccentSourceOverride.ForestTeal, saved[^1].AccentSourceOverride);

            var allTexts = view.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Select(t => t.Text ?? string.Empty)
                .Concat(view.GetLogicalDescendants().OfType<Button>().Select(b => b.Content?.ToString() ?? string.Empty))
                .ToArray();
            Assert.DoesNotContain(allTexts, t => t.Contains("重置", StringComparison.Ordinal));
            Assert.Contains(allTexts, t => t.Contains("DEV-LOCAL-01", StringComparison.Ordinal));
        });

    [Fact]
    public void All_views_render_capsule_action_buttons_with_uniform_pill_radii_and_un_stretched_heights() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var peer = new DeviceSnapshotPayload("DEV-1", "主力工作站", true, "online", true, true, true, "192.168.1.10:42461");
            var discovered = new DeviceSnapshotPayload("DEV-2", "Pixel 9", false, "online", null, null, null, "192.168.1.11:42461");
            var prompt = new FilePromptSnapshotPayload("tr-1", "DEV-1", "very-long-file-name-for-layout-wrap-check.tar.zst", 2048, 30_000);
            var active = new TransferSnapshotPayload("tr-2", "DEV-1", "send", "archive.zip", 2, 5, false, 10_000);
            var history = new LocalTransferHistoryEntry("tr-0", "DEV-1", "主力工作站", "receive", "notes.txt", 512, "done", DateTimeOffset.UtcNow, "/tmp");

            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                LocalDeviceId = "DEV-SELF",
                LocalDisplayName = "Workstation",
                LocalAddresses = ["192.168.1.50"],
                QuicPort = 42461,
                Devices = [peer, discovered],
                FilePrompts = [prompt],
                ActiveTransfers = [active],
                History = [history],
            });

            var tdView = new ThisDeviceView(new ThisDeviceViewModel(client, new FakeDaemonController(), new ShellPreferences(), _ => { }));
            var connView = new ConnectionView(new ConnectionViewModel(client));
            var trView = new TransfersView(new TransfersViewModel(client));
            var devVm = new DevicesViewModel(client);
            devVm.SelectDevice("DEV-2");
            var devView = new DevicesView(devVm);

            foreach (Control view in new Control[] { tdView, connView, trView, devView })
            {
                var win = new Window { Width = 1040, Height = 820, Content = view };
                win.Measure(new Size(1040, 820));
                win.Arrange(new Rect(0, 0, 1040, 820));

                foreach (var btn in view.GetLogicalDescendants().OfType<Button>())
                {
                    if (!btn.IsVisible || btn.Bounds.Height <= 0)
                    {
                        continue;
                    }

                    Assert.True(
                        btn.Bounds.Height <= 46.0,
                        $"Button '{btn.Name ?? btn.Content?.ToString()}' stretched to height {btn.Bounds.Height:F1}dp in {view.GetType().Name}.");

                    if (btn.GetVisualAncestors().OfType<ExpressiveButtonGroup>().Any())
                    {
                        continue;
                    }

                    if (btn.Classes.Any(c => c is "Filled" or "FilledTonal" or "Outlined") && btn.Bounds.Height >= 38.0)
                    {
                        Assert.Equal(new CornerRadius(20), btn.CornerRadius);
                    }
                }
            }
        });

    [Fact]
    public void DevicesView_keeps_long_names_and_addresses_clear_of_status_and_pairing_controls() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            const string trustedName = "已配对设备 · 很长的设备名称用于检查列表宽度和状态徽标";
            const string discoveredName = "sdk_gphone64_x86_64 · ContextAll · Long device name";
            const string address = "[fe80:1234:5678:9abc:def0:1234:5678:9abc]:42461";
            var client = new FakeLocalDaemonClient();
            client.EmitState(LocalShellState.Empty with
            {
                ConnectionStatus = LocalConnectionStatus.Connected,
                Devices =
                [
                    new DeviceSnapshotPayload("trusted-device", trustedName, true, "online", true, true, true, address),
                    new DeviceSnapshotPayload("nearby-device", discoveredName, false, "reachable", null, null, null, address),
                ],
            });
            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1280, 800);
            try
            {
                foreach (var width in new[] { 1280, 480 })
                {
                    vm.ApplyWidth(width);
                    AvaloniaUiTestHost.Layout(window, width, 800);
                    foreach (var (name, status) in new[] { (trustedName, "已连接"), (discoveredName, "局域网可达") })
                    {
                        var title = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == name && text.IsEffectivelyVisible);
                        var row = title.GetVisualAncestors().OfType<ExpressiveGroupTile>().First();
                        var badge = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == status);
                        var badgeBorder = Assert.IsType<Border>(badge.Parent);
                        var pair = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "配对"));
                        var right = pair.IsEffectivelyVisible ? pair.TranslatePoint(default, row)?.X : row.Bounds.Width;
                        Assert.True(title.TranslatePoint(new Point(title.Bounds.Width, 0), row)?.X <= badgeBorder.TranslatePoint(default, row)?.X);
                        Assert.True(badgeBorder.TranslatePoint(new Point(badgeBorder.Bounds.Width, 0), row)?.X <= right);
                        var endpoint = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains(address, StringComparison.Ordinal) == true);
                        Assert.True(endpoint.TranslatePoint(new Point(endpoint.Bounds.Width, 0), row)?.X <= right);
                    }
                }
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void DevicesView_binds_pairing_digits_and_lan_status_classes_without_localvalue_brushes() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new DevicesViewModel(client);
            var view = new DevicesView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1100, 720);
            try
            {
                client.EmitState(LocalShellState.Empty with
                {
                    ConnectionStatus = LocalConnectionStatus.Disconnected,
                    PairingPrompt = new PairingSnapshotPayload("DEV-NEW", "新手机", "482913", 30_000),
                });
                AvaloniaUiTestHost.Layout(window, 1100, 720);

                Assert.Equal("482913", vm.PairingCodeText);
                Assert.Equal(2, vm.PairingDigitGroups.Count);
                Assert.Equal(["4", "8", "2"], vm.PairingDigitGroups[0].Digits);
                Assert.Equal(["9", "1", "3"], vm.PairingDigitGroups[1].Digits);
                Assert.True(vm.LanStatusIsError);

                var pill = Assert.Single(view.GetLogicalDescendants().OfType<Border>(), b => b.Name == "LanStatusPillBorder");
                Assert.Contains("StatusPill", pill.Classes);
                Assert.Contains("Error", pill.Classes);

                var digitTexts = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(t => t.Text)
                    .OfType<string>()
                    .Where(t => t.Length == 1 && char.IsDigit(t[0]))
                    .ToArray();
                Assert.Equal(["4", "8", "2", "9", "1", "3"], digitTexts);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task ConnectionView_binds_guide_steps_and_error_feedback_class() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new ConnectionViewModel(client);
            var view = new ConnectionView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1100, 720);
            try
            {
                vm.OpenGuide();
                AvaloniaUiTestHost.Layout(window, 1100, 720);
                Assert.Equal("无需账号 · 本地直连", vm.CurrentGuideStep.Tag);
                Assert.Contains(
                    view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text),
                    t => t == vm.CurrentGuideStep.Title);

                vm.NextGuideStep();
                AvaloniaUiTestHost.Layout(window, 1100, 720);
                Assert.Equal("双向确认 · 安全信任", vm.CurrentGuideStep.Tag);
                Assert.Contains(
                    view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text),
                    t => t == vm.CurrentGuideStep.Title);

                vm.EndpointInput = "not-an-endpoint";
                Assert.False(await vm.DialEndpointAsync());
                AvaloniaUiTestHost.Layout(window, 1100, 720);
                Assert.True(vm.DialIsError);
                var banner = Assert.Single(view.GetLogicalDescendants().OfType<Border>(), b => b.Name == "FeedbackBanner");
                Assert.Contains("StatusPill", banner.Classes);
                Assert.Contains("Error", banner.Classes);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task TransfersView_binds_notice_status_pill_and_text_draft_preview_without_localvalue_brushes() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(() =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new TransfersViewModel(client);
            var view = new TransfersView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 1100, 720);
            try
            {
                Assert.Null(vm.CreateAndSendTextFile("note.txt", "hello"));
                AvaloniaUiTestHost.Layout(window, 1100, 720);
                Assert.True(vm.NoticeIsError);
                var banner = Assert.Single(view.GetLogicalDescendants().OfType<Border>(), b => b.Name == "NoticeBanner");
                Assert.Contains("StatusPill", banner.Classes);
                Assert.Contains("Error", banner.Classes);
                Assert.Contains(
                    view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text),
                    t => t == "请先选择一台已配对设备再发送文件。");

                vm.OpenNewTextFileDialog();
                vm.TextContentDraft = "hello";
                AvaloniaUiTestHost.Layout(window, 1100, 720);
                Assert.Equal(
                    "UTF-8 大小：5 B（5 B） · 共 1 个 64 KiB 分块",
                    vm.TextDraftPreviewText);
                Assert.Contains(
                    view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text),
                    t => t == vm.TextDraftPreviewText);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });

    [Fact]
    public void Appearance_changes_publish_resources_in_batches() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var app = Assert.IsType<App>(Application.Current);
            app.ApplyAppearanceOverrides(AppearanceModeOverride.Dark, AccentSourceOverride.BrandBlue);
            var lightResources = app.Resources.ThemeDictionaries[Avalonia.Styling.ThemeVariant.Light];
            var notifications = 0;
            void CountChange(object? sender, ResourcesChangedEventArgs args) => notifications++;
            app.ResourcesChanged += CountChange;
            try
            {
                app.ApplyAppearanceOverrides(AppearanceModeOverride.Light, AccentSourceOverride.BrandBlue);
                Assert.Equal(Avalonia.Styling.ThemeVariant.Light, app.ActualThemeVariant);
                Assert.InRange(notifications, 1, 3);
                Assert.Same(lightResources, app.Resources.ThemeDictionaries[Avalonia.Styling.ThemeVariant.Light]);
                var blue = Assert.IsType<Color>(app.Resources["PrimaryColor"]);

                notifications = 0;
                app.ApplyAppearanceOverrides(AppearanceModeOverride.Light, AccentSourceOverride.BrandBlue);
                Assert.Equal(0, notifications);
                app.ApplyAppearanceOverrides(AppearanceModeOverride.Light, AccentSourceOverride.ForestTeal);
                Assert.NotEqual(blue, Assert.IsType<Color>(app.Resources["PrimaryColor"]));
                Assert.InRange(notifications, 1, 16);
            }
            finally
            {
                app.ResourcesChanged -= CountChange;
                app.ApplyAppearanceOverrides(null, null);
            }
        });

    [Fact]
    public void Appearance_buttons_keep_focus_and_update_the_theme_without_being_replaced() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var app = Assert.IsType<App>(Application.Current);
            var vm = new ThisDeviceViewModel(new FakeLocalDaemonClient(), new FakeDaemonController(), new ShellPreferences(), _ => { });
            var view = new ThisDeviceView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 840, 720);
            vm.PreferencesChanged += preferences => app.ApplyAppearanceOverrides(preferences.AppearanceModeOverride, preferences.AccentSourceOverride);
            try
            {
                var group = Assert.IsType<ExpressiveButtonGroup>(view.FindControl<ExpressiveButtonGroup>("AppearanceModeGroup"));
                var light = group.Buttons[1];
                var dark = group.Buttons[2];
                foreach (var (button, index, theme) in new[] { (dark, 2, Avalonia.Styling.ThemeVariant.Dark), (light, 1, Avalonia.Styling.ThemeVariant.Light), (dark, 2, Avalonia.Styling.ThemeVariant.Dark) })
                {
                    Assert.True(button.Focus());
                    var controlTheme = button.Theme;
                    button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    AvaloniaUiTestHost.Layout(window, 840, 720);
                    Assert.Same(button, group.Buttons[index]);
                    Assert.Same(controlTheme, button.Theme);
                    Assert.True(button.IsFocused);
                    Assert.Equal(theme, view.ActualThemeVariant);
                    Assert.Equal(new CornerRadius(20), button.CornerRadius);
                }
            }
            finally
            {
                window.Close();
                app.ApplyAppearanceOverrides(null, null);
            }
        });

    [Fact]
    public Task ThisDeviceView_binds_status_and_service_pills_without_localvalue_brushes() =>
        AvaloniaUiTestHost.RunOnUiThreadAsync(async () =>
        {
            var client = new FakeLocalDaemonClient();
            var vm = new ThisDeviceViewModel(client, new FakeDaemonController(), new ShellPreferences(), _ => { });
            var view = new ThisDeviceView(vm);
            var window = AvaloniaUiTestHost.ShowAndLayout(view, 960, 760);
            try
            {
                vm.DisplayNameDraft = string.Empty;
                Assert.False(await vm.SaveDisplayNameAsync());
                AvaloniaUiTestHost.Layout(window, 960, 760);
                Assert.True(vm.StatusIsError);

                var statusBanner = Assert.Single(view.GetLogicalDescendants().OfType<Border>(), b => b.Name == "StatusBanner");
                Assert.Contains("StatusPill", statusBanner.Classes);
                Assert.Contains("Error", statusBanner.Classes);
                Assert.Contains(
                    view.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text),
                    t => t == "本机名称不能为空。");

                var servicePill = Assert.Single(view.GetLogicalDescendants().OfType<Border>(), b => b.Name == "ServiceStatusPill");
                Assert.Contains("StatusPill", servicePill.Classes);
                Assert.Contains("Muted", servicePill.Classes);

                Assert.All(
                    view.GetLogicalDescendants().OfType<ExpressiveGroupTile>(),
                    tile => Assert.Equal(new Thickness(0), tile.BorderThickness));

                var appearance = Assert.Single(
                    view.GetLogicalDescendants().OfType<ExpressiveButtonGroup>(),
                    g => g.Name == "AppearanceModeGroup");
                var selected = Assert.Single(appearance.Buttons, button => button.Classes.Contains("GroupSelected"));
                Assert.Equal("跟随系统", selected.Content);
                Assert.NotEqual(appearance.Buttons[1].Background, selected.Background);
                Assert.DoesNotContain(appearance.Buttons, b => b.Classes.Contains("Outlined"));

                var edit = Assert.Single(view.GetLogicalDescendants().OfType<Button>(), b => b.Name == "EditNameButton");
                Assert.Equal(40, edit.Width);
                Assert.Equal(40, edit.Height);
                Assert.Equal(new CornerRadius(20), edit.CornerRadius);

                var serviceActions = Assert.Single(
                    view.GetLogicalDescendants().OfType<ExpressiveButtonGroup>(),
                    g => g.Name == "ServiceActionGroup");
                Assert.Equal(2, serviceActions.Buttons.Count);

                client.EmitState(LocalShellState.Empty with { ConnectionStatus = LocalConnectionStatus.Connected });
                AvaloniaUiTestHost.Layout(window, 960, 760);
                Assert.DoesNotContain("Muted", servicePill.Classes);
                Assert.Single(serviceActions.Buttons);
            }
            finally
            {
                window.Close();
            }
        });

    private sealed class FakeDaemonController : IDaemonController
    {
        public bool IsRunningFromAppImage => false;
        public int ResetCalls { get; private set; }
        public DaemonStartResult NextStartResult { get; set; } = new(DaemonStartOutcome.Started);

        public Task<DaemonStartResult> StartDaemonAsync(CancellationToken ct = default) =>
            Task.FromResult(NextStartResult);

        public Task<DaemonActionResult> EnableAutostartAsync(CancellationToken ct = default) =>
            Task.FromResult(new DaemonActionResult(true));

        public Task<DaemonActionResult> ResetIdentityAndSettingsAsync(CancellationToken ct = default)
        {
            ResetCalls++;
            return Task.FromResult(new DaemonActionResult(true));
        }
    }
}
