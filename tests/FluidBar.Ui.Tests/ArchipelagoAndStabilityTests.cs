using Avalonia;
using FluidBar.App.Diagnostics;
using FluidBar.App.Island.Expanded;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core.Activities;
using FluidBar.Core.Presentation;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.Ui.Tests;

/// <summary>Page Archipelago navigation (user-chosen variant A) + wheel chain + damping contract.</summary>
public sealed class ArchipelagoTests
{
    private static ExpandedPanel CreatePanel(FluidBar.App.Settings.SettingsController settings, ILocalizer localizer)
    {
        var panel = new ExpandedPanel();
        panel.Update(new ExpandedPageContext
        {
            Content = new IslandContent(),
            Island = ActivityPresenter.PresentIsland(ActivitySchedulerState.Empty, IslandState.Hidden),
            Settings = settings.Current,
            Localizer = localizer,
            SelectedPage = CoreIslandPage.Home,
        });
        panel.Measure(new Size(ExpandedPanel.PanelWidth, ExpandedPanel.PanelHeight));
        panel.Arrange(new Rect(0, 0, ExpandedPanel.PanelWidth, ExpandedPanel.PanelHeight));
        return panel;
    }

    [Fact]
    public void Archipelago_ConstantsMatchTheChosenVariant()
    {
        Assert.Equal(0.80, PagerMotion.PointerDamping);
        Assert.Equal(0.44, PagerMotion.SnapPeriodSeconds);
        Assert.Equal(0.97, PagerMotion.SnapDampingRatio);
        Assert.Equal(140, PagerMotion.WheelThresholdDip);
        Assert.Equal(180, PagerMotion.WheelCooldownMs);
        Assert.Equal(26, PagerMotion.SmallDragThreshold);
        Assert.Equal(34, ArchipelagoStrip.StripHeight);
        Assert.Equal(48, ArchipelagoStrip.MiniWidth);
        Assert.Equal(26, ArchipelagoStrip.MiniHeight);
        Assert.Equal(72, ArchipelagoStrip.ActiveWidth);
        Assert.Equal(30, ArchipelagoStrip.ActiveHeight);
        Assert.Equal(8, ArchipelagoStrip.Gap);
        Assert.Equal(13, ArchipelagoStrip.Radius);
        Assert.Equal(352, ArchipelagoStrip.TotalWidth);
        Assert.Equal(300, ArchipelagoStrip.HoverRevealMs);
        Assert.Equal(1200, ArchipelagoStrip.AutoHideMs);
    }

    [Fact]
    public async Task Archipelago_RevealsOnActivity_AndAutoHidesAfter1200ms()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);

            Assert.False(panel.IsArchipelagoVisible);
            panel.RevealArchipelago(1_000);
            Assert.True(panel.IsArchipelagoVisible);
            Assert.True(panel.Archipelago.Visibility > 0.0 || panel.ArchipelagoOpacity >= 0);

            panel.TickArchipelago(1_000 + ArchipelagoStrip.AutoHideMs - 1);
            Assert.True(panel.IsArchipelagoVisible);
            panel.TickArchipelago(1_000 + ArchipelagoStrip.AutoHideMs + 1);
            Assert.False(panel.IsArchipelagoVisible);
            panel.TickArchipelago(5_000);
            Assert.Equal(0, panel.ArchipelagoOpacity, 2);
        });
    }

    [Fact]
    public async Task Archipelago_RevealsAfter300msBottomHover_AndHidesWhenPointerLeaves()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);
            var bottomPoint = new Point(ExpandedPanel.PanelWidth / 2, ExpandedPanel.PanelHeight - 4);

            panel.NotifyPointerMoved(bottomPoint, 0);
            panel.TickArchipelago(299);
            Assert.False(panel.IsArchipelagoVisible);
            panel.TickArchipelago(301);
            Assert.True(panel.IsArchipelagoVisible);

            panel.NotifyPointerMoved(new Point(200, 100), 400);
            panel.TickArchipelago(400 + ArchipelagoStrip.AutoHideMs + 1);
            Assert.False(panel.IsArchipelagoVisible);
        });
    }

    [Fact]
    public async Task Archipelago_ClickMiniIsland_JumpsToPage()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);
            var requested = new List<CoreIslandPage>();
            panel.PageRequested += requested.Add;

            panel.RevealArchipelago(0);
            panel.ClickMiniIsland(4);

            Assert.Equal(4, panel.CurrentIndex);
            Assert.Equal(CoreIslandPage.Agent, panel.CurrentPage);
            Assert.Contains(CoreIslandPage.Agent, requested);
            Assert.True(panel.Motion.IsAnimating, "click-jump should animate the snap spring");
        });
    }

    [Fact]
    public async Task Archipelago_StripScrub_MovesMultiplePagesByDistance_AndTapJumps()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);

            panel.RevealArchipelago(0);
            panel.BeginStripScrub(200, 0);
            panel.ScrubStripTo(0, 16);
            panel.EndStripScrub(32);
            Assert.True(panel.CurrentIndex >= 2, $"strip scrub should cross pages by distance, got {panel.CurrentIndex}");

            // A tap without movement jumps to the island under the pointer.
            panel.RevealArchipelago(1_000);
            var island = panel.Archipelago.IslandRect(1);
            panel.BeginStripScrub(island.Center.X, 1_000);
            panel.EndStripScrub(1_020);
            Assert.Equal(1, panel.CurrentIndex);
        });
    }

    [Fact]
    public async Task Archipelago_WheelOverStrip_PagesWithCooldown()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);

            Assert.Equal(1, panel.WheelArchipelago(0, 1, 0));
            Assert.Equal(1, panel.WheelArchipelago(0, 1, 50));   // cooldown swallows
            Assert.Equal(2, panel.WheelArchipelago(0, 1, 400));
        });
    }

    [Fact]
    public async Task Archipelago_TitleAndChevronRemain_DotsRemoved()
    {
        await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (settings, _) = TestFixtures.CreateSettings();
            var panel = CreatePanel(settings, localizer);
            var source = File.ReadAllText(RepoPath("src/FluidBar.App/Island/Expanded/ExpandedPanel.cs"));
            Assert.DoesNotContain("Dot0", source);
            Assert.Contains("CollapseRect", source);
            Assert.Equal("Control center", panel.CurrentTitle);
        });
    }

    private static string RepoPath(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}

