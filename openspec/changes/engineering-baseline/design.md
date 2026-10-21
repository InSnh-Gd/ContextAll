# Design

## Context

See proposal.md for why this change exists. This change does not redesign product behavior. `session-protocol` is the merged MVP, including the shell, Wayland delivery, theme, and AppImage. The decisions below are the current constraints this change has to fit.

Already decided in `session-protocol`:

- .NET 10. `Decide` is testable without a network. `Effects` calls it. `Host` and `App.Android` enter `Effects`. The CLI and `App` reach a running daemon through `Ipc`.
- `ContextAll.Protocol` has no project reference and no socket, file, HTTP, or UI dependency. `ContextAll.Core` (`net10.0`, `IsAotCompatible`) references `Protocol` and holds identity, pairing, discovery, and clipboard and file rules. It does not reference Avalonia or Android.
- `ContextAll.Ipc` references only `Protocol`. It is the frontend SDK: a Unix-socket client on Linux and a named-pipe client on Windows. It does not listen. The CLI and `ContextAll.App` use it. Android does not.
- `ContextAll.Host` references `Core`, `Ipc`, and `Effects`. It is one NativeAOT binary. `serve` listens on the local socket. Shared effects run in `Effects`: `Decide`, QUIC writes, and file segments. Discovery, clipboard, and paths stay in `Host`. Other commands use `Ipc`, do not start the daemon, and do not open QUIC.
- `ContextAll.App` references only `Ipc` and holds the Avalonia views, view models, tray, and popups for Linux (`CCSWE.Avalonia.Material` 12.x). `ContextAll.App.Desktop` references `App` and `Ipc` and is the Linux entry. `ContextAll.App.Windows` is the unpackaged WinUI 3 shell, references only `Ipc`, and is excluded from the Linux build.
- `ContextAll.App.Android` references `Core` and `Effects`. It holds the Android UI. The UI calls one method, and that method enters `Effects`. Discovery, clipboard, and paths stay in this project. Android is API 31–36 with a `connectedDevice` foreground service. Linux is one Wayland AppImage.
- Protocol: QUIC on port 42461, one bidirectional stream, a 16-byte frame, mDNS plus a UDP beacon on port 42462, ECDSA P-256 identity, short code shown only after both identities are known, both operators must accept. Clipboard order is the hybrid logical clock. Each peer has one outbound file transfer and one inbound file transfer, resumed from a bitmap.
- On Linux and Windows the daemon owns the YAML file. The CLI and `App` talk to it over the local channel. `App.Android` keeps its own settings and calls `Core` in-process. Linux clipboard is read in the daemon through data-control. Window minimum size and column breakpoints stay unset until the shells are implemented.
- Appearance on Linux reads only the desktop portal `color-scheme`. See `docs/adr/0001-linux-color-scheme.md`.

This change adds the repository rules and the shape of a core decision. Specs for those rules are under `specs/`.

## Goals / Non-Goals

**Goals:**

- One git repository, MIT licensed. The Linux build excludes `ContextAll.App.Windows` and does not need the Windows SDK.
- A test suite that GitHub Actions runs on push and that a pre-push hook runs locally.
- Core decisions that are pure functions from state and input to the next state plus effect values.
- Production code that awaits, returns expected failures as values, and decouples seams with `Action` or `Func`.

**Non-Goals:**

- Changing pairing, clipboard, file, CLI, or shell behavior specified in `session-protocol`.
- Choosing desktop window sizes or layout breakpoints.
- An Android or WinUI job in the push workflow.
- A plugin host, an HTTP stack, or the LanguageExt effect system (`Eff` / `Aff`).

## Decisions

### Repository layout

One solution contains every project, under `src/`:

- `ContextAll.Protocol` — pure protocol. No project reference and no socket, file, HTTP, or UI package.
- `ContextAll.Core` — `Decide` and device rules. References Protocol. No sockets, no UI.
- `ContextAll.Ipc` — frontend SDK. Only a local client: Unix domain socket on Linux, named pipe on Windows. References Protocol. Does not listen.
- `ContextAll.Effects` — shared effect work: `Decide`, QUIC writes, and file segments. References Core. No UI, no local client.
- `ContextAll.Host` — one NativeAOT binary for `serve` and every CLI command. References Core, Ipc, and Effects. Discovery, clipboard, and paths stay here. Other commands use Ipc and do not start the daemon.
- `ContextAll.App` — Avalonia views, view models, tray, and popups for Linux. References Ipc.
- `ContextAll.App.Desktop` — Linux entry. References App and Ipc. Calls `UseWayland()` on Linux.
- `ContextAll.App.Windows` — unpackaged WinUI 3 shell. References Ipc. Excluded from the Linux build. Trimming and single-file publish stay off.
- `ContextAll.App.Android` — Android UI. References Core and Effects. The UI calls one method, which enters Effects. Discovery, clipboard, and paths stay here.

Tests live under `tests/` and reference only the project they test: `ContextAll.Core.Tests`, `ContextAll.Protocol.Tests`.

