# Spec Delta

## Purpose

Carries session, sync, and bulk bytes over one QUIC connection and over the desktop local socket with the same frame header.

## ADDED Requirements

### Requirement: One reliable byte stream on the network
A paired connection SHALL carry frames on a single QUIC bidirectional stream protected by TLS 1.3. The system MUST NOT open a second QUIC stream for files or sync.

#### Scenario: File bytes share the connection stream
- **WHEN** a trusted peer sends a file segment
- **THEN** the segment is a frame on the same QUIC stream as session messages

### Requirement: Sixteen-byte frame header
Each frame SHALL begin with a 16-byte header: a big-endian payload length, a type byte, a priority byte, a big-endian flags field, a big-endian stream id, and a reserved field that is zero. The payload length MUST be the number of bytes after the header and MUST NOT exceed 64 KiB.

#### Scenario: Payload longer than 64 KiB
- **WHEN** a peer sends a frame whose payload length is greater than 64 KiB
- **THEN** the receiver closes the connection and does not change trust

#### Scenario: Reserved field is not zero
- **WHEN** a frame header has a non-zero reserved field
- **THEN** the receiver closes the connection and does not change trust

### Requirement: Stream ids and priorities
Stream id 0 SHALL be the session stream and stream id 1 SHALL be the sync stream. A sender MUST allocate stream ids of 2 and above for each file transfer or large payload. Priority 0 is the highest and priority 3 is the lowest. The sender MUST transmit a queued higher-priority frame before a queued lower-priority frame.

#### Scenario: A session frame is queued behind bulk data
- **WHEN** a session frame and a bulk frame are both waiting to be written
- **THEN** the session frame is written first

### Requirement: Desktop local channel uses the same header
On Linux the local channel SHALL be the Unix socket `$XDG_RUNTIME_DIR/contextall/daemon.sock` with mode `0600`, in a directory of mode `0700`. On Windows it SHALL be the named pipe `\\.\pipe\ContextAll\<current user SID>`, accessible to the current user only. Frames on that channel MUST use the same 16-byte header as QUIC. The desktop local channel MUST NOT use gRPC. Several front ends MAY be connected at once.

#### Scenario: A local client sends a command
- **WHEN** a desktop front end sends a command to the daemon
- **THEN** the bytes begin with the same 16-byte header used for a QUIC frame

### Requirement: Stream ids belong to the sender
A sender SHALL allocate stream ids from 2 upward for the life of one connection. File transfers and clipboard chunk streams MUST share that counter. An id that has been used MUST NOT be used again on that connection. A new connection MUST start again at 2. A stream id names a stream of the device that wrote the frame. Peer frame type `18` is a clipboard chunk. Frame types 64 and above are local commands and events. A QUIC frame whose type is 64 or above MUST close the connection without a trust change. An unrecognized type on the local socket MUST close that front-end connection only.

#### Scenario: Clipboard chunks and a file share a connection
- **WHEN** one device sends a file and a chunked clipboard value on the same connection
- **THEN** the two streams use different ids from that sender's counter

#### Scenario: Local type arrives on QUIC
- **WHEN** a peer sends a frame whose type is 64 or above
- **THEN** the receiver closes the connection and does not change trust

### Requirement: Android front end stays in-process
The Android shell SHALL invoke the same codec as the daemon inside the app process. It MUST NOT open a local socket for those commands.

#### Scenario: Android operator pairs a device
- **WHEN** the operator accepts a pairing in the Android shell
- **THEN** the shell submits that decision in-process and no local socket is opened
