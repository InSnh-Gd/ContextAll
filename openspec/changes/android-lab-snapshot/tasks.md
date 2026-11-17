# Tasks

## 1. ShellOperator and LabSnapshot

- [x] 1.1 Add `ShellOperator` and `OutboundFile` in `app.contextall`; make `NativeSessionController` implement it without importing `lab`
- [x] 1.2 Add `LabSnapshot` in `app.contextall.lab` with canned `DEVELOPER_LAB` snapshot, closed patch table, `simulate*`, and lab-only pause/retry/resend
- [x] 1.3 Replace `InProcessSessionEngineTest` with `LabSnapshot` tests for snapshot shape, listed patches, rejected unknown commands, and simulate inbound pairing/file/clipboard

## 2. ViewModel and dead UI

- [x] 2.1 Point `ContextAllViewModel` at `ShellOperator`; overlay chrome; default to native; never start lab because native is missing
- [x] 2.2 Stop `SessionHost` when entering `DEVELOPER_LAB`; restart native when leaving if `LinkService` is running
- [x] 2.3 Delete `InProcessSessionEngine` and unused `ScenarioLabBottomSheet`

## 3. Docs

- [x] 3.1 Add Chinese ADR 0009 and update `docs/README.md`
- [x] 3.2 Rewrite the Android lab sentence in `docs/specs/device-shell.md` to match implemented behavior
