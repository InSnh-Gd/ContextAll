# Spec Delta

## MODIFIED Requirements

### Requirement: Route table is keyed by device id
The system SHALL keep multiple candidate paths per device id and one live session. Discovery paths not refreshed for 3 minutes MUST be removed. The remote address of a live session MUST NOT be dialed after that session closes. After trust, a peer MAY announce its own on-link unicast addresses, at most 8, replacing the previous announcement. Link-local addresses MUST NOT be announced. An announced address outside a local on-link prefix MUST be ignored. Announced and discovery paths MUST be dialed in order of most recent refresh. Path loss MUST NOT remove trust. When this device's local on-link address set changes, it MUST announce the new set to every trusted live peer, including an empty announcement when no on-link address remains.

#### Scenario: Session closes
- **WHEN** the live session ends
- **THEN** the system does not dial the address it observed on that session

#### Scenario: A trusted peer disappears from discovery
- **WHEN** a trusted peer's discovery paths expire
- **THEN** the trust entry remains and the peer is offline

#### Scenario: Local address set changes
- **WHEN** an interface address is added or removed while a trusted session is live
- **THEN** the peer receives a new announcement containing the current on-link addresses

### Requirement: Dialing
The system SHALL dial a trusted peer that has a fresh candidate and no live session. A failed dial MUST wait until that candidate is refreshed, and a failed candidate MUST NOT prevent dialing another fresh candidate. An untrusted peer MUST be dialed only when the local operator starts pairing. An incoming session from an untrusted peer MUST be accepted into pairing.

#### Scenario: Untrusted peer is only discovered
- **WHEN** an untrusted device is discovered and the operator has not started pairing
- **THEN** the system lists it and does not dial it

#### Scenario: The most recent candidate fails
- **WHEN** the most recently refreshed candidate fails and another fresh candidate exists
- **THEN** the system dials the next fresh candidate

### Requirement: Unpair and display name
Unpair SHALL delete the local trust entry immediately; when a session is live it MUST write and flush an `UNPAIR` frame before closing. A received unpair MUST delete trust and close. The display name MUST be 1 to 64 Unicode scalars and MUST NOT contain characters below U+0020. A rename during pairing MUST NOT be a new HELLO. After the session becomes trusted, a name that differs from the sent HELLO MUST be sent as a display-name update, including a name changed while that pairing was in progress. The initial name MUST come from the platform host name, or `ContextAll` when that name is empty after the rules are applied.

#### Scenario: Rename while pairing
- **WHEN** the operator renames this device before both sides have accepted
- **THEN** the peer still completes pairing with the HELLO name, and receives the new name only after trust is stored

#### Scenario: Live session is unpaired
- **WHEN** the operator unpairs a peer with a live session
- **THEN** the `UNPAIR` frame is flushed before the connection closes
