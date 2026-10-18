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
        Assert.Equal(media.Width, state.ShellWidth, 1);
        Assert.Equal(media.Height, state.ShellHeight, 1);
        Assert.Equal("Midnight City", state.Compact.Value.Model.Title);
        Assert.Equal("M83", state.Compact.Value.Model.Subtitle);
        Assert.Equal(46, state.Compact.Value.Model.ProgressPercent, 1);
        Assert.Equal("#F06AB0", state.Compact.Value.Model.AccentHex);
        Assert.True(state.Compact.Value.Model.IsActive);
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
    public void ClickCompact_ExpandsToTheFixed440x284Shell()
    {
        using var fixture = ControllerFixture.Create();
        var compactShell = IslandMetrics.ShellRect(fixture.Controller.RenderState);

        var handled = fixture.Controller.HandlePointerPressed(compactShell.MidX, compactShell.MidY);
        Assert.True(handled);
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
        Assert.Equal(IslandPage.Media, fixture.Controller.State.Page);

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
        Assert.Equal(IslandPage.Home, fixture.Controller.State.Page);
    }

    [Fact]
    public void Wheel_OneNotchOnePage_WithCooldownAndLocking()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        var first = fixture.Controller.HandleWheel(PagerNotchDip, horizontal: false, shift: false);
        Assert.True(first.Paged);
        Assert.Equal(1, first.Page);
        fixture.RunToRest();

        var cooldown = fixture.Controller.HandleWheel(PagerNotchDip, horizontal: false, shift: false);
        Assert.False(cooldown.Paged);

        fixture.Time.Advance(TimeSpan.FromMilliseconds(200));
        var second = fixture.Controller.HandleWheel(PagerNotchDip, horizontal: false, shift: false);
        Assert.True(second.Paged);
        Assert.Equal(2, second.Page);
    }

    [Fact]
    public void Wheel_WhileTheSnapIsRunning_IsFrozenByTheScrollChain()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        var first = fixture.Controller.HandleWheel(PagerNotchDip, horizontal: false, shift: false);
        Assert.True(first.Paged);
        fixture.Time.Advance(TimeSpan.FromMilliseconds(500));

        // No Advance() between the two events: the snap spring is still running.
        var locked = fixture.Controller.HandleWheel(PagerNotchDip, horizontal: false, shift: false);
        Assert.False(locked.Paged);
        Assert.True(locked.Locked);
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
        var play = PageLayout.MediaPlay(mediaPage);
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

        var light = SkiaIslandThemeMapper.Map(AppSettings.Default with { Theme = AppTheme.Light });
        Assert.Equal(IslandThemeMode.Light, light.Mode);

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

    private const double PagerNotchDip = FluidBar.Core.Interaction.PagerConstants.WheelNotchDip;

    private sealed class ControllerFixture : IDisposable
    {
        private ControllerFixture(
            SkiaIslandController controller,
            SettingsController settings,
            DesignIslandDataSource data,
            FakeTimeProvider time)
        {
            Controller = controller;
            Settings = settings;
            Data = data;
            Time = time;
        }

        public SkiaIslandController Controller { get; }

        public SettingsController Settings { get; }

        public DesignIslandDataSource Data { get; }

        public FakeTimeProvider Time { get; }

        public static ControllerFixture Create(AppSettings? settings = null)
        {
            var time = new FakeTimeProvider();
            var localizer = new FakeLocalizer();
            var store = new InMemoryUiSettingsStore(settings ?? (AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 }));
            var settingsController = new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
            var data = new DesignIslandDataSource(localizer, time, static action => action());
            var controller = new SkiaIslandController(new SkiaIslandHostOptions
            {
                Localizer = localizer,
                Settings = settingsController,
                Data = data,
                TimeProvider = time,
                TargetFrameRate = 120,
            });
            controller.Start();
            return new ControllerFixture(controller, settingsController, data, time);
        }

        /// <summary>Snaps the springs to their targets and rebuilds the render state.</summary>
        public void Settle()
        {
            Controller.Motion.SnapToTarget();
            Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        /// <summary>Expands the island through a primary click and settles the shell.</summary>
        public void ExpandAndSettle()
        {
            var shell = IslandMetrics.ShellRect(Controller.RenderState);
            Controller.HandlePointerPressed(shell.MidX, shell.MidY);
            Settle();
        }

        /// <summary>Steps frames until every spring/pager/strip animation is at rest.</summary>
        public void RunToRest(int maxFrames = 900)
        {
            for (var i = 0; i < maxFrames && Controller.IsAnimating; i++)
            {
                Controller.Advance(TimeSpan.FromMilliseconds(16));
            }
        }

        public void Dispose()
        {
            Controller.Dispose();
            Data.Dispose();
            Settings.Dispose();
        }
    }

    /// <summary>Minimal localizer for headless mapping tests (no Avalonia asset loader).</summary>
    private sealed class FakeLocalizer : ILocalizer
    {
        private readonly Dictionary<string, string> _messages = new(StringComparer.Ordinal)
        {
            ["Page.Home.Title"] = "Control center",
            ["Page.Media.Title"] = "Now playing",
            ["Page.Notifications.Title"] = "Notifications",
            ["Page.Clipboard.Title"] = "Clipboard",
            ["Page.Agent.Title"] = "Agent activity",
            ["Page.Widgets.Title"] = "Widgets",
            ["Page.Widgets.Task.Review"] = "Review",
            ["Page.Widgets.Task.Ship"] = "Ship",
            ["Page.Widgets.Task.Inbox"] = "Inbox",
            ["Common.Next"] = "Next",
            ["Common.Charging"] = "Charging",
            ["Common.Copied"] = "Copied",
            ["Media.NowPlaying"] = "Now playing",
            ["Media.NoSession"] = "Nothing playing",
            ["Media.UnknownTitle"] = "Unknown title",
            ["Media.UnknownArtist"] = "Unknown artist",
            ["Agent.Running"] = "Running",
            ["Agent.Completed"] = "Completed",
            ["Agent.Title"] = "Agent",
            ["Battery.Low"] = "Low battery",
            ["Timer.Pomodoro"] = "Pomodoro",
            ["Activity.Idle"] = "Idle",
        };

        private string _culture = "en-US";

        public string Culture => _culture;

        public int Version { get; private set; }

        public event EventHandler? Changed;

        public IReadOnlyList<string> AvailableCultures => ["en-US", "zh-Hans"];

        public bool IsRightToLeft => false;

        public CultureInfo CurrentCulture => CultureInfo.GetCultureInfo(_culture);

        public string GetCultureDisplayName(string cultureTag) => cultureTag;

        public string Get(string key) => _messages.TryGetValue(key, out var value) ? value : key;

        public string Format(string key, IReadOnlyDictionary<string, object?> arguments) => Get(key);

        public string Format(string key, params (string Name, object? Value)[] arguments) => Get(key);

        public bool TryGet(string key, out string value) => _messages.TryGetValue(key, out value!);

        /// <summary>Switches the pseudo culture so title changes are observable (tests).</summary>
        public void SetCulture(string culture)
        {
            if (string.Equals(_culture, culture, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _culture = culture;
            // Non-English titles exercise the relocalization path without a real catalog.
            _messages["Page.Home.Title"] = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "控制中心" : "Control center";
            Version++;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
