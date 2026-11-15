using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CCSWE.Avalonia.Material;
using ContextAll.App.Platform;
using ContextAll.App.Theming;

namespace ContextAll.App;

public sealed class App : Application
{
    private LinuxAppearanceCoordinator? _appearance;
    private TrayIcon? _trayIcon;

    public static event Action<string?>? ExternalActivationRequested;

    public static void NotifyExternalActivation(string? token = null) =>
        ExternalActivationRequested?.Invoke(token);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public void ApplyAppearanceOverrides(AppearanceModeOverride? mode, AccentSourceOverride? accent)
    {
        _appearance ??= new LinuxAppearanceCoordinator(
            new DesktopLinuxAppearanceSource(),
            variant => RequestedThemeVariant = variant,
            Resources,
            RunOnUiThread,
            Styles.OfType<MaterialTheme>().FirstOrDefault());
        _appearance.SetOverrides(mode, accent);
    }

    private static void RunOnUiThread(Action work)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            work();
        }
        else
        {
            Dispatcher.UIThread.Post(work);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var icon = TryLoadAppIcon();
            var preferencesPath = ShellPreferencesStore.PreferencesPath(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
            var initialPrefs = ShellPreferencesStore.Load(preferencesPath);
            var materialTheme = Styles.OfType<MaterialTheme>().FirstOrDefault();

            var window = new MainWindow(initialPrefs, preferencesPath)
            {
                Icon = icon,
            };
            desktop.MainWindow = window;

            _appearance = new LinuxAppearanceCoordinator(
                new DesktopLinuxAppearanceSource(),
                variant => RequestedThemeVariant = variant,
                Resources,
                RunOnUiThread,
                materialTheme);
            _appearance.SetOverrides(initialPrefs.AppearanceModeOverride, initialPrefs.AccentSourceOverride);

            _ = StartAppearanceAsync(_appearance);

            SetupTrayIcon(icon, window);
            window.TrayAvailabilityUpdated += available =>
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.IsVisible = available;
                }
            };

            ExternalActivationRequested += _ => Dispatcher.UIThread.Post(window.RestoreAndFocusFromActivation);
            desktop.Exit += (_, _) => _ = DisposeResourcesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(WindowIcon? icon, MainWindow window)
    {
        if (icon is null)
        {
            return;
        }

        var showItem = new NativeMenuItem("显示 ContextAll");
        showItem.Click += (_, _) => Dispatcher.UIThread.Post(window.RestoreAndFocusFromActivation);

        var quitItem = new NativeMenuItem("退出 ContextAll");
        quitItem.Click += (_, _) => Dispatcher.UIThread.Post(window.ForceQuit);

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "ContextAll — 局域网剪贴板与文件传输",
            Menu = menu,
            IsVisible = false,
        };
        _trayIcon.Clicked += (_, _) => Dispatcher.UIThread.Post(window.RestoreAndFocusFromActivation);
        TrayIcon.SetIcons(this, [_trayIcon]);
    }

    private static WindowIcon? TryLoadAppIcon()
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("ContextAll.App.Assets.contextall.png");
        return stream is null ? null : new WindowIcon(stream);
    }

    private static async Task StartAppearanceAsync(LinuxAppearanceCoordinator coordinator)
    {
        try
        {
            await coordinator.StartAsync();
        }
        catch
        {
            // Keep default brand seed palette if desktop portal/settings fail unexpectedly.
        }
    }

    private async Task DisposeResourcesAsync()
    {
        _trayIcon?.Dispose();
        if (_appearance is not null)
        {
            await _appearance.DisposeAsync();
        }
    }
}
