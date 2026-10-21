using System.Globalization;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;
using AppTheme = FluidBar.App.Settings.AppTheme;
using AppMaterial = FluidBar.App.Settings.IslandMaterial;
using IslandThemeMode = FluidBar.Island.IslandThemeMode;
using IslandMaterial = FluidBar.Island.IslandMaterial;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless tests for the Skia island host path: Core state → <see cref="IslandRenderState"/> mapping,
/// compact kind/size tokens, pager input policies, archipelago reveal/dots and theme mapping. No
/// Avalonia window, no HWND, no UI thread is required.
/// </summary>
public sealed class SkiaIslandHostMappingTests
{
    [Fact]
    public void Start_RevealsIdleCompact_AtTheCoreSizeToken()
    {
        using var fixture = ControllerFixture.Create();

        var state = fixture.Controller.RenderState;
        Assert.Equal(ShellForm.Compact, state.Form);
        Assert.Equal(LayoutTokens.CompactSize(null).Width, state.ShellWidth, 1);
        Assert.Equal(LayoutTokens.CompactSize(null).Height, state.ShellHeight, 1);
        Assert.Equal(ActivityKind.Idle, state.Compact!.Value.Kind);
        Assert.False(string.IsNullOrEmpty(state.Compact.Value.Model.ClockText));
        Assert.True(state.IsVisible);
        // Top-center float: the compact pill is centred inside the fixed 488 DIP host surface.
        Assert.Equal(LayoutTokens.WindowPadding + ((LayoutTokens.ExpandedWidth - state.ShellWidth) / 2), state.ShellOrigin.X, 1);
        Assert.Equal(LayoutTokens.WindowPadding, state.ShellOrigin.Y, 1);
    }

