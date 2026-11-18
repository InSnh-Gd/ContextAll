# 剪贴板同步

对应 `session-protocol` 与 `linux-desktop-shell` 的 `clipboard-sync`。寄存器、分片、`clipboard.json` 与 Linux Wayland/X11 回退均有测试。Linux 守护进程通过 `LinuxClipboardSource`（上报 `ClipboardBackendKind`：`wayland`、`x11` 或 `none`）依次尝试后端：优先通过 `ext-data-control-v1` 读写纯文本，没有该全局对象时使用 `wlr-data-control-unstable-v1`；当两者均不可用且 `DISPLAY` 非空时，通过 `X11Clipboard`（经 `NativeLibrary.TryLoad` 按需加载 `libX11.so.6` 与 `libXfixes.so.3`，不依赖 GNOME Shell 扩展或外部 CLI 工具）监听 `XFixes` `CLIPBOARD` 选区所有权变化并过滤自写回声，使用直传与最长 2 秒块超时的 `INCR` 分片协议读写上限 1 MiB 的 UTF-8 纯文本（忽略图像与文件列表目标；测试：`X11ClipboardTests`）。当无头主机缺少 X11 动态库或所有后端均不可用时，剪贴板保持禁用而不报错，配对与文件传输正常启动。纯文本接受 `text/plain` 和 `text/plain;charset=utf-8`，两者都有时用后者。

## 对端剪贴板

两个守护进程互相信任并把剪贴板同步打开之后，一侧写入的纯文本会出现在另一侧。空字符串会清空另一侧。这次更新不写 `notifications.log`。同一时间本地套接字上的帧类型是 80–86 和 96。

## 时钟和寄存器

混合逻辑时钟按毫秒、计数器、再按 16 字节摘要比较。摘要按无符号大端整数比较，不按设备 id 的 Base32 文本排序。本机下一次发出的时钟严格大于已经发出或应用过的时钟。版本向量里的序号从本机每次文本变更开始计数，和逻辑时钟的计数器不是同一个数。

只同步纯文本，上限 1 MiB。图像和文件列表不产生增量。双方都打开同步时，较新的一侧只发送当前文本。任一侧关闭时仍然交换版本向量，不改对方剪贴板文本。增量只发给直接对端，不转给第三个设备，也不发回起源设备。应用成功的文本不增加通知计数。

重复窗口是内存里的 512 条 `(origin, seq)`。窗口里已有的一对会被丢掉。

## 分片

能放进 64 KiB 的 JSON 增量是一条类型 15，流 id 为 1，优先级 1。更大的文本先发不含正文的增量，写明流 id、字节数和 SHA-256，再按类型 18、优先级 3 发送 `u32` 偏移加上字节。字节数、哈希和 UTF-8 都吻合，并且时钟严格更新时才应用。字节数小于等于 0 或大于 1 MiB 时不分配组装缓冲区，已有文本不变。更新的时钟会丢掉尚未收齐、时钟更旧的组装。

## 重启

状态文件是 `$XDG_STATE_HOME/contextall/clipboard.json`，未设置时用 `~/.local/state/contextall/clipboard.json`。字段有文本、时钟、版本向量、上次发出或应用的时钟、`appliedText` 和序号。没有重复窗口字段。写入用临时文件、刷新、再改名，文件模式 `0600`。

文件缺失时照常开始监听，窗口为空。重启后剪贴板文本和文件里的文本相同，序号不变。文本既不等于已存文本也不等于 `appliedText` 时，序号加一，这是一次新的本地变更。

## Android 后台自动采集

Android 10+ 只有持有焦点或充当默认输入法才能读剪贴板；`READ_CLIPBOARD_IN_BACKGROUND` 是 `signature|role`，普通安装无法获得。为在界面后台采集，Android 侧可选地采用 KDE 风格的机制（ADR 0012），默认关闭，只在本机 `SharedPreferences` 持久化。

前台服务 `LinkService` 持有日志监视器，逐行读取 `logcat`，把系统对本机记下的「剪贴板访问被拒」当作变化信号。只认 `Denying clipboard access to <包名>` 且包名匹配本机的一行（纯函数 `ClipboardDenialSignal`，测试：`ClipboardDenialSignalTest`），其它行忽略。同一信号进入 `ClipboardSignalWindow` 的冷却窗口（默认 600ms），窗口内忽略、超时后放行、切换开关时 `reset()` 清空（测试：`ClipboardSignalWindowTest`）。

开关开启且去重窗口通过时，`ClipboardAutoCapture` 短暂打开透明 `ClipboardCaptureActivity`，等它真正取得窗口焦点才读一次当前剪贴板，非空、未标敏且非本应用写入的文本发布到进程内 `ClipboardCaptureBus`，读后即 `finishAndRemoveTask()`。`LinkService` 收集总线并走现有推送路径；它不注册输入法。

采集只在调试安装中提供。开启后需允许读取系统日志和显示在其他应用上层；Android 13 及以上系统可能另外要求确认一次性日志访问。撤销授权、关闭开关或停止服务均不再读取。正式包不声明这两项权限。

## 共享组附件续传

已入组设备的连接中断时，接收端暂存尚未完整的附件。重连握手携带该附件的身份、哈希和已收字节数；支持续传的发送端只补发剩余字节，旧版本仍可完整重发。只有全部字节与 SHA-256 校验通过才进入历史。未被重连认领的分段最多保留十分钟；结束进程、移除成员或解除配对后不保留。
