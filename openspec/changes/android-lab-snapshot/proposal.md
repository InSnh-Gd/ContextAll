# Proposal

## Why

Android 的 `DEVELOPER_LAB` 用一份 Kotlin `InProcessSessionEngine` 再实现一遍会话规则，并在 `libcontextall_native.so` 加载失败时顶替生产路径。会话工作已经在 `SessionHost` 里；这份克隆让配对、传输和剪贴板要改两处，也让操作者在 native 不可用时看到假会话。

## What Changes

- 生产路径只走 `NativeSessionController` → JNI → `SessionHost`。native 加载失败时停留在不可用快照，不启动实验室。
- `DEVELOPER_LAB` 换成 `LabSnapshot`：向 Shell 提供罐头 `SessionSnapshotUi`，本地命令只打显式快照补丁，入站演示走 `simulate*`。
- ViewModel 通过 `ShellOperator` 打 Native 与实验室两个 adapter；页签、主题、选中设备、前台服务和权限由 ViewModel 盖到快照上。
- 进入实验室时停止 `SessionHost`；切回消费者且 `LinkService` 在跑时再启动。
- 删除 `InProcessSessionEngine`。Kotlin 单测不再断言会话规则（去重、身份、配对状态机）。
- 删除无引用的 `ScenarioLabBottomSheet`。

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `device-shell`: Android 实验室是罐头快照，不是会话引擎；消费者路径在 native 不可用时不得回退到实验室。

## Impact

- `app/src/main/java/app/contextall/`（`ShellOperator`、`lab/LabSnapshot`、`ContextAllViewModel`、`NativeSessionController`）
- 删除 `core/InProcessSessionEngine.kt` 与 `ThisDeviceAndLabScreens.kt` 中的死代码
- `app/src/test/` 用 `LabSnapshot` 测试替换引擎测试
- `docs/specs/device-shell.md`、`docs/adr/0009-android-lab-is-canned-snapshots.md`、`docs/README.md`
