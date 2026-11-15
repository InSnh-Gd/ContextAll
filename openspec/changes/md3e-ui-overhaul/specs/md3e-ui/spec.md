# MD3E UI Overhaul Specification

## ADDED Requirements

### Requirement: 48-Role HCT Expressive ThemeDictionaries Synchronization
`ContextAll.App` SHALL generate full 48-role HCT Expressive Light and Dark palettes (preserving the desktop primary hue and using Light-mode Tone 30 container foregrounds) and atomically populate `MaterialTheme.Resources.ThemeDictionaries["Light"]` and `["Dark"]` with all unprefixed `CCSWE.Avalonia.Material` color/brush keys as well as state-layer brushes and backward-compatible `Md3*` aliases.

### Requirement: Compiled AXAML Views and TemplatedControls
All five shell views (`ShellRootView`, `DevicesView`, `ConnectionView`, `TransfersView`, `ThisDeviceView`) SHALL be authored as compiled `.axaml` (`x:DataType`) without imperative C# `LocalValue` visual overrides, and custom MD3E controls SHALL be implemented as `TemplatedControl`s styled via `Themes/*.axaml`.

### Requirement: FerroUI Motion, Ripple, Wavy Progress, and Morphing Loader
`ContextAll.App` SHALL provide dual-track `MotionScheme.Expressive` damped spring physics (`SpringSolver` with velocity handoff and `ReducedMotion`), bounded/unbounded `MaterialRipple`, `LinearWavyProgressIndicator`, and 7-polygon `ContainedLoadingIndicator`.

### Requirement: Dual-Sheet Workspace, Clean Header with Real Brand Logo, and Real-IPC Device Alignment
The shell SHALL use a borderless `SurfaceContainer` frame and `48dp` `IntegratedHeaderBar` showing the real `ContextAllLogo` + section title (without center telemetry pill), wrapping `28dp` rounded inner sheets (`g1-expressive-studio` and `g2-split-workbench` for `>= 840`; drill-in + `HorizontalFloatingToolbar` for `< 840`), global + detail broadcast-clarified clipboard push (`1.A`), real peer `DeviceSnapshotPayload` + `LocalShellState.History` details (`2.A`), and dynamic Trusted/Discovered grouping with a unified organic empty state (`3.A`).

### Requirement: Lightweight Appearance Override Persistence
`ShellPreferences` in `shell.json` (`0600`) SHALL support optional nullable `AppearanceModeOverride` (`FollowSystem`/`Light`/`Dark`) and `AccentSourceOverride` (`FollowSystem`/`BrandBlue`/preset), defaulting to following desktop Portal/GNOME/KDE settings while maintaining full backward compatibility.
