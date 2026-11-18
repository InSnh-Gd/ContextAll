# Spec Delta

## ADDED Requirements

### Requirement: Windows graphical project builds without Windows SDK
`ContextAll.App.Windows` SHALL target `net10.0` without Windows App SDK. Building that project on Linux MUST succeed with the .NET SDK alone.

#### Scenario: Build Windows graphical project on Linux
- **WHEN** a developer runs `dotnet build` on `ContextAll.App.Windows` on Linux
- **THEN** the build completes without the Windows SDK or Windows App SDK

## MODIFIED Requirements

### Requirement: Linux build excludes the Windows project
Building the Linux programs on Linux MUST NOT require `ContextAll.App.Windows` or the Windows SDK to compile. `ContextAll.Linux.slnf` and `ContextAll.Tests.slnf` MUST omit `ContextAll.App.Windows`.

#### Scenario: Build on Linux
- **WHEN** the Linux programs are built on Linux with `ContextAll.Linux.slnf` or `ContextAll.Tests.slnf`
- **THEN** the build completes without compiling `ContextAll.App.Windows` and without the Windows SDK
