# Android 端采用 QUIC 且不设降级传输

状态：已接受

Android 端在应用进程内直接运行会话引擎 `SessionHost`，对端间配对与文件传输统一运行在 QUIC 协议之上，端口为 42461，ALPN 为 `contextall`。Android Debug 与 Release APK 均打包原生 `libmsquic.so`（覆盖 `arm64-v8a`、`armeabi-v7a` 与 `x86_64`）。在 Android 运行时初期验证中，托管层 `System.Net.Quic` 的 `QuicListener.IsSupported` 虽返回真且监听器可启动，但在 TLS 证书校验阶段，`System.Net.Quic` 内部链路（`CertificateValidation.BuildChainAndVerifyProperties`）因 P/Invoke 依赖 `libSystem.Security.Cryptography.Native.OpenSsl`，而 Android 平台仅提供 `libSystem.Security.Cryptography.Native.Android`，触发 `DllNotFoundException`。为此，`ContextAll.Effects` 引入轻量级传输后端缝隙 `IQuicBackend`（桌面端默认走 `System.Net.Quic`），Android 端通过 `AndroidMsQuicBackend` 直接经 `Microsoft.Quic` C# P/Invoke 驱动 APK 内置的 `libmsquic.so`：启用 `QUIC_CREDENTIAL_FLAG_USE_PORTABLE_CERTIFICATES` 提取对端 DER 叶子证书，复用 `CertificateChecks.Evaluate` 完成双向 ECDSA P-256 SPKI 核验，保持单双向流、60 秒空闲超时与 15 秒保活间隔。该方案已在 Android 模拟器实机插桩测试与跨平台（Linux 桌面 `System.Net.Quic` 对接 Android `libmsquic.so`）配对及双向文件传输测试中完整跑通。

不为 Android 端提供 TCP、WebSocket、HTTP 或本地套接字等降级传输方案。ContextAll 的会话安全、对端证书互认、帧调度与流状态机建立在 QUIC 的单流与复用语义之上，决策返回的效果在桌面与 Android 端执行完全一致的 16 字节帧协议且不打开 Android 本地通道。若引入 TCP 等降级通道，不仅需要复制一套并行的连接管理、TLS 握手与拆包分帧逻辑，破坏与桌面守护进程间的协议等价性（protocol equivalence），还会成倍增加维护与测试负担。当运行环境报告 `IQuicBackend.IsSupported`（桌面端对应 `QuicListener.IsSupported`）为假时，`QuicLink.Listen` 直接抛出 `quic-unsupported`，绝不静默切换至非 QUIC 传输。

这一决定使桌面守护进程与 Android 应用共享同一套 `ContextAll.Session` 引擎与线级帧协议，代价是 Android 端需维护一层面向 `libmsquic.so` 的 `AndroidMsQuicBackend` 原生回调封装。若未来上游 .NET Android 运行时修复托管 `System.Net.Quic` 对 OpenSSL 符号的耦合，可在不改动上层会话与协议的前提下将 Android 后端切回 `System.Net.Quic`。

## 实现补充（NativeAOT 与零 OpenSSL 证书加载）

Android Compose 外壳（`app/`）通过 `ContextAll.Native`（NativeAOT 编译为 `libcontextall_native.so`）驱动 `SessionHost` 与 `AndroidMsQuicBackend`。由于 `linux-bionic-*` NativeAOT 运行时无法加载系统 `libcrypto.so`，且 `libmsquic.so` 内置 OpenSSL 3 拒绝加载部分 PKCS#12 封装：

- Kotlin 侧 `NativeCryptoHelper` 通过 Android `java.security` 生成并持久化 P-256 身份密钥与自签名证书（`device.key`、`device.crt` PEM 与 `device.spki` DER）。
- `ContextAll.Core` 提供纯托管 FIPS 180-4 SHA-256（`PlatformCrypto` / `ManagedSha256`）与纯 ASN.1 DER 证书校验（`CertificateChecks`），在 Android NativeAOT 下不依赖系统 OpenSSL。
- `AndroidMsQuicBackend` 实现 `IPortableQuicBackend`：在存在 `device.crt` 与 `device.key` 路径时优先以 `QUIC_CREDENTIAL_TYPE.CERTIFICATE_FILE` 直接交给 `libmsquic.so` 加载 PEM 证书与私钥（仅提供字节数组时回退至 `CERTIFICATE_PKCS12`），保持 UDP 42461 上与桌面端一致的 QUIC + mTLS 协议等价性。
