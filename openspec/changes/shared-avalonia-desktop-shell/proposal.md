# Proposal

## Why

Windows still ships a stub WinUI 3 window (`ListView`) that does not share the Linux MD3E shell. Maintaining two desktop UIs splits every Devices / Connection / Transfers / This Device change. 直呼内行 has reversed the WinUI decision: Windows uses Avalonia, and `ContextAll.App` is the shared desktop shell.

## What Changes

- Replace `ContextAll.App.Windows` WinUI 3 / Windows App SDK with an Avalonia Win32 host that references `ContextAll.App`.
- Keep views, controls, and view models in `ContextAll.App` (`net10.0`). Linux `App.Desktop` and Windows `App.Windows` are thin platform entries.
- Add a desktop platform seam with Linux and Windows adapters for preferences path, daemon start, tray probe, appearance, and single-instance.
- Windows talks to the existing named-pipe daemon (`windows-daemon`). No WinUI, no Windows App SDK, no `net10.0-windows` targeting pack.
- **BREAKING** for anyone building the old WinUI project: Windows App SDK and WinUI 3 are removed from this repository.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `device-shell`: the Windows graphical shell is the shared desktop shell (same four sections as Linux), not a WinUI 3 stub.
- `repository-baseline`: Linux program filters still omit `ContextAll.App.Windows` and MUST NOT need the Windows SDK. That project MUST compile on Linux as `net10.0` without Windows App SDK.

## Impact

- Delete WinUI `App.xaml` / `MainWindow.xaml` and Windows App SDK package references.
- `ContextAll.App.Windows` becomes Avalonia `UseWin32()` + `Program.Main`, referencing `ContextAll.App`.
- `ContextAll.App` gains `IDesktopPlatform` adapters; `App` / `MainWindow` stop hard-coding Linux D-Bus / XDG / `LinuxDaemonController`.
- Tests in `ContextAll.App.Tests` (run on Linux) cover path layout, appearance mapping, tray policy, host resolution, single-instance, and the Windows csproj contract.
- Docs: `docs/specs/device-shell.md`, `docs/specs/repository-baseline.md`, `docs/specs/md3e-ui.md`, `docs/architecture.md`, `docs/adr/0011-shared-avalonia-desktop-shell.md`, `CLAUDE.md`, `README.md`.
- Android Compose and `windows-daemon` Host work stay out of scope. Linux AXAML restyle continues separately.
