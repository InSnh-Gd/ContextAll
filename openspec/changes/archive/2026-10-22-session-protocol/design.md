# Design

## Context

See proposal.md for why this change exists. This change is the merged MVP. `lan-device-link` was folded in here; where the two disagreed, the protocol in this change won. `engineering-baseline` still requires a pure `Decide` core, `Fin` for expected failures, tests before production code, and a NativeAOT host. No application code exists yet.

The transfer cap, local frame types, resume announcement, and clipboard capture rules are specified in the delta specs. `docs/adr/0002-file-sync-is-a-transfer.md` records why a file does not travel on the clipboard.

## Goals / Non-Goals

**Goals:**

- One frame codec for QUIC and for the desktop local socket.
- Pairing, discovery, and routing that follow the device id rather than a single IP.
- Clipboard catch-up by version vector, with the HLC deciding the winning text.
- File resume that does not retransmit a segment already recorded in the bitmap.
- One protocol core shared by the daemon, the CLI, and every shell, with Linux delivered as one Wayland AppImage.

**Non-Goals:**

- A historical clipboard log, multi-hop relay, or version-vector sequence numbers reused as HLC counters.
- Clipboard images, clipboard file lists, a GNOME Shell extension, and an Android input method.
- KDE Connect compatibility and message sync beyond the clipboard register.
- gRPC on the desktop local channel.
- Reading GNOME or KDE private appearance keys. See `docs/adr/0001-linux-color-scheme.md`.
- Choosing the desktop minimum window size or the widths that switch columns. The device-shell spec leaves those numbers unset on purpose.

## Decisions

### Frame codec

The header is 16 bytes:

| Offset | Field |
| --- | --- |
| 0 | `uint32` big-endian payload length |
| 4 | `uint8` type |
| 5 | `uint8` priority, 0 highest through 3 lowest |
| 6 | `uint16` big-endian flags |
| 8 | `uint32` big-endian stream id |
| 12 | `uint32` reserved, must be 0 |

Control and sync payloads are UTF-8 JSON from a source-generated `JsonSerializerContext`. Segment payloads are binary: `uint32` big-endian index, 32-byte SHA-256, then the bytes. Flag bit 0 means a bitmap chunk is not the last chunk. A priority outside 0..3, a non-zero reserved field, or a payload over 64 KiB closes the connection and does not change trust.

Stream id 0 is session, 1 is sync, and the sender allocates 2 and above. The write loop always takes the highest-priority queued frame. Bulk frames stay at one segment so a session frame can pass between them.

Types:

| Type | Name |
| --- | --- |
| 1 | `HELLO` |
| 2 | `PAIR_ACCEPT` |
| 3 | `PAIR_REJECT` |
| 4 | `SESSION_DUPLICATE_CLOSE` |
| 5 | `UNPAIR` |
| 6 | `DISPLAY_NAME` |
| 7 | `PATH_ANNOUNCE` |
| 8 | `FILE_OFFER` |
| 9 | `FILE_ACCEPT` |
| 10 | `FILE_REJECT` |
| 11 | `FILE_RESUME` |
| 12 | `FILE_ACK` |
| 13 | `FILE_CANCEL` |
| 14 | `CRDT_STATE_VECTOR` |
| 15 | `CRDT_DELTA_BATCH` |
| 16 | `FILE_BITMAP` |
| 17 | `FILE_SEGMENT` |
| 18 | `CLIPBOARD_CHUNK` |
| 64–75 | Local commands, desktop socket only |
| 80–86 | Local events, desktop socket only |
| 96 | `COMMAND_RESULT`, desktop socket only |

`FILE_ACK` carries the transfer id and the segment index just recorded. `FILE_BITMAP` carries the transfer id, `bitOffset`, and a Base64 slice. `FILE_OFFER` also carries `streamId`. `FILE_ACCEPT` carries only `transferId`. `FILE_REJECT` carries `transferId` and `reason` (`declined`, `busy`, `timeout`, `gone`, or `invalid`). `FILE_RESUME` carries `transferId` and `streamId`, and the announcement ends with `{"done":true}`.

