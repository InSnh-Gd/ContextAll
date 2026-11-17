# Design

## Context

See `proposal.md` for motivation and `specs/` for normative behavior. Today:

- `ContextAll.Session/SessionHost.cs` already tracks `LocalDeviceId`, `Downloads` (resolved folder), `LocalAddresses()`, `QuicPort`, `RemoteClipboard(deviceId)`, and `ConnectEndpointAsync(IPEndPoint, alias, CancellationToken)` — all consumed in-process by Android's `NativeSessionBridge` — whereas `ContextAll.Host/LocalClient.cs` omits those fields from the framed local snapshot and exposes no manual endpoint or manual clipboard push commands.
- `ContextAll.Ipc/LocalChannelClient.cs` contains only static socket/pipe path helpers, and `ContextAll.App/MainWindow.axaml` is a 26-line placeholder.
- `Avalonia.Desktop` 12.1.4 already bundles `Avalonia.Wayland`, `Avalonia.X11`, `Avalonia.FreeDesktop` (`DBusPlatformSettings`, `DBusSystemDialog` Portal `FileChooser` with `IPortalParentLease`, `StatusNotifierItem`, and Fcitx5/IBus IME), and `Tmds.DBus.Protocol`, while `CCSWE.Avalonia.Material` provides baseline Material 3 controls without Material Design 3 Expressive (MD3E) HCT generation, split-card corner morphing, connected button groups, wide navigation rails, floating docks, or gap progress bars.
- All new C# production code must obey `functional-core`: pure decision/reducer logic separated from I/O effects, `await` on all incomplete operations, and zero unapproved null-forgiving (`!`) operators.

## Goals / Non-Goals

**Goals:**

- Deliver a responsive, single-instance Linux desktop shell in `ContextAll.App` / `ContextAll.App.Desktop` visually and functionally aligned with Android's 4-section MD3E shell.
- Share one pure local-state reducer and async framed IPC client in `ContextAll.Ipc` across Linux and Windows desktop shells.
- Adapt cleanly to both Wayland and X11 Linux desktops (GNOME, KDE Plasma, wlroots, Xorg) for windowing, accent colors, fonts, CSD/SSD window buttons, Portal file dialogs, tray icons, notifications, and clipboard capture without breaking headless `serve` or NativeAOT host builds.
- Keep UI-only preferences (`shell.json`) strictly isolated from daemon-owned `config.yaml`, and enforce the explicit identity-reset boundary.

**Non-Goals:**

- Switching the GUI rendering toolkit from Avalonia 12 to GTK4 (`GirCore.Gtk-4.0`) or Qt.
- Porting Android's offline fake-data `Developer Lab / Scenario Lab`.
- Automatic folder ZIP compression on drag-and-drop (directories are skipped with an inline notice; regular files are queued).
- Windows WinUI shell redesign or Windows clipboard capture (covered separately).

## Decisions

### 1. Async framed IPC client and pure state reducer (`ContextAll.Ipc`)

Build `LocalDaemonConnection` (`IAsyncDisposable`) on top of `FrameCodec` (`ContextAll.Protocol`) over `Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)` on Linux and `NamedPipeClientStream` on Windows, paired with a pure immutable `LocalShellState` reducer (`ApplySnapshot`, `ApplyEvent`, `ApplyDisconnected`):

```text
+--------------------+   16-byte framed stream    +----------------------+
| ContextAll.App     | <========================> | ContextAll.Host      |
| ViewModels (pure   |   Snapshot(80) / Events    | LocalClient / Pump   |
| commands + state)  |   Commands(64..77)->96     | ($XDG_RUNTIME_DIR/   |
+--------------------+                            |  contextall/         |
          |                                       |  daemon.sock)        |
          v                                       +----------------------+
  $XDG_CONFIG_HOME/contextall/shell.json (UI-only prefs)
```

- Every outbound command allocates a monotonic lowercase hex `requestId` and awaits its correlated type-`96` result via `TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously)`.
- When `daemon.sock` is absent or drops, the client transitions state to `Disconnected` (never auto-spawning `serve`) and runs a jittered background reconnect probe (`500 ms` -> `2 s` cap) so external CLI/systemd `serve` startups connect automatically.
- **Alternative**: Expose gRPC or HTTP over Unix sockets. Rejected: violates `framed-channel` ("MUST NOT use gRPC") and duplicates framing already proven in `ContextAll.Protocol`.

