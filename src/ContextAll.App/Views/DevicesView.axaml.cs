using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E <c>g1-expressive-studio</c> Devices view supporting responsive dual-sheet &amp; drill-in navigation,
/// real <see cref="DeviceSnapshotPayload"/> details, transfer history, and clipboard broadcast clarification.
/// </summary>
public partial class DevicesView : UserControl
{
    private readonly DevicesViewModel _vm;
    private readonly Action<string>? _onSendFilesRequested;

    private Grid? _dualSheetGrid;
    private Border? _listPane;
    private Border? _detailPane;
    private StackPanel? _trustedPanel;
    private StackPanel? _discoveredPanel;
    private StackPanel? _detailContentPanel;
    private Border? _pairingBanner;
    private TextBlock? _pairingPeerLabel;
    private TextBlock? _pairingCodeLabel;
    private StackPanel? _emptyStatePanel;
    private MaterialDialog _unpairDialog = new();
    private MaterialDialog _clipboardDialog = new();
    private readonly TextBox _clipboardInput;
    private readonly TextBlock _clipboardCounterLabel;

    public DevicesView()
        : this(new DevicesViewModel(new LocalDaemonConnection()))
    {
    }

    public DevicesView(DevicesViewModel vm, Action<string>? onSendFilesRequested = null)
    {
        _vm = vm;
        _onSendFilesRequested = onSendFilesRequested;
        DataContext = vm;

        _clipboardInput = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 112,
            MaxHeight = 200,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            PlaceholderText = "在此输入要广播推送的纯文本（最大 1 MiB）…",
        };
        _clipboardCounterLabel = new TextBlock();
        _clipboardCounterLabel.Classes.Add("BodySmall");

        _clipboardInput.TextChanged += (_, _) => _vm.ClipboardDraftText = _clipboardInput.Text ?? string.Empty;

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();
        WireDialogs();

