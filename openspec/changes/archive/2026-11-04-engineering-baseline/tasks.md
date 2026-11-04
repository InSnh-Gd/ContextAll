# Tasks

## 1. Repository

- [x] 1.1 Add the MIT `LICENSE` at the repository root. Use the author from the existing commits as the copyright line, and stop to ask if that name should not appear. Verify the file is the MIT license and the copyright line matches that author.
- [x] 1.2 Add `Directory.Build.props` that enables nullable reference types and states that the null-forgiving operator needs operator approval. Verify the props file sets `Nullable` to `enable`.
- [x] 1.3 Create the .NET 10 solution and the `src/` projects named in design.md (`Protocol`, `Core`, `Ipc`, `Effects`, `Host`, `App`, `App.Desktop`, `App.Windows`, `App.Android`), plus `tests/ContextAll.Core.Tests` and `tests/ContextAll.Protocol.Tests`. Add `ContextAll.Tests.slnf` (excludes `App.Android` and `App.Windows`) and `ContextAll.Linux.slnf` (includes the Linux desktop projects and Android, excludes `App.Windows`). Verify both filters omit `ContextAll.App.Windows`, the test filter also omits `ContextAll.App.Android`, and `dotnet build` of `ContextAll.Tests.slnf` succeeds.
- [x] 1.4 Reference `LanguageExt.Core` (net10 5.0 beta, pinned) from `ContextAll.Core` and set `IsAotCompatible` on that project. Verify the package reference is pinned in the project file and `dotnet build` of `ContextAll.Core` succeeds.

## 2. Functional core

- [x] 2.1 Write a failing test that calling `Decide` twice with the same state and input returns equal results and does not invoke the effect delegate. Then implement `Decide`, `Fin<Transition>`, and the effect records so the test passes. Verify `dotnet test` of `ContextAll.Tests.slnf` passes and that test asserts the delegate was not called.
- [x] 2.2 Write a failing test that a refused input returns a `Fin` failure, does not throw, and leaves the accepted state unchanged. Then implement that refusal. Verify that test passes.
- [x] 2.3 Write a failing test that the host effect delegate is invoked only when the host dispatches a returned effect, and that the dispatch awaits. Then implement `Func<Effect, CancellationToken, ValueTask<Fin<Unit>>>`. Verify that test passes and the new production sources do not call `.Result`, `.Wait()`, or `GetResult()`.
- [x] 2.4 Add a banned-API list for `Task.Result`, `Task.Wait`, and `GetAwaiter().GetResult()`, and enable nullable warnings as errors on hand-written projects. Verify the banned list contains those members and `dotnet build` of `ContextAll.Tests.slnf` succeeds with no null-forgiving operator in `src/`.

## 3. Verification gates

- [x] 3.1 Add `.github/workflows/test.yml` that runs on `push`, sets up the .NET 10 SDK, and runs `dotnet test` on `ContextAll.Tests.slnf` in Release. Verify the workflow trigger is `push` and its test command names that filter.
- [x] 3.2 Add `.githooks/pre-push` that runs the same `dotnet test` command as the workflow, and `scripts/enable-hooks` that sets `core.hooksPath` to `.githooks`. Verify the hook and the workflow name the same filter and the same configuration, and that running the hook script exits 0.

## 4. NativeAOT check

- [x] 4.1 Set `ContextAll.Host` to `PublishAot` and publish it for `linux-x64` with the Core reference in the graph. Verify the publish produces a native binary. If the publish fails because of LanguageExt, stop and report that failure instead of removing NativeAOT or LanguageExt.
