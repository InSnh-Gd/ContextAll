# Tasks

## 1. QUIC gate

- [ ] 1.1 Build or obtain MsQuic for the Android ABIs the app ships and package it in the APK. Verify the native libraries are listed in the built APK for `arm64-v8a` and `armeabi-v7a`.
- [ ] 1.2 Run a minimal app that checks `QuicListener.IsSupported` and completes a loopback handshake on API 31 and API 36. Verify the result is recorded in the change; if unsupported, stop and open a transport-decision change.
- [ ] 1.3 Record the transport decision in a Chinese ADR under `docs/adr/`. Verify the ADR states the QUIC result and the no-fallback rule.

## 2. Shared session engine

- [ ] 2.1 Create `ContextAll.Session` with `SessionHost`, `PeerLink`, identity and settings stores, transfer store, and the discovery service, referencing `Core`, `Effects`, and `Protocol`. Verify the project builds and the Linux tests still pass.
- [ ] 2.2 Make `ContextAll.Host` an adapter over `SessionHost` and keep the local-channel fan-out in the host. Verify `dotnet test ContextAll.Tests.slnf -c Release` passes unchanged.
- [ ] 2.3 Add platform seams `IHostPaths`, `IClipboardSource`, `IDiscovery`, and `ITransferSink` with Linux implementations. Verify unit tests for each seam and that the two-daemon test still passes.
- [ ] 2.4 Record the layer split in a second Chinese ADR under `docs/adr/`. Verify the ADR explains why the engine is not inside `Effects`.

## 3. Android shell and permissions

- [ ] 3.1 Add the manifest permissions for internet, network and Wi-Fi state, multicast, nearby Wi-Fi devices, foreground service, connected-device service type, and notifications. Verify the merged manifest contains all of them.
- [ ] 3.2 Implement `LinkService` as a foreground `connectedDevice` service that owns `SessionHost`, and start it from the activity. Verify an instrumented test that the service starts and reports a running session.
- [ ] 3.3 Implement Android paths: identity and transfers in `NoBackupFilesDir`, settings in `FilesDir`, completed files through `MediaStore.Downloads`. Verify an instrumented test that each file lands in the expected location.

## 4. Android discovery

- [ ] 4.1 Select only Wi-Fi and Ethernet networks from `ConnectivityManager` and bind beacon sockets to the selected network. Verify an instrumented test with a cellular and a Wi-Fi network where only Wi-Fi produces a beacon.
- [ ] 4.2 Register and browse `_contextall._udp.local` through `NsdManager` with a multicast lock, and feed results into the route table. Verify an instrumented test that registration succeeds and a record produces a candidate path.
- [ ] 4.3 Fail soft when NSD registration is denied or unavailable. Verify the beacon path still discovers a peer.

## 5. Android session

- [ ] 5.1 Run QUIC listening and dialing from the in-process `SessionHost`. Verify an instrumented loopback handshake and that no local socket is opened.
- [ ] 5.2 Wire in-process command handling and event delivery from the service to the activity. Verify an instrumented test that a pairing decision reaches the engine and the resulting device change reaches the UI layer.
- [ ] 5.3 Complete an inbound file transfer into `MediaStore.Downloads`. Verify an instrumented test that the downloaded bytes match and the partial is removed.

## 6. Documentation

- [ ] 6.1 Update `docs/specs/framed-channel.md`, `docs/specs/lan-session.md`, and `docs/specs/local-operator.md` in Chinese with the Android behaviour that now has tests. Verify each claim names its test.
- [ ] 6.2 Update the synced table in `docs/README.md`. Verify the Android transport is no longer described as a placeholder.

## 7. Integration

- [ ] 7.1 Run a desktop-to-Android check on one network: discovery, pairing, and one file transfer. If the CI environment cannot run two emulators with multicast, record the manual result.
- [ ] 7.2 Run the Linux suite and verify it still passes after the engine extraction.

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
