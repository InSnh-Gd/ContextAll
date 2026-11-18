# Linux 外观采用 MD3E 并支持门户与桌面强调色回退

状态：已接受（替代 [ADR 0001](0001-linux-color-scheme.md)）

## 背景与决定

ADR 0001 曾规定 Linux 图形外壳只读取桌面门户的 `color-scheme`，不读取强调色，也不回退 GNOME 或 KDE 的私有配置。随着 Android 端确立 Material Design 3 Expressive（MD3E）设计体系（见 [ADR 0006](0006-kotlin-compose-android-shell.md)），Linux 图形外壳（Avalonia 12）同步转向 MD3E 视觉语言，需要以用户桌面强调色作为种子色动态派生容器层级色板，并在窗口元素上与 GTK / Qt 桌面自然融合。

与此同时，XDG Desktop Portal 已在 `org.freedesktop.appearance` 命名空间下标准化了 `accent-color`（sRGB 浮点三元组），GNOME 47+ 与 KDE Plasma 6 均已通过门户发布该值；但在旧版桌面环境或独立窗口管理器中，门户可能不返回有效强调色，且 GNOME Wayland 不支持服务端窗口装饰（SSD）。

自本决策生效起：

1. **MD3E 动态取色与级联回退**：Linux 图形外壳优先通过会话 D-Bus 实时读取并监听 `org.freedesktop.portal.Settings` 的 `color-scheme`（`1` 为暗色，`0`、`2` 或缺失为亮色）与 `accent-color`。当门户不可达或未提供合法 `accent-color` 时，依次尝试读取 KDE `~/.config/kdeglobals`（`AccentColor` / `DecorationFocus`）与 GNOME `org.gnome.desktop.interface` 强调色配置；仍不可得时回退品牌种子色 `#1B5E91`，经 HCT 算法生成完整亮暗色板。
2. **原生桌面元素复用**：文件与目录选择统一走 `org.freedesktop.portal.FileChooser` 以调起宿主机原生 GTK / Qt 对话框；光标主题与 Fcitx5 / IBus 输入法跟随系统；界面正文字体族跟随 `fontconfig` 及桌面默认字体设置（设备 ID 与 6 位配对短码保留等宽字体）。
3. **一体化 CSD 顶栏与可选原生 SSD**：默认采用 MD3E 一体化无边框客户端装饰（CSD，窗口最大化或平铺时自动切换直角），并解析 GNOME `org.gnome.desktop.wm.preferences` 的 `button-layout` 与 KDE `~/.config/kwinrc` 的 `ButtonsOnLeft` / `ButtonsOnRight` 同步窗口控制按钮的左右侧顺序与显隐；同时在前端独立偏好文件 `$XDG_CONFIG_HOME/contextall/shell.json` 中提供「使用系统原生窗口标题栏（SSD）」开关。开启后向合成器请求服务端装饰；仅当窗口管理器实际绘制系统标题栏时才隐藏集成页眉。GNOME / Mutter 等拒绝 SSD 时继续使用应用内页眉，不叠 Avalonia 自绘的 Win32 风格按钮。Avalonia 不能嵌入 GTK `HeaderBar`，Linux 上的「系统标题栏」就是当前窗口管理器的 SSD（KWin、部分 X11 WM），不是 GTK 自绘顶栏。

## 后果

- **收益**：Linux 外壳与 Android 端保持统一的 MD3E 交互质感，同时在 GNOME（GTK）与 KDE（Qt）新旧版本下都能跟随系统强调色、字体、原生文件框及标题栏按钮习惯。
- **代价**：引入了对旧版 KDE / GNOME 外观与窗口按钮配置的只读解析分支；所有桌面配置读取必须通过可注入接口隔离，解析失败时静默降级为默认值而不阻塞界面启动。
