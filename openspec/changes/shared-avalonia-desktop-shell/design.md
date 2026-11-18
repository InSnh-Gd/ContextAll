# Design

## Context

See `proposal.md`. `ContextAll.App` already holds the Avalonia MD3E shell. `ContextAll.App.Desktop` is the Linux entry (`UseWayland` / `UseX11`). `ContextAll.App.Windows` is still unpackaged WinUI 3 and only references `Ipc`. `App`, `MainWindow`, and designer fallbacks construct `LinuxDaemonController`, `DbusStatusNotifierProbe`, and XDG `shell.json` directly.

`windows-daemon` already gives Windows `serve` on `\\.\pipe\ContextAll\<SID>` and `%LOCALAPPDATA%\ContextAll\` host paths. `LocalDaemonConnection` already opens that pipe on Windows.

## Goals / Non-Goals

**Goals:**

- One desktop UI module (`ContextAll.App`) for Linux and Windows.
- Windows entry is Avalonia Win32, `net10.0`, no Windows App SDK.
- Platform differences sit behind one `IDesktopPlatform` seam with two adapters.
- Linux.slnf / Tests.slnf still omit `App.Windows`; `dotnet build` of that csproj works on Linux.

**Non-Goals:**

- Android Compose restyle.
- Completing `windows-daemon` task 6.2 (manual pair/transfer on a Windows box).
- Windows clipboard capture in the daemon.
- MSIX packaging.
- Restyling AXAML (Codex continues Linux UI polish on the shared views).

## Decisions

### D1. Shared UI stays `net10.0` in `ContextAll.App`

Call: do not retarget `ContextAll.App` to `net10.0-windows`. Linux keeps building without a Windows targeting pack. Hosts inject adapters.

Alternative: multi-TFM `net10.0;net10.0-windows`. Rejected: Linux CI would still need the Windows pack or `#if` sprawl.

### D2. One `IDesktopPlatform` seam, two adapters

Interface (small): preferences path, default native title bar, `IDaemonController`, `IStatusNotifierProbe`, `ILinuxAppearanceSource`.

- `LinuxDesktopPlatform`: XDG `shell.json`, D-Bus tray probe, portal appearance, `LinuxDaemonController`, native title bar off by default.
- `WindowsDesktopPlatform`: `%LOCALAPPDATA%\ContextAll\shell.json`, tray probe always available, Apps light/dark (+ optional DWM accent) mapped through existing `AppearanceResolver`, `WindowsDaemonController`, native title bar on when `shell.json` is missing.

`DesktopPlatform.Current` defaults to Linux so App tests and the AXAML designer keep working on Linux. `App.Windows` `Program.Main` sets the Windows adapter before `StartWithClassicDesktopLifetime`.

Alternative: `#if WINDOWS` in views. Rejected: views stay platform-free for the shared UI.

### D3. Windows host is `UseWin32()`, not WinUI

`ContextAll.App.Windows` references `ContextAll.App`, `Avalonia.Win32` / `Avalonia.Desktop`, keeps a DPI `app.manifest`, deletes `App.xaml` / `MainWindow.xaml` / Windows App SDK. Trimming and single-file stay off.

### D4. Single-instance on Windows is mutex + named pipe

Linux keeps `$XDG_RUNTIME_DIR/contextall/shell.sock`. Windows uses `Local\ContextAll-shell-<SID>` plus pipe `ContextAll-shell\<SID>` with the same `ACTIVATE` line protocol so tests can run the pipe path on Linux.

### D5. Daemon start on Windows

`WindowsDaemonController` starts sibling `ContextAll.Host.exe serve` and waits until the per-user pipe accepts a client. Autostart writes `HKCU\...\Run` (no-op / fail on non-Windows). Identity reset runs `identity reset --yes`. AppImage install remains Linux-only (`IsRunningFromAppImage` is false).

### D6. Appearance

Reuse `AppearanceResolver`: Windows probe fills `PortalColorScheme` (`1` dark when Apps are dark) and `PortalAccentColor` when DWM colorization is available. Fonts default to Segoe UI / Cascadia Mono. Linux portal/KDE/GNOME path is unchanged.

## Risks / Trade-offs

- [Avalonia.Win32 restore on Linux] → pin 12.1.4 like Desktop; verify `dotnet build` of `App.Windows` on Linux in this change.
- [Codex editing the same views] → this change must not restyle AXAML; only App startup, Platform, Theming source, and the Windows host.
- [Win32 SSD vs GNOME CSD] → existing `ChromeModeState` already keeps the integrated header when the platform paints a managed caption; Windows Full decorations should be real SSD.

## Migration Plan

1. Failing App tests for paths, tray policy, appearance mapping, host resolution, single-instance, and the Windows csproj contract.
2. Adapters + App/MainWindow wiring + replace WinUI host.
3. Chinese docs and ADR 0011.

Rollback is revert of the change; `shell.json` keys stay compatible. Existing Linux `~/.config/contextall/shell.json` is untouched.
