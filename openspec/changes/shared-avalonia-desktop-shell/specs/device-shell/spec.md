# Spec Delta

## ADDED Requirements

### Requirement: Windows shell uses the shared desktop UI
The Windows graphical shell SHALL present Devices, Connection, Transfers, and This Device from the same desktop shell as Linux. It MUST talk to the local daemon over the per-user named pipe. It MUST NOT be a WinUI 3 application.

#### Scenario: Windows operator opens the shell
- **WHEN** the operator starts the Windows graphical shell with a reachable local daemon
- **THEN** the window shows the same four sections as the Linux shell and reads daemon state from the named pipe

#### Scenario: Windows project is not WinUI
- **WHEN** a reader inspects `ContextAll.App.Windows`
- **THEN** the project has no WinUI or Windows App SDK dependency and references the shared desktop shell

### Requirement: Windows shell is single-instance
A second Windows graphical launch SHALL activate the existing window and exit zero.

#### Scenario: Second Windows launch
- **WHEN** the Windows shell is already open and the operator launches it again with no arguments
- **THEN** the existing window is raised and the new process exits zero

### Requirement: Windows UI preferences stay out of daemon config
Windows UI preferences SHALL be stored in `%LOCALAPPDATA%\ContextAll\shell.json` and MUST NOT be written to daemon `config.yaml`.

#### Scenario: Windows shell.json location
- **WHEN** the Windows shell saves tray or title-bar preferences
- **THEN** they are written under `%LOCALAPPDATA%\ContextAll\shell.json` and daemon `config.yaml` is unchanged

### Requirement: Windows tray hide does not need StatusNotifierWatcher
When tray residency is enabled on Windows and the platform tray is available, closing the window SHALL hide to the tray. Windows MUST NOT require a D-Bus StatusNotifierWatcher.

#### Scenario: Windows tray enabled
- **WHEN** tray residency is enabled in Windows `shell.json` and the operator closes the window
- **THEN** the process stays running in the tray

## MODIFIED Requirements

### Requirement: Appearance follows the system
The shell SHALL follow the operating system light or dark appearance. On Linux it MUST use the desktop portal `color-scheme` value: `1` means dark, and `0` or `2` means light, with KDE and GNOME fallbacks then light. On Windows it MUST follow the system Apps light/dark setting. Both desktop shells MAY derive a tonal palette from a system accent when one is published, otherwise the brand seed. On Android the shell MUST follow system night mode.

#### Scenario: Linux portal reports dark
- **WHEN** the Linux desktop portal `color-scheme` value is `1`
- **THEN** the Linux shell uses a dark appearance

#### Scenario: Linux portal reports no preference or light
- **WHEN** the Linux desktop portal `color-scheme` value is `0` or `2`
- **THEN** the Linux shell uses a light appearance

#### Scenario: Linux portal is missing
- **WHEN** the Linux desktop portal setting cannot be read
- **THEN** the Linux shell uses a light appearance and continues to run

#### Scenario: Windows system theme is dark
- **WHEN** the Windows shell reads that Apps use a dark theme
- **THEN** the Windows shell uses a dark appearance

#### Scenario: System appearance changes
- **WHEN** the operator changes the system light or dark appearance while the shell is open
- **THEN** the shell updates to match it
