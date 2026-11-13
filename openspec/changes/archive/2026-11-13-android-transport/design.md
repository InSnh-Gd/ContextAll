# Design

## Context

See `proposal.md` for motivation. `ContextAll.App.Android` previously referenced only `ContextAll.Core` and `ContextAll.Effects`, while the session engine (discovery, routes, pairing, QUIC, transfers, command handling) lived in `ContextAll.Host` together with the Unix channel, `LinuxPaths`, and `WaylandClipboard`. `framed-channel` as amended by this change requires the Android app to run that engine in-process over QUIC, with no local socket and no non-QUIC fallback.

## Goals / Non-Goals

**Goals:**

- Extract the session engine so the desktop daemon and the Android app run the same code.
- Decide, before writing transport code, whether QUIC is available on Android and verify real TLS 1.3 mutual P-256 handshakes.
- Implement Android discovery, QUIC, foreground service, paths, and permissions.

**Non-Goals:**

- Android clipboard capture and the clipboard sync path.
- The device-list UI and file picker.
- Message sync or background operation when the service is stopped.

## Decisions

### Resolve the QUIC gate first

The first implementation task built a minimal Android spike packaging `libmsquic.so` (`arm64-v8a`, `armeabi-v7a`, `x86_64`) via `VpnHood.Net.Quic.MsQuic.AndroidNative`. While `QuicListener.IsSupported` returned `true`, `.NET Android`'s managed `System.Net.Quic` threw `DllNotFoundException` on `libSystem.Security.Cryptography.Native.OpenSsl` during TLS certificate validation (`CertificateValidation.BuildChainAndVerifyProperties`). To preserve genuine wire-identical QUIC without any non-QUIC fallback, `ContextAll.Effects` exposes a minimal `IQuicBackend` seam (`System.Net.Quic` by default on desktop), and `ContextAll.App.Android` provides `AndroidMsQuicBackend`, driving packaged `libmsquic.so` directly through `Microsoft.Quic` C# P/Invoke (`QUIC_CREDENTIAL_FLAG_USE_PORTABLE_CERTIFICATES` + `CertificateChecks.Evaluate`, UDP `42461`, ALPN `contextall`, 60s idle timeout, 15s keep-alive, single bidirectional stream). Verified via on-emulator loopback handshake (`QuicLoopbackHandshake_Succeeds_On_Android`) and cross-platform Linux `System.Net.Quic` ↔ Android `libmsquic.so` pairing and bidirectional file transfer. No TCP, WebSocket, or local-socket fallback is added (`docs/adr/0004-quic-android-transport.md`).

Alternative: assume managed `System.Net.Quic` works out of the box or fall back to TCP. Rejected because every other task depends on genuine QUIC protocol equivalence, and ADR 0004 explicitly forbids fallback transports.

### Extract `SessionHost` into a new shared project

Add `ContextAll.Session` (references `Core`, `Effects`, `Protocol`) containing:

- `SessionHost`: the engine extracted from `LanHost` minus front-end clients, Unix socket, Wayland, and XDG paths.
- `PeerLink`, `IdentityStore`, `SettingsStore`, `TransferStore`, and the discovery service from `transport-parity`.
- Platform seams: `IHostPaths`, `IClipboardSource`, `IDiscovery`, `ITransferSink`.

`ContextAll.Host` keeps the local-channel server and front-end fan-out; it becomes an adapter over `SessionHost`. `ContextAll.App.Android` references `Session` directly and calls it in-process. `ContextAll.Effects` keeps only `QuicLink` (`IQuicBackend`) and effect execution, so ADR 0003 still holds: shared effects stay in one module, and orchestration is a separate layer.

Alternative: move everything into `ContextAll.Effects`. Rejected because it would pull YAML settings, file stores, and discovery into the effects module and blur the boundary the architecture document describes.

Recorded in `docs/adr/0005-session-layer-split.md` alongside `docs/adr/0004-quic-android-transport.md`.

### Android platform adapters

