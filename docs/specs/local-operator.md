# 本地操作

对应 `session-protocol`、`android-transport` 与 `windows-daemon` 的 `local-operator`。身份、设置、本地帧、命令行、Windows 守护进程与 Android 进程内宿主均有测试。

## 身份和设置

Linux 身份目录是 `$XDG_DATA_HOME/contextall/identity`，未设置时用 `~/.local/share/contextall/identity`。目录模式 `0700`。两个文件都缺失时创建 `device.key`（PKCS#8 PEM）和 `device.spki`（DER），文件模式 `0600`。只缺其中一个，或私钥和 SPKI 对不上，启动失败，已有的 `device.key` 保持原字节。

设置文件是 `$XDG_CONFIG_HOME/contextall/config.yaml`，未设置时用 `~/.config/contextall/config.yaml`。文件不存在时写入初始显示名、空的下载目录和空的信任列表。解析失败时进程不采用这份设置，文件字节不变。信任项带设备 id、显示名、SPKI、`ClipboardSync` 和 `AskBeforeSave`。新建信任项的剪贴板同步是关，保存前询问是开。下载目录为空时表示该用户的 `Downloads`。设置文件里没有私钥，也没有发现路径。

Windows 身份、设置与传输目录由 `HostPaths.ForThisPlatform()` 解析为 `%LOCALAPPDATA%\ContextAll\identity\`、`%LOCALAPPDATA%\ContextAll\config.yaml`、`%LOCALAPPDATA%\ContextAll\` 与 `%LOCALAPPDATA%\ContextAll\transfers\`；默认下载目录是外壳 Downloads 已知文件夹，解析失败时回退到用户配置文件下的 `Downloads`（测试：`HostPathsTests`）。

Android 端由 `NativeSessionController` 与 `NativeCryptoHelper` 将身份密钥与 PEM 证书（`device.key`、`device.crt`、`device.spki`）及传输暂存目录放在不随系统备份同步的 `Context.NoBackupFilesDir/identity` 与 `Context.NoBackupFilesDir/transfers`，将设置放在 `Context.FilesDir/config.yaml`；入站文件经 `SessionHost` 校验整文件 SHA-256（Android NativeAOT 下使用 `PlatformCrypto.ManagedSha256`）通过后，由 Kotlin 侧 `MediaStoreTransferSink` 经 `MediaStore.Downloads`（先置 `IsPending = 1` 写入字节，完成后置 `0`）提交到系统下载集合，并清理 `.part` 与 `.meta.json` 暂存文件（测试：`AndroidSeamParityTests.SessionHost_Runs_Over_Custom_QuicBackend_Discovery_And_InboundCommitSink`、`NativeCryptoHelperTest`、`NativeSessionBridgeTests`）。

非交互的 `identity reset` 没有确认标记时返回 1，文件不动。交互时操作者拒绝，同样返回 1。确认之后：密钥可用而设置不可用时只重写设置，密钥不变；其余情况更换密钥、清空信任、保留合法显示名，并删除传输目录里的文件。命令返回 0，不监听。

## 本地套接字与 Android 前台服务

`serve` 在 `$XDG_RUNTIME_DIR/contextall/daemon.sock` 上监听，套接字模式 `0600`，目录模式 `0700`。运行时目录为空，或该路径是文件而不是目录时，`serve` 返回 1，不在 `/tmp` 下建套接字。已经有进程在听时，第二次 `serve` 返回 1，第一次继续接受连接。没人听的残留套接字文件会被删掉再绑定。退出时删除套接字文件。

`device list` 这类命令连不上守护进程时打印守护进程未运行，退出码非 0，不增加 `serve` 的启动次数。

Windows `serve` 走 `DaemonProcess.ServeDefault`：不读取 `XDG_RUNTIME_DIR`，在当前用户 SID 的命名管道 `\\.\pipe\ContextAll\<SID>` 上启动与 Linux 相同的 `LanHost` 会话引擎。管道用 `NamedPipeServerStreamAcl` 授予当前用户完全控制并拒绝 Everyone；允许多个前端同时连接。第二个 `serve` 无法取得 `Local\ContextAll-serve-<SID>` 互斥量时返回 1。客户端连上后先收到类型 80 的快照帧（测试：`WindowsDaemonTests.Windows_serve_starts_without_xdg_variables`、`WindowsDaemonTests.Pipe_daemon_serves_two_clients_the_same_snapshot`、`WindowsDaemonTests.Second_pipe_serve_exits_when_the_name_is_taken`、`WindowsDaemonTests.Pipe_security_grants_the_current_user_and_denies_everyone`）。

桌面客户端发出的帧用任务 1.1 的同一套 16 字节头。连接建立后守护进程先写快照。Linux 套接字与 Windows 管道上类型 64 到 77 的命令都由 `LanHost` 转交 `SessionHost` 处理（其中 `76` `ConnectEndpoint` 在锁外异步发起端点拨号），回复都是类型 96。

Windows 安装时以管理员运行：

```sh
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/windows/install-firewall.ps1
```

该脚本为 UDP 42461（QUIC）和 42462（信标）创建仅 Private 配置文件的入站允许规则。预览可用 `-WhatIf`。守护进程只读取防火墙配置，不在运行时改规则（测试：`WindowsDaemonTests.Firewall_script_declares_private_inbound_udp_rules`、`WindowsDaemonTests.Firewall_script_parses_and_dry_run_lists_both_ports`）。

`ContextAll.Ipc` 提供纯状态规约器 `LocalShellState` / `LocalShellReducer`（处理类型 80–86 帧、实时维护设备/配对/文件提示/传输列表及断线时保留传输历史）与异步自动重连客户端 `LocalDaemonConnection`（Linux Unix 域套接字 / Windows 命名管道，按 `requestId` 关联类型 96 结果、支持并发命令与指数退避重连，且离线时不拉起 `serve`；测试：`LocalShellReducerTests`、`LocalDaemonConnectionTests`）。

Android 清单（`app/src/main/AndroidManifest.xml`）声明网络、Wi-Fi 状态、多播锁、附近 Wi-Fi 设备（`neverForLocation`）、前台服务、`connectedDevice` 前台服务类型与通知权限，并将 `.service.LinkService` 声明为 `exported="false"` 且 `foregroundServiceType="connectedDevice"`（测试：`F10_AndroidPermissionsManifestTests`、`B10_ManifestHardeningTests`、`F11_ForegroundLinkServiceTests`）。Kotlin Compose 应用启动 `LinkService` 前台服务，并由 `NativeSessionController` 持有进程内 `NativeSessionBridge` / `SessionHost` 生命周期；界面不打开本地通道，直接通过 `NativeSessionJni` 提交本地命令并轮询/接收事件回传至 `ContextAllViewModel` 状态流（测试：`F17_InProcessCommandsEventsTests`、`NativeSessionBridgeTests`）。

## 本地帧

类型 64 到 77 都回复类型 96，载荷里有 `requestId`、`ok` 和 `error`。`requestId` 必须是 1 到 64 个小写十六进制字符，否则 `invalid`。重复的 `requestId` 得到 `duplicate-request`。这些检查以及命令类型、显示名、下载目录、端点与剪贴板载荷的校验都在 `LocalOperator` 里。Linux 本地通道、Windows 命名管道（`LanHost.Command`）和 Android JNI（`NativeSessionBridge.SubmitCommandJson`）共用这一模块，不再各自复制一份。`SessionHost.Apply` 对除 `SendFile` 以外的本地命令调用 `LocalOperator.Validate`；`SendFile` 仍按会话里原有顺序处理，未信任先于非文件。`LocalDesk.Handle` 也走同一模块（测试：`LocalOperatorTests`、`LocalDeskTests`、`LocalProtocolExtensionsTests.LanHost_duplicate_and_invalid_request_ids_match_shared_operator`、`NativeSessionBridgeTests.SubmitCommandJson_ValidatesFrameTypesAndDeduplicatesRequestId`）。发送目录得到 `not-a-file`。不存在的下载目录得到 `invalid-path`。不合法的显示名得到 `invalid-name`。类型 76（`ConnectEndpoint`）校验主机名/IP 与 `1..65535` 端口，非法端点返回 `invalid-endpoint`。类型 77（`PushClipboardText`）向已建立互通的在线对端主动推送 UTF-8 纯文本，受 `1 MiB` 上限约束，超限拒绝并返回 `too-large`；超过 `16 KiB` 时由 `LocalDaemonConnection` 自动切分为 `32 KiB` Base64 分片经多帧发送并由 `LanHost` 按连接组装，单帧载荷不超过 `64 KiB`（测试：`LocalDeskTests`、`LocalProtocolExtensionsTests`、`LocalDaemonConnectionTests`）。

快照包含 `localDisplayName`、`downloadDirectory`、`devices`、`pairing`、`filePrompts` 和 `transfers`，并扩展携带 `localDeviceId`、`resolvedDownloadDirectory`、`localAddresses`、`quicPort` 与 `clipboardBackend`；设备项与 `DeviceChanged` 事件（81）扩展包含 `remoteClipboardSync` 与 `endpoint`，传输项与 `TransferProgress`（85）扩展包含 `totalSize`（缺省向后兼容旧解码结果）。每台设备的 presence 是 `offline`、`reachable` 或 `connected`。连接时若已有尚未写入分段的传输，快照里该传输的已完成分段数是 0。

QUIC 上收到类型大于等于 64 的帧时，会话关闭，信任列表不变。

### Linux 桌面通知

Linux 守护进程通过 `ILinuxNotificationSink` 与零反射 `Tmds.DBus.Protocol` 实现的 `DbusNotificationSink` 连接会话总线 `org.freedesktop.Notifications`（无头主机缺少会话总线时安全降级）：收到配对请求（帧 82）时弹出包含对端显示名与 6 位确认码的通知，启用 `askBeforeSave` 收到文件请求（帧 84）时弹出包含文件名与大小的通知，提供 `default`（向 `$XDG_RUNTIME_DIR/contextall/shell.sock` 发送 `ACTIVATE` 唤起界面而不裁决请求）、`accept` 与 `reject` 动作。动作响应与图形界面/命令行共享 `SessionHost.Gate` 下的先到先得裁决；当 GUI/CLI 先做出决定或配对进入信任状态时自动关闭对应通知。用户关闭或超时撤回通知（`NotificationClosed`）不会自动拒绝，提示继续挂起；本机与对端剪贴板同步从不触发桌面通知（测试：`LinuxNotificationTests`）。

## 命令行

子命令有 `device list`、`pair <device-id>`、`connect <endpoint> [alias]`（支持 IPv4 与 `[IPv6]:port` 端点直连）、`clipboard <device-id> <switch>`、`ask-before-save <device-id> <switch>`、`file send <device-id> <path>` 和 `identity reset --yes`。非终端下缺少必填参数时打印用法并返回非 0。没有子命令且标准输入是终端时才打开 Spectre.Console 菜单，菜单项包含设备列表、配对、端点直连（`Connect endpoint`）、剪贴板、保存前询问和发送文件（测试：`LocalProtocolExtensionsTests`、`IdentityAndDaemonTests`）。带了子命令时不进入菜单。
