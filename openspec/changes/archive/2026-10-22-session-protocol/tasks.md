# Tasks

## 1. Frame codec

- [x] 1.1 Write a failing test that a 16-byte header round-trips payload length, type, priority, flags, and stream id, and that a non-zero reserved field or a payload longer than 64 KiB is a failure value. Then implement the codec in `ContextAll.Protocol` with source-generated JSON for control payloads. Verify the test passes and the failure does not throw.
- [x] 1.2 Write a failing test that a queued priority-0 frame is written before a queued priority-3 frame. Then implement that order. Verify the test passes.
- [x] 1.3 Write a failing test that a malformed frame closes the session state without changing a trust entry. Then implement that transition. Verify the test passes.

## 2. Identity and reset

- [x] 2.1 Write a failing test that a missing key pair creates `device.key` and `device.spki`, and that one missing file or a mismatched pair refuses creation. Then implement identity startup. Verify the test uses the Linux path from design.md and does not replace a mismatched key.
- [x] 2.2 Write a failing test that a missing settings file is created with an empty trust list, and an unreadable settings file is left unchanged. Then implement that. Verify the private key is absent from the settings file.
- [x] 2.3 Write a failing test that a confirmed key reset replaces the key, clears trust, and deletes transfer files while keeping a readable display name, and that declining reset leaves the files unchanged. Then implement `identity reset` so it exits without listening. Verify a non-interactive run without the confirmation flag does not reset.
- [x] 2.4 Write a failing test that a new trust entry stores clipboard sync off and ask-before-save on, and that an empty download directory means the system downloads folder. Then implement those settings fields. Verify the private key and discovery paths stay out of the file.

## 3. Session

- [x] 3.1 Write a failing test for the `ca1` device id, rejection of a non-P-256 leaf, and SPKI mismatch closing without deleting trust. Then implement those checks. Verify an expired certificate with the stored SPKI still matches.
- [x] 3.2 Write a failing test that both HELLO messages are emitted without waiting, deduplication keeps the session opened by the greater digest, and the short code is produced only for that session and never placed in a frame. Then implement pairing. Verify a 60-second timeout and a single rejection write no trust entry.
- [x] 3.3 Write a failing test that trust is stored only after both accepts, and that `PATH_ANNOUNCE` is withheld until then. Then implement that order. Verify a rename during pairing does not send a second HELLO.

## 4. Discovery and routes

- [x] 4.1 Write a failing test that a beacon round-trips `CA01`, version 1, the digest, and the port, and that the local digest is ignored. Then implement encode and decode. Verify an illegal display name keeps the path.
- [x] 4.2 Write a failing test that a discovery path expires after 3 minutes, the observed session address is not dialed after close, and an announced off-link address is ignored. Then implement the route table. Verify path expiry does not remove trust.
- [x] 4.3 Write a failing test that an untrusted peer is not dialed until `StartPairing`, and a trusted peer is dialed when a fresh candidate exists. Then implement that policy. Verify a failed dial waits for a refresh.

## 5. Clipboard

- [x] 5.1 Write a failing test that HLC order is milliseconds, then counter, then digest, and that a later local timestamp is strictly greater. Then implement the clock. Verify the logical counter is not reused as the version-vector sequence.
- [x] 5.2 Write a failing test that a sync open exchanges version vectors, a strictly newer register sends only the current text, and a duplicate inside the 512-entry window is ignored. Then implement the register. Verify mutual opt-in off still exchanges vectors and does not change clipboard text.
- [x] 5.3 Write a failing test that a delta is not forwarded to a third peer and is not sent back to its origin. Then implement direct delivery. Verify the test uses three device ids and one forward hop.
- [x] 5.4 Write a failing test that a value which fits in 64 KiB stays in one sync delta, a larger value is applied only after type-18 chunks match the announced hash, and a newer HLC discards an incomplete assembly. Then implement chunking. Verify an image or file list sends no delta.
- [x] 5.5 Write a failing test that restart with the same plain text keeps the sequence, a text equal to neither the stored text nor `appliedText` is a new local change, and a missing clipboard file does not stop listening. Then implement `clipboard.json`. Verify the 512-entry window is empty after restart.

## 6. File resume

