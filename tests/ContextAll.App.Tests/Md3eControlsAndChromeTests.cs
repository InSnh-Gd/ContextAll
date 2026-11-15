using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.VisualTree;
using ContextAll.App.Chrome;
using ContextAll.App.Controls;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eControlsAndChromeTests
{
    [Fact]
    public void ExpressiveGroupTile_morphs_positional_radii_across_rest_hover_press_and_selection()
    {
        Assert.Equal(new CornerRadius(28), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Single, false, false, false));
        Assert.Equal(new CornerRadius(22), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Single, true, false, false));

        Assert.Equal(new CornerRadius(28, 28, 6, 6), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.First, false, false, false));
        Assert.Equal(new CornerRadius(28, 28, 12, 12), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.First, true, false, false));

        Assert.Equal(new CornerRadius(6), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Middle, false, false, false));
        Assert.Equal(new CornerRadius(12), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Middle, true, false, false));

        Assert.Equal(new CornerRadius(6, 6, 28, 28), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Last, false, false, false));
        Assert.Equal(new CornerRadius(12, 12, 28, 28), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Last, true, false, false));

        Assert.Equal(new CornerRadius(12), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.First, false, true, false));
        Assert.Equal(new CornerRadius(28), ExpressiveGroupTile.ComputeCornerRadius(GroupPosition.Middle, true, false, true));

        var tile = new ExpressiveGroupTile { Position = GroupPosition.First };
        Assert.Equal(new CornerRadius(28, 28, 6, 6), tile.CornerRadius);
        tile.IsHoveredState = true;
        Assert.Equal(new CornerRadius(28, 28, 12, 12), tile.CornerRadius);
        tile.IsSelected = true;
        Assert.Equal(new CornerRadius(28), tile.CornerRadius);
        tile.IsPressedState = true;
        Assert.Equal(new CornerRadius(12), tile.CornerRadius);
    }

    [Fact]
    public void ExpressiveSwitch_renders_checkmark_or_cross_glyph_and_fires_toggled()
    {
        var sw = new ExpressiveSwitch();
        var observed = new List<bool>();
        sw.Toggled += observed.Add;

        Assert.False(sw.IsChecked);
        Assert.Equal("✕", sw.ThumbGlyph);

        sw.Toggle();
        Assert.True(sw.IsChecked);
        Assert.Equal("✓", sw.ThumbGlyph);
        Assert.Equal([true], observed);

        sw.Toggle();
        Assert.False(sw.IsChecked);
        Assert.Equal("✕", sw.ThumbGlyph);
        Assert.Equal([true, false], observed);
    }

    [Fact]
    public void ExpressiveButtonGroup_connects_ends_with_20px_outer_and_6px_inner_radii()
    {
        Assert.Equal(new CornerRadius(20), ExpressiveButtonGroup.ComputeItemRadius(0, 1, false));
        Assert.Equal(new CornerRadius(20, 6, 6, 20), ExpressiveButtonGroup.ComputeItemRadius(0, 3, false));
        Assert.Equal(new CornerRadius(6), ExpressiveButtonGroup.ComputeItemRadius(1, 3, false));
        Assert.Equal(new CornerRadius(6, 20, 20, 6), ExpressiveButtonGroup.ComputeItemRadius(2, 3, false));
        Assert.Equal(new CornerRadius(20), ExpressiveButtonGroup.ComputeItemRadius(1, 3, true));

        var group = new ExpressiveButtonGroup();
        group.SetItems([
            new ExpressiveGroupButtonItem("a", "A", IsSelected: false),
            new ExpressiveGroupButtonItem("b", "B", IsSelected: true),
            new ExpressiveGroupButtonItem("c", "C", IsSelected: false),
        ]);

        Assert.Equal(3, group.Buttons.Count);
        Assert.Equal(new CornerRadius(20, 6, 6, 20), group.Buttons[0].CornerRadius);
        Assert.Equal(new CornerRadius(20), group.Buttons[1].CornerRadius);
        Assert.Equal(new CornerRadius(6, 20, 20, 6), group.Buttons[2].CornerRadius);
    }

    [Fact]
    public void ExpressiveGapProgressBar_shows_gap_only_while_strictly_between_0_and_1()
    {
        var bar = new ExpressiveGapProgressBar { Maximum = 100, Value = 0 };
        Assert.Equal(0.0, bar.Fraction);
        Assert.False(bar.ShowGap);

        bar.Value = 45;
        Assert.Equal(0.45, bar.Fraction, precision: 4);
        Assert.True(bar.ShowGap);

        bar.Value = 120;
        Assert.Equal(1.0, bar.Fraction);
        Assert.False(bar.ShowGap);
    }

    [Fact]
    public void Navigation_controls_switch_between_wide_rail_and_narrow_dock_and_keep_480x640_minimum()
    {
        Assert.Equal(480, ShellLayout.MinimumWidth);
        Assert.Equal(640, ShellLayout.MinimumHeight);
        Assert.False(ShellLayout.TwoColumns(839));
        Assert.True(ShellLayout.TwoColumns(840));

        var rail = new WideNavigationRail();
        var dock = new ExpressiveFloatingNavDock();
        Assert.Equal(WideNavigationRail.CompactWidth, rail.Width);
        rail.IsExpanded = true;
        Assert.Equal(WideNavigationRail.ExpandedWidth, rail.Width);

        ShellSection? dockPicked = null;
        dock.SectionSelected += s => dockPicked = s;
        dock.SelectedSection = ShellSection.Transfers;
        Assert.Equal(ShellSection.Transfers, dock.SelectedSection);
        Assert.Null(dockPicked);
    }

    [Theory]
    [InlineData("':minimize,maximize,close'", "", "minimize,maximize,close")]
    [InlineData("'close,minimize,maximize:'", "close,minimize,maximize", "")]
    [InlineData("':close'", "", "close")]
    [InlineData("'appmenu:minimize,close'", "", "minimize,close")]
    [InlineData("'close,spacer,menu:maximize'", "close", "maximize")]
    public void WindowButtonLayoutParser_parses_gnome_layout_permutations_and_visibility(
        string raw,
        string expectedLeftCsv,
        string expectedRightCsv)
    {
        Assert.True(WindowButtonLayoutParser.TryParseGnome(raw, out var layout));
        Assert.Equal(expectedLeftCsv, string.Join(',', layout.Left.Select(b => b.ToString().ToLowerInvariant())));
        Assert.Equal(expectedRightCsv, string.Join(',', layout.Right.Select(b => b.ToString().ToLowerInvariant())));
    }

    [Fact]
    public void WindowButtonLayoutParser_parses_kde_kwinrc_and_resolves_desktop_precedence()
    {
        var kdeCustom = """
            [org.kde.kdecoration2]
            ButtonsOnLeft=X
            ButtonsOnRight=MIA
            """;

        var kdeResolved = WindowButtonLayoutParser.Resolve("KDE", "':close'", kdeCustom);
        Assert.Equal([WindowActionButton.Close], kdeResolved.Left);
        Assert.Equal([WindowActionButton.Minimize, WindowActionButton.Maximize], kdeResolved.Right);

        var gnomeResolved = WindowButtonLayoutParser.Resolve("GNOME", "'close,minimize:maximize'", kdeCustom);
        Assert.Equal([WindowActionButton.Close, WindowActionButton.Minimize], gnomeResolved.Left);
        Assert.Equal([WindowActionButton.Maximize], gnomeResolved.Right);

        var header = new IntegratedHeaderBar { ButtonLayout = kdeResolved };
        Assert.Equal([WindowActionButton.Close], header.LeftButtons);
        Assert.Equal([WindowActionButton.Minimize, WindowActionButton.Maximize], header.RightButtons);

        var fallback = WindowButtonLayoutParser.Resolve("XFCE", "", "");
        Assert.Equal(WindowButtonLayout.DefaultRight, fallback);
    }

    [Fact]
    public void ChromeModeState_squares_corners_on_maximize_or_tile_and_toggles_native_ssd()
    {
        var csdNormal = ChromeModeState.Compute(useNativeTitleBar: false, WindowState.Normal);
        Assert.Equal(WindowDecorations.BorderOnly, csdNormal.Decorations);
        Assert.True(csdNormal.ShowIntegratedHeader);
        Assert.Equal(new CornerRadius(ChromeModeState.NormalCornerRadius), csdNormal.OuterCornerRadius);

        var csdMaximized = ChromeModeState.Compute(useNativeTitleBar: false, WindowState.Maximized);
        Assert.Equal(WindowDecorations.BorderOnly, csdMaximized.Decorations);
        Assert.True(csdMaximized.ShowIntegratedHeader);
        Assert.Equal(new CornerRadius(0), csdMaximized.OuterCornerRadius);

        var csdTiled = ChromeModeState.Compute(useNativeTitleBar: false, WindowState.Normal, isTiled: true);
        Assert.Equal(new CornerRadius(0), csdTiled.OuterCornerRadius);

        var ssd = ChromeModeState.Compute(useNativeTitleBar: true, WindowState.Normal);
        Assert.Equal(WindowDecorations.Full, ssd.Decorations);
        Assert.False(ssd.ShowIntegratedHeader);
        Assert.Equal(new CornerRadius(0), ssd.OuterCornerRadius);
    }

    [Fact]
    public void ExpressiveGroupTile_is_TemplatedControl_without_local_value_background_override() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var tile = new ExpressiveGroupTile
            {
                Position = GroupPosition.First,
                IsInteractive = true,
            };

            Assert.IsAssignableFrom<TemplatedControl>(tile);
            Assert.False(tile.IsSet(TemplatedControl.BackgroundProperty));
            Assert.IsAssignableFrom<TemplatedControl>(new ExpressiveSwitch());
            Assert.IsAssignableFrom<TemplatedControl>(new ExpressiveIconBadge());
            Assert.IsAssignableFrom<TemplatedControl>(new ExpressiveButtonGroup());
        });

    [Fact]
    public void IntegratedHeaderBar_has_clean_logo_and_no_center_status_pill() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            Assert.True(WindowButtonLayoutParser.TryParseGnome("close,minimize,maximize:", out var layout));
            var header = new IntegratedHeaderBar
            {
                TitleText = "ContextAll",
                ButtonLayout = layout,
            };

            Assert.IsAssignableFrom<TemplatedControl>(header);
            Assert.Equal(48.0, header.HeaderHeight);
            Assert.NotNull(header.LogoControl);
            Assert.Null(typeof(IntegratedHeaderBar).GetProperty("CenterStatusText"));
            Assert.Equal([WindowActionButton.Close, WindowActionButton.Minimize, WindowActionButton.Maximize], header.LeftButtons);
        });

    [Fact]
    public void WideNavigationRail_collapses_and_expands_between_96_and_224_with_extended_fab() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            Assert.Equal(96.0, WideNavigationRail.CompactWidth);
            Assert.Equal(224.0, WideNavigationRail.ExpandedWidth);

            var rail = new WideNavigationRail
            {
                DaemonStatusLabel = "已连接",
                FabText = "发送文件",
            };
            Assert.IsAssignableFrom<TemplatedControl>(rail);
            Assert.Equal(96.0, rail.Width);

            var fabTriggered = 0;
            rail.FabClicked += () => fabTriggered++;
            rail.TriggerFab();
            Assert.Equal(1, fabTriggered);

            rail.IsExpanded = true;
            Assert.Equal(224.0, rail.Width);
        });

    [Fact]
    public void HorizontalFloatingToolbar_switches_sections_and_raises_SectionSelected_only_on_trigger() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var toolbar = new HorizontalFloatingToolbar();
            Assert.IsAssignableFrom<TemplatedControl>(toolbar);

            ShellSection? selected = null;
            toolbar.SectionSelected += section => selected = section;

            toolbar.SelectedSection = ShellSection.ThisDevice;
            Assert.Null(selected);

            toolbar.TriggerSection(ShellSection.Transfers);
            Assert.Equal(ShellSection.Transfers, toolbar.SelectedSection);
            Assert.Equal(ShellSection.Transfers, selected);
        });

    [Fact]
    public void LinearWavyProgressIndicator_computes_wavy_active_and_gap_geometry() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var indicator = new LinearWavyProgressIndicator
            {
                Maximum = 100,
                Value = 45,
                Width = 240,
                Height = 16,
            };
            Assert.IsAssignableFrom<TemplatedControl>(indicator);
            Assert.Equal(0.45, indicator.Fraction, precision: 3);
            Assert.True(indicator.ShowGap);

            var frame = indicator.BuildFrameGeometry(240, 16, phaseRadians: 0.5);
            Assert.NotNull(frame.ActiveWave);
            Assert.NotNull(frame.RemainingTrack);
            Assert.True(frame.HasStopIndicator);
            Assert.True(frame.GapWidth >= 4.0);
        });

    [Fact]
    public void ContainedLoadingIndicator_morphs_seven_polygons_and_respects_reduced_motion() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var indicator = new ContainedLoadingIndicator { IsActive = true };
            Assert.IsAssignableFrom<TemplatedControl>(indicator);
            Assert.Equal(7, indicator.PolygonCount);

            var g0 = indicator.BuildMorphedGeometry(size: 48, progress: 0.0);
            var g1 = indicator.BuildMorphedGeometry(size: 48, progress: 0.5);
            Assert.NotNull(g0);
            Assert.NotNull(g1);
            Assert.NotEqual(g0.Bounds, g1.Bounds);

            indicator.ReducedMotion = true;
            Assert.Equal(0, indicator.CurrentPolygonIndex);
        });

    [Fact]
    public void MaterialDialog_manages_scrim_dismiss_and_primary_cancel_actions() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var dialog = new MaterialDialog
            {
                Title = "确认推送",
                Message = "将向已启用剪贴板同步的可信设备广播",
                PrimaryActionText = "推送",
                CancelActionText = "取消",
                DismissOnScrimClick = true,
                IsOpen = true,
            };
            Assert.IsAssignableFrom<TemplatedControl>(dialog);
            Assert.Equal(new CornerRadius(28), dialog.DialogCornerRadius);

            var primaryClicks = 0;
            var cancelClicks = 0;
            dialog.PrimaryActionClicked += (_, _) => primaryClicks++;
            dialog.Canceled += (_, _) => cancelClicks++;

            dialog.InvokeScrimDismiss();
            Assert.False(dialog.IsOpen);
            Assert.Equal(1, cancelClicks);

            dialog.IsOpen = true;
            dialog.InvokePrimaryAction();
            Assert.False(dialog.IsOpen);
            Assert.Equal(1, primaryClicks);
        });

    [Fact]
    public void MaterialSnackbarHost_queues_messages_invokes_action_and_dismisses_on_timer() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var host = new MaterialSnackbarHost();
            Assert.IsAssignableFrom<TemplatedControl>(host);

            var actionInvoked = 0;
            host.Enqueue("第一条消息", "撤销", () => actionInvoked++, TimeSpan.FromSeconds(4));
            host.Enqueue("第二条消息");

            Assert.True(host.IsOpen);
            Assert.Equal("第一条消息", host.CurrentMessage);
            Assert.Equal(1, host.PendingCount);

            host.TriggerAction();
            Assert.Equal(1, actionInvoked);
            Assert.Equal("第二条消息", host.CurrentMessage);

            host.AdvanceTime(TimeSpan.FromSeconds(5));
            Assert.False(host.IsOpen);
            Assert.Equal(0, host.PendingCount);
        });

    [Fact]
    public void WideNavigationRail_has_zero_hard_1px_border_and_ContextAllLogo_initializes_geometries_eagerly() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var host = new Window { Width = 300, Height = 600 };
            var rail = new WideNavigationRail();
            host.Content = rail;
            rail.Measure(new Size(224, 600));
            rail.Arrange(new Rect(0, 0, 224, 600));
            rail.ApplyTemplate();

            Assert.Equal(new Thickness(0), rail.BorderThickness);

            var logo = new ContextAllLogo();
            var shapes = logo.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToArray();
            Assert.Equal(4, shapes.Length);
            Assert.All(shapes, p => Assert.NotNull(p.Data));
        });

    [Fact]
    public void WideNavigationRail_aligns_all_icons_on_single_48dp_vertical_axis_in_collapsed_and_expanded_states_and_resolves_all_four_section_icons() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var rail = new WideNavigationRail
            {
                DaemonStatusLabel = "已连接",
                FabText = "发送文件",
            };
            var host = new Window
            {
                Width = 300,
                Height = 720,
                Content = rail,
            };

            host.Measure(new Size(300, 720));
            host.Arrange(new Rect(0, 0, 300, 720));
            rail.Measure(new Size(WideNavigationRail.CompactWidth, 720));
            rail.Arrange(new Rect(0, 0, WideNavigationRail.CompactWidth, 720));

            var iconNames = new[]
            {
                "PART_ToggleIcon",
                "PART_FabIcon",
                "PART_NavDevicesIcon",
                "PART_NavConnectionIcon",
                "PART_NavTransfersIcon",
                "PART_NavThisDeviceIcon",
            };

            var iconsByName = rail.GetVisualDescendants()
                .OfType<PathIcon>()
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToDictionary(p => p.Name!, StringComparer.Ordinal);

            foreach (var name in iconNames)
            {
                Assert.True(iconsByName.TryGetValue(name, out var icon), $"Missing icon {name}");
                Assert.NotNull(icon!.Data);
                var center = icon.TranslatePoint(new Point(icon.Bounds.Width / 2.0, icon.Bounds.Height / 2.0), rail);
                Assert.NotNull(center);
                Assert.InRange(center.Value.X, 47.0, 49.0);
            }

            rail.Transitions = null;
            rail.IsExpanded = true;
            host.UpdateLayout();
            rail.Measure(new Size(WideNavigationRail.ExpandedWidth, 720));
            rail.Arrange(new Rect(0, 0, WideNavigationRail.ExpandedWidth, 720));

            foreach (var name in iconNames)
            {
                var icon = iconsByName[name];
                var center = icon.TranslatePoint(new Point(icon.Bounds.Width / 2.0, icon.Bounds.Height / 2.0), rail);
                Assert.NotNull(center);
                Assert.InRange(center.Value.X, 47.0, 49.0);
            }

            var labelNames = new[]
            {
                "PART_BrandText",
                "PART_FabLabel",
                "PART_NavDevicesLabel",
                "PART_NavConnectionLabel",
                "PART_NavTransfersLabel",
                "PART_NavThisDeviceLabel",
            };

            var labelsByName = rail.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => !string.IsNullOrEmpty(t.Name))
                .ToDictionary(t => t.Name!, StringComparer.Ordinal);

            foreach (var name in labelNames)
            {
                Assert.True(labelsByName.TryGetValue(name, out var label), $"Missing label {name}");
                Assert.True(label!.IsVisible, $"Expected {name} visible when expanded");
                var origin = label.TranslatePoint(new Point(0, 0), rail);
                Assert.NotNull(origin);
                Assert.True(
                    origin.Value.X is >= 87.0 and <= 89.0,
                    $"Expected expanded label '{name}' X in [87, 89], got {origin.Value.X:F2} (rail.Width={rail.Width:F1}, rail.Bounds.Width={rail.Bounds.Width:F1})");
            }

            ShellSection? railPicked = null;
            rail.SectionSelected += s => railPicked = s;
            rail.TriggerSection(ShellSection.Connection);
            Assert.Equal(ShellSection.Connection, rail.SelectedSection);
            Assert.Equal(ShellSection.Connection, railPicked);

            var toolbar = new HorizontalFloatingToolbar();
            ShellSection? toolbarPicked = null;
            toolbar.SectionSelected += s => toolbarPicked = s;
            toolbar.TriggerSection(ShellSection.Connection);
            Assert.Equal(ShellSection.Connection, toolbar.SelectedSection);
            Assert.Equal(ShellSection.Connection, toolbarPicked);
        });

    [Fact]
    public void WideNavigationRail_nav_pills_are_never_clipped_by_outer_button_and_expand_into_single_unified_capsule() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var rail = new WideNavigationRail
            {
                DaemonStatusLabel = "未连接",
                FabText = "发送文件",
                Transitions = null,
            };
            var host = new Window
            {
                Width = 300,
                Height = 720,
                Content = rail,
            };

            host.Measure(new Size(300, 720));
            host.Arrange(new Rect(0, 0, 300, 720));

            var pills = new[]
            {
                "PART_NavDevicesPill",
                "PART_NavConnectionPill",
                "PART_NavTransfersPill",
                "PART_NavThisDevicePill",
            };
            var buttons = new[]
            {
                "PART_NavDevices",
                "PART_NavConnection",
                "PART_NavTransfers",
                "PART_NavThisDevice",
            };

            var bordersByName = rail.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .GroupBy(b => b.Name!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var buttonsByName = rail.GetVisualDescendants()
                .OfType<Button>()
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .GroupBy(b => b.Name!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var btnName in buttons)
            {
                var btn = buttonsByName[btnName];
                Assert.False(btn.ClipToBounds, $"Expected nav button {btnName} not to clip its pill indicator.");
                foreach (var ancestorBorder in btn.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_Root"))
                {
                    Assert.True(
                        ancestorBorder.CornerRadius.TopLeft <= 0.0 && !ancestorBorder.ClipToBounds,
                        $"Expected {btnName} PART_Root not to apply circular clipping around pill+label.");
                }
            }

            foreach (var pillName in pills)
            {
                var pill = bordersByName[pillName];
                Assert.InRange(pill.Bounds.Width, 55.0, 57.0);
                Assert.InRange(pill.Bounds.Height, 31.0, 33.0);
                Assert.Equal(new CornerRadius(16), pill.CornerRadius);
            }

            rail.IsExpanded = true;
            host.UpdateLayout();
            rail.Measure(new Size(WideNavigationRail.ExpandedWidth, 720));
            rail.Arrange(new Rect(0, 0, WideNavigationRail.ExpandedWidth, 720));

            foreach (var pillName in pills)
            {
                var pill = bordersByName[pillName];
                Assert.True(pill.Bounds.Width >= 180.0, $"Expected expanded {pillName} to span full nav row (>= 180dp), got {pill.Bounds.Width:F1}");
                Assert.InRange(pill.Bounds.Height, 46.0, 50.0);
                Assert.Equal(new CornerRadius(24), pill.CornerRadius);
            }

            var toolbar = new HorizontalFloatingToolbar();
            var dockHost = new Window { Width = 520, Height = 200, Content = toolbar };
            dockHost.Measure(new Size(520, 200));
            dockHost.Arrange(new Rect(0, 0, 520, 200));
            foreach (var dockBtn in toolbar.GetVisualDescendants().OfType<Button>())
            {
                Assert.InRange(dockBtn.Bounds.Height, 38.0, 44.0);
            }
        });
}
