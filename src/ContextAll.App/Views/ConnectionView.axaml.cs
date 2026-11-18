using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E Connection and Network view for manual QUIC dialing, LAN endpoints copy, protocol specs,
/// per-peer connection overview, and a 4-step interactive usage guide.
/// </summary>
public partial class ConnectionView : UserControl
{
    private readonly ConnectionViewModel _vm;
    private MaterialDialog _guideDialog = new();
    private Grid? _desktopSplitGrid;
    private Control? _dialPane;
    private Control? _infoPane;
    private Control? _headerActions;
    private ExpressiveButtonGroup? _lanPresetGroup;
    private bool _isSplitLayout = true;

    public ConnectionView()
        : this(new ConnectionViewModel(IdleLocalChannel.Shared))
    {
    }

    public ConnectionView(ConnectionViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        _vm.CopyRequested += text => _ = CopyEndpointAsync(text);

        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<MaterialDialog>("GuideDialogControl") is { } guide)
        {
            _guideDialog = guide;
        }

        _desktopSplitGrid = this.FindControl<Grid>("DesktopSplitGrid");
        _dialPane = this.FindControl<Control>("DialPane");
        _infoPane = this.FindControl<Control>("InfoPane");
        _headerActions = this.FindControl<Control>("HeaderActions");
        _lanPresetGroup = this.FindControl<ExpressiveButtonGroup>("LanPresetGroup");

        if (_lanPresetGroup is not null)
        {
            _lanPresetGroup.SetItems(
            [
                .. ConnectionViewModel.LanPresets.Select(preset => new ExpressiveGroupButtonItem(preset, preset)),
            ]);
            _lanPresetGroup.ItemClicked += preset => _vm.ApplyLanPresetCommand.Execute(preset);
        }

        _guideDialog.PrimaryActionClicked += (_, _) => _vm.CloseGuide();
        _guideDialog.Canceled += (_, _) => _vm.CloseGuide();

    }

    public bool IsGuideVisible => _guideDialog.IsOpen || _vm.IsGuideOpen;
    public string ClipboardStatusText => _vm.ClipboardStatusText;
    public MaterialDialog GuideDialog => _guideDialog;
    public bool IsSplitLayout => _isSplitLayout;

    public void ApplyResponsiveLayout(bool split)
    {
        if (_isSplitLayout == split) return;
        _isSplitLayout = split;
        if (_headerActions is not null)
        {
            Grid.SetRow(_headerActions, split ? 0 : 1);
            Grid.SetColumn(_headerActions, split ? 1 : 0);
            Grid.SetColumnSpan(_headerActions, split ? 1 : 2);
        }
        if (_desktopSplitGrid is null || _dialPane is null || _infoPane is null)
        {
            return;
        }

        if (split)
        {
            _desktopSplitGrid.ColumnDefinitions.Clear();
            _desktopSplitGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _desktopSplitGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(_dialPane, 0);
            Grid.SetRow(_dialPane, 0);
            Grid.SetColumn(_infoPane, 1);
            Grid.SetRow(_infoPane, 0);
        }
        else
        {
            _desktopSplitGrid.ColumnDefinitions.Clear();
            _desktopSplitGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(_dialPane, 0);
            Grid.SetRow(_dialPane, 0);
            Grid.SetColumn(_infoPane, 0);
            Grid.SetRow(_infoPane, 1);
        }
    }

    private void OnOpenGuideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenGuide();

    private async void OnDialClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.DialEndpointAsync();

    private async Task CopyEndpointAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
