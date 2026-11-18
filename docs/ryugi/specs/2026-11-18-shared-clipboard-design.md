# 三端共享剪贴板设计

状态：设计已确认，尚未通过发布验收。调查基线 `553fae9`；[讨论记录](http://localhost:27319/document/Um63Z11MUEqKs9yOBsny8)（本地文档 `document/shared-clipboard`）。以下是目标行为，不是当前支持清单。

当前按[执行计划](2026-11-18-shared-clipboard-execution-plan.md)推进。Android 后台自动采集已移出当前开发安排，不阻塞其余任务；暂未通过的发布条件不算验收通过。KDE 方案与真机证据留在[专项交接](2026-11-18-kdeconnect-clipboard-research.md)。

## 首版发布条件

Android 保留用户原有输入法。首次引导可选择后台自动采集，默认关闭，之后可撤销。允许用户在支持的设备上额外配置；普通前台服务、悬浮显示权限、返回应用时补读、手动分享、ADB 直接推文本给同步引擎，均不能替代这个功能。跳过配置不影响接收和历史。只有配置已生效才能显示「正在采集」。

首个验收环境可用 Android Studio 模拟器。关闭宿主与模拟器自带的剪贴板共享；保持 Gboard，ContextAll 界面不在前台。在另一应用连续复制两段不同普通文本，桌面端通过 ContextAll 收到两条，顺序和来源正确，历史各出现一次。关闭采集后复制第三条，不得同步。记录设备、镜像、应用版本、授权与撤销过程。一个镜像的结果不代表所有厂商设备。若没有可交付的配置通过，不发布首版。

### 2026-11-18：普通 APK 采集基线未通过

在独立、空白的 Google Play API 37 x86_64 模拟器中安装已有的 ContextAll 调试 APK（target 36，SHA-256 `18ba8f90b76e5a3163dfe415f4a88811a25e7581ef22632e0c5d889d4c0df0fa`，不是本次编译的产物）。保持默认 Gboard，启动 ContextAll，再打开独立测试应用 `test.contextall.clipwriter`。测试应用没有网络权限，两个按钮只把固定的 A、B 文本写入系统剪贴板并读回核对。没有配对桌面；本次验证的仅是端到端流程的第一个必要条件。

| 观测点 | 实际结果 |
| --- | --- |
| 来源窗口 | 当前焦点为 `test.contextall.clipwriter/.CopyActivity` |
| ContextAll 服务 | `LinkService` 仍是前台服务，`isForeground=true` |
| 来源应用回读 | `ContextAll-Test-A verified=true`；`ContextAll-Test-B verified=true` |
| 系统投递 | 两次写入时 `ClipboardService` 均记录 `Denying clipboard access to app.contextall, application is not in focus nor is it a system service for user 0` |
| 此镜像的权限定义 | `android.permission.READ_CLIPBOARD_IN_BACKGROUND` 为 `signature\|role`，不是用户可通过普通运行时权限对话框授予的权限 |

结论：普通安装加前台服务没有通过采集基线。没有验证获平台授权的其他部署方式，没有证明桌面收到 A/B 或关闭开关后的行为，不能放行发布。后续候选配置必须明确授权方、可安装的设备范围、撤销方式，并重新跑完整端到端验收。

### 2026-11-18：NX737J 真机的标准授权路径

通过 USB ADB 确认 NX737J（Android 16 / API 36）已安装 `app.contextall`（`0.1.0-debug`）。实际执行 `pm grant app.contextall android.permission.READ_CLIPBOARD_IN_BACKGROUND`，退出码为 255，系统返回 `SecurityException: Permission android.permission.READ_CLIPBOARD_IN_BACKGROUND is managed by role`。权限定义为 `signature|role`；该机系统权限控制器的 `xml/roles` 将此权限列在 `SYSTEM_TEXT_INTELLIGENCE` 角色下，且此角色为静态、仅系统应用、对用户不可见。单纯在普通 APK 中声明权限并请求授权不能解决这个问题。

本次只核对授权，没有读取个人剪贴板、变更输入法或测试两条复制的传输。普通授权路径未通过，不能据此放行首版；其他厂商正式授权接口未经验证。

## 同步与历史

- Windows、Linux、Android 都接到真实系统剪贴板。Windows 必须捕获其他窗口中的复制；Linux 保留 Wayland/X11，系统写入成功后再确认。Android 接收不能依赖 Activity 存活。
- 显式加入同一共享组的设备可中转，保留来源与事件序号，不重复应用。只配对未入组的设备不接收组内容。建议首版一个活动组，创建者管理邀请、移除、保留期。启用组模式时提示暂停原有组外逐对剪贴板同步。撤权信息到达即停止收发；不能远程擦除已交付的副本。
- 当前值按混合逻辑时钟排序；并发的不同复制均保留在普通历史。重连补齐入组后仍有效的历史，不逐条重放到系统剪贴板。新成员不追溯入组前内容。
- 普通历史默认从原始复制时间起保留 7 天，可调整。每日清理本地缓存，错过则启动或恢复时补做。到期即停止展示与补发；当前剪贴板、活动读取或可续传任务仍引用的副本暂不回收。不得删除用户另存的文件。
- 只认来源或系统提供的标记，不分析正文；无标记一律按普通内容处理。敏感内容永不进入 ContextAll 历史或离线补发。建议实时发送提供不同步（默认）、每次询问、仅向在线组成员同步；中转不得丢标记。尊重平台标记各自的禁止历史、禁止跨设备等语义。

## 取用入口

三端应用内展示历史和真实来源。历史「复制」只写本机，不触发广播；另设「同步到组」。Windows、Linux 提供悬浮入口。Android 通过快捷设置磁贴、连接通知打开轻量选择器，可选授权使用边缘悬浮钮，无需替换输入法。锁屏不展示正文。

Gboard 独立验收：手机停在聊天输入框，电脑复制普通文本，用户可直接在已启用的 Gboard 剪贴板面板中点选并粘贴。不访问其私有历史，不把系统剪贴板写入成功当作键盘面板验收通过。

## 最后阶段：图片和单文件

文本上限 1 MiB；图片编码后、单文件按字节暂限 10 MiB。超限不覆盖对端，可手动传输。只读取真实图片或文件格式，不把普通路径文本当作可读取文件。发送内容而非来源设备路径。复用文件分段、摘要校验与恢复；先写私有临时文件，校验完整后才能标记可复制。若传输期间已有更新复制，旧内容不能夺回当前剪贴板。

历史索引和按内容摘要保存的原始字节分离，重复文件复用本机副本。三端两两双向验证：截图在目标编辑器粘为图片，PDF 在支持文件粘贴的应用中粘为原文件。Windows 提供本机图像或文件列表，Linux 提供图片或本机文件 URI，Android 提供带临时读取权限的本机内容 URI。原设备离线且接收端重启后，完整副本在保留期内仍能再次复制。之后比较 10、25、50 MiB 的延迟、解码内存、中转流量及缓存占用，验证后再决定提限。
