using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E <c>g2-split-workbench</c> Transfers view with responsive 400dp workbench + live queue split,
/// Portal multi-file picker, drag-drop, wavy progress indicators, and incoming prompt decisions.
/// </summary>
public partial class TransfersView : UserControl
{
    private const double SplitBreakpoint = 840.0;

    private readonly TransfersViewModel _vm;
    private Grid? _workbenchGrid;
    private Border? _workbenchPane;
    private Border? _queuePane;
    private WrapPanel? _peerChipsPanel;
    private Border? _dropZone;
    private Border? _noticeBanner;
    private TextBlock? _noticeText;
    private StackPanel? _promptsPanel;
    private StackPanel? _activePanel;
    private StackPanel? _queuePanel;
    private StackPanel? _historyPanel;
    private bool _isSplitLayout = true;

    public TransfersView()
        : this(new TransfersViewModel(new LocalDaemonConnection()))
    {
    }

    public TransfersView(TransfersViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        _vm.MultiFilePicker ??= OpenPortalMultiFilePickerAsync;

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();

        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        if (_dropZone is not null)
        {
            DragDrop.AddDragOverHandler(_dropZone, OnDragOver);
            DragDrop.AddDropHandler(_dropZone, OnDrop);
        }

        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
        _vm.PropertyChanged += (_, _) => Refresh();

        Md3eResourceLookup.TrackThemeChanges(this, RefreshDynamicVisuals);
        ApplyResponsiveLayout(Bounds.Width > 0 ? Bounds.Width : 1080);
        Refresh();
    }

    public bool IsSplitLayout => _isSplitLayout;
    public bool IsNoticeVisible => _noticeBanner?.IsVisible ?? false;
    public string NoticeText => _noticeText?.Text ?? string.Empty;
    public int RenderedActiveTransferCount => _activePanel?.Children.OfType<ExpressiveGroupTile>().Count(t => t.Tag as string == "active-item") ?? 0;
    public int RenderedHistoryCount => _historyPanel?.Children.OfType<ExpressiveGroupTile>().Count(t => t.Tag as string == "history-item") ?? 0;

    public void ApplyResponsiveLayout(double availableWidth)
    {
        var split = availableWidth <= 0 || availableWidth >= SplitBreakpoint;
        _isSplitLayout = split;

        if (_workbenchGrid is { ColumnDefinitions.Count: >= 2 })
        {
            _workbenchGrid.ColumnDefinitions[0].Width = split ? new GridLength(400) : new GridLength(1, GridUnitType.Star);
            _workbenchGrid.ColumnDefinitions[1].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        if (_workbenchPane is not null)
        {
            Grid.SetRow(_workbenchPane, 0);
            Grid.SetColumn(_workbenchPane, 0);
            _workbenchPane.Margin = split ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 12);
        }

        if (_queuePane is not null)
        {
            Grid.SetRow(_queuePane, split ? 0 : 1);
            Grid.SetColumn(_queuePane, split ? 1 : 0);
        }
    }

    private void ResolveNamedParts()
    {
        _workbenchGrid = this.FindControl<Grid>("WorkbenchGrid");
        _workbenchPane = this.FindControl<Border>("WorkbenchPane");
        _queuePane = this.FindControl<Border>("QueuePane");
        _peerChipsPanel = this.FindControl<WrapPanel>("PeerChipsPanel");
        _dropZone = this.FindControl<Border>("DropZone");
        _noticeBanner = this.FindControl<Border>("NoticeBanner");
        _noticeText = this.FindControl<TextBlock>("NoticeLabel");
        _promptsPanel = this.FindControl<StackPanel>("PromptsPanel");
        _activePanel = this.FindControl<StackPanel>("ActivePanel");
        _queuePanel = this.FindControl<StackPanel>("QueuePanel");
        _historyPanel = this.FindControl<StackPanel>("HistoryPanel");
    }

    private async void OnChooseFilesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.PickAndSendFilesAsync();

