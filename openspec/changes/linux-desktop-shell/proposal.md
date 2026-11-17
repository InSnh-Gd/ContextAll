# Proposal

## Why

The Linux daemon, QUIC session engine, and CLI are working, whereas `ContextAll.App` and `ContextAll.Ipc` are empty stubs that neither connect to `daemon.sock` nor expose any operator UI. At the same time, the earlier Linux shell constraints (Wayland-only with no X11 fallback, no accent-color adaptation, no X11 clipboard fallback, and no background desktop notifications) block X11 desktop sessions and leave Linux visually and functionally behind the Material Design 3 Expressive (MD3E) Android shell.

## What Changes

- Implement a full async, auto-reconnecting framed local-channel client in `ContextAll.Ipc` that streams snapshots, live events, and command results over `daemon.sock` (and Windows named pipes).
- Extend the local operator channel and CLI with backward-compatible snapshot/event metadata (`localDeviceId`, `resolvedDownloadDirectory`, `localAddresses`, `quicPort`, `clipboardBackend`, per-device `remoteClipboardSync` and `endpoint`, per-transfer `totalSize`) plus local command types `76` (`ConnectEndpoint`, paired with a `connect` CLI subcommand) and `77` (`PushClipboardText`).
- Build the Linux Avalonia 12 graphical shell in `ContextAll.App` and `ContextAll.App.Desktop` with the four-section MD3E layout matching Android (`Devices`, `Connection`, `Transfers`, `This Device`), adaptive `WideNavigationRail` (`>= 840`) / `ExpressiveFloatingNavDock` (`< 840`), split-card tiles, expressive switches, connected button groups, gap progress bars, multi-file drag-and-drop sequential upload queue, session transfer history, manual clipboard push modal, and usage guide modal.
- **BREAKING** (relaxes prior Linux restrictions): Select native Wayland first when `WAYLAND_DISPLAY` is set, fall back to native X11 when only `DISPLAY` is set, and exit non-zero only when neither display server is available.
- **BREAKING** (replaces ADR 0001 with ADR 0007): Derive the Linux MD3E HCT light/dark palette from `org.freedesktop.portal.Settings` (`color-scheme` and `accent-color`), falling back to KDE `kdeglobals` / GNOME `org.gnome.desktop.interface` accent settings and then brand seed `#1B5E91`, while integrating XDG Portal `FileChooser`, system UI sans-serif font resolution, and integrated CSD titlebars that mirror GNOME/KDE window button layouts with an optional native SSD toggle in `shell.json`.
- **BREAKING** (relaxes prior Linux clipboard restriction per ADR 0008): Cascade `LinuxClipboardSource` from `ext-data-control-v1` to `wlr-data-control-unstable-v1` to dynamic `libX11.so.6` / `libXfixes.so.3` (`X11Clipboard` with `INCR` up to 1 MiB) when `DISPLAY` is set, keeping headless `serve` free of hard X11 library dependencies.
- Add single-instance GUI activation over `$XDG_RUNTIME_DIR/contextall/shell.sock`, isolated UI-only preferences in `$XDG_CONFIG_HOME/contextall/shell.json`, optional `StatusNotifierItem` tray residency when a watcher host exists, daemon-emitted `org.freedesktop.Notifications` D-Bus prompts for pairing (including the 6-character code) and incoming `askBeforeSave` file offers, explicit daemon onboarding / AppImage `~/.local/bin` + `systemd` + `.desktop` installation when `daemon.sock` is unreachable, and explicit corrupt-identity/settings reset recovery when daemon startup fails.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `device-shell`: Expands the Linux shell from a Wayland-only monochrome window to a Wayland-first / X11-fallback MD3E desktop shell with Portal/KDE/GNOME accent adaptation, CSD/SSD chrome adaptation, Portal file dialogs, multi-file queued sends, manual endpoint/clipboard actions, single-instance `shell.sock` activation, `shell.json` UI preferences, optional `StatusNotifierItem` tray residency, explicit daemon-stopped onboarding, AppImage user-session self-installation, and startup-failure reset recovery.
- `local-operator`: Extends local snapshot/event payloads with device/network/clipboard/transfer metadata, adds local command types `76` (`ConnectEndpoint`) and `77` (`PushClipboardText`) plus the `connect` CLI subcommand, and specifies Linux D-Bus desktop notifications from the daemon for pairing and file prompts.
- `clipboard-sync`: Adds dynamic `X11Clipboard` (`libX11` + `XFixes` + `INCR`) as the Linux fallback behind Wayland `ext-data-control-v1` and `wlr-data-control-unstable-v1` when `DISPLAY` is available.

## Impact

- `src/ContextAll.Protocol`: Additive optional fields on local snapshot/device/transfer DTOs and frame types `76` and `77`.
- `src/ContextAll.Ipc`: Full framed stream client, state reducer, and reconnection loop.
- `src/ContextAll.Host`: Extended `LocalClient` snapshot/commands, `CliApp` `connect` subcommand, `X11Clipboard` fallback in `LinuxClipboardSource`, and Linux `org.freedesktop.Notifications` D-Bus notifier.
- `src/ContextAll.App` and `src/ContextAll.App.Desktop`: MD3E theme/HCT generator, custom controls, 4-tab view models and views, Linux Portal/GNOME/KDE appearance & window-button watchers, `shell.sock` single-instance activator, `shell.json` store, `StatusNotifierItem` tray integration, and AppImage/`systemd` installer.
- `tests/ContextAll.App.Tests`, `tests/ContextAll.Host.Tests`, and `tests/ContextAll.Tests.E2E`: Unit, Avalonia headless UI, IPC integration, mocked X11/D-Bus seam tests, and CLI/daemon coverage.