### 2. Backward-compatible local channel & CLI extensions (`ContextAll.Protocol` & `ContextAll.Host`)

- Extend `SnapshotPayload`, `DevicePayload`, and `TransferPayload` in `ProtocolJsonContext` (System.Text.Json source-generated, AOT-safe) with nullable/defaultable properties: `localDeviceId`, `resolvedDownloadDirectory`, `localAddresses`, `quicPort`, `clipboardBackend` (`"wayland"` | `"x11"` | `"none"`), `DevicePayload.remoteClipboardSync` & `endpoint`, and `TransferPayload.totalSize`.
- Add local command frame `76` (`ConnectEndpoint`: `host`, `port`, optional `alias`) wired to `SessionHost.ConnectEndpointAsync`, and expose `contextall-host connect <host:port> [alias]` in `CliApp` plus the interactive CLI menu.
- Add local command frame `77` (`PushClipboardText`: UTF-8 `text` <= 1 MiB) wired to a new local clipboard-ingest entry on `LanHost` / `SessionHost` that writes to the active platform clipboard (if any) and issues a local HLC/sequence delta through the existing clipboard register.
- **Alternative**: Have the GUI read `identity/device.spki`, `NetworkInterface.GetAllNetworkInterfaces()`, or the Wayland/X11 clipboard directly. Rejected: splits truth between GUI and daemon and bypasses the HLC/version-vector register in `clipboard.json`.

### 3. MD3E design system & Linux appearance/accent pipeline (`ADR 0007`)

Keep `CCSWE.Avalonia.Material` as the base control template foundation (text fields, focus rings, popups, dialogs) and layer a dedicated `ContextAll.App/Theming` + `Controls` module:

- **HCT Tonal Palette Generator**: Port a pure C# Material Color Utilities HCT tonal solver producing the MD3E light/dark surface container ladder (`SurfaceDim`..`SurfaceBright`, `SurfaceContainerLowest`..`SurfaceContainerHighest`), primary/secondary/tertiary containers, and semantic badge pairs, injected as dynamic Avalonia `ResourceDictionary` brushes at runtime.
- **Linux Appearance & Accent Watcher (`ILinuxAppearanceSource`)**:
  1. Subscribe to `org.freedesktop.portal.Settings` (`ReadOne` + `SettingChanged` signal on `Tmds.DBus.Protocol`) for `org.freedesktop.appearance` `color-scheme` (`1` -> Dark; `0`/`2`/missing -> Light) and `accent-color` `(double r, double g, double b)` in `[0..1]`.
  2. When Portal `accent-color` is absent or out-of-range, inspect `$XDG_CONFIG_HOME/kdeglobals` (`[General] AccentColor` or `[Colors:View] DecorationFocus`, watched with `FileSystemWatcher`) and GNOME `org.gnome.desktop.interface` (`accent-color` / `gtk-theme`, queried via D-Bus `ca.desrt.dconf` or non-blocking `gsettings` process fallback).
  3. Fall back to brand seed `#1B5E91`.
- **Custom MD3E Controls**:
  - `WideNavigationRail` (`>= 840px`, collapsible icon rail <-> expanded icon+label pill rail) and `ExpressiveFloatingNavDock` (`< 840px`, pill floating bar).
  - `ExpressiveGroupTile` (positional `Top` / `Middle` / `Bottom` / `Single` `28px` outer / `6px` inner radii with `2px` vertical gap and spring corner animation to `28px` on hover/press/selection).
  - `ExpressiveSwitch` (`52x32` track with `✓` / `✕` thumb glyph morph), connected `ExpressiveButtonGroup` (replacing retired `SegmentedButton`), `ExpressiveIconBadge` (`15px` squircle), and `ExpressiveGapProgressBar` (active pill + `6px` gap + remaining track + stop indicator dot).
- **Fonts**: Resolve the desktop default UI sans-serif family via `fontconfig` / Portal / DE config for body copy while keeping monospace (`JetBrains Mono` / `monospace`) for device IDs and 6-character pairing codes.

