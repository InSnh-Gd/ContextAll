using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

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
    private ExpressiveGroupTile? _lanStatusBannerTile;
    private ExpressiveIconBadge? _lanStatusIconBadge;
    private Border? _lanStatusPillBorder;
    private TextBlock? _lanStatusPillText;
    private ExpressiveGroupTile? _clipboardQuickCard;
    private Border? _pairingBanner;
    private TextBlock? _pairingPeerLabel;
    private StackPanel? _pairingCodeBoxesPanel;
    private LinearWavyProgressIndicator? _pairingProgressBar;
    private TextBlock? _pairingCountdownLabel;
    private StackPanel? _emptyStatePanel;
    private MaterialDialog _unpairDialog = new();
    private MaterialDialog _clipboardDialog = new();
    private readonly TextBox _clipboardInput;
    private readonly TextBlock _clipboardCounterLabel;
    private string _displayedPairingCode = string.Empty;

    public DevicesView()
        : this(new DevicesViewModel(IdleLocalChannel.Shared))
    {
    }

    public DevicesView(DevicesViewModel vm, Action<string>? onSendFilesRequested = null)
    {
        _vm = vm;
        _onSendFilesRequested = onSendFilesRequested;
        DataContext = vm;
        _vm.CopyRequested += (text, notice) => _ = CopyToClipboardAsync(text, notice);
        _vm.SendFilesRequested += id => _onSendFilesRequested?.Invoke(id);

        _clipboardInput = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 112,
            MaxHeight = 200,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "输入要发送的纯文本内容（上限 1 MiB）…",
        };
        _clipboardCounterLabel = new TextBlock();
        _clipboardCounterLabel.Classes.Add("BodySmall");

        _clipboardInput.TextChanged += (_, _) => _vm.ClipboardDraftText = _clipboardInput.Text ?? string.Empty;

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();
        WireDialogs();

        if (_lanStatusBannerTile is not null)
        {
            _lanStatusBannerTile.Activated += () => NavigateToConnectionRequested?.Invoke();
        }
        if (_clipboardQuickCard is not null)
        {
            _clipboardQuickCard.Activated += () => _vm.OpenClipboardModal();
        }

        Md3eResourceLookup.TrackThemeChanges(this, RefreshAll);
        _vm.PropertyChanged += (_, _) => RefreshAll();
        RefreshAll();
    }

    public event Action? NavigateToConnectionRequested;

    public bool IsListPaneVisible => _vm.ShowListPane;
    public bool IsDetailPaneVisible => _vm.ShowDetailPane;
    public bool IsPairingBannerVisible => _pairingBanner?.IsVisible ?? false;
    public string DisplayedPairingCode => _displayedPairingCode;
    public bool IsClipboardModalVisible => _clipboardDialog.IsOpen;
    public bool IsEmptyStateVisible => _emptyStatePanel?.IsVisible ?? false;
    public MaterialDialog ClipboardDialog => _clipboardDialog;

    private void ResolveNamedParts()
    {
        _dualSheetGrid = this.FindControl<Grid>("DualSheetGrid");
        _listPane = this.FindControl<Border>("ListPane");
        _detailPane = this.FindControl<Border>("DetailPane");
        _lanStatusBannerTile = this.FindControl<ExpressiveGroupTile>("LanStatusBannerTile");
        _lanStatusIconBadge = this.FindControl<ExpressiveIconBadge>("LanStatusIconBadge");
        _lanStatusPillBorder = this.FindControl<Border>("LanStatusPillBorder");
        _lanStatusPillText = this.FindControl<TextBlock>("LanStatusPillText");
        _clipboardQuickCard = this.FindControl<ExpressiveGroupTile>("ClipboardQuickCard");
        _pairingBanner = this.FindControl<Border>("PairingBanner");
        _pairingPeerLabel = this.FindControl<TextBlock>("PairingPeerLabel");
        _pairingCodeBoxesPanel = this.FindControl<StackPanel>("PairingCodeBoxesPanel");
        _pairingProgressBar = this.FindControl<LinearWavyProgressIndicator>("PairingProgressBar");
        _pairingCountdownLabel = this.FindControl<TextBlock>("PairingCountdownLabel");
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

    private ClipboardHistoryWindow? _historyWindow;

    private void OnClipboardHistoryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_historyWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new ClipboardHistoryWindow(_vm.ClipboardHistoryClient);
        _historyWindow = window;
        window.Closed += (_, _) => _historyWindow = null;
        window.Show();
    }

    private async void OnAcceptPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.AcceptPairingAsync();

    private async void OnRejectPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.RejectPairingAsync();

    private void RefreshAll()
    {
        ApplyResponsiveColumns();
        UpdateLanStatusBanner();
        UpdatePairingBanner();
        UpdateModals();
    }

    private void UpdateLanStatusBanner()
    {
        var tone = !_vm.IsServiceConnected
            ? BadgeTone.Error
            : _vm.IsLanReady ? BadgeTone.Primary : BadgeTone.Secondary;

        if (_lanStatusIconBadge is not null)
        {
            _lanStatusIconBadge.Tone = tone;
        }

        if (_lanStatusPillBorder is not null && _lanStatusPillText is not null)
        {
            var (bg, fg) = tone switch
            {
                BadgeTone.Error => (
                    Md3eResourceLookup.ResolveBrush(this, "ErrorContainer", static p => p.ErrorContainer),
                    Md3eResourceLookup.ResolveBrush(this, "OnErrorContainer", static p => p.OnErrorContainer)),
                BadgeTone.Primary => (
                    Md3eResourceLookup.ResolveBrush(this, "PrimaryContainer", static p => p.PrimaryContainer),
                    Md3eResourceLookup.ResolveBrush(this, "OnPrimaryContainer", static p => p.OnPrimaryContainer)),
                _ => (
                    Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer),
                    Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer)),
            };
            _lanStatusPillBorder.Background = bg;
            _lanStatusPillText.Foreground = fg;
        }
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
            _displayedPairingCode = string.Empty;
            _pairingCodeBoxesPanel?.Children.Clear();
            return;
        }

        if (_pairingPeerLabel is not null)
        {
            _pairingPeerLabel.Text = $"{prompt.DisplayName}（{ShellFormatting.ShortId(prompt.DeviceId)}）请求配对";
        }

        var rawChars = prompt.Code.Where(c => !char.IsWhiteSpace(c)).ToArray();
        _displayedPairingCode = new string(rawChars);

        if (_pairingCodeBoxesPanel is not null)
        {
            _pairingCodeBoxesPanel.Children.Clear();
            var boxBg = Md3eResourceLookup.ResolveBrush(this, "Surface", static p => p.Surface);
            var boxBorder = Md3eResourceLookup.ResolveBrush(this, "OutlineVariant", static p => p.OutlineVariant);
            var boxFg = Md3eResourceLookup.ResolveBrush(this, "Primary", static p => p.Primary);

            var groupSize = 3;
            for (var start = 0; start < rawChars.Length; start += groupSize)
            {
                var triplet = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                };
                var end = Math.Min(rawChars.Length, start + groupSize);
                for (var idx = start; idx < end; idx++)
                {
                    var digitText = new TextBlock
                    {
                        Text = rawChars[idx].ToString(),
                        Foreground = boxFg,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    digitText.Classes.Add("TitleLargeEmphasized");
                    digitText.Classes.Add("MonoTabular");

                    triplet.Children.Add(new Border
                    {
                        Width = 34,
                        Height = 42,
                        CornerRadius = new CornerRadius(12),
                        Background = boxBg,
                        BorderBrush = boxBorder,
                        BorderThickness = new Thickness(1),
                        Child = digitText,
                    });
                }

                _pairingCodeBoxesPanel.Children.Add(triplet);
            }
        }

        if (_pairingProgressBar is not null)
        {
            var maxMs = Math.Max((double)DevicesViewModel.PairingPromptTimeoutMs, prompt.RemainingMilliseconds);
            _pairingProgressBar.Maximum = maxMs;
            _pairingProgressBar.Value = Math.Clamp(prompt.RemainingMilliseconds, 0.0, maxMs);
            _pairingProgressBar.IndicatorBrush = Md3eResourceLookup.ResolveBrush(this, "Primary", static p => p.Primary);
            _pairingProgressBar.TrackBrush = Md3eResourceLookup.ResolveBrush(this, "SurfaceContainerHighest", static p => p.SurfaceContainerHighest);
        }

        if (_pairingCountdownLabel is not null)
        {
            _pairingCountdownLabel.Text = $"请在 {_vm.PairingRemainingSeconds} 秒内完成确认";
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

    private async Task CopyToClipboardAsync(string text, string notice)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }

        _vm.NotifyCopied(notice);
    }
}
