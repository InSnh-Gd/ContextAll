# Design

## Context

Android 10+ 读剪贴板必须持有输入焦点或充当默认输入法；`READ_CLIPBOARD_IN_BACKGROUND` 是 `signature|role`，普通安装无法获得。运行前台服务不等于拿到焦点，也不授予后台读权限。`ContextAllViewModel` 现状用 `ClipboardManager.OnPrimaryClipChangedListener` 在界面有焦点时把复制推给 `NativeSessionController`，接收路径由 `LinkService`（前台服务）承载。本变更只新增「后台采集」机制，不改接收路径。

## Goals / Non-Goals

**Goals:**

- 可选后台自动采集：默认关闭，本机持久化，可在引导或本机设置显式开启与撤销。
- 遵循平台焦点规则：读剪贴板只发生在透明采集 Activity 真正取得窗口焦点之后，读取一次即关闭。
- 用系统对 `app.contextall` 的剪贴板访问拒绝日志作为「别处复制」信号，前台服务观察该日志。
- 只在机制真实生效时显示「正在采集」；关闭或日志不可达时如实标记未采集，不打开透明 Activity。
- 纯逻辑单测覆盖信号解析与去重窗口。

**Non-Goals:**

- 不替代现行聚焦读取或手动发送，不修改其他应用的权限或输入法。
- 不注册为输入法，不请求 `READ_CLIPBOARD_IN_BACKGROUND`。
- 不改接收侧（桌面→手机仍写系统剪贴板）、不引入图片自动同步。
- 不为普通安装伪造生效状态；`READ_LOGS` 不可达时不承诺采集。
- 不承诺每次都能拿到焦点；时序风险（读取前 A 已被 B 替换）由平台焦点竞态决定，不在此改动。

## Decisions

### 1. 信号是「系统对本机的剪贴板访问拒绝日志」

`app.contextall` 在界面后台时，若系统拒绝了某次剪贴板访问，会把 `Denying clipboard access to <package>...` 写进系统日志。KDE Connect 可选模式正是观察这条日志而不用剪贴板正文作为信号（日志不携带正文）。本变更用 `ClipboardDenialSignal.isDenialFor(line, packageName)` 纯函数解析行与包名，只认拒绝标记与本机包名，其它行忽略。

### 2. 去重窗口防止同一次复制反复触发

同一次复制可能产生多条日志或一次焦点取到旧值，因此维护一个冷却窗口：`ClipboardSignalWindow.shouldTrigger(nowMs)` 在两次信号间隔小于 `cooldownMs`（默认 600ms）时拒绝，否则接受并记录。`reset()` 在开关切换时清空，避免旧信号诱发误采。

### 3. 透明采集 Activity 持焦读一次

`ClipboardCaptureActivity` 使用全透明主题、`excludeFromRecents`、`noHistory`。在 `onWindowFocusChanged(true)` 时读 `primaryClip` 的纯文本，非空时发布到进程内 `ClipboardCaptureBus`，随后 `finishAndRemoveTask()`。它不注册输入法；这是「焦点规则」下唯一被允许的读取时机，也是本变更对 `clipboard-sync` 规格透明 Activity 例外的唯一入口。

### 4. 前台服务观察日志，开关决定是否行动

`LinkService`（前台服务）持有 `ClipboardSignalWatcher`：在协程里 `logcat` 逐行回调，若开启且去重窗口通过则启动透明采集 Activity（`FLAG_ACTIVITY_NEW_TASK`，应用上下文）。开关与「正在采集」状态由 `ClipboardAutoCaptureStore`（`SharedPreferences`，默认关）+ `ClipboardAutoCapture` 协调器统一暴露为 `StateFlow`，`LinkService` 与 `ContextAllViewModel` 共享同一来源。`ClipboardCaptureBus.captures` 由 ViewModel 收集并走现有 `pushClipboard` 路径，复用非空与「最近一次远端应用文本」的防回声判断。

### 5. 无效通道如实标记

`READ_LOGS` 是 `signature|privileged|development`，普通安装拿不到系统日志。协调器只在日志监视真正在跑时把 `capturing` 置真；一旦开启但监视退出或从未启动，`capturing` 为假，UI 文案使用「未生效」而非「正在采集」。

## 测试与文档

Kotlin 单测覆盖 `ClipboardDenialSignal`（只认本机拒绝行，忽略其它行）与 `ClipboardSignalWindow`（冷却去重、超时放行、reset 清空）。Android 框架相关（日志、Activity、prefs）无单元测试环境，保留在真机/模拟器验收清单。新增 ADR 0012，`docs/specs/clipboard-sync.md` 与 `docs/README.md` 同步更新。
