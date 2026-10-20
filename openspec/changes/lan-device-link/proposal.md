# Proposal

## Why

People keep a phone and more than one computer on the same LAN, and still copy text or send a file by hand. ContextAll should link those devices directly, without an account server or a cloud relay, and stay light enough to run as a user session daemon.

## What Changes

- A device discovers other ContextAll devices on the LAN, pairs with a short code, and keeps a direct session with each paired peer.
- Paired devices can sync plain-text clipboard contents, and only when both sides have enabled each other.
- A user can send one file to a paired device. The receiver asks before saving unless that device is set to accept files without asking.
- Linux and Windows can operate the local device from an interactive CLI or from scripted subcommands. A graphical shell on Windows, Linux, and Android shows the same device state.
- Linux ships as one Wayland-native AppImage that includes the shell, the CLI, and the daemon.
- Not in this change: message sync, sync channels, mirroring other apps' notifications, clipboard images or files, KDE Connect interoperability, X11, and Material 3 Expressive.

## Capabilities

### New Capabilities

- `lan-session`: LAN discovery, device identity, pairing, and direct sessions among multiple devices.
- `clipboard-sync`: Mutual opt-in plain-text clipboard sync, including clear and size limits.
- `file-transfer`: One-file send to a paired device, with a per-device ask-before-save switch.
- `local-operator`: The user daemon, its YAML settings, and the CLI that controls it on Linux and Windows.
- `device-shell`: Graphical device list, pairing, and settings on Windows, Linux, and Android, including system light/dark and Linux Wayland delivery.

### Modified Capabilities

- None. The repository has no existing specs.

## Impact

- Greenfield repository: new .NET 10 solution, no existing application code.
- New runtime processes on Linux (user systemd service inside an AppImage), Windows (unpackaged WinUI 3 shell plus daemon), and Android (one app with a foreground service).
- New LAN protocol, local control socket, and YAML configuration owned by the daemon.
- New dependencies for the CLI, Avalonia shells, WinUI 3, and the Linux Wayland backend. Exact packages are decided in design.