- [x] 6.1 Write a failing test that an offer carries the root hash and not a hash per segment, and that a verified segment is stored at `index × segment size` before its bit is set. Then implement the bitmap and metadata. Verify a resume sends only the clear bits.
- [x] 6.2 Write a failing test that the ninth in-flight segment is withheld, a bitmap larger than 64 KiB is split with `bitOffset`, and data waits until the bitmap is complete. Then implement the window and `FILE_BITMAP`. Verify a checksum mismatch resets only that transfer.
- [x] 6.3 Write a failing test that one peer can send and receive at the same time, a second outbound offer is rejected with `busy`, and cancelling the inbound transfer leaves the outbound transfer and clipboard sync running. Then implement the slots and cancel. Verify an occupied slot is the one send or the one receive for that peer, not a global single transfer.
- [x] 6.4 Write a failing test that reconnect announces incomplete outbound transfers, ends with `{"done":true}`, answers an unknown announcement with `gone`, and sends only clear bits after the bitmap. Then implement the announcement. Verify a new offer before the peer's done frame is `busy`.
- [x] 6.5 Write a failing test that a new accept sends an empty bitmap before segments, a 60-second prompt timeout frees the slot, and a sanitized name does not replace an existing download. Then implement accept, timeout, and naming. Verify a failed final rename sets `moveBlocked` and keeps the slot.

## 7. Channels

- [x] 7.1 Bind a QUIC listener to the beacon addresses on port 42461 with ALPN `contextall`, open one bidirectional stream from the initiator, and verify a second application stream is not opened. If Android reports QUIC unsupported, stop and report that instead of adding a TCP fallback.
- [x] 7.2 Serve the same frames on `$XDG_RUNTIME_DIR/contextall/daemon.sock` with mode `0600` and on the Windows per-user pipe. Verify a desktop client frame decodes with the codec from task 1.1. Verify a second `serve` exits while the first keeps listening, and a command other than `serve` does not start a daemon.
- [x] 7.3 Write a failing test that local command types 64 through 75 return type 96 to the sender, a snapshot on connect includes prompts and transfers, and a type of 64 or above on QUIC closes the session without a trust change. Then implement the local frames. Verify the type numbers match design.md.

## 8. CLI

- [x] 8.1 Add subcommands for device list, pair, clipboard switch, ask-before-save, and file send, and verify a non-terminal invocation with a missing argument exits non-zero and prints usage. Verify a command that cannot reach the daemon exits non-zero and does not start one.
- [x] 8.2 Open the Spectre.Console menu only when there is no subcommand and standard input is a terminal, and verify a subcommand path does not enter the menu.

## 9. Shells

- [x] 9.1 Build the Avalonia shell with `CCSWE.Avalonia.Material` 12.x, in-page device choice, and width-based layout. While doing this, choose the desktop minimum size and the column breakpoint, record those numbers in this change's Open Questions, and verify a narrow layout shows one column and a wide layout shows the list beside the selection.
- [x] 9.2 Map portal `color-scheme` `1` to dark and `0` or `2` to light, use light when the portal is missing, and ignore accent color. Verify a unit test of that mapping, including the missing-portal case.
- [x] 9.3 Call `UseWayland()` from the Linux entry and exit with a Wayland-required message when no Wayland display is present. Verify that check's unit test, and verify `serve` still starts without Wayland.
- [x] 9.4 Add the Android activity and `connectedDevice` foreground service, and verify the manifest declares that service type and the device list is a page list rather than a flyout.
- [x] 9.5 Add the unpackaged self-contained WinUI 3 shell (`WindowsPackageType` `None`, trimming off, single-file off) in `ContextAll.App.Windows`, and verify the Linux solution filter still builds without that project.

## 10. AppImage

- [x] 10.1 Pack the shell, the NativeAOT host, and a launcher into one AppImage. Verify no arguments start the shell, `serve` starts the host, and any other arguments reach the CLI.
- [x] 10.2 Point the user unit `ExecStart` at the AppImage `serve` entry and verify a user-service start runs the daemon.

## 11. Integration

- [x] 11.1 Run two daemons on one machine, pair them, enable clipboard both ways, and verify a text copy and a clear arrive on the other side without a notification.
- [x] 11.2 Send one file between those daemons with ask-before-save on, then off, and verify accept writes into the download directory, reject leaves nothing, and a dropped connection resumes the same partial file.
