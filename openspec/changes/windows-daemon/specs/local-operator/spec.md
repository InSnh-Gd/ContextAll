# Spec Delta

## MODIFIED Requirements

### Requirement: One daemon owns the local socket
`serve` SHALL listen on the local channel. When `XDG_RUNTIME_DIR` is unset, Linux `serve` MUST exit non-zero and MUST NOT create a socket under `/tmp`. When the runtime path exists and is not a directory, `serve` MUST exit non-zero and MUST leave identity and settings unchanged. When the directory is missing, `serve` MUST create it with mode `0700`. Before listening, `serve` MUST connect to the existing socket. A successful connection MUST be closed at once and `serve` MUST exit non-zero. The running daemon MUST treat that immediate end as a front end leaving and MUST NOT change pairing, clipboard, or transfers. When the connection fails and a socket file is present, `serve` MUST remove that file and listen. A clean exit MUST remove the socket file. On Windows, `serve` MUST start the same daemon and session engine as Linux, MUST NOT require `XDG_RUNTIME_DIR`, MUST restrict the per-user pipe to the current user, and a pipe name that is already taken MUST make `serve` exit non-zero. A control command other than `serve` MUST exit non-zero when the local channel cannot be reached and MUST NOT start a daemon.

#### Scenario: Daemon is already listening
- **WHEN** a second `serve` connects to the existing socket
- **THEN** the second process exits non-zero and the running daemon keeps its identity, settings, and transfers

#### Scenario: Socket file remains after a crash
- **WHEN** `serve` finds the socket path but nothing accepts the connection
- **THEN** it removes that file and listens

#### Scenario: Control command without a daemon
- **WHEN** a device-list command cannot reach the local socket
- **THEN** it exits non-zero and does not start a daemon

#### Scenario: Windows serve without an XDG runtime path
- **WHEN** `serve` runs on Windows and `XDG_RUNTIME_DIR` is unset
- **THEN** the daemon listens on the current user's named pipe and starts discovery and sessions
