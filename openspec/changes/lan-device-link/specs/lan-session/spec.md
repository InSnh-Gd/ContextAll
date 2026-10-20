# Spec Delta

## Purpose

Lets a device find other ContextAll devices on the local network, pair with them, and keep a direct session with each paired peer.

## ADDED Requirements

### Requirement: LAN discovery
The system SHALL discover other ContextAll devices on the same local network and present each one with the display name that device advertises. The system MUST NOT treat a peer outside local-network addresses as discovered.

#### Scenario: Peer appears on the LAN
- **WHEN** another ContextAll device becomes reachable on the same local network
- **THEN** the local device shows it as available to pair, using that device's advertised display name

#### Scenario: Peer outside the LAN
- **WHEN** a session attempt comes from an address that is not a local-network address
- **THEN** the system rejects the session and does not show that peer as available

### Requirement: Persistent identity
The system SHALL keep a device identity across restarts and SHALL present a display name to peers. The operator MUST be able to change the local display name, and peers MUST observe the new name after the change is announced.

#### Scenario: Name survives restart
- **WHEN** the operator sets a display name and the local daemon starts again
- **THEN** peers still see that display name

### Requirement: Pairing confirmation
The system SHALL pair two devices only after both operators accept a short code derived from both devices' identities. Until both have accepted, the system MUST NOT exchange clipboard contents or files with that peer.

#### Scenario: Both operators accept
- **WHEN** both devices display the short code and both operators accept it
- **THEN** each device stores the peer as paired and may open a session with it

#### Scenario: One operator rejects
- **WHEN** either operator rejects the short code
- **THEN** neither device stores the peer as paired, and clipboard and file exchange with that peer stay blocked

### Requirement: Sessions with many peers
The system SHALL keep an independent direct session with each paired device that is currently reachable. Loss of one peer MUST NOT drop sessions with other paired devices.

#### Scenario: Two peers online
- **WHEN** two paired devices are reachable at the same time
- **THEN** the local device maintains a session with each of them

#### Scenario: One peer leaves
- **WHEN** one paired device becomes unreachable
- **THEN** the local device marks only that peer unavailable and keeps the session with each other reachable paired device

### Requirement: Pairing survives restart
The system SHALL reconnect to an already paired device without asking for the short code again.

#### Scenario: Restart after pairing
- **WHEN** a paired device stops and later becomes reachable again
- **THEN** the session resumes without a new pairing confirmation

### Requirement: Unpair
The operator SHALL be able to unpair a device. The system MUST end the session with that peer and MUST forget the pairing.

#### Scenario: Operator unpairs a peer
- **WHEN** the operator unpairs a paired device
- **THEN** the session ends, the peer is no longer paired, and clipboard and file exchange with it are blocked until a new pairing
