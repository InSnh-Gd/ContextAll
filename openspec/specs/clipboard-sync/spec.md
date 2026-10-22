# clipboard-sync Specification

## Purpose

Replicates plain-text clipboard state to directly connected peers that have both opted in, using a hybrid logical clock and a version vector.

## Requirements

### Requirement: Mutual opt-in plain text
The system SHALL send or apply clipboard text only when each peer has enabled clipboard sync for the other. The value MUST be plain text of at most 1 MiB. Images, file paths, and file bytes MUST NOT be clipboard values. A value larger than 1 MiB MUST be dropped locally and MUST NOT be sent. An empty string MUST clear the peer clipboard when it is the winning value. Applying a remote value MUST NOT raise a notification. File bytes MUST travel only through file transfer.

#### Scenario: Only one side enables sync
- **WHEN** only one of two trusted peers has enabled clipboard sync
- **THEN** neither side applies the other's clipboard text

#### Scenario: Text exceeds 1 MiB
- **WHEN** the local clipboard becomes plain text larger than 1 MiB
- **THEN** no clipboard delta is sent

#### Scenario: An image is copied
- **WHEN** the local clipboard becomes an image or a file list
- **THEN** no clipboard delta is sent and the stored text stays unchanged

#### Scenario: Remote text arrives
- **WHEN** a peer's clipboard text is applied locally
- **THEN** the operator receives no notification for that update

### Requirement: Hybrid logical clock orders clipboard values
A clipboard value SHALL carry `(PhysicalTimeMs, LogicalCounter, OriginDeviceId)`. Comparison MUST be milliseconds, then counter, then the unsigned 16-byte digest. After this device issues or receives a timestamp, every later timestamp it issues MUST be strictly greater. Applying a remote value MUST advance the local clock past that timestamp. The logical counter in the timestamp MUST NOT be used as the version-vector sequence.

#### Scenario: Two values have the same millisecond
- **WHEN** two clipboard values have the same millisecond and different counters
- **THEN** the greater counter wins

#### Scenario: A remote value is older
- **WHEN** a clipboard delta has an HLC less than or equal to the local register
- **THEN** the local clipboard text stays unchanged

### Requirement: Version vector catch-up
Each local clipboard change SHALL take the next sequence number for this device, starting at 1. The version vector MUST store the highest sequence incorporated from each origin. When the sync stream opens, both sides MUST exchange version vectors immediately, including the current clipboard HLC when a value exists. If the peer's clipboard HLC is strictly greater, it MUST send one delta containing only the current text. The receiver MUST NOT require historical clipboard values. After applying that delta, each side MUST set its vector to the component-wise maximum.

#### Scenario: A peer reconnects
- **WHEN** a trusted peer's sync stream opens and its clipboard HLC is greater
- **THEN** this device receives the current text and does not receive earlier clipboard values

#### Scenario: Sync is disabled during catch-up
- **WHEN** clipboard sync is not mutually enabled and the sync stream opens
- **THEN** the version vectors are still exchanged and clipboard text is not applied

### Requirement: Direct delivery and a short duplicate window
A clipboard delta SHALL be sent only to directly connected peers with mutual sync enabled, and MUST NOT be forwarded to a further peer. A delta MUST NOT be sent back to its origin device. The system MUST drop an operation whose origin and sequence are in the most recent 512 such pairs. An older duplicate MUST still be rejected by the version vector or the HLC when it is outside that window.

#### Scenario: Three devices are online
- **WHEN** device A changes its clipboard while connected to B and C
- **THEN** B applies the winning text and does not forward that delta to C

#### Scenario: The same delta arrives twice
- **WHEN** a delta with an origin and sequence already in the 512-entry window arrives again
- **THEN** the clipboard is not changed a second time

