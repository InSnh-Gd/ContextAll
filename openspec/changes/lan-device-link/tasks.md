# Tasks

## 1. Solution

- [ ] 1.1 Create the projects named in design.md (`Core`, `Client`, `Host`, shared view models, `Shell.Avalonia`, `Linux`, `Android`, `Windows`) and a Linux solution filter that excludes `ContextAll.Windows`. Verify `dotnet build` of that filter succeeds on Linux.
- [ ] 1.2 Set `Core` and `Host` as `IsAotCompatible`, set `Host` to `PublishAot`, and add YamlDotNet plus its static generator, System.CommandLine, and Spectre.Console on `Host`. Verify `dotnet publish -r linux-x64` of `Host` produces a native binary.

## 2. Session core

- [ ] 2.1 Implement length-prefixed frame encode and decode in `Core` and verify a round-trip test for a JSON control frame and a file chunk.
- [ ] 2.2 Implement device identity, short-code pairing, and the unpaired-session gate, and verify tests for both sides accepting, one side rejecting, and clipboard or file exchange staying blocked until both accept.
- [ ] 2.3 Implement LAN address filtering and multi-peer session tracking, and verify a test rejects a non-local source and a test drops one peer without clearing the other.
- [ ] 2.4 Implement mDNS advertise and browse plus UDP broadcast in `Core`, and verify a loopback test sees an advertised name on the local host.

## 3. Clipboard and files

- [ ] 3.1 Implement clipboard routing (mutual opt-in, default off, plain text only, silent flag, clear, 1 MiB reject, timestamp plus device-id order, echo drop) and verify one test per rule in `specs/clipboard-sync/spec.md`.
- [ ] 3.2 Implement single-file transfer (ask-before-save default on, download directory, partial-file delete, busy rejection) and verify tests for accept, reject, disconnect, the off switch, and a second send while one is active.

## 4. Daemon and settings

- [ ] 4.1 Persist pairings, display name, peer switches, and the download directory through the YamlDotNet static context, and verify a restart test loads the same values and that `Client` cannot write the file directly.
- [ ] 4.2 Serve the local control channel (Unix socket mode `0600` on Linux, named pipe on Windows) from `Host`, and verify a `Client` test lists devices and flips a switch against a daemon on a temp socket.
- [ ] 4.3 Accept paired TLS sessions on port 42461 and reject non-local peers, and verify two in-process hosts pair, reconnect without a new short code, and unpair.
- [ ] 4.4 Add the Linux user unit (`Type=notify`, `WantedBy=default.target`) and verify the unit file matches that and that `UseSystemd()` is on the generic host.

## 5. CLI

- [ ] 5.1 Add subcommands for device list, pair, clipboard switch, ask-before-save, and file send, and verify a non-terminal invocation with a missing argument exits non-zero and prints usage.
- [ ] 5.2 Open the Spectre.Console menu only when there is no subcommand and standard input is a terminal, and verify a subcommand path does not enter the menu.

## 6. Linux and Android shell

- [ ] 6.1 Build the Avalonia shell with `CCSWE.Avalonia.Material` 12.x, in-page device choice, and width-based layout. While doing this, choose the desktop minimum size and the column breakpoint, record those numbers in this change's design Open Questions, and verify a narrow layout shows one column and a wide layout shows the list beside the selection.
- [ ] 6.2 Map portal `color-scheme` `1` to dark and `0` or `2` to light, use light when the portal is missing, and ignore accent color. Verify a unit test of that mapping, including the missing-portal case.
- [ ] 6.3 Call `UseWayland()` from the Linux entry and exit with a Wayland-required message when no Wayland display is present. Verify that check's unit test, and verify `serve` still starts without Wayland.
- [ ] 6.4 Add the Android activity and `connectedDevice` foreground service that owns the socket and multicast lock, and verify the manifest declares that service type and the device list is a page list rather than a flyout.

## 7. Windows shell

- [ ] 7.1 Add the unpackaged self-contained WinUI 3 shell (`WindowsPackageType` `None`, trimming off, single-file off) sharing the view models, and verify the Linux solution filter still builds and the Windows project file contains those publish settings.

## 8. AppImage

- [ ] 8.1 Pack the shell, the NativeAOT host, and a launcher into one AppImage. Verify no arguments start the shell, `serve` starts the host, and any other arguments reach the CLI.
- [ ] 8.2 Point the user unit `ExecStart` at the AppImage `serve` entry and verify a user-service start runs the daemon.

## 9. Integration

- [ ] 9.1 Run two daemons on one machine, pair them, enable clipboard both ways, and verify a text copy and a clear arrive on the other side without a notification path being invoked.
- [ ] 9.2 Send one file between those daemons with ask-before-save on, then off, and verify accept writes into the download directory, reject leaves nothing, and a dropped connection deletes the partial file.
