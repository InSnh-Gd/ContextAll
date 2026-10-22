# Proposal

## Why

People keep a phone and more than one computer on the same LAN, and still copy text or send a file by hand. ContextAll should link those devices directly, without an account server or a cloud relay. `lan-device-link` and the later protocol decisions are one MVP: the shell and packaging from the first change, and the QUIC session from the second. Where those two disagreed, this change keeps the protocol.

## What Changes

- One change holds the MVP. `lan-device-link` is removed after its shell, theme, and AppImage requirements are copied here.
- Peers talk over one QUIC byte stream. The desktop local channel uses the same 16-byte frame header. Android runs shared effects in-process and does not use that channel.
- A device is the SHA-256 of its SPKI, truncated and encoded as a `ca1` id. Pairing is TOFU plus a short code. Routing is a multi-path table keyed by that id.
- Clipboard state is an LWW register ordered by an HLC, plain text only, at most 1 MiB. Deltas go only to directly connected peers.
- A transfer resumes from a bitmap of verified segments. Each peer has one outbound and one inbound transfer.
- Linux ships as one Wayland-native AppImage. Windows uses an unpackaged WinUI 3 shell. Android uses one app with a foreground service and runs shared effects in-process.
- Not in this change: clipboard images or files, message sync, KDE Connect interoperability, X11, a GNOME Shell extension, and an Android input method.

## Capabilities

### New Capabilities

- `framed-channel`: The 16-byte frame header, priorities, and stream ids shared by QUIC and desktop local IPC.
- `lan-session`: Identity, discovery, pairing, deduplication, trust, and the multi-path route table.
- `clipboard-sync`: Direct-peer clipboard deltas, the HLC order, the version vector, and the duplicate window.
- `file-transfer`: Offers, bitmap resume, in-flight segments, and transfer files on disk.
- `local-operator`: Identity and config files, reset, the CLI, and the local front ends that use the framed channel.
- `device-shell`: Graphical device list, pairing, and settings on Windows, Linux, and Android, including system light/dark, Wayland, and the AppImage.

### Modified Capabilities

- None. `openspec/specs/` has no published capabilities.

## Impact

- Depends on `engineering-baseline` for the functional core, tests-before-code, and NativeAOT host.
- Adds `System.Net.Quic` and a bundled `libmsquic`. Android uses that stack or the transport decision stops.
- `docs/adr/0002-file-sync-is-a-transfer.md` states why files stay on the transfer path. `docs/adr/0001-linux-color-scheme.md` states why Linux appearance reads only the desktop portal. `docs/adr/0003-shared-effects.md` states why QUIC writes and file segments live in one module used by the desktop daemon and the Android app.
