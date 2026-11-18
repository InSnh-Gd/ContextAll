using ContextAll.Ipc;
using ContextAll.Protocol;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ContextAll.App.Windows;

public sealed partial class MainWindow : Window
{
    private readonly LocalDaemonConnection _client = new();
    private int? _nextOffset;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        Title = "ContextAll · 剪贴板历史";
        Closed += async (_, _) => await _client.DisposeAsync();
        _client.StateChanged += state =>
        {
            if (state.ConnectionStatus == LocalConnectionStatus.Connected)
            {
                DispatcherQueue.TryEnqueue(async () => await LoadAsync());
            }
        };
        _ = _client.StartAsync();
    }

    private void OnPinChanged(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox pin && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = pin.IsChecked == true;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs args)
    {
        var days = double.IsNaN(RetentionDays.Value) ? 7 : (int)RetentionDays.Value;
        var result = await _client.ConfigureClipboardHistoryAsync(HistoryEnabled.IsOn, days);
        await LoadAsync();
        if (!result.Ok) Status.Text = "未能保存设置";
    }

    private async void OnRefresh(object sender, RoutedEventArgs args) => await LoadAsync();

    private async void OnGroupCommand(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string operation }) return;
        var peer = GroupDevice.Text.Trim();
        if (peer.Length == 0 && operation is "join" or "invite" or "remove")
        {
            GroupStatus.Text = "请输入已配对设备 ID";
            return;
        }

        var result = operation switch
        {
            "create" => await _client.ConfigureClipboardGroupAsync("self"),
            "join" => await _client.ConfigureClipboardGroupAsync(peer),
            "invite" => await _client.ConfigureClipboardGroupAsync(null, peer, true),
            "remove" => await _client.ConfigureClipboardGroupAsync(null, peer, false),
            _ => await _client.ConfigureClipboardGroupAsync(""),
        };
        if (result.Ok) await LoadAsync();
        else GroupStatus.Text = "未能修改共享组，请核对设备和权限";
    }

    private async void OnMore(object sender, RoutedEventArgs args)
    {
        if (_nextOffset is int offset) await LoadAsync(offset);
    }

    private async Task LoadAsync(int offset = 0)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var result = await _client.ReadClipboardHistoryAsync(offset);
            HistoryEntries.Items.Clear();
            _nextOffset = result.ClipboardHistory?.NextOffset;
            More.IsEnabled = _nextOffset is not null;
            if (result.ClipboardHistory is not { } page)
            {
                Status.Text = "剪贴板历史不可用";
                return;
            }

            HistoryEnabled.IsOn = page.Enabled;
            HistoryEnabled.IsEnabled = page.GroupOwner is null;
            RetentionDays.Value = page.RetentionDays;
            GroupStatus.Text = page.GroupOwner is { } owner ? $"组主：{owner}" : "未加入共享组";
            Status.Text = !page.Enabled ? "历史已关闭" : page.Entries.Length == 0 ? "暂无历史" : "";
            foreach (var entry in page.Entries)
            {
                HistoryEntries.Items.Add(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = entry.Preview.Length == 0 ? "空文本" : entry.Preview, TextWrapping = TextWrapping.Wrap, MaxLines = 3 },
                        new TextBlock { Text = entry.Source, TextTrimming = TextTrimming.CharacterEllipsis },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { EntryButton(entry, "复制到本机", false), EntryButton(entry, "同步", true) } },
                    },
                });
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private Button EntryButton(ClipboardHistoryEntry entry, string label, bool sync)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            var result = await _client.UseClipboardHistoryAsync(entry.Id, sync);
            Status.Text = result.Ok ? (sync ? "已提交同步" : "已请求复制") : "条目已失效或剪贴板不可用";
        };
        return button;
    }
}
