# Linux appearance uses the desktop portal

Status: accepted

The Linux shell follows light and dark from the session bus interface `org.freedesktop.portal.Settings` on `org.freedesktop.portal.Desktop`, key `org.freedesktop.appearance` `color-scheme`. Value `1` is dark. Values `0` and `2` are light. If the portal cannot be read, the shell stays light and keeps running. It does not read an accent color.

GNOME's `org.gnome.desktop.interface` and KDE's private appearance keys are not fallbacks. Those keys differ by desktop and would split the AppImage into per-desktop behavior. The portal is the setting both desktops already publish for toolkits.

Revisit this if the portal stays at `0` on a desktop that is actually dark and only publishes that fact through its private keys.
