using Avalonia;
using Avalonia.Controls;
using FluidBar.App.Controls;
using FluidBar.App.Theme;
using FluidBar.App.Hosting;
using FluidBar.App.Island;
using FluidBar.App.Island.Expanded;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Core.Presentation;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.Ui.Tests;

/// <summary>End-to-end headless host behavior: state wiring, effects, placement and window-bounds contract.</summary>
public sealed class IslandHostTests
{
    [Fact]
    public async Task Start_ShowsIdlePill_PlacesOnce_AndWiresStateMachine()
    {
        var result = await AvaloniaTest.RunAsync<bool>(async () =>
        {
            using var fixture = HostFixture.Create();
            await fixture.Host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Single(fixture.Host.Windows);
            Assert.True(fixture.Backend.Attached);
            Assert.Single(fixture.Backend.Placements);
            var placement = fixture.Backend.Placements[0];
            Assert.Equal(488, placement.Width / 1.0, 1);
            Assert.Equal(IslandStateKind.Compact, fixture.Host.State.Kind);

            fixture.PumpFramesToRest();
            Assert.Equal(132, fixture.Host.Motion!.Frame.Width, 1);
            Assert.Equal(36, fixture.Host.Motion.Frame.Height, 1);
            Assert.Equal(1, fixture.Host.Motion.Frame.CompactOpacity, 2);
            Assert.Equal(0, fixture.Host.Motion.Frame.ExpandedOpacity, 2);
            return true;
        });

        Assert.True(result);
    }

    [Fact]
    public async Task HoverPeek_ExpandsToPeekViaDeadlineTimer()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            using var fixture = HostFixture.Create();
            await fixture.Host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
            fixture.PumpFramesToRest();

