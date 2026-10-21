# 模块边界

同一仓库里放 Linux、Windows、Android 三个程序。项目引用如下。

| 项目 | 引用 | 职责 |
| --- | --- | --- |
| `ContextAll.Protocol` | 无 | 纯协议。帧长度常量在这里。没有套接字、文件、HTTP 或界面依赖。 |
| `ContextAll.Core` | Protocol | `Decide`。同一状态和输入得到同一个 `Fin<Transition>`。不执行效果。 |
| `ContextAll.Ipc` | Protocol | 前端用的本地通道客户端。Linux 上是 Unix socket，Windows 上是命名管道。客户端不监听。 |
| `ContextAll.Effects` | Core | 按顺序 await 决策返回的效果委托。QUIC 监听也在这里；没有 `libmsquic` 时监听失败，不改用 TCP。 |
| `ContextAll.Host` | Core、Ipc、Effects | NativeAOT 入口。`serve` 监听本地帧，其余命令不启动守护进程。 |
| `ContextAll.App` | Ipc | Linux 上的 Avalonia 视图。主题是 `CCSWE.Avalonia.Material` 12.x。 |
| `ContextAll.App.Desktop` | App、Ipc | Linux 图形入口，在 Linux 上调用 `UseWayland()`。 |
| `ContextAll.App.Windows` | Ipc | 非打包的 WinUI 3 入口。 |
| `ContextAll.App.Android` | Core、Effects | Android 界面。界面进一个方法，该方法进入 `Effects`。 |

`ContextAll.Tests.slnf` 是推送前和 GitHub Actions 跑的套件，包含 `net10.0` 项目及其测试，不含 `ContextAll.App.Android` 和 `ContextAll.App.Windows`。`ContextAll.Linux.slnf` 包含 Linux 桌面项目和 `ContextAll.App.Android`，不含 `ContextAll.App.Windows`。当前环境没有 Android 工作负载，所以这份过滤器还编不过。

已经有测试的会话行为写在 [局域网会话](specs/lan-session.md)、[剪贴板同步](specs/clipboard-sync.md)、[文件传输](specs/file-transfer.md) 和 [本地操作](specs/local-operator.md)。帧头常量仍是 `ContextAll.Protocol.FrameLimits` 的 16 字节和 64 KiB。文件分段长度是 65500 字节，好让段号和 SHA-256 一起放进一帧。共享效果的放置见 [ADR 0003](adr/0003-shared-effects.md)。
