# Linux 外观只读桌面门户

状态：已接受

Linux 界面的明暗跟随会话总线上的 `org.freedesktop.portal.Settings`，总线名是 `org.freedesktop.portal.Desktop`，键是 `org.freedesktop.appearance` 的 `color-scheme`。值 `1` 为暗色。值 `0` 和 `2` 为亮色。读不到门户时，界面保持亮色并继续运行。界面不读取强调色。

GNOME 的 `org.gnome.desktop.interface` 和 KDE 的私有外观键不是后备。这些键随桌面而不同，会让 AppImage 按桌面分成不同行为。门户是这两种桌面已经向工具包发布的那一项设置。

若某个实际为暗色的桌面把门户停在 `0`，并且只通过私有键公布暗色，再重新看这条决定。
