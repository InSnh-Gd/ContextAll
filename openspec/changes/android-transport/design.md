# Design

## Context

See `proposal.md` for motivation. `ContextAll.App.Android` references only `ContextAll.Core` and `ContextAll.Effects`. The session engine it needs (discovery, routes, pairing, QUIC, transfers, command handling) currently lives in `ContextAll.Host` together with the Unix channel, `LinuxPaths`, and `WaylandClipboard`. `framed-channel` as amended by this change requires the Android app to run that engine in-process over QUIC, with no local socket and no non-QUIC fallback.

## Goals / Non-Goals

**Goals:**

- Extract the session engine so the desktop daemon and the Android app run the same code.
- Decide, before writing transport code, whether QUIC is available on Android.
- Implement Android discovery, QUIC, foreground service, paths, and permissions.

**Non-Goals:**

- Android clipboard capture and the clipboard sync path.
- The device-list UI and file picker.
- Message sync or background operation when the service is stopped.

## Decisions

### Resolve the QUIC gate first

The first implementation task is a spike that builds a minimal Android app using `System.Net.Quic` with a packaged MsQuic native library and checks `QuicListener.IsSupported` and a loopback handshake on API 31 and 36. If either fails, the change stops and a separate change reopens the transport decision. No TCP, WebSocket, or local-socket fallback is added.

Alternative: assume QUIC works and build the rest first. Rejected because every other task depends on the answer, and the existing design explicitly makes this a stop condition.

### Extract `SessionHost` into a new shared project

Add `ContextAll.Session` (references `Core`, `Effects`, `Protocol`) containing:

- `SessionHost`: the engine currently in `LanHost` minus front-end clients, Unix socket, Wayland, and XDG paths.
- `PeerLink`, `IdentityStore`, `SettingsStore`, `TransferStore`, and the discovery service from `transport-parity`.
- Platform seams: `IHostPaths`, `IClipboardSource`, `IDiscovery`, `ITransferSink`.

`ContextAll.Host` keeps the local-channel server and front-end fan-out; it becomes an adapter over `SessionHost`. `ContextAll.App.Android` references `Session` directly and calls it in-process. `ContextAll.Effects` keeps only `QuicLink` and effect execution, so ADR 0003 still holds: shared effects stay in one module, and orchestration is a separate layer.

Alternative: move everything into `ContextAll.Effects`. Rejected because it would pull YAML settings, file stores, and discovery into the effects module and blur the boundary the architecture document describes.

Record this layer split in a new Chinese ADR (`docs/adr/0004-*`) as part of the change.

### Android platform adapters

- **Paths**: identity under `Context.NoBackupFilesDir/identity`, settings under `Context.FilesDir/config.yaml`, transfers under `NoBackupFilesDir/transfers`, completed files inserted into `MediaStore.Downloads`.
- **Discovery**: `ConnectivityManager` supplies only `TransportType.Wifi` and `TransportType.Ethernet` networks, never cellular or VPN. Beacon sockets bind to the selected network. mDNS uses `NsdManager` (`_contextall._udp.local`) with a `WifiManager.MulticastLock`; raw 5353 sockets are not used.
- **Session**: `AndroidSession` starts `SessionHost` in the application process. `LinkService` is a `connectedDevice` foreground service that owns it and publishes a notification. Commands call the engine directly; no local channel is opened.
- **Clipboard**: disabled for this change; `IClipboardSource` returns no changes. The engine still exchanges version vectors.

### Permissions and NativeAOT

The manifest adds `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`, `NEARBY_WIFI_DEVICES` (with `neverForLocation` on API 33+), `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, and `POST_NOTIFICATIONS`. MsQuic native libraries are packaged per ABI; the Android build fails if the library is missing rather than silently disabling transport.

### Tests

The extracted `SessionHost` is covered by the existing Linux host tests after the adapter move. Android-specific adapters get unit tests where possible and instrumented tests for service start, NSD registration, network selection, and a QUIC loopback handshake. Two emulators on a virtual network cover desktop-to-Android discovery and pairing if the CI runtime permits; otherwise the gap is recorded in the change.

## Risks / Trade-offs

- [MsQuic has no official Android build] → The spike is a hard gate. If a supported build cannot be produced for the target ABIs, stop and record the decision instead of substituting another transport.
- [Android restricts multicast and NSD] → Use `MulticastLock`, the platform `NsdManager`, and runtime permission handling for `NEARBY_WIFI_DEVICES`; degrade to beacon-only discovery if NSD registration fails.
- [Foreground service limits and Doze] → Keep the service type `connectedDevice`, hold the session only while the service runs, and let the OS stop it rather than fighting the battery manager.
- [Extracting the engine churns the desktop daemon] → Do the extraction as a behaviour-preserving step first, keep the Linux test suite green, then add Android. The existing two-daemon test is the regression gate.
- [MediaStore writes need scoped storage] → Insert through `MediaStore.Downloads` on API 31+ and keep the state partial in no-backup storage.

## Migration Plan

No desktop data changes. The Android app is new, so first start creates identity, settings, and empty transfer state. Removing the app deletes no-backup state; completed downloads remain in `MediaStore`.

## Open Questions

- Whether the CI environment can run two Android emulators with working multicast/QUIC networking. If not, the desktop-to-Android integration test becomes a documented manual check.