### 4. Windowing backend, CSD/SSD chrome adaptation, and Portal dialogs

- **Wayland-first startup (`ContextAll.App.Desktop/Program.cs`)**: Evaluate a pure `LinuxDisplaySelector.Select(waylandDisplay, x11Display)` helper before building Avalonia:
  - Non-empty `WAYLAND_DISPLAY` -> configure native Wayland (`WaylandPlatformOptions`).
  - Empty `WAYLAND_DISPLAY` and non-empty `DISPLAY` -> configure X11 (`X11PlatformOptions` with DBus IME + system cursor theme).
  - Both empty -> print diagnostic to `Console.Error` and return exit code `1`.
- **Integrated CSD vs Native SSD**:
  - Default (`useNativeTitleBar: false` in `shell.json`): borderless client-side header (`ExtendClientAreaToDecorationsHint = true` / custom titlebar drag region) whose corner radius drops to `0` when `WindowState` is `Maximized` or `FullScreen` (or tiled). A pure parser `WindowButtonLayoutParser` reads GNOME `org.gnome.desktop.wm.preferences` `button-layout` (e.g. `appmenu:minimize,maximize,close` vs `close,minimize,maximize:`) and KDE `~/.config/kwinrc` `[org.kde.kdecoration2]` (`ButtonsOnLeft` / `ButtonsOnRight`, tokens `N`/`I`/`A`/`X`) to position minimize, maximize, and close buttons on the user's configured side and order.
  - Opt-in (`useNativeTitleBar: true` in `shell.json`): switch to native server-side decorations (`SystemDecorations.Full`, hiding the custom window buttons) for KDE KWin / Xorg / tiling-WM users.
- **FileChooser**: Always call Avalonia `TopLevel.StorageProvider` (`OpenFilePickerAsync` with `AllowMultiple = true`, `OpenFolderPickerAsync`), which `Avalonia.FreeDesktop` routes through `org.freedesktop.portal.FileChooser` with `IPortalParentLease` (`wayland:<handle>` or `x11:<xid>`).

### 5. Dynamic X11 clipboard fallback (`ADR 0008`)

Refactor `LinuxClipboardSource` in `ContextAll.Host` to report `ClipboardBackendKind` (`Wayland`, `X11`, `None`) and try backends in order:

1. Native Wayland (`WaylandClipboard`: `ext-data-control-v1` -> `wlr-data-control-unstable-v1`).
2. `X11Clipboard` when `DISPLAY` is non-empty: loads `libX11.so.6` and `libXfixes.so.3` exclusively via `NativeLibrary.TryLoad` + unmanaged function pointers wrapped behind `IX11Api`. A dedicated background pump thread owns an unmapped selection-owner window, subscribes to `XFixesSelectSelectionInput(..., CLIPBOARD, XFixesSetSelectionOwnerNotifyMask)`, reads `UTF8_STRING` / `TEXT` (ignoring image/file targets), supports `INCR` chunked reads and writes up to `1 MiB`, and suppresses self-owned selection notifications so local writes do not echo as new captures.
3. Graceful `None` fallback when neither initializes.

- **Alternative**: Spawn `xclip` / `xsel` subprocesses. Rejected: external tools are often missing inside minimal X11 installations or AppImage environments and cannot watch `XFixes` ownership changes without polling.

### 6. Daemon notifications, single-instance `shell.sock`, tray, and AppImage setup

