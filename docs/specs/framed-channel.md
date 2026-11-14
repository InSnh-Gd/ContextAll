# 成帧通道

对应 `android-transport` 的 `framed-channel`。帧头、优先级、QUIC 单流与 Android 进程内会话均有测试。

## 帧头

每帧以 16 字节开头：大端载荷长度、类型、优先级、大端标志、大端流 id，以及必须为 0 的保留字段。载荷是头后面的字节，最长 64 KiB。保留字段不是 0，或载荷长度超过 64 KiB 时，解码返回失败值，不抛异常。优先级 0 最高，3 最低。队列里同时有优先级 0 和 3 时，先写出 0。

流 id 0 是会话，1 是同步。文件和剪贴板分块从 2 起分配，同一条连接上不重复使用。新连接再从 2 开始。类型 18 是剪贴板分块。类型 64 及以上是本地命令和事件。QUIC 上收到这种帧时会话关闭，信任项不变。

本地通道的类型判断集中在一处。前端发来未知的本地类型时，只关闭这个前端；同一守护进程上的其他前端继续工作。

写线程有排空阶段。解绑时先写出并刷新 `UNPAIR`，队列写完后再关闭连接，对端能在读循环结束前收到这一帧。

## QUIC

ALPN 是 `contextall`，端口是 42461。`ListenerEndpoints` 把信标地址收成不重复的端点，每个地址一个；IPv4 和 IPv6 都收，`fe80::1%2` 的接口 scope id 保留到端点。监听绑在这些地址上，不绑 `0.0.0.0`。没有可用的本机地址时，守护进程打印 `quic-skipped no-beacon-address`。

发起方打开一条双向流，接收方接受这一条，不再另开应用流。测试在回环地址上完成握手，协商到的 ALPN 是 `contextall`，随后两边都等不到第二条流。`QuicLink` 通过 `IQuicBackend` / `IPortableQuicBackend` 访问底层 QUIC 实现：桌面端默认使用 `System.Net.Quic`（`QuicListener.IsSupported`），Android 端在 `ContextAll.Native` 内由 `AndroidMsQuicBackend` 直接经 P/Invoke 驱动 APK 打包的原生 `libmsquic.so` 完成双向 ECDSA P-256 证书校验与单双向流读写（测试：`NativeSessionBridgeTests`、`AndroidSeamParityTests.SessionHost_Runs_Over_Custom_QuicBackend_Discovery_And_InboundCommitSink`）。后端不可用时，`Listen` 抛出 `quic-unsupported`，不改走 TCP、WebSocket 或其他非 QUIC 传输（测试：`B02_QuicUnsupportedNoFallbackTests.Quic_Throws_QuicUnsupported_When_Not_Supported`）。

空闲超时 60 秒，保活间隔 15 秒。

## 本地通道和 Android

Linux 本地通道是 `$XDG_RUNTIME_DIR/contextall/daemon.sock`，Windows 是当前用户的命名管道。两边都用上面的 16 字节头。套接字权限、第二个 `serve` 和命令回复见 [本地操作](local-operator.md)。

Android 不打开本地通道，也不为配对会话提供非 QUIC 降级路径：`NativeSessionController` 通过 `NativeSessionJni` 在应用进程内直接驱动由 `ContextAll.Native` 托管的 `SessionHost` 会话引擎（覆盖发现、配对、QUIC 会话、传输与本地命令处理），复用同一套 `FrameCodec` 与 `ControlJson` 编解码器（测试：`F16_InProcessQuicTransportTests`、`NativeSessionBridgeTests`、`NativeProtocolMapperTest`）。操作者在 Kotlin Compose 界面提交配对决策时，命令直接送入进程内 `NativeSessionBridge` / `SessionHost`，产生的设备状态事件直接回调至 `ContextAllViewModel` 界面层，全程不打开本地套接字（测试：`F17_InProcessCommandsEventsTests`、`NativeSessionBridgeTests`）。Android 与 Linux 桌面守护进程配对通信时，复用完全一致的 16 字节帧头、单双向 QUIC 流与分段传输状态机。
