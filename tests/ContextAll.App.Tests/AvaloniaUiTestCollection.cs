using System.Runtime.InteropServices;
using Avalonia;
using Xunit;

namespace ContextAll.App.Tests;

[CollectionDefinition("AvaloniaUI", DisableParallelization = true)]
public sealed class AvaloniaUiTestCollection;

public static partial class AvaloniaUiTestHost
{
    private static readonly object Gate = new();
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
                AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .SetupWithoutStarting();
            }

            _initialized = true;
        }
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
