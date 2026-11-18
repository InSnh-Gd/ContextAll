# Linux MD3E 外壳

对应 `md3e-ui-overhaul` 的 `md3e-ui`。以下仅描述 `ContextAll.App` 已实现、并由 App 测试证明的桌面图形外壳外观行为。Linux 入口是 `ContextAll.App.Desktop`，Windows 入口是 Avalonia Win32 的 `ContextAll.App.Windows`，两者共用这些视图（见 [ADR 0011](../adr/0011-shared-avalonia-desktop-shell.md)）。Linux 取色决策见 [ADR 0007](../adr/0007-linux-appearance-and-accent-fallback.md)。

## 色板与 ThemeDictionaries

`HctColorScheme` 从桌面强调色种子生成亮/暗 48 角色板。Fixed 角色在亮暗之间保持不变；Light 容器前景落在 HCT Tone 约 18–36；Inverse / Success / Warning / Info 容器对比度不少于 4.5:1（见 `Md3eThemeAndAppearanceTests.Hct_palette_provides_48_roles_including_fixed_inverse_semantic_and_expressive_tone30_containers`、`Hct_palette_enforces_wcag_aa_contrast_for_light_and_dark_themes`）。

`Md3eThemeAdapter` 把色板写入 `MaterialTheme.Resources.ThemeDictionaries["Light"]` 与 `["Dark"]`：无前缀 CCSWE 键（如 `PrimaryColor`、`Primary` 画刷、`OnSurfaceHover` 状态层）以及顶层 `Md3*` 别名（见 `Md3eThemeAdapter_syncs_unprefixed_ccswe_light_and_dark_theme_dictionaries_and_fonts`）。

强调色解析顺序为门户 `accent-color` → KDE `kdeglobals` → GNOME 强调色 → 品牌种子 `#1B5E91`。门户 `color-scheme` 为 `1` 时用暗色，`0` / `2` / 缺失时用亮色（见 `Accent_cascade_prefers_portal_then_kde_then_gnome_then_brand_default`、`Color_scheme_cascade_respects_portal_gnome_kde_and_defaults_to_light`）。

## 字体、动效与几何

品牌字体栈含 Plus Jakarta Sans 与 CJK 回退，正文字体栈含 DM Sans；设备编号、配对短码与端点使用独立等宽族（见 `Fonts_resolve_system_ui_sans_and_keep_monospace_separate`、`AppearanceResolver_honors_manual_mode_and_accent_overrides_and_builds_cjk_mixed_fonts`）。

`Styles/Md3eTypography.axaml` 提供 15 档 Emphasized 样式；`Styles/Md3eTokens.axaml` 定义 28dp 页片圆角与域图标几何（见 `Md3eTokensAndRippleTests`）。`MotionScheme.Expressive` 有 6 个弹簧角色；`SpringSolver` 在欠阻尼空间角色上过冲并在 `ReducedMotion` 时立即贴目标（见 `Md3eMotionAndGeometryTests`）。`LinearWavyProgressIndicator` 与 7 边形 `ContainedLoadingIndicator` 的几何由同一套测试约束。

四个页签（设备 / 连接 / 传输 / 本机）切换走 MD3E 共享轴 X：前进从右侧滑入 30dp、后退从左侧滑入，位移用 `DefaultSpatial`，进入透明度用 `DefaultEffects`、离开用 `FastEffects`。首次布局只显示当前页，其它三页隐藏；播放期间页片关掉 `UseLayoutRounding` 并启用 `BitmapCache`，落定后只保留当前页的缓存；宿主本身不做布局像素对齐。`SectionTransitionHost.ReducedMotion` 为真时立即落定（见 `ExpressiveSharedAxis_slides_forward_from_the_end_and_backward_from_the_start`、`SectionTransitionHost_plays_shared_axis_spring_and_snaps_when_reduced_motion`、`SectionTransitionHost_caches_section_bitmaps_and_skips_layout_rounding_during_shared_axis`、`ShellRootView_shows_only_the_selected_section_on_first_layout`、`SectionTransitionHost_hides_inactive_children_when_section_is_assigned_after_attach`）。

## TemplatedControl 与品牌标

自定义 MD3E 控件是 `TemplatedControl`，视觉树在 `Themes/Md3eControls.axaml`：`ExpressiveGroupTile`、`ExpressiveButtonGroup`、`MaterialRipple`、`IntegratedHeaderBar`、`WideNavigationRail`、`HorizontalFloatingToolbar`、`LinearWavyProgressIndicator`、`ContainedLoadingIndicator`、`MaterialDialog`、`MaterialSnackbarHost`、`ContextAllLogo`（见 `Md3eControlsAndChromeTests`）。`ExpressiveGroupTile` 不在 C# 里写 `LocalValue` 背景，也没有 1px 描边。