        Md3eResourceLookup.TrackThemeChanges(this, RefreshAll);
        _vm.PropertyChanged += (_, _) => RefreshAll();
        RefreshAll();
    }

    public bool IsListPaneVisible => _vm.ShowListPane;
    public bool IsDetailPaneVisible => _vm.ShowDetailPane;
    public bool IsPairingBannerVisible => _pairingBanner?.IsVisible ?? false;
    public string DisplayedPairingCode => _pairingCodeLabel?.Text ?? string.Empty;
    public bool IsClipboardModalVisible => _clipboardDialog.IsOpen;
    public bool IsEmptyStateVisible => _emptyStatePanel?.IsVisible ?? false;
    public MaterialDialog ClipboardDialog => _clipboardDialog;

    private void ResolveNamedParts()
    {
        _dualSheetGrid = this.FindControl<Grid>("DualSheetGrid");
        _listPane = this.FindControl<Border>("ListPane");
        _detailPane = this.FindControl<Border>("DetailPane");
        _trustedPanel = this.FindControl<StackPanel>("TrustedPanel");
        _discoveredPanel = this.FindControl<StackPanel>("DiscoveredPanel");
        _detailContentPanel = this.FindControl<StackPanel>("DetailContentPanel");
        _pairingBanner = this.FindControl<Border>("PairingBanner");
        _pairingPeerLabel = this.FindControl<TextBlock>("PairingPeerLabel");
        _pairingCodeLabel = this.FindControl<TextBlock>("PairingCodeLabel");
        _emptyStatePanel = this.FindControl<StackPanel>("EmptyStatePanel");

        if (this.FindControl<MaterialDialog>("UnpairDialogControl") is { } unpair)
        {
            _unpairDialog = unpair;
        }
        if (this.FindControl<MaterialDialog>("ClipboardDialogControl") is { } clipboard)
        {
            _clipboardDialog = clipboard;
        }
    }

    private void WireDialogs()
    {
        _unpairDialog.PrimaryActionClicked += async (_, _) => await _vm.ConfirmUnpairAsync();
        _unpairDialog.Canceled += (_, _) => _vm.CancelUnpair();

        _clipboardDialog.BodyContent = new StackPanel
        {
            Spacing = 8,
            Children = { _clipboardInput, _clipboardCounterLabel },
        };
        _clipboardDialog.PrimaryActionClicked += async (_, _) => await _vm.PushClipboardAsync();
        _clipboardDialog.Canceled += (_, _) => _vm.CloseClipboardModal();
    }

    private void OnGlobalPushClipboardClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenClipboardModal();

    private async void OnAcceptPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.AcceptPairingAsync();

    private async void OnRejectPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.RejectPairingAsync();

    private void RefreshAll()
    {
        ApplyResponsiveColumns();
        UpdatePairingBanner();
        RebuildDeviceGroup(_trustedPanel, _vm.TrustedDevices, isTrusted: true);
        RebuildDeviceGroup(_discoveredPanel, _vm.DiscoveredDevices, isTrusted: false);
        RebuildDetailPane();
        UpdateModals();
    }

    private void ApplyResponsiveColumns()
    {
        var showList = _vm.ShowListPane;
        var showDetail = _vm.ShowDetailPane;

        if (_listPane is not null)
        {
            _listPane.IsVisible = showList;
            _listPane.Margin = showList && showDetail ? new Thickness(0, 0, 12, 0) : new Thickness(0);
        }

        if (_detailPane is not null)
        {
            _detailPane.IsVisible = showDetail;
        }

        if (_dualSheetGrid is { ColumnDefinitions.Count: >= 2 })
        {
            _dualSheetGrid.ColumnDefinitions[0].Width = !showList
                ? new GridLength(0)
                : showDetail ? new GridLength(404) : new GridLength(1, GridUnitType.Star);
            _dualSheetGrid.ColumnDefinitions[1].Width = showDetail
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private void UpdatePairingBanner()
    {
        var prompt = _vm.PairingPrompt;
        if (_pairingBanner is not null)
        {
            _pairingBanner.IsVisible = prompt is not null;
        }

        if (prompt is null)
        {
            return;
        }

        if (_pairingPeerLabel is not null)
        {
            _pairingPeerLabel.Text = $"{prompt.DisplayName}（{prompt.DeviceId}）请求配对";
        }
        if (_pairingCodeLabel is not null)
        {
            _pairingCodeLabel.Text = prompt.Code;
        }
    }

    private void RebuildDeviceGroup(StackPanel? container, IReadOnlyList<DeviceSnapshotPayload> items, bool isTrusted)
    {
        if (container is null)
        {
            return;
        }

        container.Children.Clear();
        var count = items.Count;
        for (var i = 0; i < count; i++)
        {
            var device = items[i];
            var position = ComputePosition(i, count);
            var selected = string.Equals(device.DeviceId, _vm.SelectedDeviceId, StringComparison.Ordinal);

            var badge = new ExpressiveIconBadge
            {
                Tone = device.Presence == "online"
                    ? BadgeTone.Primary
                    : (isTrusted ? BadgeTone.Secondary : BadgeTone.Neutral),
                PathData = "M4,6h18V4H4C2.9,4 2,4.9 2,6v11H0v3h14v-3H4V6zM23,8h-6c-0.55,0 -1,0.45 -1,1v10c0,0.55 0.45,1 1,1h6c0.55,0 1,-0.45 1,-1V9C24,8.45 23.55,8 23,8zM22,17h-4v-7h4V17z",
                ContainerSize = 42,
                IconSize = 20,
            };

            var nameBlock = new TextBlock { Text = device.DisplayName };
            nameBlock.Classes.Add("TitleSmall");
            var metaBlock = new TextBlock { Text = $"{ShortId(device.DeviceId)} · {FormatPresence(device.Presence)}" };
            metaBlock.Classes.Add("BodySmall");

            var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(nameBlock);
            info.Children.Add(metaBlock);

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12,
            };
            Grid.SetColumn(badge, 0);
            Grid.SetColumn(info, 1);
            row.Children.Add(badge);
            row.Children.Add(info);

            if (!isTrusted)
            {
                var pairBtn = new Button
                {
                    Content = "配对",
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(12, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                pairBtn.Classes.Add("Outlined");
                var peerId = device.DeviceId;
                pairBtn.Click += async (_, _) => await _vm.StartPairingAsync(peerId);
                Grid.SetColumn(pairBtn, 2);
                row.Children.Add(pairBtn);
            }

            var tile = new ExpressiveGroupTile
            {
                Position = position,
                IsInteractive = true,
                IsSelected = selected,
                Child = row,
            };
            var selectedId = device.DeviceId;
            tile.Activated += () => _vm.SelectDevice(selectedId);
            container.Children.Add(tile);
        }
    }

    private void RebuildDetailPane()
    {
        if (_detailContentPanel is null)
        {
            return;
        }

        _detailContentPanel.Children.Clear();
        var device = _vm.SelectedDevice;
        if (device is null)
        {
            return;
        }

        if (_vm.CanNavigateBack)
        {
            var backBtn = new Button
            {
                Content = "← 返回设备列表",
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(12, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            backBtn.Classes.Add("Text");
            backBtn.Click += (_, _) => _vm.ClearSelection();
            _detailContentPanel.Children.Add(backBtn);
        }

        // PrimaryContainer Hero Card with real DeviceSnapshotPayload properties ONLY (2.A)
        var heroTitle = new TextBlock { Text = device.DisplayName };
        heroTitle.Classes.Add("HeadlineSmallEmphasized");

        var presenceText = new TextBlock
        {
            Text = $"{FormatPresence(device.Presence)} · {(device.Trusted ? "已配对可信" : "待配对设备")}",
        };
        presenceText.Classes.Add("LabelLarge");

        var metaPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
        metaPanel.Children.Add(BuildMetaLine("设备 ID", device.DeviceId, monospace: true));
        metaPanel.Children.Add(BuildMetaLine(
            "网络端点",
            string.IsNullOrWhiteSpace(device.Endpoint) ? "未上报端点" : device.Endpoint,
            monospace: true));
        if (device.Trusted)
        {
            var remoteSync = device.RemoteClipboardSync switch
            {
                true => "对端已开启剪贴板同步",
                false => "对端已关闭剪贴板同步",
                _ => "对端剪贴板状态未知",
            };
            metaPanel.Children.Add(BuildMetaLine("对端状态", remoteSync, monospace: false));
        }

        var heroBadge = new ExpressiveIconBadge
        {
            Tone = BadgeTone.Primary,
            ContainerSize = 52,
            IconSize = 26,
            PathData = "M4,6h18V4H4C2.9,4 2,4.9 2,6v11H0v3h14v-3H4V6zM23,8h-6c-0.55,0 -1,0.45 -1,1v10c0,0.55 0.45,1 1,1h6c0.55,0 1,-0.45 1,-1V9C24,8.45 23.55,8 23,8zM22,17h-4v-7h4V17z",
        };

        var heroHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
        };
        var heroTitles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heroTitles.Children.Add(heroTitle);
        heroTitles.Children.Add(presenceText);
        Grid.SetColumn(heroBadge, 0);
        Grid.SetColumn(heroTitles, 1);
        heroHeader.Children.Add(heroBadge);
        heroHeader.Children.Add(heroTitles);

        var heroBody = new StackPanel { Spacing = 8 };
        heroBody.Children.Add(heroHeader);
        heroBody.Children.Add(metaPanel);

        var heroCard = new Border
        {
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(20),
            Child = heroBody,
        };
        if (this.TryFindResource("PrimaryContainer", ActualThemeVariant, out var pc) &&
            pc is Avalonia.Media.IBrush pcBrush)
        {
            heroCard.Background = pcBrush;
        }
        if (this.TryFindResource("OnPrimaryContainer", ActualThemeVariant, out var onPc) &&
            onPc is Avalonia.Media.IBrush onPcBrush)
        {
            Avalonia.Controls.Documents.TextElement.SetForeground(heroCard, onPcBrush);
        }
        _detailContentPanel.Children.Add(heroCard);

        var peerId = device.DeviceId;
        if (device.Trusted)
        {
            var actionGroup = new ExpressiveButtonGroup();
            actionGroup.SetItems([
                new ExpressiveGroupButtonItem("send", "发送文件", IsSelected: true),
                new ExpressiveGroupButtonItem("clipboard", "推送剪贴板"),
                new ExpressiveGroupButtonItem("unpair", "取消配对"),
            ]);
            actionGroup.ItemClicked += id =>
            {
                switch (id)
                {
                    case "send":
                        _onSendFilesRequested?.Invoke(peerId);
                        break;
                    case "clipboard":
                        _vm.OpenClipboardModal();
                        break;
                    case "unpair":
                        _vm.RequestUnpair(peerId);
                        break;
                }
            };
            _detailContentPanel.Children.Add(actionGroup);

            var syncSwitch = new ExpressiveSwitch { IsChecked = device.ClipboardSync == true };
            syncSwitch.Toggled += async enabled => await _vm.SetClipboardSyncAsync(peerId, enabled);

            var askSwitch = new ExpressiveSwitch { IsChecked = device.AskBeforeSave != false };
            askSwitch.Toggled += async enabled => await _vm.SetAskBeforeSaveAsync(peerId, enabled);

            var switchesStack = new StackPanel { Spacing = 3 };
            switchesStack.Children.Add(BuildSwitchTile(
                GroupPosition.First,
                "剪贴板同步",
                "允许与该设备双向同步剪贴板文本",
                syncSwitch));
            switchesStack.Children.Add(BuildSwitchTile(
                GroupPosition.Last,
                "接收文件前先询问",
                "关闭后将自动保存来自该设备的文件",
                askSwitch));
            _detailContentPanel.Children.Add(switchesStack);
        }
        else
        {
            var startPairBtn = new Button
            {
                Content = "发起配对请求",
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            startPairBtn.Classes.Add("Filled");
            startPairBtn.Click += async (_, _) => await _vm.StartPairingAsync(peerId);
            _detailContentPanel.Children.Add(startPairBtn);
        }

        // 2.A Real transfer history for this selected peer (omitted when empty)
        var history = _vm.SelectedDeviceHistory;
        if (history.Count > 0)
        {
            var histHeading = new TextBlock { Text = "最近传输记录", Margin = new Thickness(4, 6, 0, 2) };
            histHeading.Classes.Add("LabelLargeEmphasized");
            _detailContentPanel.Children.Add(histHeading);

            var histStack = new StackPanel { Spacing = 3 };
            var count = Math.Min(history.Count, 8);
            for (var i = 0; i < count; i++)
            {
                var entry = history[i];
                var direction = entry.Direction == "send" ? "发出" : "收到";
                var titleBlock = new TextBlock { Text = $"{direction} · {entry.FileName}" };
                titleBlock.Classes.Add("TitleSmall");
                var subBlock = new TextBlock
                {
                    Text = $"{entry.Outcome} · {FormatBytes(entry.TotalSize)} · {entry.EndedAt.ToLocalTime():MM-dd HH:mm}",
                };
                subBlock.Classes.Add("BodySmall");

                var entryPanel = new StackPanel { Spacing = 2 };
                entryPanel.Children.Add(titleBlock);
                entryPanel.Children.Add(subBlock);

                histStack.Children.Add(new ExpressiveGroupTile
                {
                    IsInteractive = false,
                    Position = ComputePosition(i, count),
                    Child = entryPanel,
                });
            }

            _detailContentPanel.Children.Add(histStack);
        }
    }

    private void UpdateModals()
    {
        _unpairDialog.IsOpen = !string.IsNullOrEmpty(_vm.ConfirmingUnpairDeviceId);

        _clipboardDialog.IsOpen = _vm.IsClipboardModalOpen;
        if (!string.Equals(_clipboardInput.Text ?? string.Empty, _vm.ClipboardDraftText, StringComparison.Ordinal))
        {
            _clipboardInput.Text = _vm.ClipboardDraftText;
        }

        _clipboardCounterLabel.Text = _vm.IsClipboardTooLarge
            ? $"当前体积 {_vm.ClipboardUtf8Bytes:N0} 字节（已超过 1 MiB 限制）"
            : $"UTF-8 字节数：{_vm.ClipboardUtf8Bytes:N0} / 1,048,576";
    }

    private static Control BuildMetaLine(string label, string value, bool monospace)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
        };
        var keyBlock = new TextBlock { Text = label };
        keyBlock.Classes.Add("LabelMedium");
        var valBlock = new TextBlock
        {
            Text = value,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        valBlock.Classes.Add(monospace ? "BodySmall MonoTabular" : "BodySmall");

        Grid.SetColumn(keyBlock, 0);
        Grid.SetColumn(valBlock, 1);
        row.Children.Add(keyBlock);
        row.Children.Add(valBlock);
        return row;
    }

    private static ExpressiveGroupTile BuildSwitchTile(
        GroupPosition position,
        string title,
        string subtitle,
        ExpressiveSwitch control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
        };
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock { Text = title };
        titleBlock.Classes.Add("TitleSmall");
        var subBlock = new TextBlock
        {
            Text = subtitle,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        subBlock.Classes.Add("BodySmall");
        labels.Children.Add(titleBlock);
        labels.Children.Add(subBlock);

        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(control, 1);
        row.Children.Add(labels);
        row.Children.Add(control);

        return new ExpressiveGroupTile
        {
            Position = position,
            IsInteractive = false,
            Child = row,
        };
    }

    private static GroupPosition ComputePosition(int index, int count) => count switch
    {
        <= 1 => GroupPosition.Single,
        _ when index == 0 => GroupPosition.First,
        _ when index == count - 1 => GroupPosition.Last,
        _ => GroupPosition.Middle,
    };

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    private static string FormatPresence(string presence) => presence switch
    {
        "online" => "在线",
        "connecting" => "连接中",
        _ => "离线",
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F2} MB",
    };
}
