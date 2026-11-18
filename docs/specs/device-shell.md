# 图形外壳

对应 `linux-desktop-shell`、`session-protocol` 和 `polish-desktop-md3e-layout` 的 `device-shell`。以下仅描述仓库内已经实现并有通过测试证明的行为。

## Linux

- **显示后端与单实例**：`LinuxDisplaySelector` 与 `Program.Main` 在 `WAYLAND_DISPLAY` 非空时直接启用原生 Wayland，否则当 `DISPLAY` 非空时回退到 X11，两者均为空时向标准错误打印提示并退出码 1；守护进程 `serve` 与 CLI 子命令无需图形显示（见 `ShellStartupAndPreferencesTests`、`ShellRulesTests`）。第二个图形实例通过 `$XDG_RUNTIME_DIR/contextall/shell.sock`（权限 `0600`）唤醒已运行窗口并退出 `0`（见 `ShellStartupAndPreferencesTests.SingleInstanceShell_second_launch_activates_existing_instance_and_recovers_stale_socket`）。UI 偏好独立保存在 `$XDG_CONFIG_HOME/contextall/shell.json`（权限 `0600`），不写入 `config.yaml`。
- **响应式布局与 MD3E 外观**：窗口最小尺寸 480×640 逻辑像素。宽度小于 840 时显示单栏与底部浮动胶囊导航（`ExpressiveFloatingNavDock`），选中设备后切入详情页；宽度达到 840 时显示左侧固定 96dp 导航栏（`WideNavigationRail`）并并排呈现设备列表与详情。`HctColorScheme` 与 `LinuxAppearanceWatcher` 基于 `org.freedesktop.portal.Settings`（`color-scheme` 与 `accent-color` 实时监听）计算 CAM16/HCT 亮/暗色调并确保文字/容器对比度满足 WCAG AA（>= 4.5:1）；缺少门户时按 `kdeglobals` → GNOME `gsettings` → 默认品牌色 `#1B5E91` 回退，并将系统无衬线 UI 字体与等宽字体（设备编号、6 位配对短码、`IP:端口`）分离渲染（见 `Md3eThemeAndAppearanceTests`、`Md3eControlsAndChromeTests`）。保留 `PortalColorScheme.Variant` 将门户 `1` 映射为暗色、`0`/`2`/缺失映射为亮色。
- **MD3E 控件与自适应窗口框**：实现了 `ExpressiveGroupTile`（28px/6px 组位圆角及悬停/按压/选中形变）、`ExpressiveSwitch`（`✓`/`✕` 拇指字形）、连接式 `ExpressiveButtonGroup` 与带 4px 物理间隙的 `ExpressiveGapProgressBar`。集成客户端标题栏 `IntegratedHeaderBar` 通过 `WindowButtonLayoutParser` 跟随 GNOME `button-layout` 与 KDE `kwinrc`（`ButtonsOnLeft`/`ButtonsOnRight`）渲染最小化/最大化/关闭按钮位置及可见性；最大化或平铺时窗口外圆角自动转为直角，并可在「本机」设置切换桌面原生标题栏（SSD，使用 Avalonia 12 `WindowDecorations.Full`）（见 `Md3eControlsAndChromeTests`）。
- **四页签状态驱动视图与有序文件发送**：提供「设备」、「连接」、「传输」、「本机」四个中文页签，所有状态经本地通道与 `daemon.sock` 同步。生产路径只在 `MainWindow` 构造 `LocalDaemonConnection`；`DevicesView`、`TransfersView`、`ConnectionView` 通过构造函数接收已有客户端，无参构造给设计器用 `IdleLocalChannel.Shared`。设备列表与详情、传输队列与历史、连接页的端点与对端一览、以及「连接」页四步使用指南，都由 AXAML `ItemsControl` 或绑定属性接到 ViewModel，不再在 code-behind 里拼视觉树或写画刷（见 `Md3eAxamlViewsTests`、`TransfersAndQueueTests`、`DevicesAndConnectionViewModelTests`）。MD3E 色板、TemplatedControl 与 `shell.json` 外观覆盖见 [Linux MD3E 外壳](md3e-ui.md)：
  - 「设备」与「连接」支持主从列表/钻取、6 位等宽配对短码核对、每设备 `clipboardSync` 与 `askBeforeSave` 开关、二次确认取消配对、命令 `77` 纯文本剪贴板快速推送（拒绝 > 1 MiB）、命令 `76` 手动拨号 `IP:端口` / `[IPv6]:端口`、本机可达地址与剪贴板后端诊断，以及使用指南弹窗（见 `DevicesAndConnectionViewModelTests`）。
  - 「传输」通过桌面 `StorageProvider`（XDG `FileChooser` 门户）或拖放接收多文件，`PerPeerUploadQueue` 为同一目标对端保持最多 1 个并发出站文件并按顺序依次调度，遇到文件夹时直接跳过且不自动打包同时显示中文说明横幅；支持待确认接收卡片、分段进度条与取消以及会话历史「打开所在文件夹」（见 `TransfersAndQueueTests`）。
