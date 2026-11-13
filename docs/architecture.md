# 模块边界

同一仓库里放 Linux、Windows、Android 三个程序。项目引用如下。

| 项目 | 引用 | 职责 |
| --- | --- | --- |
| `ContextAll.Protocol` | 无 | 纯协议。帧长度常量在这里。没有套接字、文件、HTTP 或界面依赖。 |
| `ContextAll.Core` | Protocol | `Decide`。同一状态和输入得到同一个 `Fin<Transition>`。不执行效果。 |
| `ContextAll.Ipc` | Protocol | 前端用的本地通道客户端。Linux 上是 Unix socket，Windows 上是命名管道。客户端不监听。 |
| `ContextAll.Effects` | Core | 按顺序 await 决策返回的效果委托。底层 QUIC 传输后端抽象（`QuicLink` / `IQuicBackend`）与分段读写。不包含业务协调与配置存储。 |
| `ContextAll.Session` | Core、Effects、Protocol | 跨平台会话引擎（`SessionHost`、`PeerLink`、存储与平台缝隙接口）。AOT 兼容。 |
| `ContextAll.Host` | Core、Ipc、Session | NativeAOT 入口与 Linux 宿主适配器。提供 Unix socket 本地通道监听、CLI 命令调度及 Linux 平台缝隙实现。 |
| `ContextAll.App` | Ipc | Linux 上的 Avalonia 视图。主题是 `CCSWE.Avalonia.Material` 12.x。 |
| `ContextAll.App.Desktop` | App、Ipc | Linux 图形入口，在 Linux 上调用 `UseWayland()`。 |
| `ContextAll.App.Windows` | Ipc | 非打包的 WinUI 3 入口。 |
| `ContextAll.App.Android` | Session、Core、Effects、Protocol | Android 宿主外壳适配器。通过 `LinkService`（`connectedDevice`）前台服务在进程内运行 `SessionHost`，经 `AndroidMsQuicBackend` 驱动 `libmsquic.so` 执行 QUIC 会话传输，并对接 Android NSD、网络筛选与 `MediaStore.Downloads`。 |

`ContextAll.Tests.slnf` 是推送前和 GitHub Actions 跑的套件，包含 `net10.0` 项目（含 `ContextAll.Session`）及其测试，不含 `ContextAll.App.Android` 和 `ContextAll.App.Windows`。`ContextAll.Linux.slnf` 包含 Linux 桌面项目，可在未安装 Android / Windows 工作负载的纯 Linux 环境下编译；`ContextAll.App.Android` 在安装 `.NET Android` 工作负载后独立编译打包（含多架构 `libmsquic.so`）并在模拟器上通过插桩测试验证。

已经有测试的会话行为写在 [局域网会话](specs/lan-session.md)、[剪贴板同步](specs/clipboard-sync.md)、[文件传输](specs/file-transfer.md)、[本地操作](specs/local-operator.md) 和 [成帧通道](specs/framed-channel.md)。帧头常量仍是 `ContextAll.Protocol.FrameLimits` 的 16 字节和 64 KiB。文件分段长度是 65500 字节，好让段号和 SHA-256 一起放进一帧。共享效果的放置见 [ADR 0003](adr/0003-shared-effects.md)，Android QUIC 传输决策见 [ADR 0004](adr/0004-quic-android-transport.md)，会话引擎与底层效果的分层见 [ADR 0005](adr/0005-session-layer-split.md)。