    private void OnDismissNoticeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.DismissNotice();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles()?.Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items is null || items.Length == 0)
        {
            return;
        }

        var paths = items
            .Select(StorageProviderExtensions.TryGetLocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .OfType<string>()
            .ToArray();
        _vm.EnqueueFilesForPeer(_vm.SelectedTargetPeerId, paths);
        e.Handled = true;
    }

    private async Task<IReadOnlyList<string>> OpenPortalMultiFilePickerAsync(CancellationToken ct)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { CanOpen: true } provider)
        {
            return [];
        }

        var picked = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要发送的文件",
            AllowMultiple = true,
        });

        var result = new List<string>(picked.Count);
        foreach (var item in picked)
        {
            var local = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(local))
            {
                result.Add(local);
            }
        }

        return result;
    }

    private void Refresh()
    {
        RebuildPeerChips();
        UpdateNoticeBanner();
        RebuildPrompts();
        RebuildActiveTransfers();
        RebuildQueue();
        RebuildHistory();
    }

    private void RefreshDynamicVisuals()
    {
        RebuildPeerChips();
        UpdateNoticeBanner();
        RebuildPrompts();
    }

    private void UpdateNoticeBanner()
    {
        if (_noticeBanner is null || _noticeText is null)
        {
            return;
        }

        _noticeBanner.IsVisible = !string.IsNullOrWhiteSpace(_vm.NoticeMessage);
        _noticeText.Text = _vm.NoticeMessage ?? string.Empty;
        _noticeBanner.Background = _vm.NoticeIsError
            ? Md3eResourceLookup.ResolveBrush(this, "ErrorContainer", static p => p.ErrorContainer)
            : Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer);
        _noticeText.Foreground = _vm.NoticeIsError
            ? Md3eResourceLookup.ResolveBrush(this, "OnErrorContainer", static p => p.OnErrorContainer)
            : Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer);
    }

    private void RebuildPeerChips()
    {
        if (_peerChipsPanel is null)
        {
            return;
        }

        _peerChipsPanel.Children.Clear();
        if (_vm.TrustedPeers.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "暂无已配对设备，请先在「设备」页完成配对。",
                TextWrapping = TextWrapping.Wrap,
            };
            empty.Classes.Add("BodyMedium");
            _peerChipsPanel.Children.Add(empty);
            return;
        }

        foreach (var peer in _vm.TrustedPeers)
        {
            var selected = string.Equals(peer.DeviceId, _vm.SelectedTargetPeerId, StringComparison.Ordinal);
            var chip = new Button
            {
                Content = peer.DisplayName,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 7),
                Margin = new Thickness(0, 0, 8, 8),
            };
            chip.Classes.Add(selected ? "FilledTonal" : "Outlined");
            var id = peer.DeviceId;
            chip.Click += (_, _) => _vm.SelectTargetPeer(id);
            _peerChipsPanel.Children.Add(chip);
        }
    }

    private void RebuildPrompts()
    {
        if (_promptsPanel is null)
        {
            return;
        }

        _promptsPanel.Children.Clear();
        if (_vm.FilePrompts.Count == 0)
        {
            var empty = new TextBlock { Text = "当前没有待确认接收的文件请求" };
            empty.Classes.Add("BodyMedium");
            _promptsPanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Content = empty,
            });
            return;
        }

        var containerBg = Md3eResourceLookup.ResolveBrush(this, "TertiaryContainer", static p => p.TertiaryContainer);
        var containerFg = Md3eResourceLookup.ResolveBrush(this, "OnTertiaryContainer", static p => p.OnTertiaryContainer);

        foreach (var prompt in _vm.FilePrompts)
        {
            var acceptBtn = new Button
            {
                Content = "接收文件",
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 6),
            };
            acceptBtn.Classes.Add("Filled");

            var rejectBtn = new Button
            {
                Content = "拒绝",
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(12, 6),
            };
            rejectBtn.Classes.Add("Outlined");

            var tid = prompt.TransferId;
            acceptBtn.Click += async (_, _) => await _vm.AcceptFileAsync(tid);
            rejectBtn.Click += async (_, _) => await _vm.RejectFileAsync(tid);

            var title = new TextBlock
            {
                Text = prompt.FileName,
                Foreground = containerFg,
            };
            title.Classes.Add("TitleSmall");

            var subtitle = new TextBlock
            {
                Text = $"来自 {_vm.ResolveDeviceName(prompt.DeviceId)} · {FormatBytes(prompt.TotalSize)}",
                Foreground = containerFg,
            };
            subtitle.Classes.Add("BodySmall");
            subtitle.Classes.Add("MonoTabular");

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                ColumnSpacing = 8,
            };
            var info = new StackPanel
            {
                Spacing = 2,
                Children = { title, subtitle },
            };

            Grid.SetColumn(info, 0);
            Grid.SetColumn(acceptBtn, 1);
            Grid.SetColumn(rejectBtn, 2);
            row.Children.Add(info);
            row.Children.Add(acceptBtn);
            row.Children.Add(rejectBtn);

            var card = new Border
            {
                CornerRadius = new CornerRadius(24),
                Padding = new Thickness(16, 14),
                Background = containerBg,
                Child = row,
            };
            TextElement.SetForeground(card, containerFg);
            _promptsPanel.Children.Add(card);
        }
    }

    private void RebuildActiveTransfers()
    {
        if (_activePanel is null)
        {
            return;
        }

        _activePanel.Children.Clear();
        if (_vm.ActiveTransfers.Count == 0)
        {
            var empty = new TextBlock { Text = "暂无进行中的文件传输" };
            empty.Classes.Add("BodyMedium");
            _activePanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Content = empty,
            });
            return;
        }

        for (var i = 0; i < _vm.ActiveTransfers.Count; i++)
        {
            var active = _vm.ActiveTransfers[i];
            var cancelBtn = new Button
            {
                Content = "取消传输",
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 6),
            };
            cancelBtn.Classes.Add("Outlined");
            var tid = active.TransferId;
            cancelBtn.Click += async (_, _) => await _vm.CancelTransferAsync(tid);

            var bar = new LinearWavyProgressIndicator
            {
                Maximum = Math.Max(1, active.SegmentCount),
                Value = active.CompletedSegments,
            };

            var dirLabel = active.Direction == "send" ? "发送" : "接收";
            var nameLabel = new TextBlock { Text = $"{dirLabel} · {active.FileName}" };
            nameLabel.Classes.Add("TitleSmall");

            var statsLabel = new TextBlock
            {
                Text = $"{_vm.ResolveDeviceName(active.DeviceId)} · 分片 {active.CompletedSegments}/{active.SegmentCount}",
            };
            statsLabel.Classes.Add("BodySmall");
            statsLabel.Classes.Add("MonoTabular");

            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var title = new StackPanel
            {
                Spacing = 2,
                Children = { nameLabel, statsLabel },
            };
            Grid.SetColumn(title, 0);
            Grid.SetColumn(cancelBtn, 1);
            header.Children.Add(title);
            header.Children.Add(cancelBtn);

            _activePanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Position = ResolvePosition(i, _vm.ActiveTransfers.Count),
                Tag = "active-item",
                Content = new StackPanel { Spacing = 8, Children = { header, bar } },
            });
        }
    }

    private void RebuildQueue()
    {
        if (_queuePanel is null)
        {
            return;
        }

        _queuePanel.Children.Clear();
        var items = _vm.PendingQueueItems;
        if (items.Count == 0)
        {
            var empty = new TextBlock { Text = "发送队列空闲" };
            empty.Classes.Add("BodyMedium");
            _queuePanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Content = empty,
            });
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var stateTag = item.IsInFlight ? "正在发送" : "排队等待";
            var label = new TextBlock
            {
                Text = $"[{stateTag}] {_vm.ResolveDeviceName(item.DeviceId)} → {item.FileName}",
            };
            label.Classes.Add("BodyMedium");

            _queuePanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Position = ResolvePosition(i, items.Count),
                Content = label,
            });
        }
    }

    private void RebuildHistory()
    {
        if (_historyPanel is null)
        {
            return;
        }

        _historyPanel.Children.Clear();
        if (_vm.History.Count == 0)
        {
            var empty = new TextBlock { Text = "暂无会话历史记录" };
            empty.Classes.Add("BodyMedium");
            _historyPanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Content = empty,
            });
            return;
        }

        for (var i = 0; i < _vm.History.Count; i++)
        {
            var entry = _vm.History[i];
            var openBtn = new Button
            {
                Content = "打开所在文件夹",
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 6),
                IsVisible = entry.Direction != "send" && !string.IsNullOrWhiteSpace(entry.SavedDirectory),
            };
            openBtn.Classes.Add("FilledTonal");
            var captured = entry;
            openBtn.Click += (_, _) => _vm.OpenContainingFolder(captured);

            var title = new TextBlock { Text = $"{entry.FileName} ({FormatOutcome(entry.Outcome)})" };
            title.Classes.Add("TitleSmall");

            var meta = new TextBlock
            {
                Text = $"{entry.DeviceName} · {FormatBytes(entry.TotalSize)} · {entry.EndedAt.LocalDateTime:HH:mm:ss}",
            };
            meta.Classes.Add("BodySmall");
            meta.Classes.Add("MonoTabular");

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var info = new StackPanel
            {
                Spacing = 2,
                Children = { title, meta },
            };
            Grid.SetColumn(info, 0);
            Grid.SetColumn(openBtn, 1);
            row.Children.Add(info);
            row.Children.Add(openBtn);

            _historyPanel.Children.Add(new ExpressiveGroupTile
            {
                IsInteractive = false,
                Position = ResolvePosition(i, _vm.History.Count),
                Tag = "history-item",
                Content = row,
            });
        }
    }

    private static GroupPosition ResolvePosition(int index, int count) => count <= 1
        ? GroupPosition.Single
        : index == 0
            ? GroupPosition.First
            : index == count - 1
                ? GroupPosition.Last
                : GroupPosition.Middle;

    private static string FormatOutcome(string outcome) => outcome switch
    {
        "done" or "completed" => "已完成",
        "rejected" => "已拒绝",
        "cancelled" or "canceled" => "已取消",
        _ => outcome,
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KiB";
        return $"{bytes / (1024.0 * 1024.0):0.00} MiB";
    }
}
