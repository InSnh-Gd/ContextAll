# KDE Connect Android 剪贴板调查

核对日期：2026-11-18。Android 稳定发行版为 **1.37.3**（版本代码 `13703`）：[KDE 源码标签](https://invent.kde.org/network/kdeconnect-android/-/tags/v1.37.3)于 11 月 12 日创建；[KDE 官网列出的 F-Droid 渠道](https://f-droid.org/en/packages/org.kde.kdeconnect_tp/#1.37.3)于 11 月 17 日收录并将它标为推荐版。这里不推断 Google Play 的分批上架进度。

## 普通安装怎么用

- **手机 → 电脑：** 配对并启用剪贴板插件后，在手机复制文本，手动选「发送剪贴板」（`Send Clipboard`）。[KDE 用户指南](https://userbase.kde.org/KDEConnect#Clipboard)列出 Android 10 及以上的常驻通知按钮，以及 Android 14 及以上的快捷设置磁贴。`1.37.3` [源码也提供设备页内入口](https://invent.kde.org/network/kdeconnect-android/-/blob/v1.37.3/src/main/java/org/kde/kdeconnect/plugins/clipboard/ClipboardPlugin.kt#L116)。这些入口只发送当时的剪贴板，不会开启持续采集。
- **电脑 → 手机：** 插件收到文本后写入 Android 系统剪贴板，用户可在手机粘贴；这与后台读取手机剪贴板是两件事。KDE 用户指南明确不支持图片剪贴板同步，[该版本的 Android 写入实现](https://invent.kde.org/network/kdeconnect-android/-/blob/v1.37.3/src/main/java/org/kde/kdeconnect/plugins/clipboard/ClipboardListener.kt#L101)也写入纯文本。剪贴板收到内容并不等于已验证 Gboard 面板会显示它。

## 后台自动发送的限制

[Android 官方隐私说明](https://developer.android.com/about/versions/10/privacy/changes#clipboard-data)规定：从 Android 10 起，普通应用只有在当前持有焦点或充当默认输入法时才能读取剪贴板。运行前台服务或允许显示通知，不等于应用窗口持有焦点，也不会授予后台读权限。[AOSP Android 16 权限定义](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/android16-release/core/res/AndroidManifest.xml#8123)将 `READ_CLIPBOARD_IN_BACKGROUND` 标为 `signature|role`，并注明不供第三方应用使用，不是普通应用的运行时授权。

[KDE Connect 1.37.3 源码](https://invent.kde.org/network/kdeconnect-android/-/blob/v1.37.3/src/main/java/org/kde/kdeconnect/plugins/clipboard/ClipboardPlugin.kt#L180)在 Android 10 以下默认支持自动同步；在 Android 10 及以上，普通安装（未做特殊配置）不启用自动发送。其可选自动模式用额外的 `READ_LOGS` 权限观察系统拒绝剪贴板访问的日志，把它当作变化信号。日志不是剪贴板正文；应用随后短暂打开不可见的 Activity，等窗口取得焦点才读取系统剪贴板，读完即关闭。[事件监听](https://invent.kde.org/network/kdeconnect-android/-/blob/v1.37.3/src/main/java/org/kde/kdeconnect/plugins/clipboard/ClipboardListener.kt#L53)和[焦点回调](https://invent.kde.org/network/kdeconnect-android/-/blob/v1.37.3/src/main/java/org/kde/kdeconnect/plugins/clipboard/ClipboardFloatingActivity.kt#L40)印证了这个过程。这不是给普通应用授予 `READ_CLIPBOARD_IN_BACKGROUND`，也不能保证窗口每次都能获得焦点。

取得焦点后读的是当时的当前剪贴板。如果在读取前 B 已替换 A，读取 B 不能找回 A。这个时序风险来自实现本身；尚未验证 KDE Connect 在 NX737J 上是否漏项。[ContextAll 现行 OpenSpec](../../../openspec/specs/clipboard-sync/spec.md#requirement-clipboard-capture-follows-the-platform)要求 Android 仅在界面持有焦点时读取，且禁止为了读取而打开透明 Activity，也不允许注册输入法。KDE Connect 的可选自动模式不符合该规格，不能直接作为 ContextAll 的实现或发布凭据。

## ContextAll 交接与验收

- **真机基线：** [三端设计的 NX737J 实测记录](2026-11-18-shared-clipboard-design.md#2026-11-18nx737j-真机的标准授权路径)显示，Android 16 / API 36 上的 ContextAll 普通调试安装请求标准后台读权限时被系统拒绝：`Permission android.permission.READ_CLIPBOARD_IN_BACKGROUND is managed by role`。该机的相关角色仅限系统应用且对用户不可见。未找到这台设备上既保留原输入法、又允许用户向普通 APK 主动授予持续后台剪贴板读取能力的受支持路径。若找到正式的厂商接口，需记录适用设备、授权方及撤销方式，再做实测。
- **成功条件：** 首次引导默认关闭；用户明确启用且配置真实生效后才显示采集状态。保留原输入法，ContextAll 界面保持后台；在另一应用快速连续复制两段不同的合成普通文本 A、B。已配对并加入同一共享组的桌面须实际收到两条、顺序与来源正确、历史各记录一次。模拟器测试排除宿主机剪贴板共享；不能用直接注入同步事件代替采集，也不能把手动发送、前台补读或电脑到手机收件算作通过。
- **撤销条件：** 关闭开关或撤销授权后，复制第三条 C 不得采集或发给桌面；手机仍可接收并查看已有历史。记录系统版本、镜像或机型、安装类型、授权与撤销结果；只为通过实测的环境声明支持。未通过时维持首版发布门槛。

本调查只核对公开资料、稳定版源码与既有验收记录，未安装 KDE Connect、改动设备或完成端到端验证。
