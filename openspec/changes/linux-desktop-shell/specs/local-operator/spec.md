# Spec Delta

## MODIFIED Requirements

### Requirement: Local commands use types 64 and above
Desktop front ends SHALL send commands as frames on the local channel. Each command payload MUST include a `requestId` of 1 to 64 lowercase hex characters, unique on that connection until its result is sent. A repeated id MUST be rejected with `duplicate-request`. Command types are `64` start pairing, `65` accept pairing, `66` reject pairing, `67` unpair, `68` set display name, `69` set clipboard sync, `70` set ask-before-save, `71` set download directory, `72` send file, `73` accept file, `74` reject file, `75` cancel transfer, `76` connect endpoint, and `77` push clipboard text, each with the device id, name, path, enabled flag, transfer id, endpoint (`host`, `port`, optional `alias`), or plain-text payload that command needs. The daemon MUST answer the sending connection with type `96`, carrying `requestId`, `ok`, and `error`. Failure codes MUST be `unknown-device`, `already-pending`, `already-decided`, `not-trusted`, `busy`, `invalid-name`, `unknown-transfer`, `duplicate-request`, `not-a-file`, `too-large`, `invalid-path`, and `invalid`.

`SEND_FILE` MUST resolve to a readable regular file or return `not-a-file` without occupying a slot. A file larger than 9007199254740991 bytes MUST return `too-large` and MUST NOT be offered. `SET_DOWNLOAD_DIRECTORY` MUST accept an empty string or an existing writable absolute path, and any other path MUST return `invalid-path` without changing the stored directory. `CONNECT_ENDPOINT` MUST validate a non-empty host and TCP/UDP port `1..65535` or return `invalid`. `PUSH_CLIPBOARD_TEXT` MUST accept UTF-8 plain text of at most 1 MiB (`1048576` bytes), store and broadcast it as a local clipboard change, and return `too-large` without changing the clipboard register when UTF-8 bytes exceed 1 MiB.

Events go to every connected front end. Type `80` is the snapshot sent on connect. Type `81` is one device change at priority 1. Type `82` is a pairing prompt at priority 0 with the device id, display name, six-character code, and remaining milliseconds. Type `83` ends pairing with outcome `trusted`, `rejected`, `timeout`, or `closed`. Type `84` is a file prompt. Type `85` is transfer progress at priority 2. Type `86` ends a transfer. Command results use priority 0. Android MUST invoke the same operations in-process and MUST NOT put these type numbers on a socket.

#### Scenario: Two front ends stay in step
- **WHEN** the CLI changes a peer switch while the shell is connected
- **THEN** both receive the device change and only the CLI receives the command result

#### Scenario: Send target is a directory
- **WHEN** the operator sends a directory path
- **THEN** the command result is `not-a-file` and no offer is made

#### Scenario: Manual endpoint command has invalid port
- **WHEN** a front end sends command `76` with port `0` or empty host
- **THEN** the command result is `invalid` and no connection attempt is made

#### Scenario: Push clipboard text exceeds 1 MiB
- **WHEN** a front end sends command `77` with UTF-8 text larger than 1 MiB
- **THEN** the command result is `too-large` and the stored clipboard register stays unchanged

### Requirement: Snapshot shows the whole local state
The snapshot SHALL include `localDeviceId`, `localDisplayName`, `downloadDirectory`, `resolvedDownloadDirectory`, `localAddresses`, `quicPort`, `clipboardBackend`, `devices`, `pairing`, `filePrompts`, and `transfers`. An empty `downloadDirectory` MUST mean the operating-system folder, while `resolvedDownloadDirectory` MUST report the effective directory path. `clipboardBackend` MUST report the active capture backend (`wayland`, `x11`, or `none` on Linux). Each device MUST include the device id, display name, trust flag, presence `offline`, `reachable`, or `connected`, and any known `endpoint`. A trusted device MUST also include `clipboardSync`, `remoteClipboardSync`, and `askBeforeSave`. `pairing` MUST be null or the same fields as a pairing prompt. Each file prompt MUST include the transfer id, device id, file name, total size, and remaining milliseconds, with at most one prompt per peer. A transfer entry MUST include the transfer id, device id, direction `send` or `receive`, file name, `totalSize`, completed segment count, segment count, and `moveBlocked`. An offer that is still waiting for acceptance MUST appear only under `filePrompts`.

#### Scenario: Front end connects during a transfer
- **WHEN** a front end connects after a transfer has been accepted and before any segment is stored
- **THEN** the snapshot lists that transfer with its `totalSize` and zero completed segments

#### Scenario: Snapshot reports local identity and network diagnostics
- **WHEN** a desktop front end connects to `daemon.sock`
- **THEN** the snapshot includes `localDeviceId`, `resolvedDownloadDirectory`, `localAddresses`, `quicPort`, and `clipboardBackend`

### Requirement: Scriptable and interactive CLI
On Linux and Windows, the CLI SHALL accept subcommands for listing devices, dialing an endpoint, starting pairing, changing the clipboard switch, changing ask-before-save, and sending a file. A subcommand with its required arguments MUST complete without prompting when standard input is not a terminal. A missing required argument outside a terminal MUST print usage and exit non-zero. When the operator starts the CLI with no subcommand and standard input is a terminal, the system SHALL open an interactive menu for those same actions. The menu MUST NOT be the only way to perform them. A subcommand MUST NOT open the menu.

#### Scenario: Script lists devices
- **WHEN** a script runs the device-list subcommand while the daemon is running
- **THEN** the command prints the devices and exits without waiting for input

#### Scenario: Script dials a manual endpoint
- **WHEN** a script runs the `connect` subcommand with `<host:port>` while the daemon is running
- **THEN** the daemon dials that endpoint through command `76` and exits without opening the interactive menu

#### Scenario: No arguments in a terminal
- **WHEN** the operator starts the CLI with no subcommand in an interactive terminal
- **THEN** an interactive menu offers the device list, endpoint connect, pairing, peer switches, and file send

## ADDED Requirements

### Requirement: Linux daemon desktop notifications
On Linux, when a session D-Bus notification service is available, the daemon SHALL publish desktop notifications for incoming pairing prompts, including the peer display name and six-character short code, and for incoming file prompts when ask-before-save is enabled, offering accept and reject actions. Dismissing a notification without an explicit action MUST NOT decide the prompt. Remote clipboard updates MUST NOT publish notifications.

#### Scenario: Pairing prompt arrives while shell is closed
- **WHEN** an untrusted peer initiates pairing while no graphical shell window is open and a D-Bus notification server is running
- **THEN** the daemon publishes a notification showing the peer display name, six-character code, and accept/reject actions

#### Scenario: Operator accepts a file prompt from the notification
- **WHEN** an incoming file triggers an ask-before-save notification and the operator invokes its accept action
- **THEN** the daemon accepts that file prompt under the same first-decision-wins rule as the CLI and shell

#### Scenario: Notification is dismissed without choosing an action
- **WHEN** the operator closes or times out the desktop notification banner without clicking accept or reject
- **THEN** the pairing or file prompt remains pending until answered or its monotonic deadline expires
