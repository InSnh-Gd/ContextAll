# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: Linux shell is Wayland-native`
- TO: `### Requirement: Linux shell prefers Wayland with X11 fallback`

## MODIFIED Requirements

### Requirement: Same local state on every shell
The Windows, Linux, and Android shells SHALL organize controls into Devices, Connection, Transfers, and This Device sections, and SHALL let the operator view discovered and paired devices, dial a manual endpoint, pair, unpair, push plain-text clipboard text, change the local display name, peer switches, and download directory, send files, and inspect transfer progress and session history. Desktop shells MUST use the local daemon's state; Android MUST use its in-app service.

#### Scenario: Switch changed in the shell
- **WHEN** the operator turns on clipboard sync for a peer in the Windows or Linux shell
- **THEN** the daemon records that enablement and the CLI reports the same value

#### Scenario: Manual endpoint dial from the shell
- **WHEN** the operator submits an IP address and port in the desktop Connection section
- **THEN** the shell sends that request through the local daemon and reflects the resulting discovery or pairing state

#### Scenario: Android uses the in-app service
- **WHEN** the Android shell is open
- **THEN** it shows the state of the service running in that app, and clipboard and file actions go through that service

### Requirement: Layout follows available width
The shell SHALL adapt its navigation and content to available width. Below 840 logical pixels, it SHALL show one primary column with a bottom floating navigation dock and drill-in device detail. At or above 840 logical pixels, it SHALL show a collapsible side navigation rail and display the device list beside the selected device. Desktop shells MUST enforce a minimum window size of 480 by 640 logical pixels.

#### Scenario: Narrow surface
- **WHEN** the shell width is below 840 logical pixels
- **THEN** the operator sees one primary column with a bottom floating navigation dock and can move between the device list and the selected device

#### Scenario: Wide surface
- **WHEN** the shell width is 840 logical pixels or wider
- **THEN** the operator sees a collapsible side navigation rail and can view the device list and the selected device at the same time

### Requirement: Appearance follows the system
The shell SHALL follow system light or dark appearance. On Linux, portal `color-scheme` `1` means dark and `0`, `2`, or missing means light; its tonal palette MUST derive from portal `accent-color`, falling back to KDE or GNOME accent settings and then `#1B5E91`. Linux MUST follow the system UI sans-serif font while rendering device IDs and pairing codes in monospace. Windows MUST follow the system theme, and Android MUST follow system night mode.

#### Scenario: Linux portal reports dark
- **WHEN** the Linux desktop portal reports `color-scheme` `1` and an `accent-color` tuple
- **THEN** the Linux shell uses a dark tonal palette derived from that accent color

#### Scenario: Linux portal reports no preference or light
- **WHEN** the Linux desktop portal `color-scheme` value is `0` or `2`
- **THEN** the Linux shell uses a light tonal palette

#### Scenario: Linux portal is missing
- **WHEN** neither the Linux desktop portal nor KDE or GNOME accent settings can be read
- **THEN** the Linux shell uses a light tonal palette seeded from `#1B5E91` and continues to run

#### Scenario: Accent color falls back to KDE or GNOME
- **WHEN** portal `accent-color` is unavailable while KDE or GNOME defines an accent color
- **THEN** the Linux shell derives its tonal palette from that desktop accent color

#### Scenario: System appearance changes
- **WHEN** the operator changes the system light/dark mode or accent color while the shell is open
- **THEN** the shell updates its palette live without restarting

### Requirement: Linux shell prefers Wayland with X11 fallback
When `WAYLAND_DISPLAY` is non-empty, the Linux graphical shell SHALL connect as a native Wayland client without XWayland. When `WAYLAND_DISPLAY` is unset or empty and `DISPLAY` is non-empty, it SHALL connect as an X11 client. When both variables are unset or empty, it MUST write an error to standard error and exit non-zero. The daemon and CLI MUST run without a graphical session.

#### Scenario: Wayland session
- **WHEN** the operator starts the Linux shell with `WAYLAND_DISPLAY` set
- **THEN** the shell connects to Wayland directly and opens its window

#### Scenario: X11 session
- **WHEN** the operator starts the Linux shell in an X11 session with `WAYLAND_DISPLAY` unset and `DISPLAY` set
- **THEN** the shell connects through X11 and opens its window

#### Scenario: Headless session with no display server
- **WHEN** the operator starts the Linux shell with both `WAYLAND_DISPLAY` and `DISPLAY` unset
- **THEN** the shell reports that a display session is required and exits non-zero

#### Scenario: Daemon without Wayland
- **WHEN** the operator starts the daemon or a CLI subcommand with neither `WAYLAND_DISPLAY` nor `DISPLAY` set
- **THEN** that command runs without requiring Wayland or X11

