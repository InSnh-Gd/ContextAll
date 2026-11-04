# Design

## Context

See `proposal.md` for motivation. The current implementation is concentrated in three places:

- `LanHost` owns one IPv4-only UDP socket bound to `0.0.0.0:42462`, sends a beacon every second, and enumerates interfaces through `RefreshPrefixes`, which skips every non-IPv4 address.
- `ListenAsync` creates the QUIC listeners once, before `AcceptLoop`, and never revisits them.
- `PeerLink.EnterTrusted` sends `PATH_ANNOUNCE` once; `PeerLink.WriteLoop` exits as soon as its cancellation token fires, which is what races the `UNPAIR` frame.

`ContextAll.Protocol` already has the beacon codec and frame types. `ContextAll.Core.RouteTable` already enforces path expiry, suppression of a closed session's address, link-local filtering, the eight-address cap, and refresh ordering.

## Goals / Non-Goals

**Goals:**

- Provide one discovery path for Linux and Windows built from mDNS and IPv4/IPv6 beacons.
- Make local address changes trigger listener rebuild and a fresh announcement without dropping live sessions.
- Close the four requirement-level edge gaps: unrecognized local frame type, `UNPAIR` delivery, pairing-time rename, failed-candidate dialing.

**Non-Goals:**

- Windows daemon startup, paths, pipe ACL, and firewall profile gating. Those belong to `windows-daemon`.
- Android discovery and transport. Those belong to `android-transport`.
- Changing frame layouts, pairing cryptography, or transfer resume.

## Decisions

### One discovery service, two transports

Introduce `host/Discovery.cs` with an `IEnumerable<RouteCandidate>`-style output consumed by `LanHost`. It owns:

- a beacon sender/receiver on UDP 42462;
- an mDNS responder and browser on UDP 5353;
- one socket per eligible interface family, rebuilt from `LocalNetwork`.

`LocalNetwork` replaces `RefreshPrefixes` and returns IPv4 and IPv6 unicast prefixes together with the interface index and link-local flag. `RouteTable` keeps its current validation; the discovery service only supplies candidates.

Alternative: keep beacon code in `LanHost` and add a separate mDNS class. Rejected because the listener rebuild needs the same interface set, and duplicating enumeration would drift.

### Minimal in-repo mDNS

Implement only what the service needs: `_contextall._udp.local` PTR/SRV/TXT/A/AAAA records, one-shot query on startup and on address change, and responses to queries. Bind with `ReuseAddress`/`ReusePort` where available and tolerate failure; if 5353 is held by avahi or systemd-resolved and cannot be shared, log and continue with beacon discovery.

Alternative: a third-party mDNS package. Rejected for now: the host is NativeAOT, the service set is tiny, and a dependency would need its own AOT and Android story. The parser must bounds-check every record and cap compression-pointer indirection.

### IPv6 paths carry a scope id

`RouteTable` stores an address as text. For link-local IPv6, the discovery service stores `fe80::1%<ifindex>` so dialing can create an `IPEndPoint` with the right scope. `IPAddress.TryParse` accepts the `%` form on Linux and Windows. Announcements still exclude link-local addresses, as `RouteTable.Announce` already does.

### Address changes rebuild, never mutate in place

Subscribe to `NetworkChange.NetworkAddressChanged` and `NetworkAvailabilityChanged`, debounce with a one-second timer, recompute `LocalNetwork`, then:

1. start listeners for new addresses;
2. stop listeners for removed addresses after their accept loops exit;
3. restart the beacon and mDNS sockets for the new interface set;
4. enqueue `PATH_ANNOUNCE` on every trusted live link, including an empty list.

A five-second reconcile also runs, so a missed platform event cannot leave the daemon stale. Live `QuicConnection`s are held by their `PeerLink`, not by the listener, so rebuilding listeners does not drop them.

### `UNPAIR` gets a drain phase

`PeerLink.Close` currently cancels the writer immediately. Add `CloseAfterDrain`: the link stops accepting new frames, wakes the writer, the writer drains the outbox, then cancels. `SendUnpair` enqueues `UNPAIR` and calls `CloseAfterDrain`. This also lets `FILE_CANCEL` frames required by the file-transfer spec reach the wire.

### Unknown local type closes one front end

Classify local frame types in one place, `LocalFrameTypes.IsCommandOrEvent`. `DaemonProcess.Pump` returns from the pump when the type is not recognized, which runs the existing `finally` and completes that front end only. `LanHost.Command` no longer silently ignores unknown types.

### Rename is a pairing event

Add `PeerLink.RenameLocal(name)`. It calls `PairingSession.Rename` for every live link. The core already emits `DISPLAY_NAME` only after trust, so the same call both stores the name for the post-trust update and updates entered links.

### Dial the next fresh candidate

Replace `ShouldDial`'s first-candidate check with `RouteTable.NextDialCandidate(deviceId)`: the first candidate whose `FailedAt` is not equal to its `RefreshedAt`, still filtered by `Suppressed`. `ShouldDial` uses that. A failed candidate stays failed until its own refresh, while other fresh candidates remain eligible.

## Risks / Trade-offs

- [mDNS port 5353 is already owned by avahi or systemd-resolved] → Try `ReuseAddress`/`ReusePort`; on failure log once and keep beacon discovery. Do not take over or disable the system resolver.
- [Hand-written DNS parsing is input from the network] → Bound record counts, label lengths, and compression pointers; reject a packet rather than throw; add fuzz-style tests.
- [Listener rebuild races with an in-flight accept] → Add new listeners first, remove old ones after their accept task observes cancellation; keep listener state in one locked set.
- [Interface enumeration includes VPN and container bridges] → Keep the spec's "eligible on-link" rule: only addresses that can carry LAN traffic, excluding loopback and tunnel interfaces; Windows additionally drops public-profile interfaces.
- [IPv6 is not covered by the existing veth test] → Add a second namespace test with `ip -6 addr` and `ff02::1`, plus unit tests for scope-qualified parsing.

## Migration Plan

No stored data changes. Roll back by reverting the daemon binary; existing identity, settings, and transfer files remain readable.

## Open Questions

- Whether to add a small Windows-specific filter for tunnel adapters beyond the firewall-profile check. Defer until the Windows daemon change supplies the profile signal.