### Requirement: Large text is one chunked change
A clipboard value whose UTF-8 JSON fits in one 64 KiB payload SHALL be a `CRDT_DELTA_BATCH` on stream id 1 at priority 1, with the text in the payload. A larger value MUST first send that delta on stream id 1 without the text, carrying the HLC, sequence, stream id, total byte count, and the SHA-256 of the UTF-8 bytes, and only then send type `18` chunks at priority 3. The sender MUST write the delta before any chunk of that value. Each chunk payload MUST be a big-endian offset followed by the bytes. One clipboard change MUST use one sequence number. The receiver MUST apply the text only when the byte count, the hash, and UTF-8 validity all match and the HLC is strictly newer. A mismatch, a total size above 1 MiB, or a stream id already used by a file transfer MUST drop that assembly, leave the connection and trust unchanged, and leave the file transfer running. A strictly newer HLC that arrives during assembly MUST discard the incomplete chunks. A new local clipboard change MUST stop the chunks already being sent and send the new value under a new sequence and HLC. Catch-up MUST send only the current text, using this chunk path when that text does not fit in one frame. A type `18` frame received before trust MUST close the connection without a trust change.

#### Scenario: Text fits in one frame
- **WHEN** a clipboard change encodes to a JSON payload of at most 64 KiB
- **THEN** the peer receives the text inside one sync delta and no type 18 frame

#### Scenario: Text needs chunks
- **WHEN** a clipboard change does not fit in one 64 KiB payload
- **THEN** the sync delta names the stream id and hash, and the text is applied only after the chunks match that hash

#### Scenario: A newer copy arrives mid-chunk
- **WHEN** a newer clipboard HLC arrives before the previous chunks are complete
- **THEN** the incomplete chunks are discarded and the newer value is the one considered

### Requirement: Clipboard register survives restart
The daemon SHALL store `clipboard.json` beside the transfer directory: `$XDG_STATE_HOME/contextall/clipboard.json` on Linux, `%LOCALAPPDATA%\ContextAll\clipboard.json` on Windows, and `clipboard.json` in the app no-backup directory on Android. The desktop file MUST be mode `0600`. The file MUST contain the current text, that text's HLC, the version vector, the last HLC this device issued or applied, and `appliedText`, the text last written to the system clipboard. The 512-entry window MUST live in memory and start empty after a restart. Updates MUST be written by replacing the file with a flushed temporary file in the same directory.

A remote change MUST be stored, with the previous `appliedText`, before the system clipboard is changed, and `appliedText` MUST be updated to the new text only after that write succeeds. On startup, plain text equal to the stored text MUST update `appliedText` to match and MUST NOT take a new sequence. Plain text equal to `appliedText` while the stored text differs MUST be replaced by the stored text without a new sequence. Plain text equal to neither MUST be a new local change. A system clipboard that is not plain text MUST leave the file unchanged and MUST NOT be overwritten with the stored text. A missing or unreadable file MUST be replaced from the current plain text when that text is within 1 MiB, including empty, or with an empty register when the clipboard is not plain text, and the daemon MUST still listen.

#### Scenario: Restart with the same text
- **WHEN** the daemon starts and the system clipboard plain text equals the stored text
- **THEN** the sequence number does not increase

#### Scenario: User edited the clipboard while stopped
- **WHEN** the daemon starts and the system clipboard plain text equals neither the stored text nor `appliedText`
- **THEN** that plain text becomes a new local clipboard change

#### Scenario: Remote text was stored but not shown
- **WHEN** the daemon starts and the system clipboard still equals `appliedText` while the stored text is newer
- **THEN** the stored text is written to the system clipboard and the sequence number does not increase

### Requirement: Clipboard capture follows the platform
On Linux the daemon SHALL read and write the clipboard through `ext-data-control-v1`, or through `wlr-data-control-unstable-v1` when the first protocol is absent. When neither protocol is offered, clipboard sync on that device MUST stay disabled while pairing and file transfer continue. The daemon MUST NOT use a GNOME Shell extension or an XWayland client for the clipboard. On Android the daemon MUST read the clipboard only while the interface has input focus, and the background service MUST still place received plain text onto the system clipboard. The Android app MUST NOT register as an input method and MUST NOT open a transparent activity in order to read the clipboard. On Windows the daemon MUST observe plain-text clipboard changes while another application's window is focused.

#### Scenario: Wayland compositor offers neither protocol
- **WHEN** the Linux daemon finds neither data-control protocol
- **THEN** clipboard sync is disabled and pairing and file transfer still work

#### Scenario: Android interface is in the background
- **WHEN** a peer's clipboard text arrives while the Android interface has no input focus
- **THEN** the service places that text on the system clipboard and does not read the clipboard back