- **Paths**: identity under `Context.NoBackupFilesDir/identity`, settings under `Context.FilesDir/config.yaml`, transfers under `NoBackupFilesDir/transfers`, completed files inserted into `MediaStore.Downloads` (`AndroidHostPaths`, `AndroidTransferSink`).
- **Discovery**: `ConnectivityManager` supplies only `TransportType.Wifi` and `TransportType.Ethernet` networks, never cellular or VPN. Beacon sockets bind to the selected network via `Network.BindSocket`. mDNS uses `NsdManager` (`_contextall._udp.local`) with a `WifiManager.MulticastLock`; raw 5353 sockets are not used (`AndroidDiscovery`).
- **Session**: `AndroidSession` starts `SessionHost` in the application process. `LinkService` is a `connectedDevice` foreground service that owns it and publishes a notification. Commands call the engine directly using the same `FrameCodec` and `ControlJson` codec; no local channel is opened.
- **Clipboard**: disabled for this change; `IClipboardSource` (`AndroidClipboardSource`) returns `IsSupported => false`. The engine still exchanges version vectors.

### Permissions and NativeAOT

The manifest adds `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`, `NEARBY_WIFI_DEVICES` (with `neverForLocation` on API 33+), `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, and `POST_NOTIFICATIONS`. MsQuic native libraries (`libmsquic.so`) are packaged per ABI (`arm64-v8a`, `armeabi-v7a`, `x86_64`) in both Debug and Release APKs.

### Tests and Verification Results

- **Desktop & Shared Engine**: `ContextAll.Tests.slnf` (including `TwoDaemonTests` and `AndroidSeamParityTests`) and the four-tier `ContextAll.Tests.E2E` suite verify `SessionHost`, platform seams, `IQuicBackend`, and protocol invariants.
- **On-Emulator Instrumented Suite (`AndroidInstrumentedTestSuite`)**: All 9/9 instrumented tests pass on Android Studio emulator `emulator-5554` (`sdk_gphone64_x86_64`, API 37), covering MsQuic loopback handshake, `LinkService` foreground session startup, `AndroidHostPaths` & `MediaStore.Downloads` placement, `ConnectivityManager` Wi-Fi/Ethernet-only filtering, live `NsdManager` + `MulticastLock` registration/resolution into `RouteTable`, soft fallback to UDP 42462 beacons when NSD fails, zero local Unix sockets in `SessionHost`, in-process pairing command/UI event delivery, and 100 KiB multi-segment inbound transfer to `MediaStore.Downloads` with partial cleanup.
- **Live Desktop-to-Android Integration (`RunLiveDesktopIntegrationAsync`)**: Verified live between a Linux desktop `SessionHost` (`System.Net.Quic` + `libmsquic 2.6.4`) and the Android Studio emulator (`emulator-5554`, `AndroidMsQuicBackend` + packaged `libmsquic.so`) over `10.0.2.2`: mutual ECDSA P-256 TLS 1.3 handshake, matching 6-character pairing code (`ylucrs`), 96 KiB Linux → Android `MediaStore.Downloads` SHA-256 verified transfer, and 70 KB Android → Linux SHA-256 verified transfer completed in 2.45s.

## Risks / Trade-offs

- [Managed `System.Net.Quic` lacks Android OpenSSL PAL] → Solved via `IQuicBackend` + `AndroidMsQuicBackend` invoking packaged `libmsquic.so` directly with portable DER certificates and `CertificateChecks.Evaluate`.
- [Android restricts multicast and NSD] → Use `MulticastLock`, the platform `NsdManager`, and runtime permission handling for `NEARBY_WIFI_DEVICES`; degrade to beacon-only discovery if NSD registration fails.
- [Foreground service limits and Doze] → Keep the service type `connectedDevice`, hold the session only while the service runs, and let the OS stop it rather than fighting the battery manager.
- [Extracting the engine churns the desktop daemon] → Done as a behaviour-preserving step first with the Linux test suite green.
- [MediaStore writes need scoped storage] → Insert through `MediaStore.Downloads` (`IsPending = 1` → `0`) on API 31+ and keep partial state in no-backup storage.

## Migration Plan

No desktop data changes. The Android app is new, so first start creates identity, settings, and empty transfer state. Removing the app deletes no-backup state; completed downloads remain in `MediaStore`.
