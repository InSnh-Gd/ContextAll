# Proposal

## Why

`lan-session` requires mDNS, IPv6 discovery, a five-second beacon cadence, and a route that survives a local address change, but the running daemon only sends IPv4 beacons every second, has no mDNS, and binds its QUIC listener once at startup. A trusted peer becomes unreachable after a DHCP lease or interface change, and three requirement-level edge cases (local unknown frame type, `UNPAIR` delivery, pairing-time rename) are not implemented.

## What Changes

- Add mDNS for `_contextall._udp.local` on UDP 5353, shared by the Linux and Windows daemon.
- Add IPv6 discovery: beacons to `ff02::1` per eligible interface, IPv6 QUIC listeners, and IPv6 on-link checks.
- Send beacons every 5 seconds, at startup, and after the local address set changes.
- Rebind QUIC listeners and re-send `PATH_ANNOUNCE` (including an empty list) when local addresses change.
- Close only the offending front-end connection when an unrecognized local frame type arrives.
- Write and flush the `UNPAIR` frame before the session closes.
- Route a pairing-time rename into the pairing session so the peer receives it after trust.
- Fall through to the next fresh dial candidate when the most recent one fails.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `lan-session`: discovery gains mDNS and IPv6, address-change rebind and re-announce, per-candidate dial fallback, and a requirement that `UNPAIR` is written before close.

## Impact

- `ContextAll.Host/LanHost.cs`, `PeerLink.cs`, `ContextAll.Core/RouteTable.cs`, and a new discovery component under `ContextAll.Host`.
- `ContextAll.Protocol` beacon types are unchanged; mDNS may add a discovery codec.
- `ContextAll.Host.Tests` gains network-change, IPv6, mDNS, local-unknown-type, `UNPAIR`, and pairing-rename coverage.
- Adds an mDNS dependency or a small in-repo DNS-SD implementation; the design records which.
