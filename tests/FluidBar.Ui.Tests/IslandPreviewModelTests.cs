using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Layout;
using AppTheme = FluidBar.App.Settings.AppTheme;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards the "settings → appearance preview" mapping (task-30). The preview is built from
/// <see cref="IslandPreviewModel"/>, which is produced by the same <c>SkiaIslandThemeMapper</c> the real
/// island uses, so these assertions cover both "the value reaches the preview" and "the preview uses
/// island tokens instead of a hard-coded second palette".
/// </summary>
public sealed class IslandPreviewModelTests
{
    private static readonly SystemAppearanceSnapshot DarkAppearance = new(false, false, "#4CC9F0");

    /// <summary>Prototype <c>.ap-shell</c> height (the preview control's fixed shell size).</summary>
    private const double ShellHeight = 62;

    private static AppSettings Baseline => AppSettings.Default with
    {
        Theme = AppTheme.Dark,
        Material = IslandMaterial.Obsidian,
        Palette = "obsidian",
        AccentMode = "custom",
        AccentFallback = "#4CC9F0",
        PanelRadius = 30,
        FillOpacity = 0.97,
        FontScale = 1.0,
    };

    [Theory]
    [InlineData("obsidian", 0x08, 0x08, 0x0A)]
    [InlineData("graphite", 0x1C, 0x1C, 0x1E)]
    [InlineData("slate", 0x11, 0x17, 0x21)]
    [InlineData("nord", 0x2E, 0x34, 0x40)]
    public void Palette_MapsToTheIslandBackgroundToken(string palette, int r, int g, int b)
    {
        var model = IslandPreviewModel.From(Baseline with { Palette = palette }, DarkAppearance);

        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), model.Background);
        Assert.Equal(palette, model.Palette);
    }

    [Fact]
    public void Material_Acrylic_UsesTheGlassTier_AndSafeModeFallsBackToObsidian()
    {
        var obsidian = IslandPreviewModel.From(Baseline, DarkAppearance);
        Assert.False(obsidian.Glass);
        Assert.Equal(obsidian.FillOpacity, obsidian.EffectiveFillOpacity, 3);

        var acrylic = IslandPreviewModel.From(
            Baseline with { Material = IslandMaterial.Acrylic, FillOpacity = 0.9 }, DarkAppearance);
        Assert.True(acrylic.Glass);
        Assert.Equal(IslandPreviewModel.GlassFillOpacity, acrylic.EffectiveFillOpacity, 3);
        Assert.Equal("acrylic", acrylic.Material);

        // Safe mode forces the opaque Tier 0 material (same rule as SkiaIslandThemeMapper).
        var safe = IslandPreviewModel.From(
            Baseline with
            {
                Material = IslandMaterial.Acrylic,
                FillOpacity = 0.9,
                Advanced = Baseline.Advanced with { SafeMode = true },
            },
            DarkAppearance);
        Assert.False(safe.Glass);
        Assert.Equal(0.9, safe.EffectiveFillOpacity, 3);

        // Tier 2: liquid glass stays translucent, but at its own fill level and with its own id.
        var liquid = IslandPreviewModel.From(
            Baseline with { Material = IslandMaterial.LiquidGlass, FillOpacity = 0.9 }, DarkAppearance);
        Assert.True(liquid.Glass);
        Assert.True(liquid.LiquidGlass);
        Assert.False(acrylic.LiquidGlass);
        Assert.Equal("liquid-glass", liquid.Material);
        Assert.Equal(IslandPreviewModel.LiquidGlassFillOpacity, liquid.EffectiveFillOpacity, 3);
        Assert.True(liquid.EffectiveFillOpacity < acrylic.EffectiveFillOpacity,
            "liquid glass should be clearer than acrylic");

        var liquidSafe = IslandPreviewModel.From(
            Baseline with
            {
                Material = IslandMaterial.LiquidGlass,
                Advanced = Baseline.Advanced with { SafeMode = true },
            },
            DarkAppearance);
        Assert.False(liquidSafe.Glass);
        Assert.False(liquidSafe.LiquidGlass);
        Assert.Equal("obsidian", liquidSafe.Material);
    }

    [Fact]
    public void FillOpacity_IsClampedToTheIslandFloor()
    {
        var model = IslandPreviewModel.From(Baseline with { FillOpacity = 0.1 }, DarkAppearance);
        Assert.Equal(LayoutTokens.MinFillOpacity, model.EffectiveFillOpacity, 3);

        var full = IslandPreviewModel.From(Baseline with { FillOpacity = 1.0 }, DarkAppearance);
        Assert.Equal(1.0, full.EffectiveFillOpacity, 3);
    }

    [Fact]
    public void Accent_FollowsTheCustomColour_AndResolvesTheSystemAccentInSystemMode()
    {
        var custom = IslandPreviewModel.From(
            Baseline with { AccentMode = "custom", AccentFallback = "#FF5A8A" }, DarkAppearance);
        Assert.Equal(Color.Parse("#FF5A8A"), custom.Accent);
        Assert.Equal("#FF5A8A", custom.AccentHex);

        var system = IslandPreviewModel.From(
            Baseline with { AccentMode = "system", AccentFallback = "#FF5A8A" },
            new SystemAppearanceSnapshot(false, false, "#0088FF"));
        Assert.Equal(Color.Parse("#0088FF"), system.Accent);

        // Artwork mode keeps the fallback colour: the preview is static, so this is what the island
        // shows before any artwork is sampled (documented next to the toggle).
        var artwork = IslandPreviewModel.From(
            Baseline with { AccentMode = "artwork", AccentFallback = "#4CC9F0" }, DarkAppearance);
        Assert.Equal(Color.Parse("#4CC9F0"), artwork.Accent);
    }

    [Fact]
    public void RadiusAndTextScale_AreClampedForThePreviewShell()
    {
        var model = IslandPreviewModel.From(
            Baseline with { PanelRadius = 30, FontScale = 1.5 }, DarkAppearance);
        Assert.Equal(30, model.RadiusFor(ShellHeight), 3);
        Assert.Equal(1.5, model.FontScale, 3);

        var huge = IslandPreviewModel.From(Baseline with { PanelRadius = 400 }, DarkAppearance);
        Assert.Equal(ShellHeight / 2, huge.RadiusFor(ShellHeight), 3);

        var tiny = IslandPreviewModel.From(Baseline with { FontScale = 9 }, DarkAppearance);
        Assert.Equal(1.6, tiny.FontScale, 3);
    }

    [Fact]
    public void DesktopTheme_DoesNotRepaintTheIslandPreview()
    {
        // The island is always Obsidian black (mapper contract); the theme setting only affects the
        // desktop, console and settings window. The UI states this next to the control.
        var dark = IslandPreviewModel.From(Baseline with { Theme = AppTheme.Dark }, DarkAppearance);
        var light = IslandPreviewModel.From(Baseline with { Theme = AppTheme.Light }, DarkAppearance);

        Assert.Equal(dark.Background, light.Background);
        Assert.Equal(dark.Foreground, light.Foreground);
        Assert.Equal(dark.Accent, light.Accent);
    }

    [Fact]
    public async Task PreviewControl_RendersTheLiquidGlassTierWithoutThrowing()
    {
        // The headless session has no pixels, but the render pass still runs the control's
        // DrawingContext calls — a broken gradient pen / clip would throw here instead of in the
        // settings window.
        var rendered = await AvaloniaTest.Run(() =>
        {
            var preview = new IslandPreview();
            preview.Update(
                Baseline with { Material = IslandMaterial.LiquidGlass, PanelRadius = 28 },
                TestFixtures.CreateLocalizer("en-US"));
            var window = new Window { Width = 620, Height = 180, Content = preview };
            window.Show();
            window.Measure(new Size(620, 180));
            window.Arrange(new Rect(0, 0, 620, 180));

            // Force a real drawing pass through the Avalonia render target (headless drawing otherwise
            // skips Render entirely, which would make this test vacuous).
            using (var target = new RenderTargetBitmap(new PixelSize(620, 180)))
            {
                target.Render(preview);
            }

            var model = preview.Model;
            var passes = preview.RenderCount;
            window.Close();
            return (Model: model, Passes: passes);
        });

        Assert.True(rendered.Passes > 0, "the liquid-glass preview never executed a render pass");
        Assert.True(rendered.Model.LiquidGlass);
        Assert.Equal("liquid-glass", rendered.Model.Material);
    }

    [Fact]
    public async Task PreviewControl_AppliesEveryMappedSettingAndNotifiesDerivedChrome()
    {
        // The headless test session draws nothing (UseHeadlessDrawing), so this asserts the control
        // state + the notification the pane uses to refresh its meta line; the pixel-level live
        // redraw is proven by the probe screenshots in the task evidence (real Win32 surface).
        var outcomes = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var preview = new IslandPreview();
            var notifications = 0;
            preview.ModelChanged += _ => notifications++;

            (IslandPreviewModel Model, int Notifications) Apply(AppSettings settings)
            {
                preview.Update(settings, localizer);
                return (preview.Model, notifications);
            }

            var baseline = Apply(Baseline).Model;
            var palette = Apply(Baseline with { Palette = "nord" }).Model;
            var material = Apply(Baseline with { Material = IslandMaterial.Acrylic }).Model;
            var accent = Apply(Baseline with { AccentFallback = "#FF5A8A" }).Model;
            var radius = Apply(Baseline with { PanelRadius = 36 }).Model;
            var fill = Apply(Baseline with { FillOpacity = LayoutTokens.MinFillOpacity }).Model;
            var fontScale = Apply(Baseline with { FontScale = 1.5 }).Model;
            var theme = Apply(Baseline with { Theme = AppTheme.Light }).Model;
            return new
            {
                Palette = palette.Background != baseline.Background,
                Material = material.Glass && material.EffectiveFillOpacity != baseline.EffectiveFillOpacity,
                Accent = accent.AccentHex != baseline.AccentHex,
                Radius = radius.PanelRadius != baseline.PanelRadius,
                Fill = fill.EffectiveFillOpacity != baseline.EffectiveFillOpacity,
                FontScale = fontScale.FontScale != baseline.FontScale,
                Theme = theme.Background == baseline.Background && theme.Foreground == baseline.Foreground,
                Notifications = notifications,
            };
        });

        Assert.True(outcomes.Palette, "palette must reach the preview model");
        Assert.True(outcomes.Material, "material must switch the preview to the glass tier");
        Assert.True(outcomes.Accent, "accent must reach the preview model");
        Assert.True(outcomes.Radius, "panel radius must reach the preview model");
        Assert.True(outcomes.Fill, "fill opacity must reach the preview model");
        Assert.True(outcomes.FontScale, "text scale must reach the preview model");
        Assert.True(outcomes.Theme, "the desktop theme is documented as not affecting the island preview");
        Assert.Equal(8, outcomes.Notifications);
    }
}
