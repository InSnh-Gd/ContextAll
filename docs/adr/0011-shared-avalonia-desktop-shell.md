# Windows 图形外壳与 Linux 共用 Avalonia

状态：已接受

## 背景与决定

Windows 端曾用非打包 WinUI 3 作为独立图形外壳，只引用 `ContextAll.Ipc`，窗口是占位 `ListView`。Linux 端的 MD3E 外壳在 `ContextAll.App`。两套 UI 无法共用页签、控件与测试。

自本决策生效起：

1. **桌面图形外壳只有一份**：`ContextAll.App`（Avalonia 12 + CCSWE Material）同时作为 Linux 与 Windows 的界面。设备 / 连接 / 传输 / 本机页签、MD3E 控件与 ViewModel 不再按操作系统分叉。
2. **Windows 入口是 Avalonia Win32，不再使用 WinUI 3 或 Windows App SDK**。`ContextAll.App.Windows` 目标框架为 `net10.0`，在 Linux 上用普通 .NET SDK 即可编译，不需要 Windows SDK 工作负载。`ContextAll.App.Desktop` 仍是 Wayland / X11 入口。
3. **操作系统差异放在 `IDesktopPlatform` 缝上**：本地偏好路径、守护进程拉起、托盘探针、外观源、单实例。Linux 继续走 XDG / D-Bus / 门户；Windows 走 `%LOCALAPPDATA%\ContextAll\shell.json`、命名管道守护进程、系统浅色/深色。前端仍只引用 `ContextAll.Ipc`，不引用 Core。

## 后果

- **收益**：Linux 上改一处界面，Windows 一起变化；去掉 WinAppSDK 依赖，Linux CI 与本机都能编译 Windows 入口。
- **代价**：Windows 不能再依赖 WinUI 控件。系统标题栏在 Windows 上走 Win32 SSD；GNOME Wayland 仍可能拒绝 SSD，继续用应用内页眉。Android 仍是 Compose，不在此决策内。
