# Spec Delta

## ADDED Requirements

### Requirement: Android lab is canned snapshots
The Android `DEVELOPER_LAB` SHALL present a canned `SessionSnapshotUi` through the same Shell operator surface as the live native session. That lab MUST NOT implement pairing, transfer, or clipboard session rules.

#### Scenario: Operator enables developer lab
- **WHEN** the operator switches the Android shell to `DEVELOPER_LAB`
- **THEN** the in-process `SessionHost` stops and the shell shows the canned lab snapshot

#### Scenario: Operator leaves developer lab
- **WHEN** the operator switches back to the consumer profile and the native library is loaded and the foreground link service is running
- **THEN** the canned lab stops and `SessionHost` starts again

### Requirement: Android lab commands are a closed patch table
Lab local commands MUST apply only the closed snapshot patch table: display name, pairing accept/reject, unpair, per-peer clipboard and ask-before-save flags, send/accept/reject/cancel file list edits, and clipboard text. Commands outside that table MUST fail. Inbound lab demonstrations MUST use lab-only simulate actions.

#### Scenario: Lab command outside the patch table
- **WHEN** the operator submits a local command the lab patch table does not list
- **THEN** the lab returns an error and does not change pairing, transfer, or clipboard session state

### Requirement: Android native failure does not fake a session
When `libcontextall_native.so` is not loaded, the Android shell MUST keep the native operator's unavailable snapshot. It MUST NOT start the lab snapshot as a fallback, including the consumer profile.

#### Scenario: Native library missing at launch
- **WHEN** the Android shell starts and the native library is not loaded
- **THEN** the shell stays on the native unavailable snapshot and does not show canned lab devices as live peers
