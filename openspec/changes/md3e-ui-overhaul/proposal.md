## Why

The initial Avalonia Linux client shell (`linux-desktop-shell`) referenced `CCSWE.Avalonia.Material 12.1.14`, but imperative C# `LocalValue` overrides, unaligned palette resource keys (`Md3*` vs unprefixed `CCSWE` keys), system-font overwriting, `1px` hard borders, and missing MD3E motion/ripple/wavy primitives prevented it from reaching authentic Material Design 3 Expressive visual quality.

## What Changes

- Rework `ContextAll.App` theming (`Theming/`) around a 48-role HCT Expressive palette + `Md3eThemeAdapter` syncing unprefixed `CCSWE` `ThemeDictionaries["Light"/"Dark"]` + `Plus Jakarta Sans` / `DM Sans` + CJK fallback font stacks and 30-style MD3E typography.
- Add pure C# `Motion/` engine (`SpringSolver` dual-track `MotionScheme.Expressive`, `TopLevel.RequestAnimationFrame` driver, `WavyProgressGeometry`, `PolygonMorphGeometry`).
- Refactor custom controls into `TemplatedControl` + compiled `.axaml` `ControlTheme`s (`MaterialRipple`, `IntegratedHeaderBar`, `WideNavigationRail`, `HorizontalFloatingToolbar`, `ExpressiveGroupTile`, `ExpressiveButtonGroup`, `LinearWavyProgressIndicator`, `ContainedLoadingIndicator`, `MaterialDialog`, `MaterialSnackbarHost`, `ContextAllLogo`).
- Migrate all 5 views (`ShellRootView`, `DevicesView`, `ConnectionView`, `TransfersView`, `ThisDeviceView`) to compiled `.axaml` (`x:DataType`) matching the approved dual-sheet `g1-expressive-studio` & `g2-split-workbench` designs and real IPC alignment decisions (`1.A / 2.A / 3.A`).
- Add `AppearanceOverride` persistence to `shell.json`, real `packaging/appimage/contextall.png` icon generation from Android vector source, and comprehensive tests in `tests/ContextAll.App.Tests`.

## Impact

- Affected project: `src/ContextAll.App`, `packaging/appimage/contextall.png`, `tests/ContextAll.App.Tests`.
- Unchanged by contract: `ContextAll.Protocol`, `ContextAll.Core`, `ContextAll.Session`, `ContextAll.Host`, `ContextAll.Ipc`, Windows/Android apps.
