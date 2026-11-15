using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using ContextAll.App.Controls;
using ContextAll.App.ViewModels;
using ContextAll.Ipc;

namespace ContextAll.App.Views;

/// <summary>
/// MD3E Connection and Diagnostics view for manual QUIC dialing, LAN endpoints copy, and onboarding guide.
/// </summary>
public partial class ConnectionView : UserControl
{
    private readonly ConnectionViewModel _vm;
    private StackPanel? _localEndpointsPanel;
    private TextBlock? _clipboardStatusLabel;
    private Border? _feedbackBanner;
    private TextBlock? _feedbackLabel;
    private MaterialDialog _guideDialog = new();

    public ConnectionView()
        : this(new ConnectionViewModel(new LocalDaemonConnection()))
    {
    }

    public ConnectionView(ConnectionViewModel vm)
    {
        _vm = vm;
        DataContext = vm;

        AvaloniaXamlLoader.Load(this);
        ResolveNamedParts();
        BuildGuideBody();

        _guideDialog.PrimaryActionClicked += (_, _) => _vm.CloseGuide();
        _guideDialog.Canceled += (_, _) => _vm.CloseGuide();

        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public bool IsGuideVisible => _guideDialog.IsOpen;
    public string ClipboardStatusText => _clipboardStatusLabel?.Text ?? string.Empty;
    public MaterialDialog GuideDialog => _guideDialog;

    private void ResolveNamedParts()
    {
        _localEndpointsPanel = this.FindControl<StackPanel>("LocalEndpointsPanel");
        _clipboardStatusLabel = this.FindControl<TextBlock>("ClipboardStatusLabel");
        _feedbackBanner = this.FindControl<Border>("FeedbackBanner");
        _feedbackLabel = this.FindControl<TextBlock>("FeedbackLabel");
        if (this.FindControl<MaterialDialog>("GuideDialogControl") is { } guide)
        {
            _guideDialog = guide;
        }
    }

    private void BuildGuideBody()
    {
        _guideDialog.BodyContent = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                BuildGuideStep("1. 确认同一局域网设备已启动 ContextAll 守护进程（默认端口 UDP 42461）。"),
                BuildGuideStep("2. 若 mDNS 未自动发现对端，可在此页面输入对方「本机局域网地址」手动拨号。"),
                BuildGuideStep("3. 配对时请核对 6 位等宽验证码，通过后可在「设备」开启剪贴板同步，或在「传输」发送多文件。"),
            },
        };
    }

    private static TextBlock BuildGuideStep(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        block.Classes.Add("BodyMedium");
        return block;
    }

    private void OnOpenGuideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenGuide();

    private async void OnDialClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.DialEndpointAsync();

    private void Refresh()
    {
        if (_localEndpointsPanel is not null)
        {
            _localEndpointsPanel.Children.Clear();
            if (_vm.LocalEndpoints.Count == 0)
            {
                var empty = new TextBlock { Text = "暂未获取本机局域网地址" };
                empty.Classes.Add("BodyMedium");
                _localEndpointsPanel.Children.Add(empty);
            }
            else
            {
                foreach (var endpoint in _vm.LocalEndpoints)
                {
                    var row = new Grid
                    {
                        ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                        ColumnSpacing = 8,
                    };
                    var epText = new TextBlock
                    {
                        Text = endpoint,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    epText.Classes.Add("BodyMedium MonoTabular");

                    var copyBtn = new Button
                    {
                        Content = "复制",
                        CornerRadius = new Avalonia.CornerRadius(16),
                        Padding = new Avalonia.Thickness(12, 6),
                    };
                    copyBtn.Classes.Add("FilledTonal");
                    var captured = endpoint;
                    copyBtn.Click += async (_, _) =>
                    {
                        var topLevel = TopLevel.GetTopLevel(this);
                        if (topLevel?.Clipboard is { } clipboard)
                        {
                            await clipboard.SetTextAsync(captured);
                        }
                    };

                    Grid.SetColumn(epText, 0);
                    Grid.SetColumn(copyBtn, 1);
                    row.Children.Add(epText);
                    row.Children.Add(copyBtn);
                    _localEndpointsPanel.Children.Add(row);
                }
            }
        }

        if (_clipboardStatusLabel is not null)
        {
            _clipboardStatusLabel.Text = $"{_vm.ClipboardBackendLabel} ({_vm.ClipboardBackendCode})";
        }

        _guideDialog.IsOpen = _vm.IsGuideOpen;
        UpdateFeedbackTone();
    }

    private void UpdateFeedbackTone()
    {
        if (_feedbackBanner is null || _feedbackLabel is null || Avalonia.Application.Current is null)
        {
            return;
        }

        var bgKey = _vm.DialIsError ? "ErrorContainer" : "SecondaryContainer";
        var fgKey = _vm.DialIsError ? "OnErrorContainer" : "OnSecondaryContainer";
        if (Avalonia.Application.Current.TryFindResource(bgKey, out var bg) && bg is Avalonia.Media.IBrush bgBrush)
        {
            _feedbackBanner.Background = bgBrush;
        }
        if (Avalonia.Application.Current.TryFindResource(fgKey, out var fg) && fg is Avalonia.Media.IBrush fgBrush)
        {
            _feedbackLabel.Foreground = fgBrush;
        }
    }
}
