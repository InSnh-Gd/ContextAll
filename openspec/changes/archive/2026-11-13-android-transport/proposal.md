# Proposal

## Why

The Android shell is a placeholder: `LinkService` is an empty service, `AndroidSession.Run` returns a constant, and the manifest declares no network permission. `lan-session` and `framed-channel` already require Android to beacon only on Wi-Fi or Ethernet, register DNS-SD, carry frames over QUIC in-process, and store transfers in the app no-backup directory, but none of that transport exists.

## What Changes

- Resolve the open transport decision first: verify `System.Net.Quic` on Android with a bundled MsQuic, and record the result in an ADR. If QUIC is unsupported, stop and open a separate spec change instead of adding a TCP fallback.
- Move the session engine currently in `ContextAll.Host` into a shared layer that Android can run in-process without a local socket, following ADR 0003.
- Implement the Android foreground `connectedDevice` service: Wi-Fi/Ethernet-only beacons, multicast lock, platform DNS-SD registration, QUIC listener and dialer, and in-process command handling.
- Add the required manifest permissions.
- Store identity, settings, transfers, and the download folder in the Android app directories.
- Out of scope: Android clipboard capture, file picker UI, and the device-list UI.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `framed-channel`: Android runs the same in-process session engine as the desktop daemon and uses QUIC for paired sessions, with no local socket or non-QUIC fallback.

## Impact

- `ContextAll.App.Android` gains a real service, permissions, and session wiring.
- `ContextAll.Effects` or a new shared session project gains the engine currently in `ContextAll.Host`; `ContextAll.Host` becomes one shell over it.
- Adds Android packaging for the QUIC native library, or triggers the transport-decision change if QUIC is not viable.
- Requires an Android build lane and device or emulator tests; the Linux test suite cannot exercise this transport.
