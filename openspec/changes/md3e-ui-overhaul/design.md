# Design

## Context

See `proposal.md` for motivation. Written visual decisions live in `docs/ryugi/specs/2026-11-15-md3e-ui-overhaul-design.md` (D1–D7, `g1-expressive-studio` / `g2-split-workbench`, 1.A / 2.A / 3.A).

`ContextAll.App` already ships the HCT 48-role adapter, FerroUI motion, compiled AXAML shells, and `TemplatedControl` chrome on `main`. Linux desktop views from `linux-desktop-shell` still paint pairing digits, LAN pills, guide body, transfer notices, and This Device banners with C# `Md3eResourceLookup.ResolveBrush` `LocalValue`s. `ContextAllLogo` is still a `Viewbox` that builds `Canvas`/`Path` children in its constructor.

This design covers how the remaining work lands on that tree without touching Protocol / Core / Session / Host / Ipc / Windows / Android.

## Goals / Non-Goals

**Goals:**

- Finish the spec’s “no C# `LocalValue` visual overrides” rule on the five shell views.
- Make `ContextAllLogo` a `TemplatedControl` whose geometry lives in `Themes/Md3eControls.axaml`, matching the other MD3E controls.
- Keep appearance overrides in `shell.json` only.

**Non-Goals:**

- Do not restyle Android Compose or WinUI.
- Do not add NuGet packages.
- Do not invent RTT / TLS telemetry (2.A still holds).

## Decisions

### D-A. Logo is a ControlTheme, not a constructor tree

`ContextAllLogo` becomes `TemplatedControl`. Path data stays as public constants so tests can assert Android vector fidelity. The visual tree (gradient clip, four `Path`s) lives in a `ControlTheme` keyed `{x:Type c:ContextAllLogo}`.

Alternative: keep `Viewbox` + constructor `Canvas`. Rejected: it is the last custom control that bypasses `Themes/*.axaml`, and `ApplyTemplate` never rebuilds it.

### D-B. Remaining LocalValue moves to VM booleans + style classes

Do not teach views to look up brushes. Expose state the AXAML already knows how to bind:

| Surface | C# today | After |
| --- | --- | --- |
| Pairing digits | code-behind `Border`s | `ItemsControl` of digit groups on `DevicesViewModel` |
| LAN pill | `Background`/`Foreground` LocalValue | `StatusPill` classes + `BadgeTone` on the existing icon |
| Connection guide | `RebuildGuideBody()` | bound `GuideStepItem` list + `CurrentGuideStep` |
| Transfer notice / text preview | `UpdateNoticeBanner` LocalValue | `StatusPill` classes + bound preview text |
| This Device banners | `UpdateBannerColors` | `StatusPill` / selection classes on the AXAML |

Code-behind may still open dialogs, copy clipboard, and call Portal pickers. It must not set `Background` / `Foreground` / `IndicatorBrush` from C#.

Alternative: keep ResolveBrush but wrap it in helpers. Rejected: that is still LocalValue and still fights ControlTheme.

### D-C. Tests assert the public tree, not private fields

New coverage lives in `ContextAll.App.Tests`: `ContextAllLogo` is `TemplatedControl` and `ApplyTemplate` yields the four paths; Devices/Connection/Transfers/ThisDevice views bind the states above without LocalValue brushes after `ShowAndLayout`.

## Risks / Trade-offs

- [Guide body lives in `MaterialDialog`] → layout the dialog (open + `ShowAndLayout`) before asserting descendant text.
- [Digit `ItemsControl` vs hand-built boxes] → keep 3+3 grouping and tabular typography in the item template so pairing remains scannable.
- [Theme switch after bind] → style classes + `DynamicResource` follow `ThemeDictionaries`; do not cache `IBrush` in the VM.

## Migration Plan

1. Add failing tests for logo template + remaining view bindings.
2. Implement ControlTheme + VM/AXAML bindings; delete the ResolveBrush painters.
3. Rewrite `docs/specs/md3e-ui.md` only for behavior that tests already cover; link ADR 0007.

Rollback is `git revert` of the follow-up commits; `shell.json` keys stay backward compatible.
