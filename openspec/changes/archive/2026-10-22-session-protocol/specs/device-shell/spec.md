# Spec Delta

## Purpose

Gives the operator a graphical view of devices, pairing, and transfer settings on Windows, Linux, and Android, and delivers the Linux shell as a Wayland application.

## ADDED Requirements

### Requirement: Same local state on every shell
The Windows and Linux shells SHALL show the discovered and paired devices, and SHALL let the operator pair, unpair, change the local display name, change per-peer clipboard enablement, change per-peer ask-before-save, change the download directory, and send one file. Those shells MUST use the local daemon's state. The Android shell MUST use the state of the service running in that app.

#### Scenario: Switch changed in the shell
- **WHEN** the operator turns on clipboard sync for a peer in the Windows or Linux shell
- **THEN** the daemon records that enablement and the CLI reports the same value

#### Scenario: Android uses the in-app service
- **WHEN** the Android shell is open
- **THEN** it shows the state of the service running in that app, and clipboard and file actions go through that service

### Requirement: Layout follows available width
The shell SHALL change its layout as the available width changes, so a narrow surface shows one primary column and a wide surface can show the device list beside the selected device. This requirement does not fix the widths at which the layout changes or the desktop window's minimum size.

#### Scenario: Narrow surface
- **WHEN** the shell has a narrow width
- **THEN** the operator sees one primary column and can move between the device list and the selected device

#### Scenario: Wide surface
- **WHEN** the shell has a wide width
- **THEN** the operator can see the device list and the selected device at the same time

### Requirement: Android choices stay on the page
On Android, the shell SHALL present device and settings choices on the current page. It MUST NOT require a floating popup menu to select a device.

#### Scenario: Choose a device on Android
- **WHEN** the operator selects a device on Android
- **THEN** the choice is made from content on the current page

### Requirement: Appearance follows the system
The shell SHALL follow the operating system light or dark appearance. On Linux it MUST use only the desktop portal `color-scheme` value: `1` means dark, and `0` or `2` means light. If that portal setting is unavailable, the Linux shell MUST use light and MUST remain usable. The shell MUST NOT follow a separate accent color. On Windows the shell MUST follow the system theme. On Android the shell MUST follow the system night mode.

#### Scenario: Linux portal reports dark
- **WHEN** the Linux desktop portal `color-scheme` value is `1`
- **THEN** the Linux shell uses a dark appearance

#### Scenario: Linux portal reports no preference or light
- **WHEN** the Linux desktop portal `color-scheme` value is `0` or `2`
- **THEN** the Linux shell uses a light appearance

#### Scenario: Linux portal is missing
- **WHEN** the Linux desktop portal setting cannot be read
- **THEN** the Linux shell uses a light appearance and continues to run

#### Scenario: System appearance changes
- **WHEN** the operator changes the system light or dark appearance while the shell is open
- **THEN** the shell updates to match it

### Requirement: Linux shell is Wayland-native
The Linux graphical shell SHALL run as a native Wayland client. It MUST NOT use XWayland. When started in a pure X11 session, it MUST tell the operator that Wayland is required and MUST exit. The daemon and the CLI MUST still be startable without a Wayland session.

#### Scenario: Wayland session
- **WHEN** the operator starts the Linux shell in a Wayland session
- **THEN** the shell connects to Wayland directly and opens its window

#### Scenario: X11 session
- **WHEN** the operator starts the Linux shell in a pure X11 session
- **THEN** the shell reports that Wayland is required and exits

#### Scenario: Daemon without Wayland
- **WHEN** the operator starts the daemon or a CLI subcommand with no Wayland session
- **THEN** that command runs without requiring Wayland

### Requirement: Linux AppImage entry points
The supported Linux distribution SHALL be a single AppImage containing the shell, the CLI, and the daemon. Starting it with no arguments MUST open the shell. Starting it with a serve argument MUST start the daemon. Any other arguments MUST be handled as CLI arguments. The user service MUST start the daemon through that serve entry.

#### Scenario: AppImage with no arguments
- **WHEN** the operator runs the AppImage with no arguments in a Wayland session
- **THEN** the graphical shell opens

#### Scenario: AppImage serve
- **WHEN** the user service starts the AppImage with the serve argument
- **THEN** the daemon runs in the user session

#### Scenario: AppImage CLI arguments
- **WHEN** the operator runs the AppImage with a CLI subcommand
- **THEN** the AppImage runs that subcommand and does not open the shell