`ContextAllLogo` 默认 28dp。路径常量与 Android 启动器矢量一致；`ApplyTemplate` 后视觉树有四条 `Path`（见 `Md3eBrandAssetTests`、`ContextAllLogo_is_templated_control_and_apply_template_yields_four_android_paths`）。嵌入图标 `packaging/appimage/contextall.png` 是 256×256 PNG。

页眉是 48dp 的 `IntegratedHeaderBar`。「优先使用系统标题栏」开启时请求 `WindowDecorations.Full`。按现有协商结果采用系统装饰时隐藏集成页眉和外壳描边；否则保留应用页眉，不额外叠加窗口按钮。设置说明反映当前窗口装饰，协商期间只显示「正在请求系统标题栏…」，不提前报告结果（见 `Title_bar_feedback_tracks_runtime_negotiation_without_persisting_the_result`）。宽栏 `WideNavigationRail` 固定 96dp，窄栏用 `HorizontalFloatingToolbar`。窗口最小 480×640dp；连接和本机设置按外壳的 840dp 断点分栏。

## 五页 AXAML 绑定

`ShellRootView`、`DevicesView`、`ConnectionView`、`TransfersView`、`ThisDeviceView` 是带 `x:DataType` 的编译 AXAML。视图 code-behind 不再设置 `Background` / `Foreground`。状态色走 `StatusPill` 样式类：

- 「设备」：6 位配对码按 3+3 分组绑定；局域网胶囊 `Classes.Error` 对应未连接（见 `DevicesView_binds_pairing_digits_and_lan_status_classes_without_localvalue_brushes`）。
- 「连接」：窗口达到 840dp 后，手动拨号与本机网络信息各占一个 28dp 圆角页片，相隔 12dp；更窄时按同序纵向排列。地址与备注保留独立标签，输入框最小高 56dp、四角 20dp、完整细描边，聚焦后描边加粗并使用主题强调色。快捷网段、四步指南和拨号错误反馈保留（见 `Connection_workspace_follows_shell_width_without_losing_inputs`、`ConnectionView_axaml_binds_endpoints_backend_and_material_guide_dialog`）。
- 「传输」：保留 `g2-split-workbench` 的分栏与纵向堆叠。累计数据在历史下方显示一次，空历史也保留（见 `TransfersView_g2_split_workbench_switches_wide_split_and_narrow_stack_and_renders_wavy_progress`）。
- 「本机」：身份、外观与主题、文件与后台、窗口和帮助共五组。宽屏用左侧分类导航和右侧编辑区，窄屏用最大宽度 640dp 的单列。行名称 16sp、说明 14sp，行高至少 72dp，内容过长会换行增高；行间距 2dp，无描边，组外角 24dp、相邻内角 6dp（见 `Settings_categories_share_editors_and_preserve_the_open_rename_draft`、`Settings_rows_grow_for_long_text_and_keep_group_corners`）。

设备列表在宽屏并排列出设备和详情（`g1-expressive-studio`）。发现已就绪但没有设备及待确认配对时显示搜索与首次连接引导；宽屏引导在详情页片，窄屏直接显示在列表中。服务断开时改为显示后台服务状态。详情只展示真实对端与 `LocalShellState.History`；列表唯一的剪贴板推送入口打开广播说明，并提示已关闭同步的对端（见 `DevicesView_g1_expressive_studio_validates_dual_sheet_empty_state_real_telemetry_and_broadcast_dialog`）。

## shell.json 外观覆盖

`$XDG_CONFIG_HOME/contextall/shell.json`（权限 `0600`）可写可空的 `AppearanceModeOverride`（`FollowSystem` / `Light` / `Dark`）与 `AccentSourceOverride`（`FollowSystem` / `BrandBlue` / `ForestTeal` / `SunsetTerracotta` / `VioletIndigo`）。缺省跟随桌面门户。旧文件没有这两项字段时按跟随系统加载（见 `ShellStartupAndPreferencesTests`、`ThisDeviceView_axaml_exposes_appearance_and_accent_overrides_with_zero_reset_controls`）。

切换浅色、深色或强调色时，设置页保留原有按钮和键盘焦点，只更新选中状态、颜色与圆角（见 `Appearance_buttons_keep_focus_and_update_the_theme_without_being_replaced`）。

主题资源按组发布；只切换亮暗时复用已生成的色板，有效外观未变时不刷新资源（见 `Appearance_changes_publish_resources_in_batches`）。
