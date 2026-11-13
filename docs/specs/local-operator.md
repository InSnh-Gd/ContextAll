# 本地操作

对应 `session-protocol` 与 `android-transport` 的 `local-operator`。身份、设置、本地帧、命令行与 Android 进程内宿主均有测试。

## 身份和设置

Linux 身份目录是 `$XDG_DATA_HOME/contextall/identity`，未设置时用 `~/.local/share/contextall/identity`。目录模式 `0700`。两个文件都缺失时创建 `device.key`（PKCS#8 PEM）和 `device.spki`（DER），文件模式 `0600`。只缺其中一个，或私钥和 SPKI 对不上，启动失败，已有的 `device.key` 保持原字节。

设置文件是 `$XDG_CONFIG_HOME/contextall/config.yaml`，未设置时用 `~/.config/contextall/config.yaml`。文件不存在时写入初始显示名、空的下载目录和空的信任列表。解析失败时进程不采用这份设置，文件字节不变。信任项带设备 id、显示名、SPKI、`ClipboardSync` 和 `AskBeforeSave`。新建信任项的剪贴板同步是关，保存前询问是开。下载目录为空时表示该用户的 `Downloads`。设置文件里没有私钥，也没有发现路径。

Android 端由 `AndroidHostPaths` 将身份密钥与传输暂存目录放在不随系统备份同步的 `Context.NoBackupFilesDir/identity` 与 `Context.NoBackupFilesDir/transfers`，将设置放在 `Context.FilesDir/config.yaml`；入站文件校验整文件 SHA-256 通过后，由 `AndroidTransferSink` 经 `MediaStore.Downloads`（先置 `IsPending = 1` 写入字节，完成后置 `0`）提交到系统下载集合，并清理 `.part` 与 `.meta.json` 暂存文件（测试：`AndroidInstrumentedTestSuite.AndroidPaths_Store_Identity_Transfers_Settings_And_MediaStore_Downloads`、`AndroidInstrumentedTestSuite.AndroidSession_Completes_Inbound_File_Transfer_To_MediaStore_Downloads_And_Removes_Partial`、`AndroidSeamParityTests.SessionHost_Runs_Over_Custom_QuicBackend_Discovery_And_InboundCommitSink`）。

非交互的 `identity reset` 没有确认标记时返回 1，文件不动。交互时操作者拒绝，同样返回 1。确认之后：密钥可用而设置不可用时只重写设置，密钥不变；其余情况更换密钥、清空信任、保留合法显示名，并删除传输目录里的文件。命令返回 0，不监听。

## 本地套接字与 Android 前台服务

`serve` 在 `$XDG_RUNTIME_DIR/contextall/daemon.sock` 上监听，套接字模式 `0600`，目录模式 `0700`。运行时目录为空，或该路径是文件而不是目录时，`serve` 返回 1，不在 `/tmp` 下建套接字。已经有进程在听时，第二次 `serve` 返回 1，第一次继续接受连接。没人听的残留套接字文件会被删掉再绑定。退出时删除套接字文件。

`device list` 这类命令连不上守护进程时打印守护进程未运行，退出码非 0，不增加 `serve` 的启动次数。

Windows 管道名是 `\\.\pipe\ContextAll\<用户 SID>`。`ServePipe` 用最大实例数 1 监听 `ContextAll\<SID>`。第二个同名监听返回 1。客户端连上后先收到类型 80 的快照帧。

桌面客户端发出的帧用任务 1.1 的同一套 16 字节头。连接建立后守护进程先写快照。Linux 套接字上类型 64 到 75 的命令由 `LanHost` 转交 `SessionHost` 处理，Windows 管道上由 `LocalDesk` 处理，回复都是类型 96。

Android 清单声明网络、Wi-Fi 状态、多播锁、附近 Wi-Fi 设备（`neverForLocation`）、前台服务、`connectedDevice` 前台服务类型与通知权限，并将 `LinkService` 声明为 `exported="false"` 且 `foregroundServiceType="connectedDevice"`（测试：`F10_AndroidPermissionsManifestTests`、`B10_ManifestHardeningTests`）。`MainActivity` 启动 `LinkService` 前台服务，由服务持有进程内 `AndroidSession` 与 `SessionHost` 生命周期并发布常驻通知；界面不打开本地通道，直接通过 `AndroidSession.SubmitCommand` / `SubmitFrame` 提交本地命令并在主线程订阅 `Published` 事件接收快照、配对提示与设备变更（测试：`AndroidInstrumentedTestSuite.LinkService_Starts_Foreground_And_Reports_Running_Session`、`AndroidInstrumentedTestSuite.AndroidSession_Delivers_InProcess_Pairing_Decision_And_Device_Change_To_Ui`、`F17_InProcessCommandsEventsTests`）。

## 本地帧

类型 64 到 75 都回复类型 96，载荷里有 `requestId`、`ok` 和 `error`。重复的 `requestId` 得到 `duplicate-request`。发送目录得到 `not-a-file`。不存在的下载目录得到 `invalid-path`。不合法的显示名得到 `invalid-name`。

快照包含 `localDisplayName`、`downloadDirectory`、`devices`、`pairing`、`filePrompts` 和 `transfers`。每台设备的 presence 是 `offline`、`reachable` 或 `connected`。连接时若已有尚未写入分段的传输，快照里该传输的已完成分段数是 0。

QUIC 上收到类型大于等于 64 的帧时，会话关闭，信任列表不变。

## 命令行

子命令有 `device list`、`pair <device-id>`、`clipboard <device-id> <switch>`、`ask-before-save <device-id> <switch>`、`file send <device-id> <path>` 和 `identity reset --yes`。非终端下缺少必填参数时打印用法并返回非 0。没有子命令且标准输入是终端时才打开 Spectre.Console 菜单，菜单项是设备列表、配对、剪贴板、保存前询问和发送文件。带了子命令时不进入菜单。
