# Design

## Context

Architecture review (2026-11-17) ranked three deepenings after the Android lab clone. `Decision.Decide` is still a counter stub; `PeerLink` and `AndroidMsQuicBackend` are long but deep. This change records the remaining work. Do not apply it in the same turn as `android-lab-snapshot`.

## Goals / Non-Goals

**Goals:** one local-command module; honest Linux `IDiscovery`; Linux lists as templates.

**Non-Goals:** splitting `SessionHost` into `Decision`; splitting `PeerLink`, `AndroidMsQuicBackend`, `MsQuicNative`, `X11Clipboard`, or `TransferBook` by line count; `windows-daemon` 6.2; archiving `linux-desktop-shell`; finishing `md3e-ui-overhaul` design/tasks; Avalonia 12 `SetTextAsync`.

## Decisions

### 1. Local operator

Deepen `SessionHost.Apply` (or fold `LocalDesk` into it) so Host and Native call one function. Delete the three `RequestIdOk` copies. Tests hit that interface, not a drifted `LocalDesk` fake.

### 2. Linux discovery

Pick A: `LanHost.TryCreate` injects `LinuxDiscovery`, `SessionHost` skips built-in beacon when `IDiscovery` is present. Pick B: delete `LinuxDiscovery` and document that desktop discovery stays inside `SessionHost` when `discovery` is null. One adapter is not a real seam.

### 3. Linux Shell lists

Move `Rebuild*` into `ItemsControl` templates. Inject `ILocalDaemonClient` at `ShellRootView`. Views stop referencing Protocol DTOs if ViewModel already has display types; otherwise keep Protocol as read-only display types in the ViewModel, not in the view constructor.