            fixture.Host.SimulatePointerEntered();
            fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
            fixture.Host.PumpTimers();
            Assert.Equal(IslandStateKind.Peek, fixture.Host.State.Kind);
            fixture.PumpFramesToRest();
            Assert.Equal(132 + LayoutTokens.PeekDeltaWidth, fixture.Host.Motion!.Frame.Width, 1);
            Assert.Equal(36 + LayoutTokens.PeekDeltaHeight, fixture.Host.Motion.Frame.Height, 1);
        });
    }

    [Fact]
    public async Task Expand_Collapse_PageSwitch_NeverResizesWindowOrRebuildsPages()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            using var fixture = HostFixture.Create();
            await fixture.Host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
            fixture.PumpFramesToRest();
            var window = fixture.Host.PrimaryWindow!;
            var mediaPageBefore = window.Panel.PageFor(CoreIslandPage.Media);
            var placementsBefore = fixture.Backend.Placements.Count;

            fixture.Host.SimulatePrimaryClick();
            fixture.PumpFramesToRest();
            Assert.Equal(IslandStateKind.Expanded, fixture.Host.State.Kind);
            Assert.Equal(440, fixture.Host.Motion!.Frame.Width, 1);
            Assert.Equal(284, fixture.Host.Motion.Frame.Height, 1);

            fixture.Host.SimulatePageRequested(CoreIslandPage.Media);
            fixture.PumpFramesToRest();
            Assert.Equal(CoreIslandPage.Media, fixture.Host.State.Page);
            Assert.Equal(440, fixture.Host.Motion.Frame.Width, 1);
            Assert.Equal(284, fixture.Host.Motion.Frame.Height, 1);
            Assert.Same(mediaPageBefore, window.Panel.PageFor(CoreIslandPage.Media));
            Assert.Equal(placementsBefore, fixture.Backend.Placements.Count);

            fixture.Host.SimulateEscape();
            fixture.PumpFramesToRest();
            Assert.Equal(IslandStateKind.Compact, fixture.Host.State.Kind);
            Assert.Equal(132, fixture.Host.Motion.Frame.Width, 1);
            Assert.Equal(placementsBefore, fixture.Backend.Placements.Count);
        });
    }

    [Fact]
    public async Task HiddenState_BecomesClickThrough_AndStopsTheFrameLoop()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            using var fixture = HostFixture.Create();
            await fixture.Host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
            fixture.PumpFramesToRest();

            fixture.Host.ApplyInput(IslandInput.HideHotkey());
            fixture.PumpFramesToRest();
            Assert.Equal(IslandStateKind.Hidden, fixture.Host.State.Kind);
            Assert.True(fixture.Host.Motion!.Frame.IsHidden);
            Assert.False(fixture.Clock.IsRunning, "hidden island must stop the frame loop");
            Assert.Contains(true, fixture.Backend.ClickThroughTransitions);
            Assert.NotEmpty(fixture.Backend.Regions);
        });
    }

    [Fact]
    public async Task ActivityEvents_DriveCompactSize_ThenExpire()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            using var fixture = HostFixture.Create();
            await fixture.Host.StartAsync().WaitAsync(TimeSpan.FromSeconds(10));
            fixture.PumpFramesToRest();

            var payload = new ActivityPayload { Title = "Doulor", Body = "Branch is green.", SourceName = "Teams" };
            var published = fixture.Host.PublishActivity(ActivityKind.Notification, payload, sequence: 1);
            Assert.True(published);
            fixture.PumpFramesToRest();
            Assert.Equal(1, fixture.Host.SchedulerState.ActivityCount);
            Assert.Equal(248, fixture.Host.Motion!.Frame.Width, 1);
            Assert.Equal(36, fixture.Host.Motion.Frame.Height, 1);

            fixture.Time.Advance(TimeSpan.FromSeconds(6));
            fixture.Host.PumpTimers();
            fixture.PumpFramesToRest();
            Assert.Equal(0, fixture.Host.SchedulerState.ActivityCount);
            Assert.Equal(132, fixture.Host.Motion!.Frame.Width, 1);
        });
    }

    [Fact]
    public async Task MediaActivity_ProducesMediaCompactModelAndHudLevels()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var content = new IslandContent();
            var scheduler = new ActivityScheduler();
            var now = DateTimeOffset.UtcNow;

            var mediaState = scheduler.Update(ActivitySchedulerState.Empty,
                IslandEvent.New(new ActivityKey(ActivityKind.Media, "gsmtc:demo"),
                    new ActivityPayload
                    {
                        Title = "Midnight City",
                        Subtitle = "M83",
                        IsActive = true,
                        ProgressPercent = 42,
                        AccentColorHex = "#F06AB0",
                    }, 1), now).State;
            var islandVm = ActivityPresenter.PresentIsland(mediaState, new IslandState
            {
                Kind = IslandStateKind.Compact,
                ContentKind = ActivityKind.Media,
                ActivityCount = 1,
            });
            var mediaModel = IslandPresenter.ToCompact(islandVm, content, localizer, isPeeked: false);
            Assert.Equal(CompactStyle.Media, mediaModel.Style);
            Assert.Equal("Midnight City", mediaModel.Title);
            Assert.Equal("M83", mediaModel.Subtitle);
            Assert.True(mediaModel.IsActive);

            var hudState = scheduler.Update(ActivitySchedulerState.Empty,
                IslandEvent.New(new ActivityKey(ActivityKind.Volume, "system:volume"),
                    new ActivityPayload { Level = 62, IsActive = true }, 1), now).State;
            var hudVm = ActivityPresenter.PresentIsland(hudState, new IslandState
            {
                Kind = IslandStateKind.Compact,
                ContentKind = ActivityKind.Volume,
                ActivityCount = 1,
            });
            var hudModel = IslandPresenter.ToCompact(hudVm, content, localizer, isPeeked: false);
            Assert.Equal(CompactStyle.Hud, hudModel.Style);
            Assert.Equal(62, hudModel.Level);
            Assert.False(hudModel.IsMuted);
            return true;
        });
    }
}

