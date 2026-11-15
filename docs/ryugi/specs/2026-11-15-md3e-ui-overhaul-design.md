# ContextAll Linux · MD3E 界面重构设计规格（Written Design）

本文档为 `ContextAll.App`（.NET 10 + Avalonia 12.1.4）基于 `CCSWE.Avalonia.Material 12.1.14` 与 FerroUI MD3E 规范重塑 Linux 桌面界面的完整工程设计。该规格已完成 Builder Check 审阅、设计对齐决策（1.A / 2.A / 3.A）及最终确认，并作为 `ryugi:executing-specs` 的唯一权威规范落盘执行。

## 1. 意图与问题根因（Why）

此前实现的 Linux 客户端虽已在 `ContextAll.App.csproj` 引入 `CCSWE.Avalonia.Material 12.1.14` 并在 `App.axaml` 挂载 `<theme:MaterialTheme />`，但视觉效果远未达到 MD3E 预期，经逐行核查确认根因有三点：

1. **动态调色板键名脱节：** `HctColorScheme.cs` 写入的是自定义前缀键（`Md3PrimaryBrush`、`Md3SurfaceColor`），而 `CCSWE.Avalonia.Material` 在 `ThemeDictionaries["Light"/"Dark"]` 中读取的是无前缀键（`Primary`、`PrimaryContainer`、``SurfaceContainerHigh`` 等），导致系统 Portal/GNOME/KDE 取色完全无法作用于标准 Material 控件。
2. **纯 C# 内联覆盖压死 ControlTheme：** `ShellRootView.cs`、`DevicesView.cs`、`ConnectionView.cs`、`TransfersView.cs`、`ThisDeviceView.cs` 及自定义控件全部在 C# 中直接写入 `LocalValue`（`Background`、`CornerRadius`、`FontSize`），覆盖了 Material 状态层（悬停/聚焦/按压）、浮动标签、图标与排版样式；同时将全局字体覆写为系统单一字体，顶掉了 `CCSWE` 内置的 `Plus Jakarta Sans` 与 `DM Sans`。
3. **硬边割裂与缺少 MD3E 表现力物理反馈：** 顶栏与侧栏保留 `1px` 边框与重复的 `"ContextAll"` 标题，内容区缺少 `SurfaceContainer` 至 `Surface` 的色阶包裹感，交互缺少二阶物理弹簧、有界水波纹（Ripple）、正弦波浪进度条与多边形加载形变。

## 2. 目标、非目标与反目标（Goals / Non-Goals / Anti-Goals）

- **Goals（核心目标）：**
  1. 以 `CCSWE.Avalonia.Material 12.1.14` 为基础控件底座与 Material Symbols（`Material_Icon*`）图标库，叠加对齐 FerroUI（AndroidX `compose.material3:1.5.0-beta02`）的 MD3E 扩展层。
  2. 全部视图与自定义控件迁移为编译型 `.axaml`（`x:DataType`）+ `TemplatedControl` `ControlTheme`，彻底消除 C# 视觉树硬编码。
  3. 实现保真型表现力 48 角色 HCT 动态色彩系统（按 `Light`/`Dark` 变体独立替换）、30 级字阶（含 15 组 `*Emphasized`）、8 级圆角、6 角色双轨物理弹簧、有界 `MaterialRipple`、正弦波浪进度条 `LinearWavyProgressIndicator` 与 7 多边形 `ContainedLoadingIndicator`。
  4. 完整落实已选定的 `g1-expressive-studio` 与 `g2-split-workbench` 双画布布局，并在顶栏替换真实 ContextAll 矢量标志及移除冗余状态胶囊。
