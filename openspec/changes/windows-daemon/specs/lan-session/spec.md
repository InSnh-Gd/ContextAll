# Spec Delta

## MODIFIED Requirements

### Requirement: Discovery follows the platform network
Linux and Windows SHALL send an IPv4 beacon from each eligible on-link address to that subnet's broadcast address, and SHALL join `ff02::1` per eligible interface for IPv6. Windows MUST NOT beacon on an interface in the public firewall profile, and MUST allow inbound UDP 42461 and 42462 only on the private profile. Android SHALL beacon only on Wi-Fi and Ethernet networks, never on cellular or VPN. Beacons SHALL be sent every 5 seconds, at startup, and when local addresses change. mDNS on Linux and Windows SHALL be sent by this system on UDP 5353. Android SHALL register and browse the same service through the platform DNS-SD API.

#### Scenario: Android has cellular and Wi-Fi
- **WHEN** the Android device has cellular data and Wi-Fi
- **THEN** beacons are sent only on Wi-Fi

#### Scenario: Windows interface is on the public profile
- **WHEN** an interface is in the public firewall profile
- **THEN** the daemon beacons on the other eligible interfaces and accepts no inbound QUIC or beacon on that one
