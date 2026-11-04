# Tasks

## 1. Platform paths

- [ ] 1.1 Add `HostPaths.ForThisPlatform()` with Linux and Windows implementations for identity, settings, state, transfers, and downloads. Verify unit tests for each platform's expected path and for the Windows download known folder.
- [ ] 1.2 Make `IdentityStore`, `SettingsStore`, `TransferStore`, and `LanHost` take resolved paths instead of calling `LinuxPaths` directly. Verify the existing host tests stay green.

## 2. Local channel selection

- [ ] 2.1 Add `IChannelServer` with a Unix socket implementation and a named-pipe implementation, and a `ServeDefault` that selects by platform. Verify a unit test that selects the pipe on Windows and the socket elsewhere.
- [ ] 2.2 Replace `ServePipe`'s `DeskFront` with a real `LanHost` and allow several pipe instances so more than one front end connects. Verify a test that starts the pipe daemon, connects two clients, and sees the same snapshot.
- [ ] 2.3 Restrict the pipe to the current user. Verify on a Windows runner that another local user cannot open it, or a unit test of the constructed `PipeSecurity` on Linux where the API allows.
- [ ] 2.4 Remove `Program`'s dependence on `XDG_RUNTIME_DIR` for Windows `serve`. Verify a test that `serve` on Windows starts with no XDG variables.

## 3. Firewall profile

- [ ] 3.1 Add `IFirewallProfile` with a Windows `NetworkListManager` implementation and a fake. Verify unit tests that a public-profile interface is skipped for beacons and listeners and a private/domain interface is used.
- [ ] 3.2 Add `packaging/windows/install-firewall.ps1` that creates private-profile inbound allow rules for UDP 42461 and 42462. Verify the script with PowerShell's parser and a dry-run listing the rules.
- [ ] 3.3 Refresh the cached profile on network change and skip an interface when the lookup fails. Verify a test where a failed lookup removes that interface without throwing.

## 4. Windows build and tests

- [ ] 4.1 Multi-target `ContextAll.Host` if the pipe ACL API requires it, and keep NativeAOT publishing per RID. Verify `dotnet build` for both targets and the Linux publish command still succeeds.
- [ ] 4.2 Add a Windows CI job that runs `dotnet test ContextAll.Tests.slnf -c Release`. Verify the job passes on the repository host.

## 5. Documentation

- [ ] 5.1 Update `docs/specs/local-operator.md` and `docs/specs/lan-session.md` in Chinese with the Windows daemon and firewall behaviour that now has tests. Verify each claim names its test.
- [ ] 5.2 Document the Windows install and firewall step in `docs/specs/local-operator.md` and update the synced table in `docs/README.md`. Verify the documented command runs as written.

## 6. Integration

- [ ] 6.1 Run the full suite on Linux and verify no regression.
- [ ] 6.2 Run a Windows manual check: `serve` with no XDG variables, pair with a Linux peer on the same private network, and verify discovery, pairing, and one file transfer.

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