- **Non-Goals（明确不做）：** 不改动 `ContextAll.Protocol` 线协议、`ContextAll.Core`、`ContextAll.Session`、`ContextAll.Host` 守护进程、`ContextAll.Ipc` 通道语义、Windows 端（`ContextAll.App.Windows`）与 Android 端（`app/`）；不引入需在线下载的新 NuGet 包（构建环境离线，严格复用已缓存的 `Avalonia 12.1.4`、`CCSWE.Avalonia.Material 12.1.14`、`Tmds.DBus.Protocol 0.94.1`）。
- **Anti-Goals（禁止退化）：** 禁止在 C# 中硬编码 `Background`/`FontSize`/`CornerRadius` 绕过 `ControlTheme`；禁止顶栏与侧栏出现 `1px` 生硬边框、重复标题或状态胶囊；禁止在已连接守护进程时暴露任何重置身份/配置入口；禁止修改或清空宿主机 `~/.local/share/contextall` 或 `~/.config/contextall/config.yaml`；禁止在 `src/` 中出现后缀 `!` 空值抑制符或阻塞式 `.Result/.Wait()`。

## 3. 工程约束（Constraints）

- **编译与静态检查：** 必须通过 `dotnet build ContextAll.Linux.slnf -c Release -p:NuGetAudit=false -warnaserror`（0 警告 0 错误）；`src/` 下 0 个后缀 `!`；保留 `AppImage` 打包与 `scripts/run-linux-dev.sh` 兼容性。
- **测试集保留：** 保持并扩展 `tests/ContextAll.App.Tests`（继续使用 `[Collection("AvaloniaUI")]` 串行隔离）；不放松对比度（`>= 4.5:1`）、`ShellLayout`（`480x640` 最小尺寸、`840` 双栏断点）、`WindowButtonLayoutParser`、单实例 `shell.sock`、托盘退出策略及排队顺序等核心断言。
- **持久化隔离：** 所有新增桌面外观覆盖设置仅持久化至 `$XDG_CONFIG_HOME/contextall/shell.json`（权限 `0600`），绝不写入守护进程 `config.yaml`。

## 4. 已选定视觉方向与画板决议（Interface Craft & Artboard Decisions）

### Interface Craft 五维视觉方向

- **Product：** 面向 Linux（Wayland / X11）局域网操作者的零账号设备互联外壳，聚焦即时配对、静默剪贴板同步与多文件高速传输。
- **Character：** 紧实触感（Tactile）、弹簧微回弹（Spring-responsive）、本地可信透明（Locally trustworthy）。
- **World：** 桌面强调色微染的中性 HCT `SurfaceContainer` 外框，包裹 `28dp` `Surface`/`SurfaceContainerLowest` 内嵌圆角画布；`Plus Jakarta Sans` + `DM Sans` 混排桌面中文无衬线字体，标识符/短码/速率采用等宽 `tabular-nums`。
- **Moments：**
  1. `Display` 级大号 6 位配对短码核对卡片（`TertiaryContainer` 语义突出）。
  2. 传输中的 `LinearWavyProgressIndicator` 正弦波浪实时进度条。
- **Signature：** 首/中/尾位置感知的 `24–28dp / 6dp` 异形分组卡片（选中展平、按压收敛为 `12dp`）、连接挤压式 `ButtonGroup`，以及与 GNOME/KDE 严格对齐的无割裂 CSD 集成顶栏。

### Artboard 画板选型与人机批注修订记录

- **外壳与设备双栏选型（已选定）：`g1-expressive-studio`（A · 双圆角画布 + 对端焦点卡）**
  - 无 `1px` 分割线的 `SurfaceContainer` 外壳包裹集成 CSD 顶栏（`48dp`）与左侧可折叠 `WideNavigationRail`（`96dp` 折叠 / `224dp` 展开，内含随侧栏收缩的 `Extended FAB` 与守护进程状态徽章）。
  - 宽屏（`>= 840`）内容区展示左侧 `404dp` 设备列表圆角画布（`28dp` 圆角）与右侧流式详情圆角画布（`28dp` 圆角），两张画布间留 `12dp` `SurfaceContainer` 底色间隙，右侧顶部设 `PrimaryContainer` 对端焦点卡。
  - 窄屏（`< 840`）退化为单栏列表/详情钻取，左侧侧栏收起并在底部显示悬浮胶囊栏 `HorizontalFloatingToolbar`。
