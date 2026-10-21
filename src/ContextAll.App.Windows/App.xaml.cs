using ContextAll.Ipc;
using Microsoft.UI.Xaml;

namespace ContextAll.App.Windows;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = LocalChannelClient.ForThisPlatform();
        window = new MainWindow();
        window.Activate();
    }
}
