# Spec Delta

## Purpose

Holds the Linux, Windows, and Android programs in one repository and publishes that repository under the MIT license.

## ADDED Requirements

### Requirement: One repository for every platform
The source for the Linux, Windows, and Android programs SHALL live in one git repository. A platform project MUST NOT be split into a separate product repository.

#### Scenario: A platform project is added
- **WHEN** a project for Linux, Windows, or Android is added
- **THEN** that project is added to this repository

### Requirement: Linux build excludes the Windows project
Building the Linux programs on Linux MUST NOT require `ContextAll.App.Windows` or the Windows SDK to compile.

#### Scenario: Build on Linux
- **WHEN** the Linux programs are built on Linux
- **THEN** the build completes without compiling the WinUI project and without the Windows SDK

### Requirement: MIT license
The repository SHALL include the MIT license at its root. The terms MUST be the MIT license.

#### Scenario: License is present
- **WHEN** a reader opens the license at the repository root
- **THEN** the text is the MIT license
