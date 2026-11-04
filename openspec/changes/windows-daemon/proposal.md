# Proposal

## Why

The repository lists Windows as a supported desktop, and `local-operator`, `framed-channel`, and `lan-session` already specify Windows paths, the per-user named pipe, and firewall-profile discovery. The current host always opens a Unix socket, `ServePipe` serves a `LocalDesk` stub with no discovery or QUIC, and `Program` derives its runtime path only from `XDG_RUNTIME_DIR`; Windows therefore has no working daemon.

## What Changes

- Select the local channel by platform: Linux keeps the Unix socket, Windows uses `\\.\pipe\ContextAll\<SID>` with a current-user-only ACL.
- Start the same `LanHost` session engine from Windows `serve`, without reading `XDG_RUNTIME_DIR`.
- Resolve `%LOCALAPPDATA%\ContextAll\` for identity, settings, transfers, and the download folder on Windows.
- Apply the firewall rules: no beacon on a public-profile interface, and inbound UDP 42461 and 42462 allowed only on the private profile.
- Keep the Windows graphical shell unchanged; it already talks to the local channel.
- Add a Windows build/test lane for the host.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-operator`: Windows `serve` starts the daemon over the per-user pipe and does not depend on `XDG_RUNTIME_DIR`.
- `lan-session`: the private-profile firewall rule also covers inbound QUIC on port 42461, not only the UDP beacon.

## Impact

- `ContextAll.Host/Program.cs`, `CliApp.cs`, `DaemonProcess.cs`, `LanHost.cs`, `LinuxPaths.cs`, and a new Windows path/profile helper.
- `ContextAll.Host.Tests` gains platform-selection and pipe-daemon coverage.
- CI or a manual Windows job is required; clipboard capture on Windows remains out of scope for this transport change.
