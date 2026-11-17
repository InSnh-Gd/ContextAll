# Proposal

## Why

`android-lab-snapshot` 只收掉 Kotlin 会话克隆。仓库里还有三处同样的摩擦：本地操作入口重复、Linux 发现 seam 没接上、Linux Shell 列表仍在 code-behind 里 Rebuild。这些不和实验室同一刀做，但要写进 OpenSpec，避免只停在对话和 `/tmp` 报告里。

## What Changes

- 本地操作：`LocalDesk`、`SessionHost.Apply`、`LanHost.Command`、`NativeSessionBridge.SubmitCommandJson` 收成一个 module；`RequestIdOk` 不再复制三份。Local channel 与 JNI 打同一 interface。
- Linux 发现：要么 `LanHost` 注入 `LinuxDiscovery` 且 `SessionHost` 不再自己开 beacon，要么删除 `LinuxDiscovery` 并写明桌面发现留在 `SessionHost`。
- Linux Shell：`DevicesView` / `TransfersView` / `ConnectionView` 的列表与详情走 AXAML 模板；View 不再 `new LocalDaemonConnection()`，不再直接依赖 Protocol 类型。
- 明确非目标：不把 `SessionHost` / `PeerLink` 拆进仍是计数器 stub 的 `Decision.Decide`；不按行数切开 `AndroidMsQuicBackend`、`MsQuicNative`、`X11Clipboard`、`TransferBook`。

已有变更不要在这里重做：`windows-daemon` 任务 6.2、`linux-desktop-shell` 归档、`md3e-ui-overhaul` 补 design/tasks、Avalonia 12 `SetTextAsync`。

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `local-operator`: 本地命令校验与去重只在一处，供 Local channel 与 Android JNI 共用。
- `lan-session`: Linux 生产路径对 `IDiscovery` 要么接上 `LinuxDiscovery`，要么删除该 adapter。
- `device-shell`: Linux 设备与传输列表由模板绑定 ViewModel 集合，不在 code-behind 里 Rebuild。

## Impact

- `ContextAll.Core/LocalDesk.cs`、`ContextAll.Session/SessionHost.cs`、`ContextAll.Host/LanHost.cs`、`ContextAll.Native/NativeSessionBridge.cs`
- `ContextAll.Host/LinuxDiscovery.cs`、`LanHost.TryCreate`
- `ContextAll.App/Views/DevicesView.axaml(.cs)`、`TransfersView`、`ConnectionView`、对应 ViewModel
- 本变更的任务全部未勾选；实现另开 apply，不要和 `android-lab-snapshot` 混在一次提交里。
