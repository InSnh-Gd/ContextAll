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
/// MD3E Connection and Network view for manual QUIC dialing, LAN endpoints copy, protocol specs,
/// per-peer connection overview, and a 4-step interactive usage guide.
/// </summary>
public partial class ConnectionView : UserControl
{
    private static readonly (string Tag, string Title, string Description)[] GuideSteps =
    [
        (
            "无需账号 · 本地直连",
            "1. 同网直连，不经云端",
            "设备连入同一局域网后即可直接建立 QUIC 加密连接，文字和文件都在本地传输，不经过任何云端服务器。"
        ),
        (
            "双向确认 · 安全信任",
            "2. 6 位配对码双向核对",
            "首次连接新设备时，双方屏幕会显示同一组 6 位配对码。核对无误并在两端确认，即可建立长期信任。"
        ),
        (
            "按设备控制 · 边界清晰",
            "3. 按设备控制同步与保存策略",
            "在「设备」页可为每台已配对设备单独开启或关闭剪贴板同步，也能自由选择收到文件时先确认或直接保存。"
        ),
        (
            "有序排队 · 分块校验",
            "4. 多文件排队与分块校验",
            "在「传输」页一次选择或拖入多个文件时，系统会按设备顺序依次发送，并逐块完成 SHA-256 完整性校验。"
        ),
    ];

    private readonly ConnectionViewModel _vm;
    private TextBox? _endpointBox;
    private Border? _feedbackBanner;
    private TextBlock? _feedbackLabel;
    private MaterialDialog _guideDialog = new();

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
        ResolveNamedParts();

        _guideDialog.PrimaryActionClicked += (_, _) => _vm.CloseGuide();
        _guideDialog.Canceled += (_, _) => _vm.CloseGuide();

        Md3eResourceLookup.TrackThemeChanges(this, RefreshDynamicVisuals);
        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public bool IsGuideVisible => _guideDialog.IsOpen;
    public string ClipboardStatusText => _vm.ClipboardStatusText;
    public MaterialDialog GuideDialog => _guideDialog;

    private void ResolveNamedParts()
    {
        _endpointBox = this.FindControl<TextBox>("EndpointBox");
        _feedbackBanner = this.FindControl<Border>("FeedbackBanner");
        _feedbackLabel = this.FindControl<TextBlock>("FeedbackLabel");
        if (this.FindControl<MaterialDialog>("GuideDialogControl") is { } guide)
        {
            _guideDialog = guide;
        }
    }

    private void RebuildGuideBody()
    {
        var stepIndex = Math.Clamp(_vm.GuideStepIndex, 0, GuideSteps.Length - 1);
        var step = GuideSteps[stepIndex];

        var stepCounter = new TextBlock
        {
            Text = $"步骤 {stepIndex + 1} / {GuideSteps.Length}",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Md3eResourceLookup.ResolveBrush(this, "OnSecondaryContainer", static p => p.OnSecondaryContainer),
        };
        stepCounter.Classes.Add("LabelMediumEmphasized");

        var counterPill = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(10, 4),
            Background = Md3eResourceLookup.ResolveBrush(this, "SecondaryContainer", static p => p.SecondaryContainer),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = stepCounter,
        };

        var dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var primaryBrush = Md3eResourceLookup.ResolveBrush(this, "Primary", static p => p.Primary);
        var inactiveDotBrush = Md3eResourceLookup.ResolveBrush(this, "SurfaceContainerHighest", static p => p.SurfaceContainerHighest);
        for (var i = 0; i < GuideSteps.Length; i++)
        {
            var active = i == stepIndex;
            dotsPanel.Children.Add(new Border
            {
                Width = active ? 24 : 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = active ? primaryBrush : inactiveDotBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 12,
        };
        Grid.SetColumn(counterPill, 0);
        Grid.SetColumn(dotsPanel, 1);
        topRow.Children.Add(counterPill);
        topRow.Children.Add(dotsPanel);

        var tagBlock = new TextBlock
        {
            Text = step.Tag,
            Foreground = primaryBrush,
        };
        tagBlock.Classes.Add("LabelLargeEmphasized");

        var titleBlock = new TextBlock
        {
            Text = step.Title,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Md3eResourceLookup.ResolveBrush(this, "OnSurface", static p => p.OnSurface),
        };
        titleBlock.Classes.Add("TitleMediumEmphasized");

        var descBlock = new TextBlock
        {
            Text = step.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Md3eResourceLookup.ResolveBrush(this, "OnSurfaceVariant", static p => p.OnSurfaceVariant),
        };
        descBlock.Classes.Add("BodyMedium");

        var card = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16),
            Background = Md3eResourceLookup.ResolveBrush(this, "SurfaceContainer", static p => p.SurfaceContainer),
            Child = new StackPanel
            {
                Spacing = 8,
                Children = { tagBlock, titleBlock, descBlock },
            },
        };

        var navRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        if (_vm.CanGoPreviousGuideStep)
        {
            var prevBtn = new Button
            {
                Content = "上一步",
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(14, 7),
                VerticalAlignment = VerticalAlignment.Center,
            };
            prevBtn.Classes.Add("Outlined");
            prevBtn.Click += (_, _) => _vm.PreviousGuideStep();
            navRow.Children.Add(prevBtn);
        }

        if (!_vm.IsLastGuideStep)
        {
            var nextBtn = new Button
            {
                Content = "下一步",
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 7),
                VerticalAlignment = VerticalAlignment.Center,
            };
            nextBtn.Classes.Add("FilledTonal");
            nextBtn.Click += (_, _) => _vm.NextGuideStep();
            navRow.Children.Add(nextBtn);
        }
        else
        {
            var finishBtn = new Button
            {
                Content = "完成",
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(16, 7),
                VerticalAlignment = VerticalAlignment.Center,
            };
            finishBtn.Classes.Add("FilledTonal");
            finishBtn.Click += (_, _) => _vm.CloseGuide();
            navRow.Children.Add(finishBtn);
        }

        _guideDialog.BodyContent = new StackPanel
        {
            Spacing = 12,
            Children = { topRow, card, navRow },
        };
    }

    private void OnOpenGuideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _vm.OpenGuide();

    private async void OnDialClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await _vm.DialEndpointAsync();

    private void Refresh()
    {
        RebuildGuideBody();
        _guideDialog.IsOpen = _vm.IsGuideOpen;
        UpdateFeedbackTone();
    }

    private void RefreshDynamicVisuals()
    {
        RebuildGuideBody();
        UpdateFeedbackTone();
    }

    private async Task CopyEndpointAsync(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void UpdateFeedbackTone()
    {
        if (_feedbackBanner is null || _feedbackLabel is null || Avalonia.Application.Current is null)
        {
            return;
        }

        var bgKey = _vm.DialIsError ? "ErrorContainer" : "SecondaryContainer";
        var fgKey = _vm.DialIsError ? "OnErrorContainer" : "OnSecondaryContainer";
        if (this.TryFindResource(bgKey, ActualThemeVariant, out var bg) && bg is IBrush bgBrush)
        {
            _feedbackBanner.Background = bgBrush;
        }
        if (this.TryFindResource(fgKey, ActualThemeVariant, out var fg) && fg is IBrush fgBrush)
        {
            _feedbackLabel.Foreground = fgBrush;
        }
    }
}