## ADDED Requirements

### Requirement: Linux window chrome and desktop dialogs
On Linux, the shell SHALL open file and directory pickers through the XDG `FileChooser` portal parented to its window. By default it SHALL render an integrated client-side header bar that follows GNOME or KDE window-button side, order, and visibility, removing rounded window corners when maximized or tiled. Operators SHALL be able to switch to native server-side decorations via a UI preference.

#### Scenario: File picker uses the desktop portal
- **WHEN** the operator chooses files to send or selects a download folder on Linux
- **THEN** the shell invokes the XDG `FileChooser` portal parented to the active shell window

#### Scenario: Integrated header follows desktop button layout
- **WHEN** client-side decorations are active on GNOME or KDE
- **THEN** the titlebar places minimize, maximize, and close buttons on the side and in the order configured by the desktop environment

#### Scenario: Operator enables native title bar
- **WHEN** the operator enables native server-side decorations in This Device settings
- **THEN** the shell delegates window titlebar chrome to the window manager and persists that preference in `shell.json`

### Requirement: Desktop multi-file queued send
Desktop shells SHALL accept multiple files from drag-and-drop or the file picker and enqueue them sequentially per target peer so at most one outbound transfer is offered per peer at a time. Dropped directories MUST be skipped without archiving and reported to the operator while regular files in the same drop proceed.

#### Scenario: Multiple files dropped onto a trusted peer
- **WHEN** the operator drops three regular files onto a connected trusted peer
- **THEN** the shell sends the first file immediately and starts each remaining file after the preceding transfer for that peer finishes

#### Scenario: Directory included in a multi-item drop
- **WHEN** the operator drops a directory together with a regular file onto a trusted peer
- **THEN** the shell skips the directory with an explanation and enqueues the regular file

### Requirement: Linux single-instance shell and UI preferences
The Linux shell SHALL listen on `$XDG_RUNTIME_DIR/contextall/shell.sock` so a second graphical launch activates the existing window and exits zero. It SHALL store UI-only preferences in `$XDG_CONFIG_HOME/contextall/shell.json` without writing them to `config.yaml`. Closing the window SHALL exit the shell process unless system-tray residency is enabled and a `StatusNotifierWatcher` host is present on D-Bus.

#### Scenario: Second launch focuses existing window
- **WHEN** the Linux shell is already open or hidden in the tray and the operator launches it again with no CLI arguments
- **THEN** the existing instance raises its window and the new process exits zero

#### Scenario: Tray enabled without a watcher host
- **WHEN** system-tray residency is enabled in `shell.json` but no `StatusNotifierWatcher` host is registered on D-Bus
- **THEN** closing the window exits the shell process rather than leaving an unreachable hidden process

#### Scenario: Tray enabled with a watcher host
- **WHEN** system-tray residency is enabled and a `StatusNotifierWatcher` host is registered on D-Bus
- **THEN** closing the window hides the shell to the tray while keeping `daemon.sock` subscribed

### Requirement: Linux daemon onboarding and AppImage installation
When `daemon.sock` is unreachable, the Linux shell SHALL NOT start the daemon automatically; it SHALL present explicit actions to start the daemon or enable user `systemd` autostart, and SHALL connect automatically when the socket appears. Enabling autostart from an AppImage SHALL install `~/.local/bin/ContextAll.AppImage`, `~/.config/systemd/user/contextall.service`, and a desktop entry.

#### Scenario: Shell opens while daemon is stopped
- **WHEN** the operator opens the Linux shell and `daemon.sock` cannot be reached
- **THEN** the shell shows daemon-stopped onboarding controls without spawning `serve` unprompted, and connects automatically once `serve` starts

#### Scenario: Operator enables autostart from an AppImage
- **WHEN** the operator clicks enable `systemd` autostart while running from an AppImage
- **THEN** the shell copies the AppImage to `~/.local/bin/ContextAll.AppImage`, writes and enables `contextall.service`, and writes `~/.local/share/applications/contextall.desktop`

### Requirement: Graphical identity reset boundary
While connected to a running daemon, the graphical shell MUST NOT expose an identity or settings reset control. When starting the daemon from the shell fails because identity or settings files are unusable, the shell SHALL present only reset or cancel with cancel as default, and a confirmed reset SHALL run an explicit reset and then start the daemon.

#### Scenario: Running daemon hides reset
- **WHEN** the Linux shell is connected to a running daemon
- **THEN** the This Device section displays no identity or settings reset action

#### Scenario: Corrupt state on shell-initiated start
- **WHEN** the operator starts the daemon from the shell, `serve` exits non-zero due to unusable identity or settings files, and the operator confirms reset
- **THEN** the shell invokes explicit reset, starts `serve`, and connects to the fresh daemon state