`ContextAll.Tests.slnf` is the automated suite. It includes the `net10.0` projects and their tests. It excludes `ContextAll.App.Android` and `ContextAll.App.Windows`, because the push gate has to run with the .NET SDK alone. `ContextAll.Linux.slnf` includes the Linux desktop projects and `ContextAll.App.Android`, and excludes `ContextAll.App.Windows`.

The root `LICENSE` is the MIT license. The copyright line uses the author of the existing commits. If that name should not appear, stop and ask before writing the file.

Alternative: a repository per platform. Rejected. The shared core is the point of the monorepo, and the request asks for one.

### Verification

Each new production behavior starts as a failing test in `tests/`, then the production change that makes it pass. Tests are xUnit.

`.github/workflows/test.yml` runs on `push`. It checks out the repository, sets up the .NET 10 SDK, and runs `dotnet test` on `ContextAll.Tests.slnf` in Release. A failed test fails the workflow.

`.githooks/pre-push` runs that same command. `scripts/enable-hooks` sets `core.hooksPath` to `.githooks` for the local clone. The hook file's header states that command. A clone that has not run the script has no local hook; the remote workflow still runs. Do not point the hook at a different set of projects than the workflow.

Alternative: Husky. Rejected. A shell hook does not add a Node dependency to a C# repository.

### Functional core

A decision is a pure function:

`Fin<Transition> Decide(State state, Input input)`

`Transition` is an immutable record of the next `State` and the effects to run. `State`, `Input`, and each effect are immutable records. Effects are a closed set of records in Core, not a reflection-based union. The same state and input return the same `Fin<Transition>`.

Expected refusals are `Fin` failures. The accepted state does not change. Core does not throw or catch for those refusals. Absence is `Option<T>`, not null.

The clock, socket sends, and file writes are not read or performed inside `Decide`. The host puts the current UTC time on the input when a rule needs it. After a successful decision, the host runs each effect by calling one delegate:

`Func<Effect, CancellationToken, ValueTask<Fin<Unit>>>`

`Func` is the seam when the call returns a value or a task. `Action` is the seam when it returns nothing and cannot fail. A one-method interface is not added for either case. Framework types that already require an interface, such as `INotifyPropertyChanged`, stay as they are.

`Decide` itself stays synchronous. Only the effect delegate is asynchronous. Production code awaits that delegate. It does not call `Task.Result`, `Task.Wait`, or `GetAwaiter().GetResult()`.

The host may catch in exactly two places. The effect delegate catches a fault from a BCL or socket call and returns a failed `Fin`. The process loop catches a fault that escapes a single connection so the daemon keeps serving the other peers. Neither catch decides pairing, clipboard, or file rules.

Nullable reference types are enabled. Hand-written production code does not use the null-forgiving operator. If a compiler warning can only be cleared with that operator, stop and ask. Generated files are not hand-edited to add it. A text search for `!` is not a useful gate, because `!=` is ordinary C#.

LINQ is the default way to filter and fold collections inside a decision.

LanguageExt is `LanguageExt.Core` on the net10 line, pinned to the 5.0 beta current at implementation. Core uses `Fin<T>`, `Option<T>`, and LINQ over them. It does not use `Eff`, `Aff`, reflection, or LanguageExt's mutable-looking builders. Effect lists are BCL immutable arrays, so the trimmed surface stays small.

Alternative: pass a clock `Func` into `Decide`. Rejected. The purity requirement says the same input returns the same result, so the time has to be part of the input.

Alternative: model effects as `Eff` or `Aff`. Rejected. The host would no longer see an explicit effect list, and that runtime is a worse fit for NativeAOT than `Fin` and `Option`.

### What this change builds

This change builds the solution skeleton, the license, the two solution filters, the workflow, the hook, and the `Decide` signature with one test that repeats a decision and checks that no effect delegate ran. Pairing, framing, clipboard, and file rules stay in the `session-protocol` tasks. Those tasks call `Decide` and assert on `Fin` and effect values instead of opening sockets.

## Risks / Trade-offs

- [LanguageExt 5 is still a beta and is not shown as `IsAotCompatible`] → Pin one 5.0 beta, keep Core on `IsAotCompatible`, and publish `Host` with NativeAOT once the reference is added. If that publish fails, stop. Do not drop NativeAOT or LanguageExt without a new decision.
- [The pre-push hook runs only after `scripts/enable-hooks`] → The remote workflow does not depend on the local git config. The hook and the workflow share one solution filter.
- [The test gate skips Android and WinUI] → `App.Android` and `App.Windows` stay in the repository. `App.Android` stays in the Linux app filter. Neither is part of the suite that blocks a push.
- [Nullable warnings as a reason to reach for `!`] → Stop and ask. Do not silence a hand-written warning with the null-forgiving operator.
- [A catch in the host could start deciding protocol rules] → Protocol refusals stay in `Decide` as `Fin` failures. The two allowed catches only translate a fault from outside Core.

## Migration Plan

The repository has no application code yet. Adding the license, the workflow, and the hook does not migrate data. Rollback is to remove those files and the skeleton projects.

## Open Questions

None. The copyright line is taken from the existing commit author at implementation time, as decided above.
