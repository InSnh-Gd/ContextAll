# Proposal

## Why

Android 10+ 之后普通应用只有在持有焦点或充当默认输入法时才能读剪贴板。ContextAll 现行规格要求「界面有焦点才读」且禁止为了读取而打开透明 Activity，因此手机在界面后台时无法把新复制自动同步给桌面。三端共享剪贴板在 Android 侧的采集基线未通过（普通安装 + 前台服务被系统拒绝，`READ_CLIPBOARD_IN_BACKGROUND` 是 `signature|role`）。KDE Connect 的可选自动模式给出了一条不改输入法、不请求系统后台读权限的可行路径，本变更把它接进 ContextAll。

## What Changes

- 新增可选的「后台自动采集」开关，默认关闭，只在本机持久化，可在首次引导或本机设置里显式开启与撤销。
- 开启且 `READ_LOGS`（可调试安装）可达时，前台服务后台持续观察系统对 `app.contextall` 的剪贴板访问拒绝日志，把它当作「别处复制了」的变化信号。
- 收到信号且处于开启状态时，短暂打开一个透明采集 Activity，等它真正取得窗口焦点才读一次系统剪贴板，读后立即关闭，并把读到的纯文本交给现有同步推送路径。
- 关闭开关或日志通道不可达时不打开透明 Activity，并如实显示「正在采集」仅在机制真实生效时成立。
- 修改 `clipboard-sync` 的 Android 平台约束：默认路径仍不读后台、不开透明 Activity、不注册输入法；仅为可选采集模式增加例外与验收场景。

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `clipboard-sync`: Android 可选「后台自动采集」遵循平台焦点规则，用系统拒绝日志作信号、透明 Activity 持焦读取一次、默认关闭且不注册输入法；不生效时如实显示未采集。

## Impact

- `app/src/main/AndroidManifest.xml`：新增 `READ_LOGS` 与透明 `ClipboardCaptureActivity` 注册、透明主题。
- `app/src/main/java/app/contextall/clipboard/`：信号解析、去重窗口、持久化开关、前台日志监视、透明活动与协调器。
- `app/src/main/java/app/contextall/viewmodel/ContextAllViewModel.kt`、`MainActivity.kt`、`ThisDeviceAndLabScreens.kt`、`OnboardingGuideScreen.kt`、`strings.xml`：开关、采集状态与入口。
- `app/src/service/LinkService.kt`：前台服务里启动/停止日志监视。
- `app/src/test/`：新增纯逻辑单测（信号解析、去重窗口）。
- `docs/specs/clipboard-sync.md`、`docs/adr/0012-kde-style-android-auto-capture.md`、`docs/README.md`。