- **条件系统托盘、引导启动、AppImage 安装与损坏恢复边界**：关闭窗口仅在 `shell.json` 启用托盘且 D-Bus `StatusNotifierWatcher` 存在宿主时隐藏到托盘，否则直接退出进程。`daemon.sock` 不可达时展示手动启动/自启引导卡片而不自动拉起 `serve`；处于 `APPIMAGE` 环境时 `AppImageInstaller` 可将其安装至 `~/.local/bin/ContextAll.AppImage` 并写入 `~/.config/systemd/user/contextall.service`（`Type=notify`、`WantedBy=default.target`）与 `~/.local/share/applications/contextall.desktop`。已连接守护进程期间「本机」不提供任何重置入口；仅当由外壳显式启动 `serve` 因本机身份/配置损坏失败时，才弹出默认焦距在「取消」的重置确认对话框（见 `ThisDeviceTrayAndInstallTests`、`IdentityAndDaemonTests`）。

## 桌面布局与状态

- 「设备」在后台服务已连接、局域网地址可用且没有设备或待确认配对时显示首次连接引导。宽屏在右侧提供同网、打开另一端和核对配对码的说明；窄屏在设备列表内显示说明与手动连接入口。服务未连接时不显示搜索动画，已有设备但未选中时显示选择提示。设备列表只保留一个全局剪贴板推送入口。
- 「连接」和「本机」按同一个 840dp 窗口断点切换。连接页在窄屏先排拨号、再排网络信息，缩放不会清空地址或备注。本机页宽屏按分类编辑，窄屏按原顺序滚动查看五组；缩放不会关闭改名对话框或清空草稿。
- 「传输」的累计发送、累计接收和完成分块／次数显示在传输记录之后，包括没有记录时。待确认接收、正在传输和等待发送的顺序不变。
- 「优先使用系统标题栏」保存的是偏好。说明随当前窗口更新：准备请求时显示待定；结果落定后说明正在使用系统标题栏、应用标题栏，或因桌面未提供系统装饰而回退到应用标题栏。结果不写入 `shell.json`。GNOME/Wayland 的回退已在实际窗口验证；系统装饰成功分支由测试模拟验证。

## Android

Android 外壳完全由 Kotlin Jetpack Compose 应用（`app/`）配合 NativeAOT 会话引擎（`ContextAll.Native` / `libcontextall_native.so`）实现，旧 `.NET for Android` 界面工程已移除（见 [ADR 0006](../adr/0006-kotlin-compose-android-shell.md)）：

- `MainActivity.kt`（Jetpack Compose）将设备列表、配对决策、传输面板与本机设置全部呈现在页面与底部工作表内，不使用浮动 `PopupMenu` 来选择设备。
- `ContextAllViewModel` 协调界面状态（`已发现` / `已配对` / `传输` / `本机`）；生产路径由 `NativeSessionController` 通过 `NativeSessionJni` 启动进程内 `.NET SessionHost` 与 `AndroidMsQuicBackend`，并由 `NativeCryptoHelper` 管理 P-256 `device.crt`、`device.key`、`device.spki` 及 PKCS#12 凭证。`libcontextall_native.so` 加载失败时停留在 native 不可用快照，不启动实验室。`DEVELOPER_LAB` 使用 `LabSnapshot` 罐头快照（`ShellOperator` 与 Native 并列），进入实验室时停止 `SessionHost`；Kotlin 单测覆盖快照形状与补丁表，会话规则仍由 C# 测试约束（见 [ADR 0009](../adr/0009-android-lab-is-canned-snapshots.md)）。
- `LinkService`（`app/src/main/java/app/contextall/service/LinkService.kt`）与 `app/src/main/AndroidManifest.xml` 声明 `connectedDevice` 前台服务类型（`exported="false"`）。

## Windows

`ContextAll.App.Windows` 是 Avalonia Win32 入口（`net10.0`，`UseWin32()`），引用共享的 `ContextAll.App`，没有 WinUI 3 或 Windows App SDK（见 `ShellRulesTests`、`WindowsDesktopShellTests`）。设备 / 连接 / 传输 / 本机与 Linux 是同一套视图。本地通道走当前用户命名管道；UI 偏好写在 `%LOCALAPPDATA%\ContextAll\shell.json`，不写守护进程 `config.yaml`。系统托盘不依赖 StatusNotifierWatcher；首次尚无 `shell.json` 时默认使用系统标题栏。决策见 [ADR 0011](../adr/0011-shared-avalonia-desktop-shell.md)。

`ContextAll.Linux.slnf` 与 `ContextAll.Tests.slnf` 仍不含 `ContextAll.App.Windows`。该项目本身可在 Linux 上用普通 .NET SDK 编译，不需要 Windows SDK。