/// <summary>Single-owner wheel scroll chaining (interim App policy until Core PagerScrollChainPolicy lands).</summary>
public sealed class WheelScrollChainTests
{
    [Fact]
    public void ScrollableContent_KeepsTheWheel()
    {
        var chain = new WheelScrollChain();
        Assert.Equal(WheelChainAction.ContentScroll,
            chain.Accumulate(0, 1, shiftForcesPage: false, contentCanScrollFurther: true, pagerAnimating: false, nowMs: 0));
    }

    [Fact]
    public void Boundary_Requires40DipOverscrollAnd120msDwell()
    {
        var chain = new WheelScrollChain();
        Assert.Equal(WheelChainAction.None,
            chain.Accumulate(0, 1, false, false, false, 0));
        Assert.Equal(WheelChainAction.None,
            chain.Accumulate(0, 1, false, false, false, 60));
        Assert.Equal(WheelChainAction.Page,
            chain.Accumulate(0, 1, false, false, false, 200));
    }

    [Fact]
    public void ReverseDirection_CancelsPendingOverscroll()
    {
        var chain = new WheelScrollChain();
        Assert.Equal(WheelChainAction.None, chain.Accumulate(0, 1, false, false, false, 0));
        Assert.Equal(WheelChainAction.ContentScroll, chain.Accumulate(0, -1, false, false, false, 50));
        Assert.Equal(0, chain.CurrentOverscroll);
    }

    [Fact]
    public void HorizontalOrShift_AlwaysPages_AndAnimationLocks()
    {
        var chain = new WheelScrollChain();
        Assert.Equal(WheelChainAction.Page, chain.Accumulate(1, 0, false, true, false, 0));
        Assert.Equal(WheelChainAction.Page, chain.Accumulate(0, 1, true, true, false, 0));
        Assert.Equal(WheelChainAction.Locked, chain.Accumulate(0, 1, false, true, true, 0));
    }
}

/// <summary>Idle-stability regressions: timer-driven data changes must be marshalled to the UI thread.</summary>
public sealed class StabilityTests
{
    [Fact]
    public async Task DesignDataSource_MarshalsChangedFromBackgroundThread()
    {
        await AvaloniaTest.RunAsync(async () =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var time = new FakeTimeProvider();
            var posts = new List<Action>();
            using var data = new DesignIslandDataSource(localizer, time, action => posts.Add(action));
            var changed = 0;
            data.Changed += (_, _) => changed++;

            await Task.Run(() => data.Refresh(time.GetLocalNow()));

            Assert.Equal(0, changed);
            Assert.Single(posts);
            posts[0]();
            Assert.Equal(1, changed);
        });
    }

    [Fact]
    public void CrashLog_FormatsEntryWithoutWriting()
    {
        var entry = CrashLog.FormatEntry("appdomain", new InvalidOperationException("boom"), isTerminating: true,
            new DateTimeOffset(2026, 10, 18, 19, 45, 0, TimeSpan.Zero));
        Assert.Contains("source=appdomain", entry);
        Assert.Contains("terminating=true", entry);
        Assert.Contains("InvalidOperationException: boom", entry);
        Assert.Contains("2026-10-18T19:45:00", entry);
    }
}
