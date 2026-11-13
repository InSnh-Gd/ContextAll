# Tasks

## 1. Local network model

- [x] 1.1 Add `LocalNetwork` returning IPv4 and IPv6 unicast prefixes with the interface index and a link-local flag. Verify unit tests exclude loopback and down interfaces and mark `fe80::/10` addresses.
- [x] 1.2 Replace `LanHost.RefreshPrefixes` with `LocalNetwork` and verify the existing route and session tests stay green.
- [x] 1.3 Extend the on-link check and `OnLinkPrefix` to IPv6, including scope-qualified addresses. Verify unit tests cover an IPv6 prefix match, a mismatch, and a scope id on a link-local address.

## 2. mDNS

- [x] 2.1 Add a minimal DNS-SD codec for `_contextall._udp.local` PTR/SRV/TXT/A/AAAA records with bounded record counts, labels, and compression pointers. Verify unit tests for a valid packet, a truncated packet, a compression loop, and an oversized name.
- [x] 2.2 Add the mDNS responder and browser on UDP 5353 with `ReuseAddress`/`ReusePort`. Verify a parser-to-record assembly test and, where the environment permits, two sockets discovering each other on the multicast group.
- [x] 2.3 Route mDNS results into `RouteTable.AddDiscovery` with the certificate-independent digest, name, and port. Verify a test where beaconing is disabled and a peer is still discovered through mDNS.
- [x] 2.4 Fail soft when 5353 is already owned. Verify a test that occupies the port, starts discovery, and confirms beacon discovery still runs and the failure is logged once.

## 3. IPv6

- [x] 3.1 Send and receive beacons on `ff02::1` for each eligible interface. Verify a `unshare -Urn` IPv6 veth test that both daemons see each other.
- [x] 3.2 Include IPv6 addresses in listener endpoints and the on-link check. Verify an IPv6 QUIC handshake on the veth pair.
- [x] 3.3 Keep the interface scope id in discovery paths and use it when dialing. Verify a unit test that parses `fe80::1%2` and produces a scoped `IPEndPoint`, and that announcements still exclude link-local addresses.

## 4. Address changes

- [x] 4.1 Send beacons every 5 seconds, at startup, and after an address change. Verify the interval constant and a test that a simulated address change triggers an immediate beacon.
- [x] 4.2 Add a listener set that adds new addresses before removing old ones and rebuilds beacon and mDNS sockets. Verify a test that an existing session stays connected while an interface address is added and removed.
- [x] 4.3 Subscribe to `NetworkChange` with a one-second debounce plus a five-second reconcile. Verify a test with a fake network source that only one rebuild runs for a burst of events.
- [x] 4.4 Send `PATH_ANNOUNCE` to every trusted live peer when the local set changes, including an empty list. Verify a host test that the peer receives the new set and the empty set.

## 5. Protocol edge cases

- [x] 5.1 Classify local frame types in one place and have the local pump close only the offending front end on an unknown type. Verify a test with two front ends where one sends type 76 and only that connection ends.
- [x] 5.2 Add a writer drain phase so `UNPAIR` is written and flushed before the connection closes. Verify a test that the peer receives `UNPAIR` before its read loop ends.
- [x] 5.3 Call `PairingSession.Rename` from `SetDisplayName` for every live link. Verify a host-level test that a name changed during pairing arrives as `DISPLAY_NAME` after trust.
- [x] 5.4 Add `RouteTable.NextDialCandidate` and dial the first fresh candidate after a failure. Verify a unit test with two fresh candidates where the newest fails and the older is dialed.

## 6. Documentation

- [x] 6.1 Update `docs/specs/lan-session.md` and `docs/specs/framed-channel.md` in Chinese with only the behaviour that now has tests. Verify every claim is backed by a named test.
- [x] 6.2 Update the synced table in `docs/README.md`. Verify the row points at the updated documents.

## 7. Integration

- [x] 7.1 Run `dotnet test ContextAll.Tests.slnf -c Release` and verify the full suite passes, including the two-daemon test.
- [x] 7.2 Extend the two-daemon scenario with an address change and an IPv6 run. Verify both daemons recover discovery and keep the live session.

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
