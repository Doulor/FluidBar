using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Font scale (0.8–1.6) and live system-appearance coverage on the real controller: the render state
/// carries the setting, compact pill sizes grow with it, overflow pages scroll, and a WM_SETTINGCHANGE
/// refresh re-resolves theme / accent / reduced motion without restarting the island.
/// </summary>
public sealed class FontScaleAndAppearanceTests
{
    [Fact]
    public void Render_state_carries_the_font_scale_and_scaled_compact_pill()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 0 });
        fixture.Settle();

        Assert.Equal(1.6, fixture.Controller.RenderState.FontScale, 3);
        var token = LayoutTokens.CompactSize(fixture.Controller.RenderState.Compact?.Kind);
        var expected = LayoutTokens.CompactSizeFor(fixture.Controller.RenderState.Compact?.Kind, IslandMode.Float, 1.6);
        Assert.Equal(expected.Width, fixture.Controller.RenderState.ShellWidth, 1);
        Assert.Equal(expected.Height, fixture.Controller.RenderState.ShellHeight, 1);
        Assert.True(fixture.Controller.RenderState.ShellWidth > token.Width);
    }

    [Fact]
    public void Font_scale_changes_apply_live_without_restarting_the_island()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0 });
        fixture.Settle();
        Assert.Equal(1.0, fixture.Controller.RenderState.FontScale, 3);

        fixture.Settings.Update(s => s with { FontScale = 0.8 });
        fixture.Controller.Motion.SnapToTarget();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(0.8, fixture.Controller.RenderState.FontScale, 3);
        var expected = LayoutTokens.CompactSizeFor(fixture.Controller.RenderState.Compact?.Kind, IslandMode.Float, 0.8);
        Assert.Equal(expected.Width, fixture.Controller.RenderState.ShellWidth, 1);
    }

    [Fact]
    public void Large_font_pages_scroll_with_the_wheel_before_turning_the_page()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { FontScale = 1.6, AutoCollapseMs = 0 });
        fixture.ExpandAndSettle();
        fixture.GoToPage(0);
        var page = (int)fixture.Controller.State.Page;
        Assert.Equal(0, page);

        var outcome = fixture.Controller.HandleWheel(PagerConstants.WheelNotchDip, horizontal: false, shift: false);
        Assert.True(outcome.Consumed);
        Assert.True(fixture.Controller.RenderState.Pages.Home.ScrollOffset > 0,
            "home content taller than the body must scroll instead of switching pages");
        Assert.Equal(0, fixture.Controller.RenderState.PageIndex);
    }

    [Fact]
    public void Normal_font_pages_still_turn_with_the_wheel()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0 });
        fixture.ExpandAndSettle();
        fixture.GoToPage(0);

        for (var i = 0; i < 3 && fixture.Controller.State.Page == FluidBar.Core.Activities.IslandPage.Home; i++)
        {
            fixture.Controller.HandleWheel(PagerConstants.WheelNotchDip, horizontal: false, shift: false);
            Assert.Equal(0, fixture.Controller.RenderState.Pages.Home.ScrollOffset, 3);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(PagerConstants.WheelCooldownMs + 40));
            fixture.RunToRest();
        }

        Assert.Equal(FluidBar.Core.Activities.IslandPage.Media, fixture.Controller.State.Page);
    }

    [Fact]
    public void Live_system_appearance_refresh_updates_theme_accent_and_motion()
    {
        var appearance = new SystemAppearanceSnapshot(false, false, null);
        using var fixture = ControllerFixture.Create(
            AppSettings.Default with
            {
                Theme = AppTheme.System,
                ReducedMotion = ReducedMotionPreference.System,
                AccentMode = "system",
                AccentFallback = "#4CC9F0",
                AutoCollapseMs = 0,
            },
            appearance: () => appearance);
        fixture.Settle();

        Assert.Equal(IslandThemeMode.Dark, fixture.Controller.RenderState.Theme.Mode);
        Assert.False(fixture.Controller.Motion.ReducedMotion);
        AssertAccent(0x4C, 0xC9, 0xF0, fixture.Controller.RenderState.Theme.Accent, fixture);

        // The user switches Windows to the light theme, disables animations and changes the accent.
        appearance = new SystemAppearanceSnapshot(true, true, "#FF5A00");
        fixture.Controller.RefreshSystemAppearance();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        // Live appearance refresh still updates accent + reduced motion, but the island stays Obsidian:
        // the desktop theme only affects windows.
        Assert.Equal(IslandThemeMode.Dark, fixture.Controller.RenderState.Theme.Mode);
        Assert.True(fixture.Controller.Motion.ReducedMotion);
        AssertAccent(0xFF, 0x5A, 0x00, fixture.Controller.RenderState.Theme.Accent, fixture);
    }

    [Fact]
    public void Appearance_refresh_keeps_explicit_settings_untouched()
    {
        var appearance = new SystemAppearanceSnapshot(true, true, "#FF5A00");
        using var fixture = ControllerFixture.Create(
            AppSettings.Default with
            {
                Theme = AppTheme.Dark,
                ReducedMotion = ReducedMotionPreference.Off,
                AccentMode = "custom",
                AccentFallback = "#22CC88",
                AutoCollapseMs = 0,
            },
            appearance: () => appearance);
        fixture.Settle();

        fixture.Controller.RefreshSystemAppearance();
        fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));

        Assert.Equal(IslandThemeMode.Dark, fixture.Controller.RenderState.Theme.Mode);
        Assert.False(fixture.Controller.Motion.ReducedMotion);
        AssertAccent(0x22, 0xCC, 0x88, fixture.Controller.RenderState.Theme.Accent, fixture);
    }

    private static void AssertAccent(byte red, byte green, byte blue, SkiaSharp.SKColor accent, ControllerFixture fixture)
    {
        Assert.True(
            accent.Red == red && accent.Green == green && accent.Blue == blue && accent.Alpha == 0xFF,
            $"accent expected #{red:X2}{green:X2}{blue:X2} but was #{accent.Red:X2}{accent.Green:X2}{accent.Blue:X2} (a={accent.Alpha:X2}, mode={fixture.Settings.Current.AccentMode})");
    }
}
