# 成帧通道

对应 `session-protocol` 的 `framed-channel`。帧头、优先级和 QUIC 单流已有测试。

## 帧头

每帧以 16 字节开头：大端载荷长度、类型、优先级、大端标志、大端流 id，以及必须为 0 的保留字段。载荷是头后面的字节，最长 64 KiB。保留字段不是 0，或载荷长度超过 64 KiB 时，解码返回失败值，不抛异常。优先级 0 最高，3 最低。队列里同时有优先级 0 和 3 时，先写出 0。

流 id 0 是会话，1 是同步。文件和剪贴板分块从 2 起分配，同一条连接上不重复使用。新连接再从 2 开始。类型 18 是剪贴板分块。类型 64 及以上是本地命令和事件。QUIC 上收到这种帧时会话关闭，信任项不变。

## QUIC

ALPN 是 `contextall`，端口是 42461。`ListenerEndpoints` 把信标地址收成不重复的端点，每个地址一个。监听绑在这些地址上，不绑 `0.0.0.0`。没有在链路上的本机地址时，守护进程打印 `quic-skipped no-beacon-address` 并返回。

发起方打开一条双向流，接收方接受这一条，不再另开应用流。测试在回环地址上完成握手，协商到的 ALPN 是 `contextall`，随后两边都等不到第二条流。`QuicListener.IsSupported` 为假时，`Listen` 抛出 `quic-unsupported`，不改走 TCP。这台 Windows 上该属性为真，测试走的是监听路径。Android 没有在这台机器上运行，因此没有收到“QUIC 不支持”的报告。

空闲超时 60 秒，保活间隔 15 秒。

## 本地通道和 Android

Linux 本地通道是 `$XDG_RUNTIME_DIR/contextall/daemon.sock`，Windows 是当前用户的命名管道。两边都用上面的 16 字节头。套接字权限、第二个 `serve` 和命令回复见 [本地操作](local-operator.md)。

Android 不打开这条本地通道。`AndroidSession.Run` 在进程内调用 `SharedEffects`。