- **传输页桌面编排（已选定）：`g2-split-workbench`（A · 左投递工作台 + 右实时波浪队列双栏）**
  - 宽屏（`>= 840`）采用左侧 `400dp` 投递工作台画布（设备筛选 FilterChip + 虚线多文件拖放区 + Portal 文件选择按钮）与 `12dp` 间隙右侧实时传输画布（`TertiaryContainer` 待确认接收卡 + `LinearWavyProgressIndicator` 进行中卡片 + 排队与会话历史分组列表）；窄屏自动转为单画布纵向流。
- **画板批注修订 #1（Thread `PoAdyRa4Kl` — 已落地）：** 将顶栏左上角占位图标替换为项目真实 Logo，矢量路径精确取自 Android 端 `app/src/main/res/drawable/ic_launcher_background.xml` 与 `ic_launcher_foreground.xml`（`#003547 → #006684` 渐变圆角方底 + `#E0F7FF`/`#70D2FF` 双设备卡片 + `#003547` 中心圆徽与白色双向/单向传输箭头）；同时内置高精矢量 `ContextAllLogo` 控件并重新生成 `packaging/appimage/contextall.png` 替换旧的纯色占位图，使窗口图标、托盘图标与集成顶栏保持一致。
- **画板批注修订 #2（Thread `Yuo4iaP_48` — 已落地）：** 删除集成顶栏中间的 `QUIC / Wayland` 状态胶囊指示，保持集成标题栏干净克制，仅保留左侧「真实 Logo + 当前分区标题」、中间可拖拽区以及按桌面布局适配（GNOME/KDE）的窗口控制按钮。

## 5. 已敲定架构决策与对齐决议

### 核心架构决策（D1–D7）

- **D1 架构分层：** 保留 `CCSWE.Avalonia.Material 12.1.14` 复用标准控件 `ControlTheme` 与 `Material_Icon*` 资源，在其之上叠加 FerroUI 对齐的 `Md3eExpressiveLayer`。
- **D2 声明式视图与控件：** 五大页面全部改为编译型 `.axaml`（带 `x:DataType`），所有自研控件重构为 `TemplatedControl` + `Themes/*.axaml`，清除 C# `LocalValue` 样式硬编码。
- **D3 无割裂外壳与响应式导航：** 顶部无框 CSD 集成顶栏与左侧可折叠 `WideNavigationRail`（`96dp ↔ 224dp`）共同坐落于 `SurfaceContainer`，与双 `28dp` 内嵌画布之间完全取消 `1px` 边框与重复标题；`< 840` 切换底部 `HorizontalFloatingToolbar`。
- **D4 保真型 48 色 HCT 调色板：** 忠实保留桌面系统强调色相作为 `Primary`，按 FerroUI `expressiveLightColorScheme`（亮色模式启用 Tone 30 鲜明容器前景色与活力 `Tertiary`）生成完整 48 个角色 + 状态层 + 语义色，分别写入并按需替换 `MaterialTheme.Resources.ThemeDictionaries["Light"]` 与 `["Dark"]`，并同步镜像写入兼容旧引用的键名。
- **D5 中英混排字体与 30 级字阶：** Brand 字族组合 `Plus Jakarta Sans`（`CCSWE` 内嵌 URI）+ 桌面 UI 无衬线字体作为中文回退；Plain 字族组合 `DM Sans` + 桌面 UI 无衬线字体；等宽字族使用系统等宽字体并启用 `tabular-nums`；完整提供 15 Baseline + 15 `*Emphasized` 排版样式类。
- **D6 FerroUI 物理动效与表现力组件：** 实现 6 角色双轨二阶物理弹簧 `MotionScheme.Expressive`（空间欠阻尼 + 效果临界阻尼，支持速度保持重定位与减弱动效降级）、有界/无界 `MaterialRipple`、正弦波浪进度条 `LinearWavyProgressIndicator`、7 多边形连续形变 `ContainedLoadingIndicator`、连接挤压式 `ButtonGroup`、`28dp` `MaterialDialog` 与右下角悬浮 `MaterialSnackbarHost`。
- **D7 页面焦点与规范图标：** `WideNavigationRail` 顶部内置可随侧栏平滑伸缩的 `Extended FAB`；「连接」与「本机」顶部设置 `PrimaryContainer`/`SecondaryContainer` 焦点卡；空状态配备有机容器徽章与 `ContainedLoadingIndicator`；全量图标迁移至 `CCSWE` `Material_Icon*`。

