# Spec Delta

## Purpose

Lets an operator send one file to a paired device, and lets the receiver decide whether that peer must ask before the file is saved.

## ADDED Requirements

### Requirement: Send one file to a paired device
The operator SHALL be able to send one file to a selected paired device. Pairing is sufficient. Clipboard enablement MUST NOT affect whether a file can be sent. The system MUST NOT start a second transfer while one is in progress.

#### Scenario: Send to a paired device
- **WHEN** the operator chooses a paired device and a file, and no transfer is in progress
- **THEN** the system offers that file to the chosen device

#### Scenario: Clipboard switch is off
- **WHEN** clipboard sync is disabled for a paired device
- **THEN** the operator can still send a file to that device

#### Scenario: A transfer is already running
- **WHEN** the operator starts a file send while another transfer is in progress
- **THEN** the system rejects the new send and the sender is told it was not started

### Requirement: Ask before saving
Each paired device SHALL have an ask-before-save switch that defaults to on. While the switch is on, the system MUST NOT write the file until the receiving operator accepts. While the switch is off, the system MUST save an incoming file from that peer without asking.

#### Scenario: Receiver is asked
- **WHEN** a file arrives from a peer whose ask-before-save switch is on
- **THEN** the receiver shows an accept or reject choice and does not write the file yet

#### Scenario: Receiver accepts
- **WHEN** the receiving operator accepts the file
- **THEN** the system writes the file into the configured download directory

#### Scenario: Receiver rejects
- **WHEN** the receiving operator rejects the file
- **THEN** the system does not leave the file on disk and the sender is told the transfer failed

#### Scenario: Ask switch is off
- **WHEN** a file arrives from a peer whose ask-before-save switch is off
- **THEN** the system writes the file into the configured download directory without prompting

#### Scenario: Default asks
- **WHEN** two devices complete pairing
- **THEN** ask-before-save is on for that peer until an operator turns it off

### Requirement: Download directory
The system SHALL store a single download directory used for accepted files. The default MUST be the operating system's downloads folder. The operator MUST be able to change that directory, and later accepted files MUST use the new directory.

#### Scenario: Default directory
- **WHEN** the operator has not set a download directory
- **THEN** accepted files are written to the operating system's downloads folder

#### Scenario: Operator changes the directory
- **WHEN** the operator sets a different download directory and later accepts a file
- **THEN** the file is written to that directory

### Requirement: Incomplete files are removed
The system MUST delete a partially written file when the receiver rejects it, the transfer fails, or the session drops before completion. The sender MUST be told that the transfer failed.

#### Scenario: Connection drops mid-transfer
- **WHEN** the session ends before the file has been fully written
- **THEN** the partial file is removed from the receiver and the sender is told the transfer failed