- **Daemon D-Bus Notifications (`ContextAll.Host`)**: Add `ILinuxNotificationSink` implemented with `Tmds.DBus.Protocol` against `org.freedesktop.Notifications` on the session bus. On Linux, `LanHost` emits notifications on `PairingPrompt` (body includes peer display name and 6-char short code; actions `accept`, `reject`, `default` -> open GUI) and `FilePrompt` (`accept`, `reject`, `default`). `ActionInvoked` routes through the exact same `AcceptPairing` / `RejectPairing` / `AcceptFile` / `RejectFile` commands (first decision wins); `NotificationClosed` is ignored so swipe-dismiss never rejects a prompt. Clipboard events never touch the sink.
- **Single-instance `shell.sock` & `shell.json`**:
  - On GUI launch, try connecting to `$XDG_RUNTIME_DIR/contextall/shell.sock` and sending an `ACTIVATE` line (with `$DESKTOP_STARTUP_ID` / `XDG_ACTIVATION_TOKEN` when present). If an existing shell answers, exit `0`; otherwise remove any stale `shell.sock`, bind `0600`, and raise/unhide `MainWindow` whenever `ACTIVATE` arrives.
  - Store `ShellPreferences` (`trayEnabled`, `useNativeTitleBar`, `windowWidth`, `windowHeight`, `guideSeen`) atomically in `$XDG_CONFIG_HOME/contextall/shell.json` (`0600`).
  - Only honor `trayEnabled` when `org.kde.StatusNotifierWatcher` (or `org.freedesktop.StatusNotifierWatcher`) has an active owner on session D-Bus; otherwise window close terminates the GUI cleanly.
- **Daemon Onboarding, AppImage Self-Install & Reset Boundary**:
  - From the disconnected onboarding card, **"启动守护进程"** spawns `<self-or-host> serve` detached; if it exits non-zero within the startup window and `IdentityReset.Probe` / exit status indicates unusable identity or settings, open the modal Reset/Cancel dialog (Cancel focused by default). Confirming runs `<self-or-host> identity reset --yes` followed by `serve`.
  - **"启用 systemd 开机自启"**: when `$APPIMAGE` is set, atomically copy `$APPIMAGE` (`0755`) to `~/.local/bin/ContextAll.AppImage`, extract/write the icon and `~/.local/share/applications/contextall.desktop`, write `~/.config/systemd/user/contextall.service` (`ExecStart=%h/.local/bin/ContextAll.AppImage serve`), and run `systemctl --user daemon-reload && systemctl --user enable --now contextall.service`.
  - While connected to a running daemon, `This Device` (`本机`) never renders any identity reset control.

### 7. Client-side per-peer file queue & transfer history

- Maintain a pure per-peer FIFO upload queue in `TransfersViewModel` / `ShellSessionController`: dropping or picking `N` paths partitions them into regular files vs skipped directories (surfacing a dismissible banner for skipped directories), sends the head file via command `72` (`SendFile`), and advances to the next queued file for that peer upon `TransferEnded` (`86`).
- Retain completed/failed/cancelled `TransferEnded` items in an in-memory session history list with an **"打开所在目录"** action (`xdg-open` / Portal `OpenDirectory`).

## Risks / Trade-offs

- **[NativeAOT trimming breaks D-Bus or JSON serialization in `ContextAll.Host`]** -> Use `Tmds.DBus.Protocol` low-level reader/writer APIs (zero reflection) and source-generated `ProtocolJsonContext` for all new DTOs; verify `dotnet publish src/ContextAll.Host -c Release -r linux-x64` in CI.
- **[GNOME has no built-in system tray without extensions]** -> Probe `StatusNotifierWatcher` ownership on D-Bus dynamically; disable or bypass close-to-tray whenever no watcher host exists so the shell can never become an unrecoverable background window.
- **[X11 `INCR` transfers stall if a peer X11 client disappears mid-selection]** -> Enforce a monotonic per-chunk timeout (`2 s`) on the X11 selection pump and cap accumulated bytes at `1 MiB`, discarding incomplete buffers cleanly without blocking the daemon.
- **[CSD corner clipping or shadow artifacts vary across compositors]** -> Square window corners automatically when maximized/tiled and expose the persisted native SSD toggle in `shell.json` so operators on KWin, Xfce, i3/Sway, or custom WMs have a first-class escape hatch.

## Migration Plan

- Wire protocol between devices is untouched; local channel changes on `daemon.sock` are strictly additive JSON fields and new frame types `76` and `77` (`serve` and GUI ship inside the same AppImage, so both sides upgrade together).
- Existing `~/.config/contextall/config.yaml`, `~/.local/share/contextall/identity/`, and `~/.local/state/contextall/` files remain 100% compatible with zero migration steps.
- UI preferences create `~/.config/contextall/shell.json` lazily on first change; rolling back simply ignores `shell.json`.