/// <summary>Headless construction of the expanded panel, settings window and consent dialog.</summary>
public sealed class ExpandedAndSettingsUiTests
{
    [Fact]
    public async Task ExpandedPanel_IsUniform440x284_WithSixCachedPagesInOneStrip()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var content = new IslandContent();
            var vm = ActivityPresenter.PresentIsland(ActivitySchedulerState.Empty, IslandState.Hidden);
            var panel = new ExpandedPanel();
            panel.Update(new ExpandedPageContext
            {
                Content = content,
                Island = vm,
                Settings = settings.Current,
                Localizer = localizer,
                SelectedPage = CoreIslandPage.Home,
            });
            panel.Measure(new Size(440, 284));
            panel.Arrange(new Rect(0, 0, 440, 284));

            Assert.Equal(440, panel.DesiredSize.Width);
            Assert.Equal(284, panel.DesiredSize.Height);
            Assert.Equal(400, ExpandedPanel.ViewportWidth);
            Assert.Equal(252, ExpandedPanel.ViewportHeight);
            foreach (var page in Enum.GetValues<CoreIslandPage>())
            {
                var pageView = panel.PageFor(page);
                Assert.Equal(IslandPageView.PageWidth, pageView.DesiredSize.Width);
                Assert.Equal(IslandPageView.PageHeight, pageView.DesiredSize.Height);
            }

            var requested = new List<CoreIslandPage>();
            panel.PageRequested += requested.Add;
            panel.SelectIndex(3);
            Assert.Equal(CoreIslandPage.Clipboard, panel.CurrentPage);
            Assert.Equal([CoreIslandPage.Clipboard], requested);
            Assert.Equal(440, panel.DesiredSize.Width);
            Assert.Equal(284, panel.DesiredSize.Height);

            // Page roots are stable across page changes (transform-only paging, no rebuild).
            var clipboardPage = panel.PageFor(CoreIslandPage.Clipboard);
            panel.SelectIndex(1);
            panel.SelectIndex(3);
            Assert.Same(clipboardPage, panel.PageFor(CoreIslandPage.Clipboard));
            Assert.Equal(3, panel.Motion.SelectedIndex);
        });
    }

    private static Point RailItemCenter(int index)
    {
        // Mirrors the rail geometry: 34 px visual, 8 px gap, vertically centred in 48x284.
        const double itemSize = 34;
        const double gap = 8;
        var block = (6 * itemSize) + (5 * gap);
        var top = (284 - block) / 2;
        var x = 48 / 2.0;
        var y = top + (index * (itemSize + gap)) + (itemSize / 2);
        return new Point(x, y);
    }
}

/// <summary>Pager interaction contract (new frozen navigation design): flick/long-drag/wheel/edge/interrupt.</summary>
public sealed class PagerMotionTests
{
    private static PagerMotion Create() => new(pageCount: 6, pageWidth: 400, initialPage: 2);

    [Fact]
    public void QuickFlick_MovesAtMostOnePage()
    {
        var pager = Create();
        pager.BeginDrag(200, 0);
        pager.DragTo(170, 16);
        var target = pager.EndDrag(32);
        Assert.InRange(target, 2, 3);
    }

    [Fact]
    public void LongDrag_CrossesMultiplePagesByDistance()
    {
        var pager = Create();
        pager.BeginDrag(100, 0);
        pager.DragTo(100 - (400 * 3.2), 900);
        var target = pager.EndDrag(950);
        Assert.Equal(5, target);
    }

    [Fact]
    public void SmallDrag_SnapsBackWithoutChangingPage()
    {
        var pager = Create();
        pager.BeginDrag(200, 0);
        pager.DragTo(180, 400);
        Assert.Equal(2, pager.EndDrag(500));
        Assert.Equal(2, pager.SelectedIndex);
    }

