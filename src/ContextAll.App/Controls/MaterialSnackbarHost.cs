using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace ContextAll.App.Controls;

/// <summary>
/// MD3E FIFO snackbar queue host with inverse surface styling, optional action button, and timed auto-dismissal.
/// </summary>
[PseudoClasses(":open", ":has-action")]
[TemplatePart("PART_ActionButton", typeof(Button))]
[TemplatePart("PART_DismissButton", typeof(Button))]
public class MaterialSnackbarHost : TemplatedControl
{
    private sealed record SnackbarEntry(
        string Message,
        string? ActionText,
        Action? ActionCallback,
        TimeSpan Duration);

    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<MaterialSnackbarHost, bool>(nameof(IsOpen), false);

    public static readonly StyledProperty<string> CurrentMessageProperty =
        AvaloniaProperty.Register<MaterialSnackbarHost, string>(nameof(CurrentMessage), string.Empty);

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<MaterialSnackbarHost, string?>(nameof(ActionText));

    public static readonly StyledProperty<bool> HasActionProperty =
        AvaloniaProperty.Register<MaterialSnackbarHost, bool>(nameof(HasAction), false);

    private readonly Queue<SnackbarEntry> _queue = new();
    private SnackbarEntry? _current;
    private TimeSpan _elapsedInCurrent;
    private DispatcherTimer? _timer;
    private Button? _actionButton;
    private Button? _dismissButton;

    public MaterialSnackbarHost()
    {
        IsVisible = false;
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string CurrentMessage
    {
        get => GetValue(CurrentMessageProperty);
        set => SetValue(CurrentMessageProperty, value);
    }

    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public bool HasAction
    {
        get => GetValue(HasActionProperty);
        set => SetValue(HasActionProperty, value);
    }

    /// <summary>Number of queued snackbars waiting behind the currently displayed entry.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Enqueues a snackbar notification in FIFO order.</summary>
    public void Enqueue(
        string message,
        string? actionText = null,
        Action? actionCallback = null,
        TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = new SnackbarEntry(
            message,
            actionText,
            actionCallback,
            duration is { TotalMilliseconds: > 0 } ? duration.Value : DefaultDuration);

        if (_current is null)
        {
            ShowEntry(entry);
        }
        else
        {
            _queue.Enqueue(entry);
        }
    }

    /// <summary>Invokes the active entry's optional action callback and advances to the next entry.</summary>
    public void TriggerAction()
    {
        var callback = _current?.ActionCallback;
        DismissCurrent();
        callback?.Invoke();
    }

    /// <summary>Dismisses the active entry and displays the next queued message, if any.</summary>
    public void DismissCurrent()
    {
        if (_queue.Count > 0)
        {
            ShowEntry(_queue.Dequeue());
        }
        else
        {
            ClearCurrent();
        }
    }

    /// <summary>Advances the active auto-dismiss timer deterministically (also used by unit tests).</summary>
    public void AdvanceTime(TimeSpan elapsed)
    {
        if (_current is null || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _elapsedInCurrent += elapsed;
        while (_current is not null && _elapsedInCurrent >= _current.Duration)
        {
            _elapsedInCurrent -= _current.Duration;
            if (_queue.Count > 0)
            {
                _current = _queue.Dequeue();
                ApplyEntryVisuals(_current);
            }
            else
            {
                ClearCurrent();
                break;
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_actionButton is not null)
        {
            _actionButton.Click -= OnActionButtonClicked;
        }
        if (_dismissButton is not null)
        {
            _dismissButton.Click -= OnDismissButtonClicked;
        }

        _actionButton = e.NameScope.Find<Button>("PART_ActionButton");
        _dismissButton = e.NameScope.Find<Button>("PART_DismissButton");

        if (_actionButton is not null)
        {
            _actionButton.Click += OnActionButtonClicked;
        }
        if (_dismissButton is not null)
        {
            _dismissButton.Click += OnDismissButtonClicked;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += OnTimerTick;
        }

        if (_current is not null)
        {
            _timer.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) =>
        AdvanceTime(TimeSpan.FromMilliseconds(250));

    private void OnActionButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        TriggerAction();

    private void OnDismissButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        DismissCurrent();

    private void ShowEntry(SnackbarEntry entry)
    {
        _current = entry;
        _elapsedInCurrent = TimeSpan.Zero;
        ApplyEntryVisuals(entry);
        _timer?.Start();
    }

    private void ApplyEntryVisuals(SnackbarEntry entry)
    {
        var hasAction = !string.IsNullOrWhiteSpace(entry.ActionText);
        CurrentMessage = entry.Message;
        ActionText = entry.ActionText;
        HasAction = hasAction;
        IsOpen = true;
        IsVisible = true;
        PseudoClasses.Set(":open", true);
        PseudoClasses.Set(":has-action", hasAction);
    }

    private void ClearCurrent()
    {
        _current = null;
        _elapsedInCurrent = TimeSpan.Zero;
        CurrentMessage = string.Empty;
        ActionText = null;
        HasAction = false;
        IsOpen = false;
        IsVisible = false;
        PseudoClasses.Set(":open", false);
        PseudoClasses.Set(":has-action", false);
        _timer?.Stop();
    }
}
