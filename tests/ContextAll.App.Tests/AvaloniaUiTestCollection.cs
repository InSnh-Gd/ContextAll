using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ContextAll.App.Tests;

[CollectionDefinition("AvaloniaUI", DisableParallelization = true)]
public sealed class AvaloniaUiTestCollection : ICollectionFixture<AvaloniaUiFixture>;

public sealed class AvaloniaUiFixture : IDisposable
{
    public AvaloniaUiFixture() => AvaloniaUiTestHost.EnsureInitialized();

    public void Dispose()
    {
    }
}

public static partial class AvaloniaUiTestHost
{
    private static readonly object Gate = new();
    private static readonly CancellationTokenSource ShutdownCts = new();
    private static bool _initialized;

    [LibraryImport("libdl.so.2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint dlopen(string filename, int flags);

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                PreloadNixSharedLibs();
            }

            if (Application.Current is null)
            {
                Exception? initError = null;
                using var ready = new ManualResetEventSlim(false);
                var uiThread = new Thread(() =>
                {
                    try
                    {
                        AppBuilder.Configure<App>()
                            .UsePlatformDetect()
                            .SetupWithoutStarting();
                    }
                    catch (Exception ex)
                    {
                        initError = ex;
                    }
                    finally
                    {
                        ready.Set();
                    }

                    if (initError is null)
                    {
                        Dispatcher.UIThread.MainLoop(ShutdownCts.Token);
                    }
                })
                {
                    IsBackground = true,
                    Name = "AvaloniaUiTestThread",
                };

                uiThread.Start();
                ready.Wait();
                if (initError is not null)
                {
                    throw new InvalidOperationException("Failed to initialize Avalonia UI test thread.", initError);
                }
            }

            _initialized = true;
        }
    }

    public static void RunOnUiThread(Action action)
    {
        EnsureInitialized();
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);
    }

    public static T RunOnUiThread<T>(Func<T> func)
    {
        EnsureInitialized();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return func();
        }

        return Dispatcher.UIThread.Invoke(func);
    }

    public static async Task RunOnUiThreadAsync(Func<Task> func)
    {
        EnsureInitialized();
        if (Dispatcher.UIThread.CheckAccess())
        {
            await func();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(func);
    }

    public static void Layout(Layoutable control, double width, double height)
    {
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        control.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public static Window ShowAndLayout(Control content, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };
        window.Show();
        Layout(window, width, height);
        return window;
    }

    private static void PreloadNixSharedLibs()
    {
        if (!Directory.Exists("/nix/store"))
        {
            return;
        }

        // Preload NixOS shared dependencies with RTLD_NOW | RTLD_GLOBAL so bare `dotnet test` runs without wrapper scripts.
        const int rtldNowGlobal = 0x102;
        (string SoName, string StoreGlob)[] libs =
        [
            ("libz.so.1", "*zlib*"),
            ("libbz2.so.1", "*bzip2*"),
            ("libexpat.so.1", "*expat*"),
            ("libpng16.so.16", "*libpng*"),
            ("libfreetype.so.6", "*freetype*"),
            ("libfontconfig.so.1", "*fontconfig*"),
            ("libICE.so.6", "*libice*"),
            ("libICE.so.6", "*libICE*"),
            ("libSM.so.6", "*libsm*"),
            ("libSM.so.6", "*libSM*"),
            ("libX11.so.6", "*libx11*"),
            ("libX11.so.6", "*libX11*"),
            ("libXfixes.so.3", "*libxfixes*"),
            ("libXfixes.so.3", "*libXfixes*"),
            ("libXi.so.6", "*libxi*"),
            ("libXi.so.6", "*libXi*"),
            ("libXrandr.so.2", "*libxrandr*"),
            ("libXrandr.so.2", "*libXrandr*"),
            ("libXcursor.so.1", "*libxcursor*"),
            ("libXcursor.so.1", "*libXcursor*"),
            ("libxkbcommon.so.0", "*libxkbcommon*"),
            ("libwayland-client.so.0", "*wayland*"),
            ("libwayland-cursor.so.0", "*wayland*"),
            ("libwayland-egl.so.1", "*wayland*"),
        ];

        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (soName, glob) in libs)
        {
            if (loaded.Contains(soName))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(soName, out _))
            {
                loaded.Add(soName);
                continue;
            }

            try
            {
                foreach (var pkgDir in Directory.EnumerateDirectories("/nix/store", glob))
                {
                    if (pkgDir.EndsWith("-dev", StringComparison.Ordinal) ||
                        pkgDir.Contains("fhsenv", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var cand = Path.Combine(pkgDir, "lib", soName);
                    if (File.Exists(cand) && dlopen(cand, rtldNowGlobal) != nint.Zero)
                    {
                        loaded.Add(soName);
                        break;
                    }
                }
            }
            catch
            {
                // Ignore restricted paths cleanly.
            }
        }
    }
}
