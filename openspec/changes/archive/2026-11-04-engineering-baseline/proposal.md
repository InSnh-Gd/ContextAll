# Proposal

## Why

`session-protocol` fixes what the product does and how the processes split. It does not fix how the C# is written, how the repository is laid out, or how a change proves itself before it lands. Those rules have to be set before implementation, and the protocol core needs a shape that can follow them.

## What Changes

- The repository is one monorepo for the Linux, Windows, and Android projects, licensed under MIT.
- Production C# is functional: LINQ and LanguageExt, `async`/`await`, expected failures as values, and `Action` or `Func` at the seams. The null-forgiving operator is not added unless the operator reviews that use.
- Behavior is added test-first. A push runs the test suite in GitHub Actions. A pre-push hook runs the same suite and stops the push when tests fail.
- The next design slice is the protocol core: pure transitions over immutable state that return the next state and effect values. The host runs those effects through delegates. Product requirements in `session-protocol` stay as they are.

## Capabilities

### New Capabilities

- `repository-baseline`: One repository for every platform project, with an MIT license at the root.
- `verification-gates`: Test-first changes, a GitHub Actions run on push, and a pre-push hook that blocks a failing suite.
- `functional-core`: Pure core transitions, errors as values, and host-side effects invoked through `Action` or `Func`.

### Modified Capabilities

- None. Published specs are empty. This change does not alter the requirements in `session-protocol`.

## Impact

- New root license, `src/` and `tests/` layout, a GitHub Actions workflow, and a versioned git hook.
- `ContextAll.Core` gains LanguageExt. The daemon remains NativeAOT, so that dependency has to survive `PublishAot`.
- Later implementation of `session-protocol` follows these rules. Window sizes stay deferred, as that change already decided.
