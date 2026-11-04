# Design

## Context

See `proposal.md` for motivation. The host currently hard-codes Linux assumptions in four places:

- `Program` reads only `XDG_RUNTIME_DIR` for the local channel.
- `CliApp.Run` sends every `serve` to `DaemonProcess.Serve`, which binds a Unix domain socket.
- `DaemonProcess.ServePipe` exists but wires a `LocalDesk`, not a `LanHost`, so no discovery or QUIC starts.
- `LanHost.TryCreate` resolves identity, settings, and state through `LinuxPaths`; `LanHost.Start` always tries the Wayland clipboard.

`local-operator`, `framed-channel`, and `lan-session` already specify the Windows behaviour; this change closes the implementation gap.

## Goals / Non-Goals

**Goals:**

- Run the same `LanHost` engine on Windows behind the per-user named pipe.
- Resolve Windows identity, settings, state, and download paths.
- Gate discovery and inbound traffic by the Windows firewall profile.
- Keep Linux behaviour byte-for-byte unchanged.

**Non-Goals:**

- Windows clipboard capture (`LanHost` will keep the clipboard disabled when Wayland is absent; clipboard sync is a separate capability).
- The WinUI shell, which already talks to the local channel.
- MSIX packaging or an auto-update channel.

## Decisions

### One platform seam, two implementations

Add `HostPaths.ForThisPlatform()` and a small `IChannelServer` abstraction:

- `UnixChannelServer`: the existing `$XDG_RUNTIME_DIR/contextall/daemon.sock`, mode `0600` in a `0700` directory.
- `PipeChannelServer`: `\\.\pipe\ContextAll\<current user SID>`, several server instances so more than one front end can connect.

`Program` and `CliApp` call `DaemonProcess.ServeDefault`, which selects the server and then runs the same `LanHost`, `FrontClient`, and `Pump` code. `ServePipe` stops using `LocalDesk`; `DeskFront` remains only as a test stub until the tests move to a real `LanHost`.

Alternative: keep `Serve` and `ServePipe` separate and duplicate the pump. Rejected because the two would drift and the existing tests already exercise the pump.

### Windows paths

`HostPaths` returns:

| Content | Windows path |
| --- | --- |
| identity | `%LOCALAPPDATA%\ContextAll\identity\` |
| settings | `%LOCALAPPDATA%\ContextAll\config.yaml` |
| state | `%LOCALAPPDATA%\ContextAll\` |
| transfers | `%LOCALAPPDATA%\ContextAll\transfers\` |
| downloads | the shell's Downloads known folder |

`TransferStore`, `SettingsStore`, and `IdentityStore` take the resolved paths from `HostPaths` instead of calling `LinuxPaths` directly. `LinuxPaths` remains the Linux implementation.

### Pipe ACL

Create the pipe with `NamedPipeServerStreamAcl.Create` and a `PipeSecurity` granting full control to the current user's SID and denying Everyone and other users. If that API requires the Windows TFM, multi-target `ContextAll.Host` as `net10.0;net10.0-windows` and keep NativeAOT publishing per RID. The Linux target must not reference the Windows assembly.

### Firewall profile gating

Use `NetworkListManager` (`INetworkListManager` / `INetworkConnection`) to read the category of the network carrying each interface. Beacons and listener bindings are limited to interfaces whose category is private or domain. The public category is skipped entirely.

The inbound allow rules for UDP 42461 and 42462 on the private profile are installed with the application, because creating firewall rules needs elevation and the daemon runs as the user. Add `packaging/windows/install-firewall.ps1` (admin, private profile only) and run it from the Windows install instructions. The daemon only reads the profile; it never edits firewall rules at runtime.

Alternative: create rules at first daemon start with an elevation prompt. Rejected: a background daemon must not trigger UAC, and a failed prompt would leave the profile requirement half-met.

### Tests on Linux

.NET named pipes work on Unix, so the pipe server, pump, and multi-front-end behaviour can be tested on Linux. The profile helper sits behind `IFirewallProfile` and is unit-tested with a fake. A Windows CI lane runs the same suite and the real profile/ACL path.

## Risks / Trade-offs

- [Windows-specific APIs are untested on the Linux CI] → Add a Windows job; keep all Windows code behind `OperatingSystem.IsWindows()` or a Windows TFM.
- [Pipe ACL API availability differs by TFM] → Multi-target the host; if the API is unavailable, stop and report rather than silently shipping a world-readable pipe.
- [Firewall rules need elevation] → Install-time script with an explicit private-profile scope; document the command. Never mutate the firewall from the daemon.
- [Windows clipboard stays disabled] → Out of scope; the daemon still pairs, discovers, and transfers files. The shell must not imply clipboard works until that capability lands.
- [Interface profile lookup is slow or COM fails] → Cache the per-interface category, refresh on network change, and fall back to skipping the interface rather than beaconing on an unknown profile.

## Migration Plan

No stored data format changes. A Windows user starts the daemon with `serve`; existing files under the Linux paths are not read. Roll back by reverting the binary and deleting `%LOCALAPPDATA%\ContextAll\` if a clean start is wanted.

## Open Questions

- The Windows packaging surface (plain installer versus MSIX) is not fixed. This change ships the firewall script and the daemon; packaging can wrap them later.