    [Fact]
    public void RubberBand_ResistsPastFirstAndLastPage()
    {
        var pager = new PagerMotion(pageCount: 6, pageWidth: 400, initialPage: 0);
        pager.BeginDrag(100, 0);
        var offset = pager.DragTo(300, 100);
        Assert.InRange(offset, -75, -40); // 200 DIP × 0.80 follow = 160, then × 0.35 edge resistance ≈ 56
        Assert.Equal(0, pager.EndDrag(120));
    }

    [Fact]
    public void Wheel_OneNotchOnePage_WithCooldownAndTrackpadAxis()
    {
        var pager = Create();
        Assert.Equal(3, pager.Wheel(0, 1, 0));
        Assert.Equal(3, pager.Wheel(0, 1, 50)); // cooldown swallows the second notch
        Assert.Equal(4, pager.Wheel(0, 1, 400));
        Assert.Equal(3, pager.Wheel(0, -1, 700)); // back one page
        Assert.Equal(4, pager.Wheel(1, 0, 1200)); // horizontal trackpad axis
    }

    [Fact]
    public void Wheel_IsIgnoredOverSliders()
    {
        var pager = Create();
        pager.IsWheelBlocked = true;
        Assert.Equal(2, pager.Wheel(0, 1, 0));
        pager.IsWheelBlocked = false;
    }

    [Fact]
    public void SnapSpring_ConvergesAndIsInterruptibleByNewDrag()
    {
        var pager = Create();
        pager.SelectPage(4);
        for (var i = 0; i < 400 && pager.IsAnimating; i++)
        {
            pager.Step(1.0 / 120.0);
        }

        Assert.False(pager.IsAnimating);
        Assert.Equal(1600, pager.Offset, 0.5);

        // Interrupt a running snap with a new drag from the current visual offset.
        pager.SelectPage(0);
        pager.Step(1.0 / 120.0);
        var mid = pager.Offset;
        pager.BeginDrag(200, 0);
        pager.DragTo(180, 16);
        Assert.True(pager.Offset > mid, "drag must continue from the current animated offset");
        Assert.True(pager.Offset < mid + 40);
    }

    [Fact]
    public void KeyboardJumpAndBoundaries()
    {
        var pager = Create();
        pager.SelectPage(0);
        pager.MoveBy(-1);
        Assert.Equal(0, pager.SelectedIndex);
        pager.SelectPage(5);
        pager.MoveBy(1);
        Assert.Equal(5, pager.SelectedIndex);
    }
}
internal sealed class HostFixture : IDisposable
{
    private HostFixture(IslandHost host, RecordingWindowBackend backend, ManualFrameClock clock, FakeTimeProvider time)
    {
        Host = host;
        Backend = backend;
        Clock = clock;
        Time = time;
    }

    public IslandHost Host { get; }

    public RecordingWindowBackend Backend { get; }

    public ManualFrameClock Clock { get; }

    public FakeTimeProvider Time { get; }

    public static HostFixture Create()
    {
        var localizer = TestFixtures.CreateLocalizer("en-US");
        var time = new FakeTimeProvider();
        var (settings, _) = TestFixtures.CreateSettings(AppSettings.Default with
        {
            AutoCollapseMs = 0,
            AutoHideMs = 0,
        });
        var data = TestFixtures.CreateData(localizer, time);
        var backend = new RecordingWindowBackend();
        var clock = new ManualFrameClock();
        var host = new IslandHost(new IslandHostOptions
        {
            Localizer = localizer,
            Settings = settings,
            Data = data,
            TimeProvider = time,
            BackendFactory = () => backend,
            FrameClockFactory = _ => clock,
        });
        return new HostFixture(host, backend, clock, time);
    }

    public void PumpFramesToRest()
    {
        for (var i = 0; i < 1200 && Clock.IsRunning; i++)
        {
            Clock.Pump(TimeSpan.FromSeconds(1.0 / 120.0));
        }
    }

    public void Dispose() => Host.Dispose();
}
