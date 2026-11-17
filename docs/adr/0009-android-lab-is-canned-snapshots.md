# Android 实验室是罐头快照，不是会话引擎

状态：已接受

Android 生产路径在进程内运行 `SessionHost`（ADR 0006）。曾经用 Kotlin `InProcessSessionEngine` 再实现一遍配对、传输和剪贴板，并在 `libcontextall_native.so` 加载失败时顶替生产路径。会话规则因此要改两处，操作者也会把假对端当成活会话。

实验室改为 `LabSnapshot`：只向 Shell 提供罐头快照和一张写死的本地命令补丁表。JNI 加载失败时停留在 native 不可用快照，不得回退到实验室。不要再建议第二份 SessionHost。
