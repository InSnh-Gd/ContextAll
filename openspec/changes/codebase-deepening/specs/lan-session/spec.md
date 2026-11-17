# Spec Delta

## ADDED Requirements

### Requirement: Linux discovery has one home
On Linux production, discovery SHALL either run behind `IDiscovery` with `LinuxDiscovery` injected into the daemon host, or it SHALL run only inside `SessionHost` and `LinuxDiscovery` MUST NOT exist. The repository MUST NOT keep an unused `LinuxDiscovery` adapter beside a null `discovery` argument in production.

#### Scenario: Production Linux daemon starts
- **WHEN** the Linux daemon starts discovery
- **THEN** beacon and mDNS run in exactly one implementation, not in both `SessionHost` and an unused `LinuxDiscovery` type
