# Design

## Context

Greenfield repository. See proposal.md for why this exists. The behavior contracts are the delta specs under `specs/`. This design fixes the process split, the libraries, and the protocol shape those specs do not name.

The operator runs Linux day to day. The WinUI project cannot be compiled there. The Linux graphical shell must be a native Wayland client, and Avalonia 12.1 does not select that backend unless the app calls `UseWayland()`.

## Goals / Non-Goals

**Goals:**

- One protocol core, testable without a network, shared by the daemon, the CLI, and every shell.
- A NativeAOT daemon that owns LAN sessions and the YAML file.
- Linux delivered as one AppImage: shell, CLI, and daemon.
- Windows shell and Android shell talk to that same core without a second protocol.

**Non-Goals:**

- A plugin host, SQLite, or an HTTP stack.
- Reading GNOME or KDE private appearance keys. See `docs/adr/0001-linux-color-scheme.md`.
- Choosing desktop minimum window size or layout breakpoints. The device-shell spec leaves those numbers unset on purpose.
- Clipboard images, clipboard files, message sync, and sync channels.

## Decisions

### Solution layout

`ContextAll.Core` (`net10.0`, `IsAotCompatible`) holds identity, framing, pairing rules, discovery, and clipboard/file routing rules. It does not reference Avalonia, WinUI, or Android.

`ContextAll.Client` is the local-channel client used by the CLI and the desktop shells.

`ContextAll.Host` is the daemon and the CLI in one NativeAOT binary (`PublishAot=true`). Subcommand `serve` runs the daemon. Other subcommands use `ContextAll.Client`.

`ContextAll.Shell.Avalonia` is the Linux and Android UI (views plus view models). `ContextAll.Linux` is the Wayland entry point. `ContextAll.Android` (`net10.0-android`) hosts the Avalonia activity and the foreground service. `ContextAll.Windows` is the unpackaged WinUI 3 entry point and is excluded from the Linux solution filter, so `dotnet build` on Linux does not require the Windows SDK.

View models live in a `net10.0` project referenced by both shells. Avalonia and WinUI each keep their own views.

Alternative: one Avalonia UI on Windows too. Rejected because the Windows shell is WinUI 3.

### Daemon, CLI, and shells

The daemon is a generic host with `UseSystemd()`. The Linux unit is a user unit, `Type=notify`, `WantedBy=default.target`. The socket is `$XDG_RUNTIME_DIR/contextall/daemon.sock` with mode `0600`. Windows uses the named pipe `\\.\pipe\contextall`.

The local channel uses the same length-prefixed JSON frames as the control messages, without TLS.

Linux clipboard read and write stay in the Wayland shell, which forwards changes to the daemon. With only the daemon running, Linux clipboard changes are not observed. Windows clipboard listening stays in the daemon with a hidden message window, so a CLI-only session still syncs. Android clipboard access stays in the app process next to the foreground service.

Alternative: a Linux daemon that reads the clipboard itself. The Wayland clipboard belongs to the client that owns the seat connection, which is the shell.

### Protocol

Discovery is implemented in Core: mDNS advertise and browse, plus a UDP broadcast on the same TCP port. No third-party mDNS package.

The default TCP port is 42461, carried in the SRV record so it can move. One connection per peer. Frames are `uint32` big-endian length, `uint8` type, then payload. Type `0` is JSON from a source-generated `JsonSerializerContext`. Type `1` is a file chunk tagged with a transfer id.

Identity is a self-signed ECDSA P-256 certificate created on first run. The private key file is mode `0600` beside the YAML. Pairing stores the peer certificate. A session presents that certificate. The short code is displayed only after both certificates are known, and both operators must accept it. Unpaired connections accept only the pairing exchange.

Clipboard order uses the originator's UTC timestamp, with the device id as a tie-break. A repeated payload is not forwarded. Updates larger than 1 MiB are dropped locally and not sent.

File bytes share the existing connection. Only one transfer id is active. On reject, failure, or disconnect, the receiver deletes the partial file.

Alternative: a second TCP port per file. Rejected to keep one firewall opening.

### Settings file

YAML via YamlDotNet and `Vecc.YamlDotNet.Analyzers.StaticGenerator`, using `StaticDeserializerBuilder` / `StaticSerializerBuilder`. Path: `$XDG_CONFIG_HOME/contextall/config.yaml` on Linux, `%LOCALAPPDATA%\ContextAll\config.yaml` on Windows, and the app private files directory on Android. The daemon is the only reader and writer. It loads the file at start.

### CLI

Argument parsing is System.CommandLine. Rendering and prompts are Spectre.Console. Spectre.Console.Cli is not used; it is not NativeAOT compatible. No subcommand plus a terminal opens the menu. A non-terminal with a missing argument prints usage and exits non-zero.

### Shells and theme

Linux and Android reference `CCSWE.Avalonia.Material` 12.x and add `MaterialTheme`. The major version tracks Avalonia 12. Android device choice is an in-page list, not a ComboBox or Flyout.

Linux appearance reads `org.freedesktop.portal.Desktop` `org.freedesktop.appearance` `color-scheme` (`1` dark, `0` and `2` light). A missing portal leaves the shell on light and running. Accent color is ignored. The reason private desktop settings are not a fallback is the ADR above.

Windows uses WinUI 3, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, self-contained publish, with trimming and single-file publish left off. The system theme is the WinUI system theme.

Android minimum API is 31 and the target is 36. The foreground service type is `connectedDevice`. The service holds the listening socket and the Wi-Fi multicast lock. The activity is only the shell.

The Linux entry calls `UseWayland()` from the `Avalonia.Wayland` package. If there is no Wayland display, the shell prints that Wayland is required and exits. `serve` and CLI subcommands do not check for Wayland.

### AppImage

One AppImage contains the Avalonia shell, the NativeAOT host binary, and a launcher. No arguments start the shell. `serve` starts the host. Anything else is passed to the host CLI. The user unit's `ExecStart` is that AppImage with `serve`. The image uses the host Wayland socket and session bus; it does not bundle a compositor or a bus daemon.

Alternative: separate distro packages per release. Rejected for the first Linux deliverable, which is one file that runs across distributions.

## Risks / Trade-offs

- [Avalonia Wayland backend is still early] → Enable it explicitly, cover clipboard and window open on a Wayland session, and keep X11 as an explicit refusal rather than a silent XWayland fallback.
- [CCSWE.Avalonia.Material is a young Material 3 theme, not Expressive] → Pin the 12.x line and keep views on stock controls that theme styles. Expressive is out of scope.
- [AppImage plus systemd user units and FUSE] → Document that `ExecStart` points at the AppImage `serve` entry, and smoke-test the user unit after install.
- [Linux clipboard pauses while the shell is closed] → Accepted. The Wayland seat connection is the shell.
- [Originator timestamps skew] → Device id only breaks ties. Large clock skew can prefer the wrong clipboard text; operators fix it by copying again.
- [Android background clipboard limits] → Clipboard access stays in the foreground-service process. If the platform returns no clipboard, sync skips that change.
- [WinUI publish options crash when trimmed or single-file] → The Windows publish profile keeps trimming and single-file off.

## Migration Plan

First run creates the device key and an empty YAML file. There is no prior data to migrate. Rollback is to stop the user service and remove the config directory and the AppImage.

## Open Questions

- Desktop minimum window size, and the widths used to switch between one column and two columns. The device-shell spec requires the layout to change with width and forbids fixing those numbers in this change. Decide them while implementing the shells.
