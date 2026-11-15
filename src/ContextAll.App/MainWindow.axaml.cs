using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ContextAll.App.Chrome;
using ContextAll.App.Platform;
using ContextAll.App.ViewModels;
using ContextAll.App.Views;
using ContextAll.Ipc;

namespace ContextAll.App;

public sealed partial class MainWindow : Window
{
    private readonly string _preferencesPath;
    private readonly ILocalDaemonClient _daemonClient;
    private readonly IStatusNotifierProbe _watcherProbe;
    private readonly ShellViewModel _shellVm;
    private readonly ShellRootView _rootView;

    private ShellPreferences _preferences;
    private bool _watcherAvailable;
    private bool _forceQuit;
    private bool _isDisposed;

    public MainWindow()
        : this(new ShellPreferences(), ShellPreferencesStore.PreferencesPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")))
    {
    }

    public MainWindow(ShellPreferences initialPreferences, string preferencesPath)
    {
        InitializeComponent();

        _preferences = initialPreferences;
        _preferencesPath = preferencesPath;
        _watcherProbe = new DbusStatusNotifierProbe();
        _daemonClient = new LocalDaemonConnection();

        Width = Math.Max(ShellLayout.MinimumWidth, _preferences.WindowWidth);
        Height = Math.Max(ShellLayout.MinimumHeight, _preferences.WindowHeight);
        MinWidth = ShellLayout.MinimumWidth;
        MinHeight = ShellLayout.MinimumHeight;

        _shellVm = new ShellViewModel(
            _daemonClient,
            new LinuxDaemonController(),
            _preferences,
            SavePreferences,
            work => Dispatcher.UIThread.Post(work));

        _rootView = new ShellRootView(_shellVm);
        ShellHost.Content = _rootView;

        _rootView.HeaderBar.MinimizeRequested += () => WindowState = WindowState.Minimized;
        _rootView.HeaderBar.MaximizeRestoreRequested += ToggleMaximizeRestore;
        _rootView.HeaderBar.CloseRequested += Close;

        _shellVm.PreferencesChanged += OnPreferencesChanged;

        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                ApplyChromeMode();
            }
        };

        ApplyChromeMode();
        ApplyWidth(Width);

        Opened += (_, _) => _ = InitializeRuntimeAsync();
    }

    public event Action<bool>? TrayAvailabilityUpdated;

    public void ApplyWidth(double width)
    {
        if (double.IsFinite(width) && width > 0)
        {
            _shellVm.ApplyWidth(width);
        }
    }

    public void RestoreAndFocusFromActivation()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public void ForceQuit()
    {
        _forceQuit = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (!_forceQuit && TrayClosePolicy.ShouldHideToTray(_preferences.TrayEnabled, _watcherAvailable))
        {
            e.Cancel = true;
            Hide();
            _ = RefreshTrayWatcherAsync();
            return;
        }

        PersistWindowDimensions();
        _ = DisposeClientAsync();
    }

    private async Task InitializeRuntimeAsync()
    {
        try
        {
            await _daemonClient.StartAsync();
        }
        catch
        {
            // Disconnected onboarding remains active if daemon is unavailable.
        }

        try
        {
            var layout = await WindowButtonLayoutParser.ProbeCurrentDesktopAsync();
            Dispatcher.UIThread.Post(() => _rootView.SetHeaderButtonLayout(layout));
        }
        catch
        {
            // Keep default right button cluster.
        }

        await RefreshTrayWatcherAsync();
    }

    private async Task RefreshTrayWatcherAsync()
    {
        var available = false;
        if (_preferences.TrayEnabled)
        {
            try
            {
                available = await _watcherProbe.IsWatcherAvailableAsync();
            }
            catch
            {
                available = false;
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            _watcherAvailable = available;
            TrayAvailabilityUpdated?.Invoke(_preferences.TrayEnabled && available);
        });
    }

    private void OnPreferencesChanged(ShellPreferences next)
    {
        _preferences = next;
        ApplyChromeMode();
        (Application.Current as App)?.ApplyAppearanceOverrides(next.AppearanceModeOverride, next.AccentSourceOverride);
        _ = RefreshTrayWatcherAsync();
    }

    private void SavePreferences(ShellPreferences next)
    {
        _preferences = next;
        ShellPreferencesStore.Save(_preferencesPath, next);
    }

    private void PersistWindowDimensions()
    {
        if (WindowState != WindowState.Normal || Bounds.Width < ShellLayout.MinimumWidth || Bounds.Height < ShellLayout.MinimumHeight)
        {
            return;
        }

        SavePreferences(_preferences with
        {
            WindowWidth = Bounds.Width,
            WindowHeight = Bounds.Height,
        });
    }

    private void ApplyChromeMode()
    {
        var chrome = ChromeModeState.Compute(_preferences.UseNativeTitleBar, WindowState);
        WindowDecorations = chrome.Decorations;
        ExtendClientAreaToDecorationsHint = false;
        TransparencyLevelHint = chrome.ShowIntegratedHeader
            ? [WindowTransparencyLevel.Transparent]
            : [];
        Background = chrome.ShowIntegratedHeader ? Brushes.Transparent : null;
        _rootView.ApplyChrome(chrome);
    }

    private void ToggleMaximizeRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async Task DisposeClientAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            await _daemonClient.DisposeAsync();
        }
        catch
        {
            // Ignore shutdown races.
        }
    }
}
