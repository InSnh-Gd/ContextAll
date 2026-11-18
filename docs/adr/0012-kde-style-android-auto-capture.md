# Android 后台自动采集采用 KDE 风格的日志信号 + 透明聚焦读取

状态：已接受（实现完成，真机验收前置）

Android 10+ 之后，普通应用只有在持有输入焦点或充当默认输入法时才能读剪贴板；`READ_CLIPBOARD_IN_BACKGROUND` 是 `signature|role`，普通安装无法获得。原 `clipboard-sync` 规格要求「界面有焦点才读」且禁止为读取而打开透明 Activity，因此手机界面在后台时无法把新复制自动同步。

为满足「后台自动采集」又不改输入法、不请求系统后台读权限，Android 侧采用 KDE Connect 可选模式的机制：

- 前台服务持续观察 `logcat`，把系统对本机（`app.contextall`）记下的「剪贴板访问被拒」当作「别处复制了」的变化信号；日志不携带正文。
- 开关开启且去重窗口通过时才短暂打开一个透明 `ClipboardCaptureActivity`，等它真正取得窗口焦点才读一次当前剪贴板，读后即关，并交给现有 `pushClipboard` 路径。
- 开关默认关闭，只在本机 `SharedPreferences` 持久化；`capturing`（「正在采集」）只有在开启、日志流可达且持有 `READ_LOGS` 时才是真。
- 透明 Activity 不注册输入法；这是规格里「不开透明 Activity」的唯一例外，仅在该可选开关且日志通道真实生效时成立。

`READ_LOGS` 是 `development` 权限，仅调试安装可获得；普通安装拿不到系统拒绝日志时如实显示「未生效」，不伪造采集。时序风险（读取前 A 已被 B 替换）来自平台焦点竞态，本轮不引入重读修复。