Each peer has one outbound and one inbound slot. On entering trusted, each side announces incomplete outbound transfers and then `done` before accepting a new offer. The sender allocates stream ids from 2 for that connection, shared by files and clipboard chunks, and does not reuse them. Stream id 0 remains session and 1 remains sync.

A clipboard JSON payload that fits in 64 KiB stays on stream id 1. A larger plain-text value uses type 18 after a delta that names the stream id, byte count, and SHA-256. `clipboard.json` in the state directory stores the text, its HLC, the version vector, the last issued or applied HLC, and `appliedText`. Linux clipboard access uses `ext-data-control-v1` and falls back to `wlr-data-control-unstable-v1`. Android reads the clipboard only while the interface has focus.

The Linux socket is `$XDG_RUNTIME_DIR/contextall/daemon.sock`. A second `serve` exits when a daemon already accepts connections. A leftover socket file with nobody listening is removed. Control commands do not start the daemon.

Alternative: keep the 5-byte length-and-type prefix and use extra QUIC streams. Rejected once the local socket had to carry the same multiplexing without QUIC stream ids.

### QUIC

`System.Net.Quic` with ALPN `contextall`. The initiator opens the one bidirectional stream; the acceptor does not open another. The listener binds the same addresses used for beacons, port 42461. `libmsquic` is bundled in the AppImage. Android uses the same stack; if `QuicListener.IsSupported` is false there, implementation stops and the transport is reopened. A peer address that is not on an allowed on-link prefix is closed before HELLO.

Keepalive is every 15 seconds and the idle timeout is 60 seconds, both on a monotonic clock.

### Identity and trust

Device id text is `ca1` plus 26 lowercase RFC 4648 Base32 characters of the first 16 bytes of SHA-256(SPKI). Comparison for deduplication uses those 16 bytes as an unsigned big-endian integer. The short code is the first 30 bits of SHA-256 of the two SPKIs ordered by that integer, shown as 6 lowercase Base32 characters.

Trust is written only after both `PAIR_ACCEPT` frames, and only then is the first `PATH_ANNOUNCE` sent. Messages that require trust are not sent while pairing. HELLO is sent as soon as the session stream is writable.

The HLC update follows the usual send and receive rules: the next issued tuple is strictly greater than every tuple this device has issued or applied. `PhysicalTimeMs` is the local Unix millisecond clock used as an input to that update, not as the sole order.

### Clipboard

The only CRDT object in this change is an LWW register. The version vector stores the highest `Seq` incorporated per origin. `Seq` starts at 1 and increments once per local clipboard change. A duplicate in the 512-entry ring of `(origin, seq)` is dropped before the HLC compare. Catch-up sends the current register only, never a log. JSON device ids use the `ca1` text.

### Files

`FILE_OFFER` JSON fields are `transferId`, `streamId`, `totalSize`, `segmentSize`, `segmentCount`, `rootSha256`, and `fileName`. Ids and hashes are lowercase hex. The receiver writes a verified segment at `index × segmentSize`, flushes it, then sets the bit and updates `completedSegmentsBase64`. The sender keeps at most 8 segments in flight and skips bits that are set. Cancel deletes that transfer's `.part` and `.meta.json` only.

Metadata also stores the root hash, segment size, total size, peer device id, direction, and file name. Linux state lives in `$XDG_STATE_HOME/contextall/transfers` (default `~/.local/state/contextall/transfers`). Windows uses `%LOCALAPPDATA%\ContextAll\transfers`. Android uses `Context.getNoBackupFilesDir()/transfers`.

### Discovery and routes

