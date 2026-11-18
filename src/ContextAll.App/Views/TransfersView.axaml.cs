using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E <c>g2-split-workbench</c> Transfers view with session KPI strip, filter chips,
/// responsive 400dp workbench + live queue split, Portal multi-file picker, text-to-file dialog,
/// drag-drop, wavy progress indicators, and incoming prompt decisions.
/// </summary>
public partial class TransfersView : UserControl
{
    private const double SplitBreakpoint = 840.0;

    private readonly TransfersViewModel _vm;

    private Grid? _workbenchGrid;
    private Border? _workbenchPane;
    private Border? _queuePane;
    private Border? _dropZone;
    private Border? _noticeBanner;
    private TextBlock? _noticeText;
    private MaterialDialog _textFileDialog = new();
    private bool _isSplitLayout = true;

    public TransfersView()
        : this(new TransfersViewModel(IdleLocalChannel.Shared))
    {
    }

    public TransfersView(TransfersViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        _vm.MultiFilePicker ??= OpenPortalMultiFilePickerAsync;
        _vm.NavigateToDevicesRequested += () => NavigateToDevicesRequested?.Invoke();

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();
        WireTextDialog();

        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        if (_dropZone is not null)
        {
            DragDrop.AddDragOverHandler(_dropZone, OnDragOver);
            DragDrop.AddDropHandler(_dropZone, OnDrop);
        }

        SizeChanged += (_, e) => ApplyResponsiveLayout(e.NewSize.Width);
        ApplyResponsiveLayout(Bounds.Width > 0 ? Bounds.Width : 1080);
    }

    public event Action? NavigateToDevicesRequested;

    public bool IsSplitLayout => _isSplitLayout;
    public bool IsNoticeVisible => _noticeBanner?.IsVisible ?? false;
    public string NoticeText => _noticeText?.Text ?? string.Empty;
    public bool IsTextDialogVisible => _textFileDialog.IsOpen;
    public MaterialDialog TextFileDialog => _textFileDialog;
    public int RenderedActiveTransferCount => CountTaggedTiles("active-item");

    public int RenderedHistoryCount => CountTaggedTiles("history-item");

    private int CountTaggedTiles(string tag) =>
        this.GetVisualDescendants().OfType<ExpressiveGroupTile>().Count(t => Equals(t.Tag, tag));

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
        _dropZone = this.FindControl<Border>("DropZone");
        _noticeBanner = this.FindControl<Border>("NoticeBanner");
        _noticeText = this.FindControl<TextBlock>("NoticeLabel");

        if (this.FindControl<MaterialDialog>("TextFileDialogControl") is { } dialog)
        {
            _textFileDialog = dialog;
        }
    }

    private void WireTextDialog()
    {
        _textFileDialog.PrimaryActionClicked += (_, _) =>
        {
            if (!_vm.SendTextDraftAsFile())
            {
                _vm.CloseTextDialog();
            }
        };
        _textFileDialog.Canceled += (_, _) => _vm.CloseTextDialog();
    }

    private async void OnChooseFilesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.PickAndSendFilesAsync();

    private void OnNewTextFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenNewTextFileDialog();

    private async void OnClipboardToFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string? clipText = null;
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard is { } clipboard)
            {
                clipText = await clipboard.TryGetTextAsync();
            }
        }
        catch
        {
            // Clipboard access may be unavailable in headless or restricted sessions.
        }

        _vm.OpenClipboardFileDialog(clipText);
    }

    private void OnDismissNoticeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.DismissNotice();

    private void OnClearQueueClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.ClearQueue();

    private void OnClearHistoryClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.ClearHistory();

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
}
