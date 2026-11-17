# Spec Delta

## MODIFIED Requirements

### Requirement: Clipboard capture follows the platform
On Linux the daemon SHALL read and write the clipboard through `ext-data-control-v1`, or through `wlr-data-control-unstable-v1` when the first protocol is absent, or through the X11 `CLIPBOARD` selection (including chunked `INCR` UTF-8 transfers up to 1 MiB) when neither Wayland data-control protocol is available, `DISPLAY` is non-empty, and X11 client libraries are available at runtime. Headless daemon startup MUST NOT require X11 libraries to be installed, and the daemon MUST NOT require a GNOME Shell extension. When none of those Linux clipboard backends can initialize, clipboard sync on that device MUST stay disabled while pairing and file transfer continue. On Android the daemon MUST read the clipboard only while the interface has input focus, and the background service MUST still place received plain text onto the system clipboard. The Android app MUST NOT register as an input method and MUST NOT open a transparent activity in order to read the clipboard. On Windows the daemon MUST observe plain-text clipboard changes while another application's window is focused.

#### Scenario: Wayland compositor offers neither protocol and X11 is available
- **WHEN** the Linux daemon finds neither Wayland data-control protocol, `DISPLAY` is set, and X11 clipboard libraries are present
- **THEN** the daemon captures and writes UTF-8 plain text through the X11 `CLIPBOARD` selection

#### Scenario: Wayland compositor offers neither protocol
- **WHEN** the Linux daemon finds neither Wayland data-control protocol and cannot initialize an X11 clipboard connection
- **THEN** clipboard sync is disabled and pairing and file transfer still work

#### Scenario: Headless host lacks X11 client libraries
- **WHEN** `serve` starts on a Linux host where X11 shared libraries are not installed
- **THEN** the daemon starts normally without failing dynamic library loading

#### Scenario: Android interface is in the background
- **WHEN** a peer's clipboard text arrives while the Android interface has no input focus
- **THEN** the service places that text on the system clipboard and does not read the clipboard back