mDNS service `_contextall._udp.local` carries the `ca1` id, display name, and QUIC port. The beacon is `CA01`, version 1, the 16-byte digest, the port, and the UTF-8 name, with a total length that must match. Linux and Windows bind the sending socket to the local on-link address and do not need `CAP_NET_RAW`. Android binds the socket to the Wi-Fi or Ethernet `Network` and holds a multicast lock. A `PATH_ANNOUNCE` is sent on entering trusted and whenever the local candidate set changes, including an empty list to clear it.

### Reset

`identity reset` rewrites files and exits. The shell, after a confirmed reset, starts the daemon. Key damage replaces the key and clears trust, keeping a readable display name. A damaged settings file with a good key rewrites only the settings file.

### Shells and delivery

`ContextAll.Protocol` has no project reference and no socket, file, HTTP, or UI library. `ContextAll.Core` (`net10.0`, `IsAotCompatible`) references `Protocol` and holds identity, pairing, discovery, and clipboard and file rules. `ContextAll.Effects` references only `Core`. It runs `Decide`, QUIC writes, and file segments. `ContextAll.Ipc` references only `Protocol`. It is a Unix-socket client on Linux and a named-pipe client on Windows. It does not listen. `ContextAll.Host` references `Core`, `Ipc`, and `Effects`. It is one NativeAOT binary. `serve` listens and supplies discovery, clipboard, and paths. Other commands use `Ipc`, do not start the daemon, and do not open QUIC.

`ContextAll.App` references only `Ipc` and holds the Avalonia views, view models, tray, and popups for Linux. `ContextAll.App.Desktop` references `App` and `Ipc`, calls `UseWayland()` on Linux, and constructs the client. Without a Wayland display the Linux shell exits. `serve` and CLI commands do not check for Wayland. `ContextAll.App.Windows` is the unpackaged WinUI 3 shell. It references only `Ipc`, keeps its own views, and is excluded from the Linux build. Trimming and single-file publish stay off. `ContextAll.App.Android` (`net10.0-android`, API 31 through 36) references `Core` and `Effects`. It holds the Android UI and a `connectedDevice` foreground service. The UI calls one method, and that method enters `Effects`. Discovery, clipboard, and paths stay in this project.

`ContextAll.App` uses `CCSWE.Avalonia.Material` 12.x. Android device choice stays on the page. Linux appearance reads only the portal `color-scheme` (`1` dark, `0` and `2` light). A missing portal leaves the shell on light. Windows follows the WinUI system theme.

Settings use YamlDotNet's static generator. The CLI uses System.CommandLine and Spectre.Console. Spectre.Console.Cli is not used. The Linux user unit is `Type=notify`, `WantedBy=default.target`, and its `ExecStart` is the AppImage `serve` entry. One AppImage contains the shell, the NativeAOT host, and a launcher.

The earlier TCP framing, wall-clock clipboard order, and delete-on-disconnect rule are not implemented.

## Risks / Trade-offs

- [One QUIC stream blocks a large segment behind itself] → Bulk frames are one segment, and a higher-priority frame is written before the next segment.
- [A bitmap larger than 64 KiB needs several frames] → Data waits until every `FILE_BITMAP` chunk has arrived.
- [The 512-entry window forgets older duplicates] → The version vector and the HLC still reject them.
- [`libmsquic` is absent on Android] → Stop and reopen the transport decision. Do not add a TCP fallback in this change.
- [JSON escaping of a 1 MiB clipboard exceeds 64 KiB] → The delta on stream id 1 carries the hash and stream id, and type 18 carries the UTF-8 bytes. The receiver applies them only when the hash matches.
- [Avalonia Wayland backend is still early] → Enable it explicitly, and exit on X11 instead of falling through to XWayland.
- [WinUI publish options crash when trimmed or single-file] → `ContextAll.App.Windows` keeps trimming and single-file off, and the Linux build does not compile that project.

## Migration Plan

There is no deployed data. First start creates the identity and an empty trust list. Rollback is to stop the daemon and delete the identity, config, and transfers directories.

## Open Questions

- Desktop minimum window size is 480 by 640. The layout uses one column below 840 and shows the device list beside the selection at 840 and above.
