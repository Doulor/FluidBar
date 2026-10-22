using FluidBar.App.Hosting;
using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless page-control tests for the Skia island: every control the renderer paints must map to a
/// semantic <see cref="IslandPlatformAction"/> and be dispatched through the fake
/// <see cref="IIslandPlatformActions"/> seam — no Avalonia, no Win32, no UI thread.
/// </summary>
public sealed class SkiaIslandControlTests
{
    [Fact]
    public void MediaTransport_Controls_MapToPlatformActions()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var page = Page(1, fixture);
        var metrics = FontScaleMetrics.From(fixture.Controller.RenderState.FontScale);
        Click(fixture, PageMetricsLayout.MediaPlay(page, metrics));
        Click(fixture, PageMetricsLayout.MediaPrevious(page, metrics));
        Click(fixture, PageMetricsLayout.MediaNext(page, metrics));

        Assert.Equal(
            new[]
            {
                IslandPlatformActionKind.MediaTogglePlay,
                IslandPlatformActionKind.MediaPrevious,
                IslandPlatformActionKind.MediaNext,
            },
            actions.Calls.Select(call => call.Kind));
    }

    [Fact]
    public void MediaSeek_MapsPointerXToAFractionOfTheTrack()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var track = PageLayout.MediaSeekTrack(Page(1, fixture));
        fixture.Controller.HandlePointerPressed(track.Left + (track.Width * 0.75), track.MidY);

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.MediaSeek, call.Kind);
        Assert.Equal(0.75, call.Value, 2);
    }

    [Fact]
    public void MediaSeek_ClampsOutsideTheTrackToTheEnds()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var page = Page(1, fixture);
        var track = PageLayout.MediaSeekTrack(page);
        // Inside the seek row but left of the track: fraction clamps to 0.
        fixture.Controller.HandlePointerPressed(page.Left + 5, track.MidY);
        // Inside the seek row but right of the track: fraction clamps to 1.
        fixture.Controller.HandlePointerPressed(page.Right - 5, track.MidY);

        Assert.Equal(2, actions.Calls.Count);
        Assert.Equal(0, actions.Calls[0].Value, 3);
        Assert.Equal(1, actions.Calls[1].Value, 3);
    }

    [Fact]
    public void HomeSliders_MapFractionsToVolumeAndBrightness()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();

        var page = Page(0, fixture);
        var volume = PageLayout.HomeVolumeTrack(page);
        var brightness = PageLayout.HomeBrightnessTrack(page);
        fixture.Controller.HandlePointerPressed(volume.Left + (volume.Width * 0.25), volume.MidY);
        fixture.Controller.HandlePointerPressed(brightness.Left + (brightness.Width * 0.80), brightness.MidY);

        Assert.Equal(
            new[] { IslandPlatformActionKind.VolumeSet, IslandPlatformActionKind.BrightnessSet },
            actions.Calls.Select(call => call.Kind));
        Assert.Equal(0.25, actions.Calls[0].Value, 2);
        Assert.Equal(0.80, actions.Calls[1].Value, 2);
    }

    [Fact]
    public void HomeQuickToggles_MapToSemanticToggleIdentities()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();

        var page = Page(0, fixture);
        var metrics = FontScaleMetrics.From(fixture.Controller.RenderState.FontScale);
        var toggles = fixture.Controller.RenderState.Pages.Home.Toggles;
        for (var i = 0; i < toggles.Count; i++)
        {
            if (toggles[i].IsEnabled)
            {
                Click(fixture, PageMetricsLayout.HomeToggle(page, i, metrics));
            }
        }

        // Focus Assist is a Limited Access Feature on this machine: its toggle is posed as disabled,
        // has no hit target, and therefore never reaches the action queue (no dead button).
        Assert.False(toggles[2].IsEnabled);
        Assert.Equal(3, actions.Calls.Count);
        Assert.All(actions.Calls, call => Assert.Equal(IslandPlatformActionKind.QuickToggle, call.Kind));
        Assert.Equal(
            new[] { IslandQuickToggle.Wifi, IslandQuickToggle.Bluetooth, IslandQuickToggle.Theme },
            actions.Calls.Select(call => call.Toggle));
    }

    [Fact]
    public void HomeMediaCard_OpensTheMediaPage_WithoutAPlatformAction()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();

        var card = PageLayout.HomeMediaCard(Page(0, fixture));
        // The play button is the right-hand control; the card body is navigation.
        fixture.Controller.HandlePointerPressed(card.Left + 80, card.MidY);
        fixture.RunToRest();

        Assert.Equal(PageRef.Media, fixture.Controller.State.Page);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void NotificationRowsAndDismiss_MapToRowActions()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);

        var page = Page(2, fixture);
        var metrics = FontScaleMetrics.From(fixture.Controller.RenderState.FontScale);
        Click(fixture, PageMetricsLayout.NotificationRow(page, 0, 0, metrics));
        Click(fixture, PageMetricsLayout.NotificationDismiss(page, 1, 0, metrics));

        Assert.Equal(2, actions.Calls.Count);
        Assert.Equal(IslandPlatformActionKind.NotificationActivate, actions.Calls[0].Kind);
        Assert.Equal(0, actions.Calls[0].Index);
        Assert.Equal(IslandPlatformActionKind.NotificationDismiss, actions.Calls[1].Kind);
        Assert.Equal(1, actions.Calls[1].Index);
    }

    [Fact]
    public void ClipboardRowsPinAndCopy_MapToRowActions()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);

        var page = Page(3, fixture);
        Click(fixture, PageLayout.ClipboardPin(page, 0));
        Click(fixture, PageLayout.ClipboardCopy(page, 1));
        Click(fixture, PageLayout.ClipboardRow(page, 2));

        Assert.Equal(3, actions.Calls.Count);
        Assert.Equal(IslandPlatformActionKind.ClipboardPin, actions.Calls[0].Kind);
        Assert.Equal(0, actions.Calls[0].Index);
        Assert.Equal(IslandPlatformActionKind.ClipboardCopy, actions.Calls[1].Kind);
        Assert.Equal(1, actions.Calls[1].Index);
        Assert.False(string.IsNullOrEmpty(actions.Calls[1].Text));
        Assert.Equal(IslandPlatformActionKind.ClipboardCopy, actions.Calls[2].Kind);
        Assert.Equal(2, actions.Calls[2].Index);
        // Row 2 is the image row: it maps to copy with no text so the seam can report it unsupported.
        Assert.Null(actions.Calls[2].Text);
    }

    [Fact]
    public void AgentRows_MapToAgentActivate()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(4);

        Click(fixture, PageLayout.AgentRow(Page(4, fixture), 1));

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.AgentActivate, call.Kind);
        Assert.Equal(1, call.Index);
    }

    [Fact]
    public void WidgetsCalendarAndTasks_MapToCalendarAndTaskActions()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(5);

        var page = Page(5, fixture);
        Click(fixture, PageLayout.WidgetsCalendarNav(page, previous: true));
        Click(fixture, PageLayout.WidgetsCalendarNav(page, previous: false));
        Click(fixture, PageLayout.WidgetsTaskRow(page, 0));

        Assert.Equal(3, actions.Calls.Count);
        Assert.Equal(IslandPlatformActionKind.CalendarNavigate, actions.Calls[0].Kind);
        Assert.Equal(-1, actions.Calls[0].Value);
        Assert.Equal(IslandPlatformActionKind.CalendarNavigate, actions.Calls[1].Kind);
        Assert.Equal(1, actions.Calls[1].Value);
        Assert.Equal(IslandPlatformActionKind.TaskToggle, actions.Calls[2].Kind);
        Assert.Equal(0, actions.Calls[2].Index);
    }

    [Fact]
    public void PageModels_MapContentRows_SoRowsAreHitTestable()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();

        var pages = fixture.Controller.RenderState.Pages;
        Assert.Equal(3, pages.Notifications.Items.Count);
        Assert.Equal(3, pages.Clipboard.Items.Count);
        Assert.Equal(2, pages.Agent.Items.Count);
        Assert.Equal(3, pages.Widgets.Tasks.Count);
        Assert.Equal(42, pages.Widgets.CalendarDays.Count);
    }

    [Fact]
    public void ThemeToggle_ReflectsTheSettingsTheme()
    {
        using var dark = ControllerFixture.Create();
        dark.ExpandAndSettle();
        Assert.True(dark.Controller.RenderState.Pages.Home.Toggles[3].IsOn);

        using var light = ControllerFixture.Create(AppSettings.Default with { Theme = AppTheme.Light });
        light.ExpandAndSettle();
        Assert.False(light.Controller.RenderState.Pages.Home.Toggles[3].IsOn);
    }

    [Fact]
    public void UnsupportedSeamResult_LogsControlUnsupported_NotUnmapped()
    {
        var actions = new RecordingIslandPlatformActions
        {
            ResultFactory = _ => IslandActionResult.Unsupported("no audio endpoint"),
        };
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();

        var page = Page(0, fixture);
        var volume = PageLayout.HomeVolumeTrack(page);
        Click(fixture, volume);

        Assert.True(fixture.Logger.HasEvent("island.skia.control-unsupported"));
        Assert.False(fixture.Logger.HasEvent("island.skia.control-unmapped"));
    }

    [Fact]
    public void DeferredSeamResult_LogsControlDeferred()
    {
        var actions = new RecordingIslandPlatformActions
        {
            ResultFactory = _ => IslandActionResult.Deferred("App-owned list state"),
        };
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);

        var metrics = FontScaleMetrics.From(fixture.Controller.RenderState.FontScale);
        Click(fixture, PageMetricsLayout.NotificationRow(Page(2, fixture), 0, 0, metrics));

        Assert.True(fixture.Logger.HasEvent("island.skia.control-deferred"));
        Assert.False(fixture.Logger.HasEvent("island.skia.control-unsupported"));
    }

    [Fact]
    public void WithoutASeam_MediaUsesTheLegacyEvent_AndOtherControlsReportUnsupported()
    {
        using var fixture = ControllerFixture.Create();
        MediaTransportCommand? command = null;
        fixture.Controller.MediaCommandRequested += value => command = value;
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);
        var metrics = FontScaleMetrics.From(fixture.Controller.RenderState.FontScale);
        Click(fixture, PageMetricsLayout.MediaPlay(Page(1, fixture), metrics));
        Assert.Equal(MediaTransportCommand.TogglePlay, command);

        fixture.GoToPage(0);
        var home = Page(0, fixture);
        Click(fixture, PageLayout.HomeVolumeTrack(home));
        Assert.True(fixture.Logger.HasEvent("island.skia.control-unsupported"));
        Assert.False(fixture.Logger.HasEvent("island.skia.control-unmapped"));
    }

    [Fact]
    public void HitTargetToActionMapping_RejectsOutOfRangeRowsAndNonActions()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);

        var state = fixture.Controller.RenderState;
        var row = new IslandHitTarget(IslandHitKind.PageControl, 2, IslandPageControl.Row, 99);
        Assert.Null(SkiaIslandControlRouter.Map(row, state, 0));

        var scroll = new IslandHitTarget(IslandHitKind.PageControl, 2, IslandPageControl.Scroll);
        Assert.Null(SkiaIslandControlRouter.Map(scroll, state, 0));

        var validRow = new IslandHitTarget(IslandHitKind.PageControl, 2, IslandPageControl.Row, 0);
        var action = SkiaIslandControlRouter.Map(validRow, state, 0);
        Assert.NotNull(action);
        Assert.Equal(IslandPlatformActionKind.NotificationActivate, action!.Value.Kind);
    }

    [Fact]
    public void HitTest_DoesNotReportRowsBeyondTheModel()
    {
        using var fixture = ControllerFixture.Create();
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);

        var state = fixture.Controller.RenderState;
        var page = IslandMetrics.PageRect(state, 2);
        // Row index 3 does not exist (three notification rows); the empty band below the last card is
        // still the list's scroll region.
        var body = PageMetricsLayout.NotificationBody(page);
        var hit = SkiaIslandHitTest.Hit(state, body.MidX, body.Bottom - 2);

        Assert.Equal(IslandHitKind.PageControl, hit.Kind);
        Assert.Equal(IslandPageControl.Scroll, hit.Control);
    }

    [Fact]
    public void Notification_takes_the_normal_island_position_and_activates_the_source_app()
    {
        var actions = new RecordingIslandPlatformActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        fixture.Controller.PublishActivity(ActivityKind.Media, new ActivityPayload
        {
            Title = "Midnight City",
            Subtitle = "M83",
            IsActive = true,
            ProgressPercent = 42,
        }, 1, "test");
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "GitHub",
            Body = "Run failed: Export Live Data",
            SourceName = "GitHub",
        }, 2, "test");
        fixture.Settle();
        fixture.RunToRest();
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Notification, state.Compact?.Kind);
        Assert.Null(state.SecondaryCompact);
        var primary = IslandMetrics.ShellRect(state);

        fixture.Controller.HandlePointerPressed(primary.MidX, primary.MidY);
        fixture.Controller.HandlePointerReleased(primary.MidX, primary.MidY);
        fixture.RunToRest();

        var action = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.NotificationActivate, action.Kind);
        Assert.Equal(0, action.Index);
        Assert.Equal(ShellForm.Compact, fixture.Controller.RenderState.Form);
    }

    [Fact]
    public void Notification_width_grows_only_for_long_text_up_to_the_1215_cap()
    {
        using var shortFixture = ControllerFixture.Create();
        shortFixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "Hi",
            Body = "短消息",
            SourceName = "QQ",
        }, 1, "test");
        shortFixture.RunToRest();
        shortFixture.Settle();
        Assert.Equal(LayoutTokens.NotificationMinWidth, shortFixture.Controller.RenderState.ShellWidth, 1);

        using var longFixture = ControllerFixture.Create();
        longFixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "Long notification",
            Body = new string('长', 200),
            SourceName = "QQ",
        }, 1, "test");
        longFixture.RunToRest();
        longFixture.Settle();
        var state = longFixture.Controller.RenderState;
        Assert.Equal(LayoutTokens.NotificationMaxWidth, state.ShellWidth, 1);
        Assert.True(IslandMetrics.ShellRect(state).Right <= HostSurface.Float.Width + 0.5f);
    }

    [Fact]
    public void Notification_spectrum_phase_advances_while_media_is_playing()
    {
        using var fixture = ControllerFixture.Create();
        fixture.Controller.PublishActivity(ActivityKind.Media, new ActivityPayload
        {
            Title = "Midnight City",
            Subtitle = "M83",
            IsActive = true,
            ShowsAudioWave = true,
            ProgressPercent = 42,
        }, 1, "test");
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "QQ",
            Body = "New message",
            SourceName = "QQ",
        }, 2, "test");
        fixture.Settle();
        fixture.RunToRest();
        fixture.Settle();

        var before = fixture.Controller.RenderState.MotionPhase;
        for (var i = 0; i < 4; i++)
        {
            fixture.Time.Advance(TimeSpan.FromMilliseconds(16));
            fixture.Controller.Advance(TimeSpan.FromMilliseconds(16));
        }

        Assert.True(fixture.Controller.RenderState.MotionPhase > before);
    }

    [Theory]
    [InlineData(StackingStrategySetting.Split)]
    [InlineData(StackingStrategySetting.Stacked)]
    public void Notification_stays_primary_when_a_higher_rank_activity_is_present(StackingStrategySetting stacking)
    {
        using var fixture = ControllerFixture.Create(
            settings: AppSettings.Default with { Stacking = stacking, AutoCollapseMs = 0, AutoHideMs = 0 });

        fixture.Controller.PublishActivity(ActivityKind.Clipboard, new ActivityPayload
        {
            Title = "Copied",
            Body = "npm run build",
        }, 1, "test");
        fixture.Controller.PublishActivity(ActivityKind.Notification, new ActivityPayload
        {
            Title = "QQ",
            Body = "New message",
            SourceName = "QQ",
        }, 2, "test");
        fixture.Settle();
        fixture.RunToRest();
        fixture.Settle();

        var state = fixture.Controller.RenderState;
        Assert.Equal(ActivityKind.Notification, state.Compact?.Kind);
        Assert.Empty(state.SplitIslets);
    }

    private static SKRect Page(int page, ControllerFixture fixture)
        => IslandMetrics.PageRect(fixture.Controller.RenderState, page);

    private static void Click(ControllerFixture fixture, SKRect rect)
    {
        fixture.Controller.HandlePointerPressed(rect.MidX, rect.MidY);
        fixture.Controller.HandlePointerReleased(rect.MidX, rect.MidY);
    }
}



