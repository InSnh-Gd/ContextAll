# Tasks

## 1. Pure capture logic

- [x] 1.1 Add `ClipboardDenialSignal` that decides whether a log line is a clipboard-access denial for this package. Verify a unit test covers a matching denial, a denial for another package, and unrelated lines.
- [x] 1.2 Add `ClipboardSignalWindow` that rejects signals inside a cooldown and accepts them after, with a `reset()`. Verify a unit test covers first-trigger, in-cooldown skip, later allow, and reset.

## 2. Android mechanism

- [x] 2.1 Add `READ_LOGS` to the manifest and register `ClipboardCaptureActivity` with a transparent theme, `noHistory`, and `excludeFromRecents`.
- [x] 2.2 Add `ClipboardCaptureBus` (process-wide channel) and `ClipboardCaptureActivity` that reads one primary-clip text when it gains focus, publishes non-empty text, then finishes.
- [x] 2.3 Add `ClipboardAutoCaptureStore` (default-off `SharedPreferences` gate) and `ClipboardAutoCapture` coordinator exposing `enabled` and `capturing` flows, launching the capture activity on an accepted denial while enabled.
- [x] 2.4 Add `ClipboardSignalWatcher` run by `LinkService` that streams `logcat` lines and feeds the coordinator; start on service start and stop on destroy.

## 3. Integration and UI

- [x] 3.1 Wire `ContextAllViewModel` to set `enabled`/`capturing` chrome, expose a `setAutoCaptureEnabled` action, and push `ClipboardCaptureBus` captures through the existing non-empty / no-echo guard.
- [x] 3.2 Add the default-off toggle and honest「正在采集/未生效」status to `ThisDeviceScreen` (the explicit enable/revoke entry) plus `strings.xml` entries; the onboarding guide keeps its concept-only demo switches.

## 4. Spec and docs

- [x] 4.1 Amend the `clipboard-sync` "Clipboard capture follows the platform" requirement to allow the optional KDE-style capture behind the explicit default-off gate and add the three auto-capture scenarios.
- [x] 4.2 Add Chinese ADR 0012, update `docs/specs/clipboard-sync.md` and the synced table in `docs/README.md`.

## Workflow follow-up

- [ ] After the on-device acceptance steps and review are satisfied, archive the change.
