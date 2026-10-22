# local-operator Specification

## Purpose

Stores this device's key and trust list, resets them only after an explicit choice, and lets local front ends operate the daemon through the framed channel.

## Requirements

### Requirement: Identity files
The daemon SHALL store `device.key` as unencrypted PKCS#8 PEM and `device.spki` as DER under `$XDG_DATA_HOME/contextall/identity/` on Linux, `%LOCALAPPDATA%\ContextAll\identity\` on Windows, and the app no-backup `identity/` directory on Android. The Linux directory MUST be mode `0700` and the desktop files MUST be mode `0600`. The Windows directory MUST grant full control to the current user and MUST NOT grant access to Everyone or other users. The daemon MUST create the directory on first start. A new identity MUST be created only when both files are absent.

#### Scenario: Both identity files are absent
- **WHEN** the daemon starts and neither identity file exists
- **THEN** it creates a new key pair and both files

#### Scenario: One identity file is missing
- **WHEN** only one of the two identity files exists
- **THEN** the daemon refuses to start and does not create a replacement key

### Requirement: Settings file
The daemon SHALL store the local display name, the download directory, and trust entries in `$XDG_CONFIG_HOME/contextall/config.yaml` on Linux, `%LOCALAPPDATA%\ContextAll\config.yaml` on Windows, and the app files `config.yaml` on Android. A trust entry MUST contain the device id text, the display name, the Base64 DER SPKI, `clipboardSync`, and `askBeforeSave`. A new trust entry MUST set `clipboardSync` false and `askBeforeSave` true. An empty download directory MUST mean the operating-system downloads folder. The private key, discovery paths, and transfer bitmaps MUST NOT be in that file. A missing file MUST be created with the initial display name and an empty trust list. A file that exists but cannot be parsed MUST leave the daemon unstarted and MUST leave the file unchanged.

#### Scenario: Settings file is missing
- **WHEN** the daemon starts with a valid identity and no settings file
- **THEN** it creates a settings file with the initial display name, an empty download directory, and no trust entries

#### Scenario: Download directory is cleared
- **WHEN** the operator sets the download directory to an empty string
- **THEN** later files use the operating-system downloads folder

#### Scenario: Settings file cannot be parsed
- **WHEN** the settings file exists and cannot be parsed
- **THEN** the daemon does not start and the file bytes are unchanged

### Requirement: Reset is explicit
When identity or settings cannot be used, `serve` MUST exit non-zero, MUST NOT listen, and MUST NOT modify the files. An interactive `identity reset` MUST ask, and the default MUST be no. A non-interactive reset MUST do nothing unless an explicit confirmation flag is present. A running daemon MUST NOT offer this reset. The graphical shell MAY show only reset or cancel, with cancel as the default. After a confirmed reset, the command MUST exit without listening, and a successful graphical reset MUST then start the daemon. A confirmed identity reset, and a confirmed settings reset that clears trust, MUST delete every file in the transfer state directory.

#### Scenario: Operator declines reset
- **WHEN** startup fails and the operator declines the reset
- **THEN** the files are unchanged and the daemon is not listening

#### Scenario: Key files are unusable and reset is confirmed
- **WHEN** the identity files are unusable and the operator confirms reset
- **THEN** a new key pair replaces them, trust entries are removed, and a readable display name is kept

#### Scenario: Only the settings file is unusable
- **WHEN** the identity files are valid, the settings file cannot be parsed, and the operator confirms reset
- **THEN** the settings file is replaced with the initial display name and an empty trust list, and the key files are unchanged

### Requirement: Front ends share one daemon
Linux and Windows front ends SHALL use the framed local channel. Android SHALL use in-process calls. The daemon MUST compute the short code and MUST send the device id text, label, six-character code, and remaining monotonic milliseconds. A device list entry MUST include the device id text, display name, trust flag, and whether the peer is offline, reachable, or connected. The first accept or reject for a pending pairing MUST win, and a later decision MUST be a failure value. `StartPairing` MUST dial an untrusted peer and MUST NOT dial again when that pairing is already pending or connected. Deadlines MUST use a monotonic clock.

#### Scenario: Two front ends answer one pairing
- **WHEN** the CLI accepts a pairing and the shell then rejects it
- **THEN** the accept stands and the reject is a failure

#### Scenario: Front end connects during pairing
- **WHEN** a front end connects while a pairing is pending
- **THEN** it receives the same short code and the remaining time

### Requirement: Local commands use types 64 and above
Desktop front ends SHALL send commands as frames on the local channel. Each command payload MUST include a `requestId` of 1 to 64 lowercase hex characters, unique on that connection until its result is sent. A repeated id MUST be rejected with `duplicate-request`. Command types are `64` start pairing, `65` accept pairing, `66` reject pairing, `67` unpair, `68` set display name, `69` set clipboard sync, `70` set ask-before-save, `71` set download directory, `72` send file, `73` accept file, `74` reject file, and `75` cancel transfer, each with the device id, name, path, enabled flag, or transfer id that command needs. The daemon MUST answer the sending connection with type `96`, carrying `requestId`, `ok`, and `error`. Failure codes MUST be `unknown-device`, `already-pending`, `already-decided`, `not-trusted`, `busy`, `invalid-name`, `unknown-transfer`, `duplicate-request`, `not-a-file`, `too-large`, `invalid-path`, and `invalid`.

