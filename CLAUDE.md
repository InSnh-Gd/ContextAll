# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ContextAll shares a plain-text clipboard and sends files between trusted devices on one LAN, peer to peer over QUIC. One repo holds the Linux, Windows, and Android programs. Linux (daemon, CLI, Avalonia UI) is the most complete.

## Commands

```sh
dotnet test ContextAll.Tests.slnf -c Release        # full suite; same as CI and the pre-push hook
dotnet test tests/ContextAll.Core.Tests -c Release --filter "FullyQualifiedName~DecisionTests"   # one class
scripts/enable-hooks                                 # sets core.hooksPath to .githooks
packaging/appimage/build.sh                          # builds artifacts/ContextAll.AppImage
```

On Linux build only `ContextAll.Tests.slnf` (or `ContextAll.Linux.slnf`, which also needs the Android workload). Neither includes `ContextAll.App.Windows`; that project builds only on Windows from `ContextAll.slnx`.

`TwoDaemonTests` in `ContextAll.Host.Tests` re-executes the test assembly under `unshare -Urn` with a veth pair, so it needs unprivileged user namespaces, `ip`, `ethtool`, and `libmsquic.so.2`. See `.github/workflows/test.yml` for the setup. The test project has a custom `Program.Main` for that re-exec (`two-daemon` argument).

## Architecture

Functional core, imperative shell:

- `ContextAll.Protocol`: 16-byte frame header, codecs, beacon, JSON contexts. No IO. Frame limits live in `FrameLimits` (64 KiB payload).
- `ContextAll.Core`: `Decision.Decide(DecisionState, DecisionInput) -> Fin<Transition>` (LanguageExt `Fin`). Pure and deterministic: no sockets, files, or clock. Expected refusals are `Fin` failures, not exceptions. `Transition` carries the next state plus `ImmutableArray<Effect>` (closed record set).
- `ContextAll.Effects`: `SharedEffects.Run` awaits each effect delegate in order and stops at the first failure. Also owns `QuicLink` (ALPN `contextall`, port 42461, one bidirectional stream; no TCP fallback when QUIC is unsupported). Shared by the desktop daemon and Android (ADR 0003).
- `ContextAll.Host`: NativeAOT daemon + CLI (System.CommandLine, Spectre.Console menu, YamlDotNet with static generator). `serve` listens on `$XDG_RUNTIME_DIR/contextall/daemon.sock`; other commands are clients of that socket. Wayland clipboard is spoken directly (`WaylandWire.cs`), not through a library.
- `ContextAll.Ipc`: front-end client for the local channel (Unix socket on Linux, named pipe on Windows). Front ends only reference Ipc, never Core.
- `ContextAll.App` / `.App.Desktop`: Avalonia 12 UI, `UseWayland()`. `.App.Windows`: unpackaged WinUI 3. `.App.Android`: calls `Effects` in-process via `AndroidSession.Run`, no local channel.

Frame types 64+ are local commands/events (replies are type 96); receiving one over QUIC closes the session.

## Rules

- Nullable warnings and `RS0030` are errors. `BannedSymbols.txt` bans `Task.Result`, `Task.Wait`, `TaskAwaiter.GetResult`; always await.
- Do not use the null-forgiving operator `!` in hand-written production code without operator approval (tests are exempt; generated files untouched).
- Every new production behavior ships with a test in the same change.
- Requirements are OpenSpec (`openspec/specs/`, changes in `openspec/changes/`). When all tasks of a capability are done, rewrite `docs/specs/<capability>.md` in Chinese with only implemented, tested behavior, update the synced table in `docs/README.md`, and record decision changes as a Chinese ADR in `docs/adr/`. `docs/` prose is Chinese.
- Terminology (`docs/glossary.md`): say Decision (not transition/command/action), Effect (not side effect/job/task), Local channel (not IPC/pipe/socket).
