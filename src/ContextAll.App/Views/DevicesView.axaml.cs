using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
    private StackPanel? _emptyStatePanel;
    private MaterialDialog _unpairDialog = new();
    private MaterialDialog _clipboardDialog = new();
    private readonly TextBox _clipboardInput;
    private readonly TextBlock _clipboardCounterLabel;

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

        Md3eResourceLookup.TrackThemeChanges(this, RefreshAll);
        _vm.PropertyChanged += (_, _) => RefreshAll();
        RefreshAll();
    }

    public event Action? NavigateToConnectionRequested;

    public bool IsListPaneVisible => _vm.ShowListPane;
    public bool IsDetailPaneVisible => _vm.ShowDetailPane;
    public bool IsPairingBannerVisible => _vm.PairingPrompt is not null;
    public string DisplayedPairingCode => _vm.PairingCodeText;
    public bool IsClipboardModalVisible => _clipboardDialog.IsOpen;
    public bool IsEmptyStateVisible => _emptyStatePanel?.IsVisible ?? false;
    public MaterialDialog ClipboardDialog => _clipboardDialog;

    private void ResolveNamedParts()
    {
        _dualSheetGrid = this.FindControl<Grid>("DualSheetGrid");
        _listPane = this.FindControl<Border>("ListPane");
        _detailPane = this.FindControl<Border>("DetailPane");
        _lanStatusBannerTile = this.FindControl<ExpressiveGroupTile>("LanStatusBannerTile");
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

    private void OnManualConnectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        NavigateToConnectionRequested?.Invoke();

    private async void OnAcceptPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.AcceptPairingAsync();

    private async void OnRejectPairingClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.RejectPairingAsync();

    private void RefreshAll()
    {
        ApplyResponsiveColumns();
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
