# Spec Delta

## Purpose

Sends one file in each direction to a trusted peer and resumes a transfer from the segments already stored, without retransmitting those segments.

## ADDED Requirements

### Requirement: Offer a file to a trusted peer
A file offer SHALL include a transfer id, a stream id, total size, segment size, segment count, the whole-file SHA-256, and the file name. The transfer id MUST be 32 lowercase hex characters. Each SHA-256 MUST be 64 lowercase hex characters. The offer MUST NOT include a hash of every segment. Segment size MUST be 64 KiB except for a shorter final segment. `segmentCount` MUST match the total size divided into 64 KiB segments, and a zero-byte file MUST use a segment count of 0. Total size, segment size, segment count, and a bitmap bit offset MUST be integers from 0 through 9007199254740991. An offer that breaks these rules MUST be answered with `FILE_REJECT` reason `invalid`, MUST occupy neither direction's slot, and MUST retire that stream id for the rest of the connection. Each segment on the wire MUST carry its index and its own SHA-256. Ask-before-save MUST apply to a new offer and MUST NOT apply again when that transfer resumes. The default ask-before-save switch MUST remain on.

#### Scenario: Ask-before-save is on
- **WHEN** a new file offer arrives from a peer whose ask-before-save switch is on
- **THEN** the receiver does not write the file until the operator accepts

#### Scenario: Resume after accept
- **WHEN** an accepted transfer resumes after a disconnect
- **THEN** the receiver does not ask again

#### Scenario: Offer fields disagree
- **WHEN** an offer reports a segment count that does not match its total size
- **THEN** the receiver rejects it with reason `invalid` and does not occupy the receive slot

### Requirement: One outbound and one inbound transfer per peer
Each peer SHALL have at most one outbound transfer and one inbound transfer at a time. A transfer that is waiting for acceptance, in progress, or retained for resume MUST occupy its direction until it completes, is cancelled, or is rejected. A further offer in an occupied direction MUST be rejected with `FILE_REJECT` reason `busy` and MUST leave the occupying transfer running. The offering front end MUST receive a command result of `busy`. An operator rejection MUST use reason `declined`.

#### Scenario: Second send to the same peer
- **WHEN** the operator sends a file to a peer that already has an outbound transfer
- **THEN** the new offer is rejected with reason `busy` and the existing outbound transfer continues

#### Scenario: Send and receive overlap
- **WHEN** a peer is receiving a file and the operator sends one file to that same peer
- **THEN** both transfers run

### Requirement: Bitmap resume
The receiver SHALL write a verified segment at `index × segment size`, make those bytes durable, then set that bit in the transfer bitmap and store the bitmap in the transfer's metadata. On resume the sender MUST transmit only segments whose bits are clear. A segment whose bit was stored before a crash MUST NOT be sent again. A segment that was in flight and not yet recorded MAY be sent again. The sender MUST keep at most 8 segments in flight for one transfer. Bits MUST be ordered from the lowest index. When the bitmap does not fit in one 64 KiB payload, it MUST be split across frames that carry a bit offset, and data MUST NOT start until the whole bitmap has arrived. Flag bit 0 MUST be set on every bitmap frame except the last.

#### Scenario: Half of the segments are stored
- **WHEN** a transfer resumes with a bitmap that marks the even segments complete
- **THEN** the sender transmits the odd segments and does not transmit the even segments

#### Scenario: Crash during an in-flight segment
- **WHEN** the process stops after a segment is acknowledged in the bitmap and before the next segment is recorded
- **THEN** the recorded segment is not sent again

### Requirement: New transfer waits for an empty bitmap
After the operator accepts, or immediately when ask-before-save is off, the receiver SHALL store the partial file and metadata, send `FILE_ACCEPT` containing only the transfer id, and then send one empty bitmap frame. The sender MUST send segments only after that empty bitmap arrives, using the stream id from the offer. A zero-byte file MUST complete after the empty bitmap without a segment. While ask-before-save is on, the receiver MUST NOT create the partial file before acceptance.

#### Scenario: Switch is off
- **WHEN** an offer arrives from a peer whose ask-before-save switch is off
- **THEN** the receiver stores the partial file, accepts, sends an empty bitmap, and only then receives segments

#### Scenario: Empty file
- **WHEN** an accepted offer has a total size of 0
- **THEN** the transfer completes after the empty bitmap and no segment is sent

### Requirement: Resume announcement before a new offer
When a session becomes trusted, each side SHALL announce every incomplete outbound transfer with `FILE_RESUME` carrying the transfer id and a new stream id, then send one `{"done":true}` frame. No incomplete outbound transfer MUST produce only that done frame. After receiving done, a device MUST delete every inbound partial and its metadata whose transfer id was absent, free that receive slot, and report outcome `gone`. An announced transfer the receiver no longer has MUST be rejected with reason `gone`, and the sender MUST then delete its partial and metadata and free the slot. When both sides still have the transfer, the receiver MUST send the full bitmap, including one empty frame when no segment is stored. The sender MUST wait for that bitmap and then send only the clear bits on the announced stream id. A version-vector exchange MUST still happen immediately. A chunked clipboard delta MUST wait until this device has sent done.

