# 图形外壳

对应 `session-protocol` 的 `device-shell`。布局、门户配色、Wayland 检查、Android 清单和 WinUI 工程属性已有测试。

## Linux

窗口最小 480×640。宽度小于 840 时只显示一列：没有选中项时是设备列表，有选中项时是详情。宽度达到 840 时列表和详情并排。

`PortalColorScheme.Variant` 把门户 `color-scheme` 的 `1` 映射为暗色，把 `0`、`2` 和读不到映射为亮色。这个映射不读取强调色。

Linux 入口调用 `UseWayland()`。`WAYLAND_DISPLAY` 为空时向标准错误打印需要 Wayland 的说明并返回 1。`serve` 和命令行不检查 Wayland。

AppImage 的 `AppRun` 在没有参数时启动 `contextall-shell`，参数是 `serve` 时启动 `contextall-host serve`，其他参数原样交给 `contextall-host`。用户单元 `packaging/contextall.service` 的 `Type` 是 `notify`，`WantedBy` 是 `default.target`，`ExecStart` 是 `%h/.local/bin/ContextAll.AppImage serve`。

## Android

Android 外壳由 Kotlin Jetpack Compose 应用（`app/`）与 NativeAOT 会话引擎（`ContextAll.Native` / `libcontextall_native.so`）协同实现：

- `ContextAllViewModel` 协调界面状态（`已发现` / `已配对` / `传输` / `本机`）；在生产配置下由 `NativeSessionController` 通过 `NativeSessionJni` 启动进程内 `.NET SessionHost` 与 `AndroidMsQuicBackend`，并由 `NativeCryptoHelper` 管理 P-256 `device.crt`、`device.key`、`device.spki` 及 PKCS#12 凭证；`InProcessSessionEngine` 保留用于 `DEVELOPER_LAB` 演示与测试。
- `LinkForegroundService`（`app/`）及 `ContextAll.App.Android` 中的 `LinkService` 与 `AndroidManifest.xml` 声明 `connectedDevice` 前台服务类型；`MainActivity`（C# 基线适配器）保留 `ListView` 且不使用 `PopupMenu`，`AndroidSession.Run` 保持对 `Effects` 的入口契约。

## Windows

`ContextAll.App.Windows` 是非打包的 WinUI 3 程序：`WindowsPackageType` 为 `None`，`WindowsAppSDKSelfContained` 为 `true`，`PublishTrimmed` 和 `PublishSingleFile` 为 `false`。它只引用 `ContextAll.Ipc`。`App.xaml` 不设置 `RequestedTheme`，外观跟随系统。窗口里是一个 `ListView`。

`ContextAll.Linux.slnf` 不含这个项目。本机执行 `dotnet build ContextAll.Linux.slnf` 停在缺少 Android 工作负载（`NETSDK1147`），输出里没有 `ContextAll.App.Windows`。过滤器中的 Linux 桌面项目可以单独编过。