### 设备页数据对齐决议（1.A / 2.A / 3.A）

1. **剪贴板广播入口与说明（1.A）：** 左侧列表顶部保留全局「推送剪贴板」入口（保证窄屏与未选设备时随时可用），右侧详情 `ButtonGroup` 亦保留快捷触发；弹窗内明确说明 `PushClipboardTextAsync` 会广播至所有已开启剪贴板同步的已信任设备（若当前选中设备关闭了接收则在弹窗提示）。
2. **真实遥测与历史替换（2.A）：** 绝不虚构后端未提供的 `RTT`、离线活跃时间与独立 TLS 指纹；详情焦点卡仅展示真实 `DeviceId`、`Endpoint`、`Presence` 与对方 `RemoteClipboardSync` 状态，底部卡片展示该设备来自 `LocalShellState.History` 的真实最近传输记录（无记录时省略该卡片）。
3. **动态双分组与统一空状态（3.A）：** 左侧列表按「已信任设备」与「已发现待配对」两段动态分组渲染（各自仅在非空时显示并应用首/中/尾异形圆角），仅当两组皆空时展示单一有机空状态徽章与 `ContainedLoadingIndicator`。

### 已确认的默认决定（Author Calls）

1. **`appearance-override = follow-with-override`：** 默认始终跟随 Linux 桌面 Portal / GNOME / KDE 自动更新；同时在「本机」页提供“外观模式（跟随系统 / 浅色 / 深色）”与“强调色来源（跟随系统 / 品牌蓝 / 预设色板）”轻量覆盖开关，并新增可空字段保存至 `shell.json`（向后兼容旧版配置文件）。
2. **`rail-fab-action = send-files`：** 点击 `Extended FAB` 时若守护进程已连接且当前存在可用目标设备，则切至「传输」页并直接唤起 XDG Portal 多文件选择器（若当前已在「设备」页选中在线设备，则自动预选该设备为发送目标）；若守护进程尚未连接或无已信任在线设备，则切换至「设备」页并通过 `MaterialSnackbarHost` 提示先启动服务或完成配对。
3. **`spec-handoff = ryugi-and-openspec`：** 本规格落盘至 `docs/ryugi/specs/2026-11-15-md3e-ui-overhaul-design.md`，并在 `openspec/changes/md3e-ui-overhaul/` 同步维护独立 delta spec。
4. **`CCSWE` 样式类映射策略：** 所有标准按钮统一使用 `CCSWE` 的 `Classes="Filled"` / `"FilledTonal"` / `"Outlined"` / `"Text"` / `"Icon"`，输入框统一使用 `Classes="Outlined"`，开关复用 `ToggleSwitch` 并补充 MD3E 弹簧拇指扩展动效与勾选图标；原有 `ExpressiveButtonGroup`、`ExpressiveGroupTile` 等命名控件重构为模板化 `TemplatedControl`（保留必要公开 API 以平滑衔接现有业务逻辑）。

## 6. 模块架构与实现边界

