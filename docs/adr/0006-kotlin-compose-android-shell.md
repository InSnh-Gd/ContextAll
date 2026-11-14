# Android 端全面采用 Kotlin Jetpack Compose 外壳并移除旧 .NET UI

状态：已接受

## 背景与决定

初期为了快速验证 `.NET` 会话引擎（`SessionHost`）与 `libmsquic.so` 在移动端的传输可行性，仓库曾维护过基于 `.NET for Android` 的原型外壳 `ContextAll.App.Android`（含基于 `ListView` 的 C# `MainActivity` 及对应 `.csproj`）。随着产品级 Android UI 在 `app/` 模块下基于 Gradle、Kotlin 与 Jetpack Compose（Material 3 Expressive）完成，并经由 `ContextAll.Native`（NativeAOT 编译的 `libcontextall_native.so`）与 `NativeSessionController` / `NativeSessionJni` 完整接入 `SessionHost` 与 `AndroidMsQuicBackend`（`libmsquic.so`），同时维持两条 Android UI 代码路径已无必要且会造成配置、签名与测试基线分裂。

自本决策生效起：

1. **Android 前端统一由 Kotlin + Jetpack Compose（`app/`）承担**：所有界面交互、权限申请、前台服务 `LinkService`（`connectedDevice`）、NSD 服务发现（`LanDiscoveryController`）、媒体下载保存（`MediaStoreTransferSink`）与 P-256 身份密钥证书生成（`NativeCryptoHelper`）均在 Kotlin 侧维护与测试。
2. **协议与会话继续复用 C# / .NET NativeAOT 内核（`ContextAll.Native`）**：`AndroidMsQuicBackend` 已从旧 `.NET Android` 工程迁入 `ContextAll.Native`，与 `ContextAll.Core`、`ContextAll.Protocol`、`ContextAll.Effects`、`ContextAll.Session` 共同编译为原生动态库，确保 Android 与桌面守护进程严格的协议等价性。
3. **移除旧 `.NET for Android` 界面工程 `src/ContextAll.App.Android/`**：删除 C# `MainActivity`、`AndroidSession` 与 `.NET Android` 构建入口，并将 `ContextAll.Tests` / E2E 清单与打包验证直接指向 `app/` 与 `scripts/build-android-native.sh`，使 `.NET` 解决方案（`ContextAll.slnx` / `ContextAll.Linux.slnf` / `ContextAll.Tests.slnf`）不再依赖 `.NET Android` 工作负载。

## 后果

- **收益**：Android 端获得现代原生 Compose UI 与标准 Gradle 构建工具链；Linux 开发者和 CI 在不安装 `.NET Android` 工作负载的情况下即可构建并验证全部 `.NET` 解决方案；Android 原生动态库打包只保留一份由 NDK + `PublishAot` 驱动的 `libcontextall_native.so` 与 `libmsquic.so`。
- **代价与约束**：Kotlin 外壳通过 JNI（`NativeSessionJni`）与 JSON 控制载荷跨越 ART / NativeAOT 边界，需保证 Kotlin 侧 `MiniJson` / `NativeProtocolMapper` 与 `ContextAll.Native` 控制帧 DTO 同步演进，并由 `ContextAll.Native.Tests` 与 `app/src/test/` 双边单元测试共同约束。

