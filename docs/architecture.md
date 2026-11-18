# 模块边界

同一仓库里放 Linux、Windows、Android 三个程序。项目引用如下。

| 项目 | 引用 | 职责 |
| --- | --- | --- |
| `ContextAll.Protocol` | 无 | 纯协议。帧长度常量在这里。没有套接字、文件、HTTP 或界面依赖。 |
| `ContextAll.Core` | Protocol | `Decide`、纯函数状态转移、剪贴板规范化、UTF-8/DER 证书校验与纯托管 SHA-256（`PlatformCrypto` / `ManagedSha256`）。不执行效果。 |
| `ContextAll.Ipc` | Protocol | 前端用的本地通道客户端。Linux 上是 Unix socket，Windows 上是命名管道。客户端不监听。 |
| `ContextAll.Effects` | Core | 按顺序 await 决策返回的效果委托。底层 QUIC 传输后端抽象（`QuicLink` / `IQuicBackend` / `IPortableQuicBackend` / `PortableCredential`）与分段读写。不包含业务协调与配置存储。 |
| `ContextAll.Session` | Core、Effects、Protocol | 跨平台会话引擎（`SessionHost`、`PeerLink`、存储与平台缝隙接口）。AOT 兼容。 |
| `ContextAll.Host` | Core、Ipc、Session | NativeAOT 入口与 Linux 宿主适配器。提供 Unix socket 本地通道监听、CLI 命令调度及 Linux 平台缝隙实现。 |
| `ContextAll.App` | Ipc | Linux 与 Windows 共用的 Avalonia MD3E 视图。主题是 `CCSWE.Avalonia.Material` 12.x。 |
| `ContextAll.App.Desktop` | App、Ipc | Linux 图形入口，在 Linux 上调用 `UseWayland()` / `UseX11()`。 |
| `ContextAll.App.Windows` | App | Avalonia Win32 入口（`net10.0`，无 WinUI / Windows App SDK）。见 [ADR 0011](adr/0011-shared-avalonia-desktop-shell.md)。 |
| `ContextAll.Native` | Core、Effects、Protocol、Session | Android NativeAOT 动态库（`libcontextall_native.so`），内置 `AndroidMsQuicBackend`（实现 `IPortableQuicBackend` 驱动 `libmsquic.so`），向 Kotlin/JVM 暴露 JNI 入口并以零 OpenSSL 方式协调 `SessionHost`；由 `scripts/build-android-native.sh` 构建并与 `libmsquic.so` 一并打包。 |
| `app/`（Gradle / Kotlin + Jetpack Compose） | `ContextAll.Native` | 唯一的 Android 应用前端外壳、服务宿主与单元测试（Compose UI、`ContextAllViewModel`、`NativeSessionController` / `NativeSessionJni`、`LabSnapshot` 罐头实验室、`NativeCryptoHelper`、`LanDiscoveryController`、`MediaStoreTransferSink`、`LinkService`）。 |

`ContextAll.Tests.slnf` 是推送前和 GitHub Actions 跑的 .NET 核心套件，包含 `net10.0` 项目（含 `ContextAll.Session` 与 `ContextAll.Native`）及其测试（含 `ContextAll.Native.Tests`），不含 `ContextAll.App.Windows`；`ContextAll.Tests.E2E` 通过 `./scripts/test-e2e.sh --all` 运行；`app/` 通过 `./gradlew :app:testDebugUnitTest :app:lintDebug :app:assembleDebug` 验证与打包。`ContextAll.Linux.slnf` 仅含 Linux 桌面与共享原生项目，可在未安装 Android / Windows .NET 工作负载的纯 Linux 环境下编译。`ContextAll.App.Windows` 仍可在 Linux 上单独编译。

已经有测试的会话行为写在 [局域网会话](specs/lan-session.md)、[剪贴板同步](specs/clipboard-sync.md)、[文件传输](specs/file-transfer.md)、[本地操作](specs/local-operator.md) 和 [成帧通道](specs/framed-channel.md)。帧头常量仍是 `ContextAll.Protocol.FrameLimits` 的 16 字节和 64 KiB。文件分段长度是 65500 字节，好让段号和 SHA-256 一起放进一帧。共享效果的放置见 [ADR 0003](adr/0003-shared-effects.md)，Android QUIC 传输决策见 [ADR 0004](adr/0004-quic-android-transport.md)，会话引擎与底层效果的分层见 [ADR 0005](adr/0005-session-layer-split.md)，Kotlin Compose 外壳确立与旧 .NET Android UI 移除见 [ADR 0006](adr/0006-kotlin-compose-android-shell.md)。
