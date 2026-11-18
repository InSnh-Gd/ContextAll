using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ContextAll.Ipc;
using ContextAll.Protocol;

namespace ContextAll.App.Views;

internal sealed class ClipboardHistoryWindow : Window
{
    private readonly ILocalDaemonClient _client;
    private readonly StackPanel _entries = new() { Spacing = 10 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _enabled = new() { Content = "保留历史" };
    private readonly NumericUpDown _days = new() { Minimum = 1, Maximum = 30, Value = 7, Width = 75 };
    private readonly Button _more = new() { Content = "更早", IsVisible = false };
    private readonly TextBlock _groupStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _groupDevice = new() { PlaceholderText = "已配对设备 ID" };
    private readonly ScrollViewer _scroll;
    private int _offset;
    private int? _nextOffset;
    private bool _loading;

    public ClipboardHistoryWindow(ILocalDaemonClient client)
    {
        _client = client;
        Title = "剪贴板历史";
        Width = 440;
        Height = 500;
        MinWidth = 340;
        MinHeight = 280;
        var pin = new CheckBox { Content = "置顶" };
        pin.IsCheckedChanged += (_, _) => Topmost = pin.IsChecked == true;
        var refresh = new Button { Content = "刷新" };
        refresh.Click += async (_, _) => await LoadAsync(0);
        var save = new Button { Content = "保存" };
        save.Click += async (_, _) =>
        {
            var result = await _client.ConfigureClipboardHistoryAsync(_enabled.IsChecked == true, (int?)_days.Value);
            await LoadAsync(0);
            if (!result.Ok) _status.Text = "未能保存设置";
        };
        _more.Click += async (_, _) =>
        {
            if (_nextOffset is int offset) await LoadAsync(offset);
        };
        var latest = new Button { Content = "最新" };
        latest.Click += async (_, _) => await LoadAsync(0);
        var groupControls = new Expander
        {
            Header = "共享组",
            Content = new StackPanel
            {
                Spacing = 6,
                Children = { _groupStatus, _groupDevice, new WrapPanel
                {
                    Children = { GroupButton("创建", "self"), GroupButton("加入"), GroupButton("邀请", invited: true),
                        GroupButton("移除", invited: false), GroupButton("退出或解散", "") },
                } },
            },
        };
        var header = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pin, refresh } },
                new WrapPanel { Children = { _enabled, new TextBlock { Text = "保留天数", VerticalAlignment = VerticalAlignment.Center }, _days, save } },
                _status,
                groupControls,
            },
        };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { latest, _more } };
        _scroll = new ScrollViewer { Content = _entries };
        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(_scroll);
        Content = layout;
        Opened += async (_, _) => await LoadAsync(0);
    }

    private async Task LoadAsync(int offset)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var result = await _client.ReadClipboardHistoryAsync(offset);
            _entries.Children.Clear();
            _scroll.Offset = default;
            _nextOffset = null;
            _more.IsVisible = false;
            if (result.ClipboardHistory is not { } page)
            {
                _status.Text = "剪贴板历史不可用";
                return;
            }

            _offset = offset;
            _enabled.IsChecked = page.Enabled;
            _enabled.IsEnabled = page.GroupOwner is null;
            _days.Value = page.RetentionDays;
            _groupStatus.Text = page.GroupOwner is { } owner ? $"组主：{owner}" : "未加入共享组";
            _status.Text = !page.Enabled ? "历史已关闭" : page.Entries.Length == 0 ? "暂无历史" : "";
            _nextOffset = page.NextOffset;
            _more.IsVisible = _nextOffset is not null;
            foreach (var entry in page.Entries)
            {
                var copy = EntryButton(entry, "复制到本机", sync: false);
                var sync = EntryButton(entry, "同步", sync: true);
                _entries.Children.Add(new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = entry.Preview.Length == 0 ? "空文本" : entry.Preview, TextWrapping = TextWrapping.Wrap, MaxLines = 3 },
                        new TextBlock { Text = entry.Source, TextTrimming = TextTrimming.CharacterEllipsis, Classes = { "LabelSmall" } },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { copy, sync } },
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
            if (result.Ok && sync) await LoadAsync(_offset);
            _status.Text = result.Ok ? (sync ? "已提交同步" : "已请求复制") : "条目已失效或剪贴板不可用";
        };
        return button;
    }

    private Button GroupButton(string label, string? owner = null, bool? invited = null)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            var device = _groupDevice.Text?.Trim();
            if (owner is null && string.IsNullOrEmpty(device))
            {
                _groupStatus.Text = "请选择已配对设备";
                return;
            }

            var result = await _client.ConfigureClipboardGroupAsync(invited is null ? owner ?? device : null,
                invited is null ? null : device, invited);
            if (result.Ok) await LoadAsync(0);
            else _groupStatus.Text = "未能修改共享组，请核对设备和权限";
        };
        return button;
    }
}
