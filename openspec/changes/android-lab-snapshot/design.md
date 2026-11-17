# Design

## Context

`ContextAllViewModel` currently owns `NativeSessionController` and `InProcessSessionEngine`, switching on `useLabEngine = !isNativeReady` and about 29 command branches. `InProcessSessionEngine` reimplements local commands, pairing delays, transfer ticks, and seed profiles. Production already runs `SessionHost` in-process via JNI (ADR 0006). `NativeSessionBackend` is the JNI seam; the lab must not implement it.

## Goals / Non-Goals

**Goals:**

- One `ShellOperator` interface implemented by `NativeSessionController` and `LabSnapshot`.
- Lab is canned snapshots plus a closed patch table and lab-only `simulate*`.
- Consumer path never starts the lab when native is missing.
- Entering lab stops `SessionHost`; leaving restarts it when `LinkService` is running.

**Non-Goals:**

- Moving Decision/Effect into this change.
- A fake `NativeSessionBackend`.
- Splitting `SessionSnapshotUi` fields out of the data class (ViewModel overlays chrome).
- Linux Shell Rebuild or `IDiscovery` (see `codebase-deepening`).

## Decisions

### 1. Seam is `ShellOperator` in `app.contextall`

Members: `snapshot`, `notices`, `start`/`stop`, `submitCommand`, `sendFiles`, `connectManual`, `pushClipboard`, `resetIdentity`, `scanLan`, `removeQueuedUpload`, `clearQueuedUploads`, `clearCompletedTransfers`, `setDeveloperUiEnabled`, `clearDebugFrameLogs`.

`NativeSessionController` implements this type. `LabSnapshot` lives in `app.contextall.lab`. Native must not import `lab`. Pause / retry / resend stay on `LabSnapshot` only.

### 2. ViewModel overlays chrome

Tab, theme, selection, `linkServiceRunning`, and `runtimePermissionsGranted` are written by the ViewModel onto whatever snapshot the current operator emits. Adapters do not store Shell chrome.

### 3. Lab patch table

`SetDisplayName`; `AcceptPairing`/`RejectPairing` (clear prompt; accept marks that device trusted); `UnpairLocal`; `SetClipboardSync`/`SetAskBeforeSave`; `SendFile` plus accept/reject/cancel file list edits; `PushClipboardText` via `pushClipboard`. `connectManual` and `scanLan` add canned peers. Unknown `submitCommand` types fail. No ticker, no QUIC, no `PairingSession`.

### 4. Tests and docs

Kotlin tests cover seed snapshot shape, patch success/failure, and `simulate*`. Session command rules stay in C# (`SessionHost` / Native tests). Record ADR 0009. Update `docs/specs/device-shell.md`.