A local send requested before this device has sent done MUST be held, and only one request MAY be held. When the announcement has finished, the held request MUST start if the outbound slot is free and MUST return `busy` if it is not. A second send during the hold MUST return `busy` immediately. A new offer that arrives before the peer's done frame MUST be rejected with reason `busy`.

#### Scenario: Receiver lost the partial
- **WHEN** the sender announces a transfer the receiver no longer stores
- **THEN** the receiver rejects it with reason `gone` and the sender deletes its local partial

#### Scenario: Both sides kept the partial
- **WHEN** both sides still store an incomplete transfer at reconnect
- **THEN** the receiver sends its bitmap and the sender transmits only the clear bits

### Requirement: Accept prompt expires
When ask-before-save is on, the prompt SHALL be shown for at most 60 seconds on a monotonic clock, starting when the prompt can be displayed, and it MUST occupy the receive slot. On timeout the receiver MUST send `FILE_REJECT` reason `timeout`, free the slot, and write no file. Front ends MUST observe outcome `timeout`. An accept that arrives after the decision MUST fail as `already-decided`. A front end that connects during the prompt MUST receive the same prompt and the remaining milliseconds in the snapshot. The first accept or reject for that prompt MUST win.

#### Scenario: Nobody answers
- **WHEN** 60 seconds pass with ask-before-save on and no accept or reject
- **THEN** the offer is rejected with reason `timeout` and the receive slot is free

#### Scenario: Two front ends answer
- **WHEN** one front end accepts a prompt and another then rejects it
- **THEN** the accept stands and the reject fails as `already-decided`

### Requirement: Cancel and failure stay on one transfer
Cancel, reject, or checksum failure of one transfer MUST delete that transfer's partial file and metadata, free its slot, and MUST NOT stop the opposite-direction transfer or clipboard sync on the same connection. A checksum mismatch MUST reset only that transfer and report outcome `checksum-failed`. A local cancel while the session is down MUST still delete the local partial and free the local slot. While the session is up, cancel MUST also send `FILE_CANCEL`.

#### Scenario: Operator cancels one file
- **WHEN** the operator cancels the inbound transfer while the outbound transfer and clipboard sync are active
- **THEN** the cancelled partial file is removed and the outbound transfer and clipboard sync continue

#### Scenario: Cancel while offline
- **WHEN** the operator cancels a retained transfer while that peer is offline
- **THEN** the local partial and metadata are deleted and the slot is free

### Requirement: Trust loss deletes retained files
Removing trust for a peer, whether by a local unpair or a received `UNPAIR`, MUST delete every partial and metadata file for that device id and free those slots. While the session is up, the device MUST send `FILE_CANCEL` for each of those transfers before `UNPAIR`. An identity reset, and a settings reset that clears the trust list, MUST delete every transfer file in the state directory. A later pairing MUST start new transfers. Partial files the former peer still holds MUST be cleared by the resume announcement's `gone` and done rules.

#### Scenario: Operator unpairs during a transfer
- **WHEN** the operator unpairs a peer that has an incomplete inbound file
- **THEN** that partial and its metadata are deleted and the receive slot is free

### Requirement: Transfer files live in the state directory
Partial bytes and metadata MUST be stored under `$XDG_STATE_HOME/contextall/transfers/` on Linux, `%LOCALAPPDATA%\ContextAll\transfers\` on Windows, and the app no-backup files directory `transfers/` on Android. The Linux directory MUST be mode `0700`. Metadata MUST keep the peer's original file name. On completion the system MUST place the file in the download directory under the sanitized name and then delete the partial file and metadata.

The sanitized name MUST be the final segment of the original name split on `/` and `\`, without control characters or the characters `":*?<>|`. An empty result, `.`, `..`, or a Windows reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1` through `COM9`, `LPT1` through `LPT9`, compared case-insensitively) MUST become `download`. The name MUST be truncated to 200 Unicode scalars. When the target name exists, the system MUST insert ` (2)`, ` (3)`, and so on before the extension until the name is free. The existing file MUST remain in place.

When the download directory is on the same filesystem as the state directory, completion MUST rename the partial into place. When rename fails because the filesystems differ, the system MUST copy to a temporary file in the download directory named with the final name, `.part-`, and the transfer id, flush it, rename it to the final name, and then delete the state partial and metadata. If the copy, flush, or final rename fails, the partial and metadata MUST stay, the receive slot MUST stay occupied, and progress MUST report `moveBlocked` true. The daemon MUST retry the move on startup and after a successful download-directory change. Outcome `completed` MUST be reported only after the file is in the download directory.

#### Scenario: Transfer completes
- **WHEN** every segment is verified and the whole-file hash matches
- **THEN** the file is in the download directory and the partial file and metadata are gone

#### Scenario: Download directory is on another filesystem
- **WHEN** renaming the finished partial into the download directory fails because the filesystems differ and the copy then succeeds
- **THEN** the finished file has the sanitized name in the download directory and the state partial is gone

#### Scenario: Move cannot finish
- **WHEN** the finished bytes cannot be placed in the download directory
- **THEN** the receive slot stays occupied and progress reports `moveBlocked` true