| 模块 / 目录 | 职责与关键设计要求 |
| :--- | :--- |
| `src/ContextAll.App/Theming/` | 扩展 `Md3ePalette` 至 48 角色 HCT + 状态层 + 语义色；`HctColorScheme.Generate` 实现保真色相 + 亮色 Tone 30 容器前景色；新增 `Md3eThemeAdapter` 原子替换 `MaterialTheme` 下 `ThemeDictionaries["Light"]` 与 `["Dark"]` 的全部 `CCSWE` 标准键，并镜像写入 `Md3*` 键别名。`AppearanceResolver` 构造 `Md3BrandFontFamily`（`Plus Jakarta Sans` + 桌面 Sans + 中文回退）与 `Md3PlainFontFamily`（`DM Sans` + 桌面 Sans + 中文回退）及等宽字体栈。 |
| `src/ContextAll.App/Motion/` | 纯 C# 二阶阻尼弹簧求解器 `SpringSolver`（`MotionScheme.Expressive` 空间三档与效果三档、速度继承、ReducedMotion 即时收敛）、`TopLevel.RequestAnimationFrame` 驱动器、`WavyProgressGeometry`（正弦波路径与 `4dp` 间隙）与 `PolygonMorphGeometry`（7 种 M3E 多边形顶点插值）。 |
| `src/ContextAll.App/Styles/` & `Themes/` | `Md3eTypography.axaml`（30 级字阶样式类）、`Md3eTokens.axaml`（8 级圆角、间距、`ContextAllLogo` 矢量图）、`Themes/*.axaml`（`TemplatedControl` 完整 `ControlTheme`、伪类状态与 `PART_Ripple`）。 |
| `src/ContextAll.App/Controls/` | `MaterialRipple`（有界圆角裁剪/无界 `75ms/225ms/150ms`）、`IntegratedHeaderBar`（无边框 `48dp`、真实 `ContextAllLogo` + 分区标题、移除状态胶囊、适配 GNOME/KDE 按钮、最大化去外圆角）、`WideNavigationRail` & `HorizontalFloatingToolbar`、`ExpressiveGroupTile`、`ExpressiveButtonGroup`、`LinearWavyProgressIndicator`、`ContainedLoadingIndicator`、`MaterialDialog`、`MaterialSnackbarHost`、`ContextAllLogo`。 |
| `src/ContextAll.App/Views/` | 五大页面 `.axaml` + 最小代码后置：`ShellRootView.axaml`、`DevicesView.axaml`（按 `g1-expressive-studio` + 1.A/2.A/3.A）、`ConnectionView.axaml`、`TransfersView.axaml`（按 `g2-split-workbench`）、`ThisDeviceView.axaml`。 |
| **留待 Builder 把控的细节** | `ExpressiveGapProgressBar` 与旧测试兼容性由 `LinearWavyProgressIndicator` 接管或严格等价验证；ViewModel 仅增补最小化 UI 绑定/Snackbar/AppearanceOverride 属性；重新生成规范 `192x192` / `256x256` `packaging/appimage/contextall.png`。 |

## 7. 验证与交付标准（Verification）

1. `dotnet build ContextAll.Linux.slnf -c Release -p:NuGetAudit=false -warnaserror`（0 警告 0 错误）。
2. `dotnet test ContextAll.Tests.slnf -c Release -p:NuGetAudit=false` 与 `./scripts/test-e2e.sh --all -p:NuGetAudit=false` 全量通过。
3. 新增单元测试完整覆盖：48 色 HCT 与 `CCSWE` `ThemeDictionaries` 同步、Tone 30 对比度（`>= 4.5:1`）、`SpringSolver` 物理收敛与 ReducedMotion、`WavyProgressGeometry` 与 `PolygonMorphGeometry`、`MaterialRipple`、五大 `.axaml` 视图在宽屏（`1280x800`）与窄屏（`520x720`）下的无头布局渲染与交互（含 1.A/2.A/3.A 与 `AppearanceOverride`）。
4. 实机真窗口验收（`ryugi:verifying`）：在本机 Linux 环境通过 `./scripts/run-linux-dev.sh gui` 启动真实窗口核验并截图或确认体验。

## 8. 关联制品索引（Artifacts Used）

- **Written Design 原稿（Ryugi）：** `document/md3e-ui-overhaul`（`.ryugi/artifacts/document/md3e-ui-overhaul/doc.xml`）
- **交互式视觉选型画板（Ryugi）：** `artboard/md3e-ui-overhaul`（`.ryugi/artifacts/artboard/md3e-ui-overhaul/canvas.json`；已选定 `g1-expressive-studio` 与 `g2-split-workbench`，已完成 `PoAdyRa4Kl` 与 `Yuo4iaP_48` 修订）
- **项目内图标与设计参考：** `app/src/main/res/drawable/ic_launcher_background.xml`、`app/src/main/res/drawable/ic_launcher_foreground.xml`、`CCSWE.Avalonia.Material 12.1.14`、FerroUI `docs/porting/material-theme.md`。
