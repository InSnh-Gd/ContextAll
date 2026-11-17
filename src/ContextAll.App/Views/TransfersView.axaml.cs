using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    private readonly TextBox _textFileNameBox;
    private readonly TextBox _textContentBox;
    private readonly TextBlock _textPreviewLabel;

    private Grid? _workbenchGrid;
    private Border? _workbenchPane;
    private Border? _queuePane;
    private Border? _dropZone;
    private Border? _noticeBanner;
    private TextBlock? _noticeText;
    private MaterialDialog _textFileDialog = new();
    private bool _isSplitLayout = true;
    private bool _syncingTextInputs;

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

        _textFileNameBox = new TextBox
        {
            PlaceholderText = "可选文件名（留空自动命名为 note-HHmmss.txt）",
        };
        _textFileNameBox.Classes.Add("Outlined");
        _textFileNameBox.Classes.Add("MonoTabular");
        _textFileNameBox.TextChanged += (_, _) =>
        {
            if (!_syncingTextInputs)
            {
                _vm.TextFileNameDraft = _textFileNameBox.Text ?? string.Empty;
            }
        };

        _textContentBox = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 120,
            MaxHeight = 220,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "输入或粘贴要作为文本文件发送的内容…",
        };
        _textContentBox.Classes.Add("Outlined");
        _textContentBox.TextChanged += (_, _) =>
        {
            if (!_syncingTextInputs)
            {
                _vm.TextContentDraft = _textContentBox.Text ?? string.Empty;
            }
        };

        _textPreviewLabel = new TextBlock();
        _textPreviewLabel.Classes.Add("BodySmall");
        _textPreviewLabel.Classes.Add("MonoTabular");

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
        _vm.PropertyChanged += (_, _) => Refresh();

        Md3eResourceLookup.TrackThemeChanges(this, RefreshDynamicVisuals);
        ApplyResponsiveLayout(Bounds.Width > 0 ? Bounds.Width : 1080);
        Refresh();
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
        _textFileDialog.BodyContent = new StackPanel
        {
            Spacing = 10,
            Children = { _textFileNameBox, _textContentBox, _textPreviewLabel },
        };
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

    private void Refresh()
    {
        UpdateNoticeBanner();
        UpdateTextDialog();
    }

    private void RefreshDynamicVisuals()
    {
        UpdateNoticeBanner();
        UpdateTextDialog();
    }

    private void UpdateTextDialog()
    {
        _textFileDialog.Title = _vm.TextDialogFromClipboard ? "剪贴板内容存为文件发送" : "作为文本文件发送";
        _textFileDialog.IsOpen = _vm.IsTextDialogOpen;

        _syncingTextInputs = true;
        try
        {
            if (!string.Equals(_textFileNameBox.Text ?? string.Empty, _vm.TextFileNameDraft, StringComparison.Ordinal))
            {
                _textFileNameBox.Text = _vm.TextFileNameDraft;
            }
            if (!string.Equals(_textContentBox.Text ?? string.Empty, _vm.TextContentDraft, StringComparison.Ordinal))
            {
                _textContentBox.Text = _vm.TextContentDraft;
            }
        }
        finally
        {
            _syncingTextInputs = false;
        }

        _textPreviewLabel.Text =
            $"UTF-8 大小：{ShellFormatting.Bytes(_vm.TextDraftUtf8Bytes)}（{_vm.TextDraftUtf8Bytes:N0} B） · 共 {_vm.TextDraftSegmentCount} 个 64 KiB 分块";
        _textPreviewLabel.Foreground = Md3eResourceLookup.ResolveBrush(this, "Primary", static p => p.Primary);
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
}