`SEND_FILE` MUST resolve to a readable regular file or return `not-a-file` without occupying a slot. A file larger than 9007199254740991 bytes MUST return `too-large` and MUST NOT be offered. `SET_DOWNLOAD_DIRECTORY` MUST accept an empty string or an existing writable absolute path, and any other path MUST return `invalid-path` without changing the stored directory.

Events go to every connected front end. Type `80` is the snapshot sent on connect. Type `81` is one device change at priority 1. Type `82` is a pairing prompt at priority 0 with the device id, display name, six-character code, and remaining milliseconds. Type `83` ends pairing with outcome `trusted`, `rejected`, `timeout`, or `closed`. Type `84` is a file prompt. Type `85` is transfer progress at priority 2. Type `86` ends a transfer. Command results use priority 0. Android MUST invoke the same operations in-process and MUST NOT put these type numbers on a socket.

#### Scenario: Two front ends stay in step
- **WHEN** the CLI changes a peer switch while the shell is connected
- **THEN** both receive the device change and only the CLI receives the command result

#### Scenario: Send target is a directory
- **WHEN** the operator sends a directory path
- **THEN** the command result is `not-a-file` and no offer is made

### Requirement: Snapshot shows the whole local state
The snapshot SHALL include `localDisplayName`, `downloadDirectory`, `devices`, `pairing`, `filePrompts`, and `transfers`. An empty `downloadDirectory` MUST mean the operating-system folder. Each device MUST include the device id, display name, trust flag, and presence `offline`, `reachable`, or `connected`. A trusted device MUST also include `clipboardSync` and `askBeforeSave`. `pairing` MUST be null or the same fields as a pairing prompt. Each file prompt MUST include the transfer id, device id, file name, total size, and remaining milliseconds, with at most one prompt per peer. A transfer entry MUST include the transfer id, device id, direction `send` or `receive`, file name, completed segment count, segment count, and `moveBlocked`. An offer that is still waiting for acceptance MUST appear only under `filePrompts`.

#### Scenario: Front end connects during a transfer
- **WHEN** a front end connects after a transfer has been accepted and before any segment is stored
- **THEN** the snapshot lists that transfer with zero completed segments

### Requirement: One daemon owns the local socket
`serve` SHALL listen on the local channel. When `XDG_RUNTIME_DIR` is unset, Linux `serve` MUST exit non-zero and MUST NOT create a socket under `/tmp`. When the runtime path exists and is not a directory, `serve` MUST exit non-zero and MUST leave identity and settings unchanged. When the directory is missing, `serve` MUST create it with mode `0700`. Before listening, `serve` MUST connect to the existing socket. A successful connection MUST be closed at once and `serve` MUST exit non-zero. The running daemon MUST treat that immediate end as a front end leaving and MUST NOT change pairing, clipboard, or transfers. When the connection fails and a socket file is present, `serve` MUST remove that file and listen. A clean exit MUST remove the socket file. On Windows, a pipe name that is already taken MUST make `serve` exit non-zero. A control command other than `serve` MUST exit non-zero when the local channel cannot be reached and MUST NOT start a daemon.

#### Scenario: Daemon is already listening
- **WHEN** a second `serve` connects to the existing socket
- **THEN** the second process exits non-zero and the running daemon keeps its identity, settings, and transfers

#### Scenario: Socket file remains after a crash
- **WHEN** `serve` finds the socket path but nothing accepts the connection
- **THEN** it removes that file and listens

#### Scenario: Control command without a daemon
- **WHEN** a device-list command cannot reach the local socket
- **THEN** it exits non-zero and does not start a daemon

### Requirement: Scriptable and interactive CLI
On Linux and Windows, the CLI SHALL accept subcommands for listing devices, starting pairing, changing the clipboard switch, changing ask-before-save, and sending a file. A subcommand with its required arguments MUST complete without prompting when standard input is not a terminal. A missing required argument outside a terminal MUST print usage and exit non-zero. When the operator starts the CLI with no subcommand and standard input is a terminal, the system SHALL open an interactive menu for those same actions. The menu MUST NOT be the only way to perform them. A subcommand MUST NOT open the menu.

#### Scenario: Script lists devices
- **WHEN** a script runs the device-list subcommand while the daemon is running
- **THEN** the command prints the devices and exits without waiting for input

#### Scenario: No arguments in a terminal
- **WHEN** the operator starts the CLI with no subcommand in an interactive terminal
- **THEN** an interactive menu offers the device list, pairing, peer switches, and file send
