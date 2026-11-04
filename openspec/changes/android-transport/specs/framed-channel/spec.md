# Spec Delta

## MODIFIED Requirements

### Requirement: Android front end stays in-process
The Android shell SHALL run the same session engine as the desktop daemon inside the app process, including discovery, pairing, QUIC sessions, transfers, and local command handling. It SHALL invoke the same codec as the daemon. It MUST NOT open a local socket for those commands and MUST NOT use a non-QUIC transport for paired sessions.

#### Scenario: Android operator pairs a device
- **WHEN** the operator accepts a pairing in the Android shell
- **THEN** the shell submits that decision to the in-process session engine and no local socket is opened

#### Scenario: Android carries a paired session
- **WHEN** an Android device is paired with a desktop peer
- **THEN** its frames travel on the same QUIC stream and header as the desktop daemon, with no TCP or local-socket path

#### Scenario: QUIC is unsupported on the platform
- **WHEN** the Android runtime reports that QUIC is unavailable
- **THEN** the implementation stops and no other transport is substituted
