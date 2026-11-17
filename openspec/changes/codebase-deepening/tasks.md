# Tasks

Do not start these until `android-lab-snapshot` is done. Each section is a separate apply.

## 1. Local operator module

- [x] 1.1 Put request-id and local-command validation in one module used by `LanHost` and `NativeSessionBridge`
- [x] 1.2 Point `SessionHost.Apply` at that module; remove duplicate `RequestIdOk` helpers
- [x] 1.3 Retire or shrink `LocalDesk` so tests use the same module
- [x] 1.4 Add tests that duplicate request ids fail identically on the Host and Native call paths

## 2. Linux IDiscovery

- [x] 2.1 Choose inject `LinuxDiscovery` or delete it; do not leave production `discovery: null` beside an unused adapter
- [x] 2.2 Cover the chosen path with a Host test

## 3. Linux Shell templates

- [x] 3.1 Bind Devices list/detail to ViewModel collections in AXAML; remove `RebuildDeviceGroup` / `RebuildDetailPane`
- [x] 3.2 Bind Transfers the same way; remove its `Rebuild*` family
- [x] 3.3 Bind Connection peer/endpoint lists; inject `ILocalDaemonClient` instead of `new LocalDaemonConnection()` in views
- [x] 3.4 Update App tests that currently depend on code-behind rebuilds
