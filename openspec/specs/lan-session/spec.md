# lan-session Specification

## Purpose

Identifies a device by its key, finds it on the LAN, pairs with a short code, and keeps one direct session per trusted peer.

## Requirements

### Requirement: Device id is derived from the key
The system SHALL identify a device by the first 128 bits of SHA-256 over the DER SubjectPublicKeyInfo of an ECDSA P-256 key. The text form MUST be the prefix `ca1` plus 26 lowercase unpadded Base32 characters. A certificate whose leaf key is not ECDSA P-256 MUST be rejected before a device id is computed.

#### Scenario: Peer presents a different key type
- **WHEN** a peer's leaf certificate is not ECDSA P-256
- **THEN** the system closes the connection and does not store a trust entry

#### Scenario: Same key is certified again
- **WHEN** the device builds a new self-signed certificate for the same key
- **THEN** its device id is unchanged

### Requirement: Certificate check uses the SPKI
The system SHALL accept a peer certificate by recomputing the device id from its SPKI and comparing the SPKI bytes with the trust entry. Hostname, public CAs, and certificate dates MUST NOT decide identity. A device id that matches an entry whose SPKI bytes differ MUST cause the connection to close without a trust change.

#### Scenario: Expired certificate with the stored key
- **WHEN** a trusted peer presents an expired certificate for the stored SPKI
- **THEN** the session continues

#### Scenario: SPKI bytes differ
- **WHEN** the computed device id matches a trust entry but the SPKI bytes differ
- **THEN** the system closes the connection and leaves the trust entry in place

### Requirement: Discovery yields paths only
The system SHALL learn candidate paths from mDNS `_contextall._udp.local` and from a UDP beacon on port 42462. A beacon SHALL carry the magic `CA01`, version 1, the 16-byte digest, the QUIC port, and the display name. The default QUIC port MUST be 42461. Identity MUST come from the certificate, not from the beacon. A beacon whose digest equals the local device id MUST be ignored.

#### Scenario: Beacon names this device
- **WHEN** a beacon contains this device's digest
- **THEN** the system does not add a route to itself

#### Scenario: Beacon display name is illegal
- **WHEN** a beacon name violates the display-name rules
- **THEN** the system still records the digest and path and discards the name

### Requirement: Discovery follows the platform network
Linux and Windows SHALL send an IPv4 beacon from each eligible on-link address to that subnet's broadcast address, and SHALL join `ff02::1` per eligible interface for IPv6. Windows MUST NOT beacon on an interface in the public firewall profile, and MUST allow inbound UDP 42462 only on the private profile. Android SHALL beacon only on Wi-Fi and Ethernet networks, never on cellular or VPN. Beacons SHALL be sent every 5 seconds, at startup, and when local addresses change. mDNS on Linux and Windows SHALL be sent by this system on UDP 5353. Android SHALL register and browse the same service through the platform DNS-SD API.

#### Scenario: Android has cellular and Wi-Fi
- **WHEN** the Android device has cellular data and Wi-Fi
- **THEN** beacons are sent only on Wi-Fi

### Requirement: Route table is keyed by device id
The system SHALL keep multiple candidate paths per device id and one live session. Discovery paths not refreshed for 3 minutes MUST be removed. The remote address of a live session MUST NOT be dialed after that session closes. After trust, a peer MAY announce its own on-link unicast addresses, at most 8, replacing the previous announcement. Link-local addresses MUST NOT be announced. An announced address outside a local on-link prefix MUST be ignored. Announced and discovery paths MUST be dialed in order of most recent refresh. Path loss MUST NOT remove trust.

#### Scenario: Session closes
- **WHEN** the live session ends
- **THEN** the system does not dial the address it observed on that session

#### Scenario: A trusted peer disappears from discovery
- **WHEN** a trusted peer's discovery paths expire
- **THEN** the trust entry remains and the peer is offline

### Requirement: Dialing
The system SHALL dial a trusted peer that has a fresh candidate and no live session. A failed dial MUST wait until that candidate is refreshed. An untrusted peer MUST be dialed only when the local operator starts pairing. An incoming session from an untrusted peer MUST be accepted into pairing.

#### Scenario: Untrusted peer is only discovered
- **WHEN** an untrusted device is discovered and the operator has not started pairing
- **THEN** the system lists it and does not dial it

### Requirement: Pairing and deduplication
Both sides SHALL send HELLO, containing a trusted-or-pairing intent and a display name, as soon as the session stream exists, without waiting for the peer HELLO. HELLO MUST arrive within 5 seconds or the connection closes without a trust change. A remote address outside an on-link prefix MUST be closed before HELLO. Deduplication MUST wait until both HELLO messages have arrived, then keep the session initiated by the greater 16-byte digest and close the other with a duplicate-close frame. The short code MUST be shown only on the surviving session. The short code MUST be computed locally from the two SPKIs ordered by digest, and MUST NOT appear on the wire. Both operators MUST accept before trust is stored. One rejection or a 60-second timeout MUST close the session without writing a new trust entry. The first front-end decision wins.

#### Scenario: Both sides dial
- **WHEN** both devices complete HELLO on two sessions
- **THEN** the session opened by the greater digest remains, and only that session shows the short code

#### Scenario: Pairing times out
- **WHEN** 60 seconds pass after pairing is ready and nobody accepts
- **THEN** the session closes and no new trust entry is written

### Requirement: Unpair and display name
Unpair SHALL delete the local trust entry immediately, send unpair if possible, and close. A received unpair MUST delete trust and close. The display name MUST be 1 to 64 Unicode scalars and MUST NOT contain characters below U+0020. A rename during pairing MUST NOT be a new HELLO. After the session becomes trusted, a name that differs from the sent HELLO MUST be sent as a display-name update. The initial name MUST come from the platform host name, or `ContextAll` when that name is empty after the rules are applied.

#### Scenario: Rename while pairing
- **WHEN** the operator renames this device before both sides have accepted
- **THEN** the peer still completes pairing with the HELLO name, and receives the new name only after trust is stored
