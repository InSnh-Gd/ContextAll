# Spec Delta

## Purpose

Keeps plain-text clipboards aligned across paired devices that have both opted in, without notifying the operator on every update.

## ADDED Requirements

### Requirement: Mutual opt-in
The system SHALL sync clipboard text between two paired devices only when each has enabled clipboard sync for the other. The enablement for each peer MUST default to off. A one-sided enablement MUST NOT change either clipboard.

#### Scenario: Both sides enable
- **WHEN** device A enables clipboard sync for device B and device B enables clipboard sync for device A
- **THEN** a later plain-text clipboard change on either device is applied on the other

#### Scenario: Only one side enables
- **WHEN** only device A has enabled clipboard sync for device B
- **THEN** clipboard changes on A do not change B, and clipboard changes on B do not change A

#### Scenario: Default is off
- **WHEN** two devices complete pairing
- **THEN** clipboard sync is disabled for that peer on both devices until an operator enables it

### Requirement: Plain text only
The system SHALL sync plain text and MUST NOT send clipboard images or files. A non-text clipboard change MUST leave paired devices' clipboards unchanged.

#### Scenario: Text is copied
- **WHEN** an operator copies plain text on a device that mutually syncs with a peer
- **THEN** that peer's clipboard becomes that text

#### Scenario: Image or file is copied
- **WHEN** an operator copies an image or a file
- **THEN** no clipboard update is sent to peers

### Requirement: Silent updates
The system MUST NOT raise a notification solely because a clipboard update was applied.

#### Scenario: Remote text arrives
- **WHEN** a peer's clipboard text is applied locally
- **THEN** the operator receives no notification for that update

### Requirement: Clear syncs
The system SHALL treat an emptied clipboard as a sync update. Mutually enabled peers MUST end with an empty clipboard.

#### Scenario: Operator clears the clipboard
- **WHEN** the operator clears the local clipboard and mutual sync is on with a peer
- **THEN** that peer's clipboard becomes empty

### Requirement: Size limit
The system SHALL refuse a clipboard update larger than 1 MiB. Other devices' clipboards MUST remain as they were.

#### Scenario: Text exceeds 1 MiB
- **WHEN** the local clipboard changes to plain text larger than 1 MiB
- **THEN** the system does not apply that text on any peer

### Requirement: Newer text wins
The system SHALL apply a clipboard update only when it is newer than the text already accepted for sync. An update that repeats the current text MUST NOT be sent onward. An older update MUST be ignored.

#### Scenario: A later copy replaces an earlier one
- **WHEN** two mutually synced devices copy different texts, and the second copy is newer
- **THEN** both devices end on the newer text

#### Scenario: Echo is dropped
- **WHEN** a device applies a remote clipboard text
- **THEN** it does not send that same text back to peers

#### Scenario: Stale update is ignored
- **WHEN** a device receives a clipboard update older than the text it has already accepted
- **THEN** its clipboard stays on the newer text
