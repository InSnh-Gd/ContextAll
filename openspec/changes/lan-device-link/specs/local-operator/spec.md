# Spec Delta

## Purpose

Lets an operator run the local link in the user session and control it from a CLI on Linux and Windows, with settings stored in one YAML document.

## ADDED Requirements

### Requirement: User-session daemon
The system SHALL run a daemon in the signed-in user session. The daemon MUST own LAN sessions. The CLI and the graphical shell MUST control the daemon through a local channel and MUST NOT open their own LAN sessions.

#### Scenario: Shell and CLI share one daemon
- **WHEN** the graphical shell and the CLI are both open
- **THEN** both observe and change the same daemon state

#### Scenario: Linux user service
- **WHEN** the Linux daemon is installed for the user
- **THEN** it runs as a user service, not as a system-wide service

### Requirement: YAML settings
The daemon SHALL persist pairing, the local display name, per-peer clipboard enablement, per-peer ask-before-save, and the download directory in a YAML file. The daemon MUST be the only component that reads and writes that file. The CLI and the shell MUST change those settings by asking the daemon. The settings MUST still be in effect after the daemon starts again.

#### Scenario: Setting change goes through the daemon
- **WHEN** the operator changes a peer switch from the CLI or the shell
- **THEN** the daemon updates the YAML file and applies the new value

#### Scenario: Restart keeps settings
- **WHEN** the daemon stops and starts again
- **THEN** pairings, display name, peer switches, and the download directory match the last saved YAML

### Requirement: Scriptable CLI
On Linux and Windows, the CLI SHALL accept subcommands for listing devices, starting pairing, changing the clipboard switch, changing ask-before-save, and sending a file. A subcommand with its required arguments MUST complete without prompting when standard input is not a terminal.

#### Scenario: Script lists devices
- **WHEN** a script runs the device-list subcommand while the daemon is running
- **THEN** the command prints the devices and exits without waiting for input

#### Scenario: Missing argument outside a terminal
- **WHEN** a subcommand is missing a required argument and standard input is not a terminal
- **THEN** the command prints usage, exits with a non-zero status, and does not wait for input

### Requirement: Interactive CLI
When the operator starts the CLI with no subcommand and standard input is a terminal, the system SHALL open an interactive menu for the device list, pairing, the clipboard switch, ask-before-save, and sending a file. The menu MUST NOT be the only way to perform those actions.

#### Scenario: No arguments in a terminal
- **WHEN** the operator starts the CLI with no subcommand in an interactive terminal
- **THEN** an interactive menu offers the device list, pairing, peer switches, and file send

#### Scenario: Subcommand skips the menu
- **WHEN** the operator passes a subcommand
- **THEN** the CLI runs that subcommand and does not open the menu
