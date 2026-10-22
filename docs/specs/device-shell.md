# 图形外壳

对应 `session-protocol` 的 `device-shell`。布局、门户配色、Wayland 检查、Android 清单和 WinUI 工程属性已有测试。

## Linux

窗口最小 480×640。宽度小于 840 时只显示一列：没有选中项时是设备列表，有选中项时是详情。宽度达到 840 时列表和详情并排。

`PortalColorScheme.Variant` 把门户 `color-scheme` 的 `1` 映射为暗色，把 `0`、`2` 和读不到映射为亮色。这个映射不读取强调色。

Linux 入口调用 `UseWayland()`。`WAYLAND_DISPLAY` 为空时向标准错误打印需要 Wayland 的说明并返回 1。`serve` 和命令行不检查 Wayland。

AppImage 的 `AppRun` 在没有参数时启动 `contextall-shell`，参数是 `serve` 时启动 `contextall-host serve`，其他参数原样交给 `contextall-host`。用户单元 `packaging/contextall.service` 的 `Type` 是 `notify`，`WantedBy` 是 `default.target`，`ExecStart` 是 `%h/.local/bin/ContextAll.AppImage serve`。

## Android

`MainActivity` 在页面上放 `ListView`，不用 `PopupMenu`。`AndroidManifest.xml` 声明 `connectedDevice`。`LinkService` 的前台服务类型是 `connectedDevice`。界面通过 `AndroidSession.Run` 进入 `Effects`。

## Windows

`ContextAll.App.Windows` 是非打包的 WinUI 3 程序：`WindowsPackageType` 为 `None`，`WindowsAppSDKSelfContained` 为 `true`，`PublishTrimmed` 和 `PublishSingleFile` 为 `false`。它只引用 `ContextAll.Ipc`。`App.xaml` 不设置 `RequestedTheme`，外观跟随系统。窗口里是一个 `ListView`。

`ContextAll.Linux.slnf` 不含这个项目。本机执行 `dotnet build ContextAll.Linux.slnf` 停在缺少 Android 工作负载（`NETSDK1147`），输出里没有 `ContextAll.App.Windows`。过滤器中的 Linux 桌面项目可以单独编过。
