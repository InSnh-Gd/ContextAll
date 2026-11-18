using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ContextAll.App.Controls;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eTokensAndRippleTests
{
    [Fact]
    public void Md3eTokens_and_Md3eTypography_compile_and_define_radii_icons_and_15_emphasized_styles() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var tokensObj = AvaloniaXamlLoader.Load(new Uri("avares://ContextAll.App/Styles/Md3eTokens.axaml"));
            var tokens = Assert.IsAssignableFrom<IResourceDictionary>(tokensObj);

            Assert.Equal(new CornerRadius(28), Assert.IsType<CornerRadius>(tokens["Md3eCornerSheet"]));
            Assert.Equal(new CornerRadius(24), Assert.IsType<CornerRadius>(tokens["Md3eCornerGroupOuter"]));
            Assert.Equal(new CornerRadius(6), Assert.IsType<CornerRadius>(tokens["Md3eCornerGroupInner"]));
            Assert.Equal(new CornerRadius(12), Assert.IsType<CornerRadius>(tokens["Md3eCornerPressed"]));

            foreach (var iconKey in new[]
                     {
                         "Material_IconDevices",
                         "Material_IconLink",
                         "Material_IconSwapHoriz",
                         "Material_IconLaptopMac",
                         "Material_IconContentPaste",
                         "Material_IconUploadFile",
                         "Material_IconSend",
                         "Material_IconDelete",
                         "Material_IconFolderOpen",
                         "Material_IconDarkMode",
                         "Material_IconPalette",
                         "Material_IconContentCopy",
                         "Material_IconEdit",
                         "Material_IconChevronRight",
                         "Material_IconShield",
                         "Material_IconInfo",
                         "Material_IconWifi",
                         "Material_IconQrCode",
                         "Material_IconDesktop",
                         "Material_IconClose",
                         "Material_IconMinimize",
                         "Material_IconCropSquare",
                         "Material_IconFilterNone",
                     })
            {
                Assert.True(tokens.ContainsKey(iconKey), $"Missing domain icon resource: {iconKey}");
                Assert.True(tokens.TryGetResource(iconKey, null, out var geomObj));
                Assert.IsAssignableFrom<Avalonia.Media.Geometry>(geomObj);
            }

            // Also confirm CCSWE built-in Material_Icon* keys resolve on Application.Current
            Assert.NotNull(Application.Current);
            Assert.True(Application.Current.TryGetResource("Material_IconClose", null, out _));
            Assert.True(Application.Current.TryGetResource("Material_IconCheck", null, out _));
            Assert.True(Application.Current.TryGetResource("Material_IconRefresh", null, out _));

            var typographyObj = AvaloniaXamlLoader.Load(new Uri("avares://ContextAll.App/Styles/Md3eTypography.axaml"));
            var styles = Assert.IsAssignableFrom<Avalonia.Styling.Styles>(typographyObj);
            var selectors = styles
                .OfType<Avalonia.Styling.Style>()
                .Select(s => s.Selector?.ToString() ?? string.Empty)
                .ToArray();

            foreach (var emphasizedClass in new[]
                     {
                         "DisplayLargeEmphasized",
                         "DisplayMediumEmphasized",
                         "DisplaySmallEmphasized",
                         "HeadlineLargeEmphasized",
                         "HeadlineMediumEmphasized",
                         "HeadlineSmallEmphasized",
                         "TitleLargeEmphasized",
                         "TitleMediumEmphasized",
                         "TitleSmallEmphasized",
                         "BodyLargeEmphasized",
                         "BodyMediumEmphasized",
                         "BodySmallEmphasized",
                         "LabelLargeEmphasized",
                         "LabelMediumEmphasized",
                         "LabelSmallEmphasized",
                         "MonoTabular",
                     })
            {
                Assert.Contains(selectors, sel => sel.Contains(emphasizedClass, StringComparison.Ordinal));
            }
        });

    [Fact]
    public void MaterialRipple_progresses_through_fade_in_expand_and_fade_out_with_bounded_corner_radius() =>
        AvaloniaUiTestHost.RunOnUiThread(() =>
        {
            var ripple = new MaterialRipple
            {
                IsBounded = true,
                CornerRadius = new CornerRadius(20),
                BoundsWidthOverride = 200,
                BoundsHeightOverride = 48,
            };

            Assert.False(ripple.HasActiveRipples);
            ripple.TriggerAt(new Point(100, 24));
            Assert.True(ripple.HasActiveRipples);

            // At 75ms (fade-in complete, expanding) opacity reaches peak (~0.12) and radius grows.
            ripple.AdvanceTime(0.075);
            var mid = ripple.SnapshotRipples();
            var active = Assert.Single(mid);
            Assert.True(active.Radius > 20);
            Assert.True(active.Opacity >= 0.10);

            // Release pointer triggers fade-out after expansion finishes
            ripple.ReleaseActive();
            ripple.AdvanceTime(0.45);
            Assert.False(ripple.HasActiveRipples);
        });
}
