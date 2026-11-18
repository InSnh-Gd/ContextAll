# Tasks

## 1. Theme, motion, and chrome already on main

- [x] 1.1 Sync 48-role HCT palettes into unprefixed CCSWE `ThemeDictionaries` and verify `Md3eThemeAndAppearanceTests` covers Light/Dark keys plus `Md3*` aliases.
- [x] 1.2 Add 30-scale MD3E typography and FerroUI `SpringSolver` / wavy / polygon geometry and verify App motion and typography tests pass.
- [x] 1.3 Convert MD3E chrome (`MaterialRipple`, rail, header, tiles, wavy indicator, dialog, snackbar) to `TemplatedControl` + `Themes/Md3eControls.axaml` and verify `Md3eControlsAndChromeTests` asserts `TemplatedControl` without LocalValue backgrounds.
- [x] 1.4 Persist `AppearanceModeOverride` / `AccentSourceOverride` on `shell.json` and verify `ShellStartupAndPreferencesTests` round-trips follow-system defaults.

## 2. Remaining LocalValue on Linux views

- [x] 2.1 Bind Devices pairing digits and LAN status through ViewModel collections/classes (no C# `ResolveBrush`) and verify an App test that `ShowAndLayout` shows the six pairing digits and LAN `StatusPill` error class.
- [x] 2.2 Bind Connection guide steps through ViewModel items (no `RebuildGuideBody` LocalValue) and verify an App test that opening the guide shows the current step title in the logical tree.
- [x] 2.3 Bind Transfers notice banner and text-draft preview through ViewModel (no `UpdateNoticeBanner` LocalValue) and verify existing Transfers view tests still pass plus notice class binding.
- [x] 2.4 Bind This Device status/service banners through ViewModel classes (no `UpdateBannerColors`) and verify This Device AXAML tests still pass with `StatusPill` classes.

## 3. Brand logo template

- [x] 3.1 Convert `ContextAllLogo` to `TemplatedControl` with a `ControlTheme` in `Md3eControls.axaml` and verify `Md3eBrandAssetTests` still matches Android path data while a chrome test asserts `TemplatedControl` + `ApplyTemplate` yields four paths.

## 4. Capability doc (only after 2.x and 3.1)

- [x] 4.1 Rewrite `docs/specs/md3e-ui.md` in Chinese with only tested behavior and add a synced row in `docs/README.md`; verify every claim names a test or committed control.

## 5. Integration

- [x] 5.1 Run `dotnet test ContextAll.Tests.slnf -c Release` and verify the Linux suite is green.

## 6. Shared-axis section transitions

- [x] 6.1 Animate peer section changes with MD3E Expressive shared-axis X (`DefaultSpatial` 30dp + effects opacity, `ReducedMotion` snap) and verify `SectionTransitionHost_plays_shared_axis_spring_and_snaps_when_reduced_motion`.

## 7. Compact rail

- [x] 7.1 Remove `WideNavigationRail` expand/collapse and keep a fixed 96dp icon rail; verify `WideNavigationRail_stays_compact_with_fab_and_has_no_expand_toggle`.

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
