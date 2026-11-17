# Linux 守护进程剪贴板支持动态加载 libX11 级联回退

状态：已接受

## 背景与决定

此前 Linux 端仅支持原生 Wayland 会话：图形外壳在 `WAYLAND_DISPLAY` 为空时直接退出，守护进程也仅通过手写 Wayland 线协议（`WaylandWire`）连接 `ext-data-control-v1` 或 `wlr-data-control-unstable-v1`。当扩展支持纯 Xorg 桌面会话，或在尚未暴露上述 `data-control` 扩展的 Wayland 合成器（如旧版 GNOME Mutter）下运行时，仅依赖 Wayland 数据控制协议会导致剪贴板同步处于禁用状态。

自本决策生效起：

1. **窗口后端优先顺序**：Linux 图形外壳在 `WAYLAND_DISPLAY` 非空时默认使用原生 Wayland（`UseWayland`）；仅当 `WAYLAND_DISPLAY` 为空且 `DISPLAY` 非空时回退 X11（`UseX11`）；两者皆空时向标准错误打印说明并返回退出码 `1`。
2. **守护进程剪贴板级联回退**：`LinuxClipboardSource` 优先尝试连接原生 Wayland（`ext-data-control-v1` 优先于 `wlr-data-control-unstable-v1`）。当无 Wayland 会话或合成器未提供这两种协议、且环境变量 `DISPLAY` 非空时，回退至 `X11Clipboard`：通过 `NativeLibrary.TryLoad` 按需动态加载宿主机 `libX11.so.6` 与 `libXfixes.so.3`，监听 `CLIPBOARD` 选择区变更并读写 UTF-8 纯文本（含超过单次属性上限至 1 MiB 的 `INCR` 分块传输）。动态库缺失或连接失败时剪贴板保持禁用，发现、配对与文件传输继续运行。

## 后果

- **收益**：纯 Xorg 桌面与缺少 `data-control` 但具备 XWayland 桥接的 Wayland 桌面均可使用纯文本剪贴板同步；`NativeLibrary.TryLoad` 避免了编译期硬链接 `libX11`，使 NativeAOT 编译的 `ContextAll.Host` 在无图形库的无头环境中仍可直接运行 `serve` 和命令行。
- **代价**：在不支持 `data-control` 且采用按需启动 XWayland 的 Wayland 会话中，剪贴板回退会唤醒 XWayland 进程；X11 原生调用需封装在薄接口后，以便在无真实 X Server 的持续集成环境中覆盖单元测试。