    [Fact]
    public void MediaActivity_MapsToCompactMediaToken_AndPresenterFields()
    {
        using var fixture = ControllerFixture.Create();

        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Midnight City",
                Subtitle = "M83",
                ProgressPercent = 46,
                AccentColorHex = "#F06AB0",
                IsActive = true,
                ShowsAudioWave = true,
                PositionTicks = TimeSpan.FromSeconds(112).Ticks,
                EndTicks = TimeSpan.FromSeconds(243).Ticks,
                LyricLine = "Waiting for a car to drive me home",
                SecondaryLyricLine = "The city is my church",
            },
            sequence: 1);
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        var media = LayoutTokens.CompactSize(ActivityKind.Media);
        Assert.Equal(ActivityKind.Media, state.Compact!.Value.Kind);
        // "Lyrics in the pill" is on by default: a known lyric widens the pill past the token, while the
        // token remains the floor and the expanded width the ceiling.
        Assert.True(state.ShellWidth >= media.Width - 0.5, $"width {state.ShellWidth} below the token");
        Assert.True(state.ShellWidth <= LayoutTokens.ExpandedWidth + 0.5, $"width {state.ShellWidth} above the surface");
        Assert.True(state.ShellWidth > media.Width + 1, "a known lyric should widen the pill");
        Assert.Equal(media.Height, state.ShellHeight, 1);
        Assert.Equal("Midnight City", state.Compact.Value.Model.Title);
        Assert.Equal("M83", state.Compact.Value.Model.Subtitle);
        Assert.Equal(46, state.Compact.Value.Model.ProgressPercent, 1);
        Assert.Equal("#F06AB0", state.Compact.Value.Model.AccentHex);
        Assert.True(state.Compact.Value.Model.IsActive);
    }

    [Fact]
    public void MediaActivity_WithLyricsSettingOff_KeepsTheCompactMediaToken()
    {
        using var fixture = ControllerFixture.Create(mediaPill: MediaPillPreferences.Off);

        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload
            {
                Title = "Midnight City",
                Subtitle = "M83",
                IsActive = true,
                LyricLine = "Waiting for a car to drive me home",
            },
            sequence: 1);
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        var media = LayoutTokens.CompactSize(ActivityKind.Media);
        Assert.Equal(media.Width, state.ShellWidth, 1);
        Assert.False(state.Compact!.Value.Model.ShowLyrics);
    }

    [Fact]
    public void HudAndNotification_Kinds_UseTheirOwnTokens_AndModels()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(
            ActivityKind.Volume,
            new ActivityPayload { Level = 62, IsActive = true },
            sequence: 1);
        fixture.Settle();

        var hud = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Volume, hud.Compact!.Value.Kind);
        Assert.Equal(LayoutTokens.CompactSize(ActivityKind.Volume).Width, hud.ShellWidth, 1);
        Assert.Equal(62, hud.Compact.Value.Model.Level);

        fixture.Controller.RetractActivity(ActivityKind.Volume, sequence: 2);
        fixture.Settle();
        fixture.Controller.PublishActivity(
            ActivityKind.Notification,
            new ActivityPayload { Title = "Design review in 5 minutes", SourceName = "Teams", VerificationCode = "418302" },
            sequence: 1);
        fixture.Settle();

        var notification = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Notification, notification.Compact!.Value.Kind);
        Assert.Equal(LayoutTokens.CompactSize(ActivityKind.Notification).Width, notification.ShellWidth, 1);
        Assert.Equal("Teams", notification.Compact.Value.Model.AppName);
        Assert.Equal("418302", notification.Compact.Value.Model.VerificationCode);
    }

    [Fact]
    public void ClickCompact_ExpandsToTheFixed440x244Shell()
    {
        using var fixture = ControllerFixture.Create();
        var compactShell = IslandMetrics.ShellRect(fixture.Controller.RenderState);

        var handled = fixture.Controller.HandlePointerPressed(compactShell.MidX, compactShell.MidY);
        Assert.True(handled);
        fixture.Controller.HandlePointerReleased(compactShell.MidX, compactShell.MidY);
        Assert.Equal(IslandStateKind.Expanded, fixture.Controller.State.Kind);

        fixture.Settle();
        var state = fixture.Controller.RenderState;
        Assert.Equal(ShellForm.Expanded, state.Form);
        Assert.Equal(LayoutTokens.ExpandedWidth, state.ShellWidth, 1);
        Assert.Equal(LayoutTokens.ExpandedHeight, state.ShellHeight, 1);
        Assert.Equal(new VisualPoint(LayoutTokens.WindowPadding, LayoutTokens.WindowPadding), state.ShellOrigin);
        Assert.Equal(1, state.ContentOpacity, 2);
    }

    [Fact]
    public void Escape_CollapsesBackToTheCompactToken()
    {
        using var fixture = ControllerFixture.Create();
        var compactShell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        fixture.Controller.HandlePointerPressed(compactShell.MidX, compactShell.MidY);
        fixture.Settle();

        fixture.Controller.HandleEscape();
        fixture.Settle();

        Assert.Equal(IslandStateKind.Compact, fixture.Controller.State.Kind);
        Assert.Equal(ShellForm.Compact, fixture.Controller.RenderState.Form);
        Assert.Equal(LayoutTokens.CompactSize(null).Width, fixture.Controller.RenderState.ShellWidth, 1);
    }

    [Fact]
    public void PagerDrag_LeftByThreeQuartersOfAPage_AdvancesOnePage_AndUpdatesTheState()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        fixture.Controller.HandleDragStarted(300, 90, timestampMs: 0);
        Assert.True(fixture.Controller.Pager.IsDragging);

        fixture.Controller.HandleDragMoved(-100, 90, timestampMs: 16);
        fixture.Controller.HandleDragEnded(-100, 90, timestampMs: 32);

        Assert.Equal(1, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(PageRef.Media, fixture.Controller.State.Page);

        fixture.RunToRest();
        Assert.Equal(1, fixture.Controller.RenderState.Pager.PageIndex);
        Assert.Equal(1, fixture.Controller.RenderState.Pager.PagePosition, 1);
    }

    [Fact]
    public void PagerDrag_SmallMovement_SnapsBackWithoutChangingPage()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        fixture.Controller.HandleDragStarted(200, 200, timestampMs: 0);
        fixture.Controller.HandleDragMoved(190, 200, timestampMs: 16);
        fixture.Controller.HandleDragEnded(190, 200, timestampMs: 32);

        Assert.Equal(0, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(PageRef.Home, fixture.Controller.State.Page);
    }

    [Fact]
    public void Wheel_TwoNotchesOnePage_WithCooldownAndLocking()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        // One physical notch is half a page step (70 of the 140 DIP prototype wheelStep): it is
        // consumed but does not turn the page — prototype2's 100 px Chrome notch also falls short of
        // its 140 px wheelStep, so a page needs two notches there as well.
        var first = fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        Assert.False(first.Paged);
        Assert.Equal(0, fixture.Controller.Pager.SelectedIndex);

        var second = fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        Assert.True(second.Paged);
        Assert.Equal(1, second.Page);
        fixture.RunToRest();

        // Two more notches inside the 180 ms cooldown accumulate but must not flip a page.
        Assert.False(fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false).Paged);
        Assert.False(fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false).Paged);

        // After the cooldown the held pair commits the next page.
        fixture.Time.Advance(TimeSpan.FromMilliseconds(FluidBar.Core.Interaction.PagerConstants.WheelCooldownMs + 40));
        var third = fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        Assert.True(third.Paged);
        Assert.Equal(2, third.Page);
    }

    [Fact]
    public void Wheel_RepeatsArePacedByTheCooldownWindow()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        var first = fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        Assert.True(first.Paged);

        // The next pair lands inside the cooldown: both notches are held, no page step.
        Assert.False(fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false).Paged);
        Assert.False(fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false).Paged);

        // After the cooldown the held pair is a fresh page step (prototype2 pacing); the old behaviour
        // froze wheel input until the snap spring finished, which swallowed gestures.
        fixture.Time.Advance(TimeSpan.FromMilliseconds(500));
        var next = fixture.Controller.HandleWheel(PerNotchDip, horizontal: false, shift: false);
        Assert.True(next.Paged);
    }

    [Fact]
    public void Archipelago_RevealsOnDrag_ShowsPagerDots_AndAutoHides()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        fixture.Controller.HandleDragStarted(300, 90, timestampMs: 0);
        Assert.True(fixture.Controller.Archipelago.IsVisible);
        fixture.Controller.HandleDragMoved(-100, 90, timestampMs: 16);
        fixture.Controller.HandleDragEnded(-100, 90, timestampMs: 32);
        fixture.RunToRest();

        var strip = fixture.Controller.RenderState.Archipelago;
        Assert.True(strip.Visible);
        Assert.Equal(1, fixture.Controller.Pager.SelectedIndex);
        Assert.Equal(0, strip.ResolvedFarLeftDots);
        Assert.Equal(3, strip.ResolvedFarRightDots);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(SkiaArchipelago.HideDelayMs + 100));
        fixture.Controller.PumpTimers();
        Assert.False(fixture.Controller.Archipelago.IsVisible);
    }

    [Fact]
    public void Archipelago_RevealsAfterHotZoneDwell_ThenHidesWhenThePointerLeaves()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
        // Bottom 12 DIP hot zone of the expanded shell.
        fixture.Controller.HandlePointerMoved(shell.MidX, shell.Bottom - 4);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(SkiaArchipelago.RevealDelayMs + 20));
        fixture.Controller.PumpTimers();
        Assert.True(fixture.Controller.Archipelago.IsVisible);

        fixture.Controller.HandlePointerLeft();
        fixture.Time.Advance(TimeSpan.FromMilliseconds(SkiaArchipelago.HideDelayMs + 100));
        fixture.Controller.PumpTimers();
        Assert.False(fixture.Controller.Archipelago.IsVisible);
    }

    [Fact]
    public void StripGeometry_MatchesTheRendererLayout()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.Controller.Pager.SelectPage(2);
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.RunToRest();

        var state = fixture.Controller.RenderState;
        using var renderer = new IslandRenderer();
        var expected = renderer.MeasureStrip(state);
        var actual = SkiaIslandHitTest.MeasureStrip(state);

        Assert.Equal(expected.Strip, actual.Strip);
        Assert.Equal(expected.Current, actual.Current);
        Assert.Equal(expected.Previous, actual.Previous);
        Assert.Equal(expected.Next, actual.Next);
        Assert.Equal(expected.Page, actual.Page);
        Assert.Equal(expected.DotsLeft.Length, actual.DotsLeft.Length);
        Assert.Equal(expected.DotsRight.Length, actual.DotsRight.Length);
    }

    [Fact]
    public void HitTest_CompactExpandedAndStrip_ReturnsTheExpectedRegions()
    {
        using var fixture = ControllerFixture.Create();
        var compact = fixture.Controller.RenderState;
        var shell = IslandMetrics.ShellRect(compact);
        Assert.Equal(IslandHitKind.Compact, SkiaIslandHitTest.Hit(compact, shell.MidX, shell.MidY).Kind);

        fixture.ExpandAndSettle();
        var expanded = fixture.Controller.RenderState;
        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(expanded));
        var collapse = PageLayout.CollapseRect(content);
        Assert.Equal(IslandHitKind.Collapse, SkiaIslandHitTest.Hit(expanded, collapse.MidX, collapse.MidY).Kind);

        fixture.Controller.Pager.SelectPage(1);
        fixture.RunToRest();
        var media = fixture.Controller.RenderState;
        var mediaPage = IslandMetrics.PageRect(media, 1);
        var play = PageMetricsLayout.MediaPlay(mediaPage, FontScaleMetrics.From(media.FontScale));
        var playHit = SkiaIslandHitTest.Hit(media, play.MidX, play.MidY);
        Assert.Equal(IslandHitKind.PageControl, playHit.Kind);
        Assert.Equal(IslandPageControl.PlayPause, playHit.Control);
    }

    [Fact]
    public void ThemeMapping_AppliesLightMaterialAccentAndFillClamp()
    {
        var dark = SkiaIslandThemeMapper.Map(AppSettings.Default);
        Assert.Equal(IslandThemeMode.Dark, dark.Mode);
        Assert.Equal(IslandMaterial.Obsidian, dark.Material);

        // The desktop theme never repaints the island (it stays Obsidian by design); only the palette does.
        var light = SkiaIslandThemeMapper.Map(AppSettings.Default with { Theme = AppTheme.Light });
        Assert.Equal(IslandThemeMode.Dark, light.Mode);

        var acrylic = SkiaIslandThemeMapper.Map(AppSettings.Default with { Material = AppMaterial.Acrylic });
        Assert.Equal(IslandMaterial.SimulatedGlass, acrylic.Material);

        var accent = SkiaIslandThemeMapper.Map(AppSettings.Default with { AccentMode = "custom", AccentFallback = "#112233" });
        Assert.Equal(new SKColor(0x11, 0x22, 0x33), accent.Accent);

        var fill = SkiaIslandThemeMapper.Map(AppSettings.Default with { FillOpacity = 0.10 });
        Assert.Equal(LayoutTokens.MinFillOpacity, fill.FillOpacity, 3);
    }

    [Fact]
    public void ThemeMapping_ReducedMotion_IsResolvedFromSettings()
    {
        Assert.True(SkiaIslandThemeMapper.ReducedMotion(AppSettings.Default with { ReducedMotion = ReducedMotionPreference.On }));
        Assert.False(SkiaIslandThemeMapper.ReducedMotion(AppSettings.Default with { ReducedMotion = ReducedMotionPreference.Off }));
        Assert.False(SkiaIslandThemeMapper.ReducedMotion(AppSettings.Default));
    }

    [Fact]
    public void ReducedMotion_DisablesTheWaveFrameLoop()
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
            ReducedMotion = ReducedMotionPreference.On,
        });
        fixture.Controller.PublishActivity(
            ActivityKind.Media,
            new ActivityPayload { Title = "Midnight City", IsActive = true, ShowsAudioWave = true },
            sequence: 1);
        fixture.Settle();
        // The media activity also navigates the pager to the media page; let that snap settle.
        fixture.RunToRest();

        Assert.False(fixture.Controller.IsAnimating, fixture.Controller.Describe());
        Assert.False(fixture.Controller.IsWaveOnly, fixture.Controller.Describe());
    }

    [Fact]
    public void SettingsModeChange_RetargetsTheDockMode_AndRequestsPlacement()
    {
        using var fixture = ControllerFixture.Create();
        var placementRequests = 0;
        fixture.Controller.PlacementRequested += () => placementRequests++;

        fixture.Settings.Update(s => s with { Mode = "dock" });

        Assert.Equal(IslandMode.Dock, fixture.Controller.Motion.Target.Mode);
        Assert.Equal(1, placementRequests);
        fixture.Settle();
        Assert.Equal(IslandMode.Dock, fixture.Controller.RenderState.Mode);
        // Dock compact is the 440x52 strip.
        Assert.Equal(LayoutTokens.DockStripWidth, fixture.Controller.RenderState.ShellWidth, 1);
        Assert.Equal(LayoutTokens.DockStripHeight, fixture.Controller.RenderState.ShellHeight, 1);
    }

    [Fact]
    public void HomePage_UsesRealVolumeAndBrightness_AndFlagsUnknownLevels()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Data.Update(content => content with
        {
            VolumePercent = 37,
            IsMuted = true,
            BrightnessPercent = null,
        });
        fixture.Settle();

        var home = fixture.Controller.RenderState.Pages.Home;
        Assert.Equal(37, home.Volume);
        Assert.True(home.HasVolume);
        Assert.True(home.IsMuted);
        // Unknown brightness must not surface a demo number.
        Assert.Equal(0, home.Brightness);
        Assert.False(home.HasBrightness);
    }

    /// <summary>One physical wheel notch (70 DIP; two notches make the 140 DIP page step).</summary>
    private const double PerNotchDip = FluidBar.Core.Interaction.PagerConstants.WheelDipPerNotch;
}


