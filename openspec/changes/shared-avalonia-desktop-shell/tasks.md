# Tasks

## 1. Platform seam and tests

- [x] 1.1 Add failing App tests for Windows `shell.json` path, always-available tray probe, Apps-dark appearance mapping, Windows host executable resolution, Windows single-instance activate, and `ContextAll.App.Windows` csproj (`net10.0`, no WinUI / Windows App SDK, references `ContextAll.App`). Verify they fail on the current WinUI project.
- [x] 1.2 Add `IDesktopPlatform` with `LinuxDesktopPlatform` and `WindowsDesktopPlatform`. Verify Linux remains the default `DesktopPlatform.Current` and the new tests pass for the Windows adapter methods.

## 2. Shared App wiring

- [x] 2.1 Point `App` and `MainWindow` at `DesktopPlatform.Current` for preferences path, appearance source, daemon controller, and tray probe. Apply Windows default native title bar only when `shell.json` is missing. Verify existing Linux App tests still pass.

## 3. Avalonia Windows host

- [x] 3.1 Replace WinUI `App.xaml` / `MainWindow.xaml` with Avalonia `Program` (`UseWin32`), keep DPI `app.manifest`, retarget `net10.0`, drop Windows App SDK. Verify `dotnet build src/ContextAll.App.Windows/ContextAll.App.Windows.csproj -c Release` succeeds on Linux and `ShellRulesTests` / Linux.slnf still omit the project.

## 4. Docs

- [x] 4.1 Write `docs/adr/0011-shared-avalonia-desktop-shell.md` in Chinese and rewrite the Windows sections of `docs/specs/device-shell.md` and `docs/specs/repository-baseline.md` with only tested behavior. Update `docs/architecture.md`, `docs/README.md` if needed, `CLAUDE.md`, and `README.md`. Verify each new claim names a test or the Linux `dotnet build` of `App.Windows`.
