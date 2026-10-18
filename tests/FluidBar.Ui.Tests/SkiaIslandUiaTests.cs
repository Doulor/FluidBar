using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Accessibility;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Headless tests for the App-side island accessibility tree and keyboard focus. The tree is built
/// from the real controller render state (no HWND, no UIAutomationCore, no Narrator).
/// </summary>
public sealed class SkiaIslandUiaTests
{
    private static (ControllerFixture Fixture, SkiaIslandUiaSource Source, RecordingIslandPlatformActions Actions) Create()
    {
        var actions = new RecordingIslandPlatformActions();
        var fixture = ControllerFixture.Create(actions: actions);
        var source = new SkiaIslandUiaSource(fixture.Controller, new FakeLocalizer(), new SkiaHostDispatcher());
        return (fixture, source, actions);
    }

    [Fact]
    public void CompactSnapshot_ExposesRootAndActivitySummary()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.Settle();

        var root = source.BuildSnapshot();
        var compact = root.Find(SkiaIslandUiaElementIds.Compact);

        Assert.Equal("FluidBar", root.Name);
        Assert.Equal(UiaElementRole.Group, root.Role);
        Assert.Equal(UiaPatternKind.Invoke | UiaPatternKind.ExpandCollapse, root.Patterns);
        Assert.Equal(UiaExpandCollapseState.Collapsed, root.State.ExpandCollapse);
        Assert.True(root.State.IsFocusable);
        Assert.False(root.State.IsOffscreen);
        Assert.NotNull(compact);
        Assert.False(string.IsNullOrWhiteSpace(compact!.Name));
        Assert.Equal(
            new UiaRect(
                IslandMetrics.ShellRect(fixture.Controller.RenderState).Left,
                IslandMetrics.ShellRect(fixture.Controller.RenderState).Top,
                IslandMetrics.ShellRect(fixture.Controller.RenderState).Width,
                IslandMetrics.ShellRect(fixture.Controller.RenderState).Height),
            root.BoundsDip);
    }

    [Fact]
    public void ExpandedSnapshot_ExposesCollapseTabsAndHomeControls()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();

        var root = source.BuildSnapshot();
        var collapse = root.Find(SkiaIslandUiaElementIds.Collapse);
        var tab = root.Find(SkiaIslandUiaElementIds.Page(0));
        var body = root.Find(SkiaIslandUiaElementIds.PageBody(0));
        var volume = root.Find("island.page.0.volume");
        var brightness = root.Find("island.page.0.brightness");

        Assert.Equal(UiaExpandCollapseState.Expanded, root.State.ExpandCollapse);
        Assert.NotNull(collapse);
        Assert.Equal(UiaPatternKind.Invoke, collapse!.Patterns);
        Assert.NotNull(tab);
        Assert.Equal(UiaElementRole.TabItem, tab!.Role);
        Assert.True(tab.State.IsSelected);
        Assert.True(tab.State.IsOffscreen); // the page strip is hidden in the default expanded state
        Assert.Equal(UiaRect.Empty, tab.BoundsDip);
        Assert.NotNull(body);
        Assert.NotNull(volume);
        Assert.Equal(UiaElementRole.Slider, volume!.Role);
        Assert.Equal(62.0, volume.State.RangeValue);
        Assert.Equal(0.0, volume.State.RangeMinimum);
        Assert.Equal(100.0, volume.State.RangeMaximum);
        Assert.NotNull(brightness);
        Assert.Equal(78.0, brightness!.State.RangeValue);
        Assert.Equal(4, body!.Children.Count(child => child.Id.StartsWith("island.page.0.toggle.", StringComparison.Ordinal)));
    }

    [Fact]
    public void MediaPage_ExposesTransportSeekAndHonestUnsupportedButtons()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        var root = source.BuildSnapshot();
        var play = root.Find("island.page.1.play");
        var previous = root.Find("island.page.1.previous");
        var next = root.Find("island.page.1.next");
        var seek = root.Find("island.page.1.seek");
        var output = root.Find("island.page.1.output");
        var queue = root.Find("island.page.1.queue");

        Assert.NotNull(play);
        Assert.Equal(UiaElementRole.Button, play!.Role);
        Assert.Contains(play.Name, new[] { "Play", "Pause" });
        Assert.NotNull(previous);
        Assert.NotNull(next);
        Assert.NotNull(seek);
        Assert.Equal(UiaElementRole.Slider, seek!.Role);
        Assert.InRange(seek.State.RangeValue ?? -1, 0, 100);
        Assert.NotNull(output);
        Assert.False(output!.State.IsEnabled);
        Assert.False(output.State.IsFocusable);
        Assert.False(string.IsNullOrWhiteSpace(output.State.ItemStatus));
        Assert.NotNull(queue);
        Assert.False(queue!.State.IsEnabled);
    }

    [Fact]
    public void RootInvoke_TogglesExpandAndCollapse()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.Settle();

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, SkiaIslandUiaElementIds.Root));
        fixture.RunToRest();
        Assert.Equal(UiaExpandCollapseState.Expanded, source.BuildSnapshot().State.ExpandCollapse);

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, SkiaIslandUiaElementIds.Root));
        fixture.RunToRest();
        Assert.Equal(UiaExpandCollapseState.Collapsed, source.BuildSnapshot().State.ExpandCollapse);
    }

    [Fact]
    public void CollapseButton_AndEscape_CollapseTheIsland()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, SkiaIslandUiaElementIds.Collapse));
        fixture.RunToRest();
        Assert.NotEqual(UiaExpandCollapseState.Expanded, source.BuildSnapshot().State.ExpandCollapse);

        fixture.ExpandAndSettle();
        source.NotifyWindowFocus(true);
        source.HandleKey(UiaKeyCommand.Escape);
        fixture.RunToRest();
        Assert.NotEqual(UiaExpandCollapseState.Expanded, source.BuildSnapshot().State.ExpandCollapse);
    }

    [Fact]
    public void TabSelect_ChangesThePage()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();

        source.Perform(new UiaElementActionRequest(UiaElementAction.Select, SkiaIslandUiaElementIds.Page(2)));
        fixture.RunToRest();

        Assert.Equal(2, fixture.Controller.RenderState.PageIndex);
        Assert.True(source.BuildSnapshot().Find(SkiaIslandUiaElementIds.Page(2))!.State.IsSelected);
    }

    [Fact]
    public void RangeValueAction_SetsVolumeThroughThePlatformSeam()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();

        source.Perform(new UiaElementActionRequest(UiaElementAction.SetRangeValue, "island.page.0.volume", 40));

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.VolumeSet, call.Kind);
        Assert.Equal(0.4, call.Value, 3);
    }

    [Fact]
    public void MediaPlayAction_DispatchesTogglePlay()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.GoToPage(1);

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.1.play"));

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.MediaTogglePlay, call.Kind);
    }

    [Fact]
    public void Keyboard_TabWalksFocus_AndEnterInvokesTheFocusedElement()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.Settle();
        source.NotifyWindowFocus(true);

        Assert.True(source.HandleKey(UiaKeyCommand.Tab));
        Assert.Equal(SkiaIslandUiaElementIds.Root, source.FocusedElementId);

        Assert.True(source.HandleKey(UiaKeyCommand.Enter));
        fixture.RunToRest();
        Assert.Equal(UiaExpandCollapseState.Expanded, source.BuildSnapshot().State.ExpandCollapse);

        Assert.True(source.HandleKey(UiaKeyCommand.Tab));
        Assert.Equal(SkiaIslandUiaElementIds.Collapse, source.FocusedElementId);
        Assert.True(source.HandleKey(UiaKeyCommand.ShiftTab));
        Assert.Equal(SkiaIslandUiaElementIds.Root, source.FocusedElementId);
    }

    [Fact]
    public void Keyboard_ArrowKeysChangePageWhenExpanded()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        source.NotifyWindowFocus(true);

        Assert.True(source.HandleKey(UiaKeyCommand.Right));
        fixture.RunToRest();
        Assert.Equal(1, fixture.Controller.RenderState.PageIndex);

        Assert.True(source.HandleKey(UiaKeyCommand.Left));
        fixture.RunToRest();
        Assert.Equal(0, fixture.Controller.RenderState.PageIndex);
    }

    [Fact]
    public void Keyboard_SliderArrowKeysAdjustTheRangeValue()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        source.NotifyWindowFocus(true);
        source.Perform(new UiaElementActionRequest(UiaElementAction.SetFocus, "island.page.0.volume"));

        Assert.True(source.HandleKey(UiaKeyCommand.Right));

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.VolumeSet, call.Kind);
        Assert.True(call.Value > 0.62);
    }

    [Fact]
    public void FocusRing_IsPublishedOnlyWhileTheWindowHasFocus()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        source.NotifyWindowFocus(true);

        source.Perform(new UiaElementActionRequest(UiaElementAction.SetFocus, "island.page.0.volume"));
        Assert.NotNull(fixture.Controller.RenderState.FocusRing);
        Assert.True(source.BuildSnapshot().Find("island.page.0.volume")!.State.IsFocused);

        source.NotifyWindowFocus(false);
        var hiddenFocus = source.BuildSnapshot();
        Assert.Null(fixture.Controller.RenderState.FocusRing);
        Assert.False(hiddenFocus.Find("island.page.0.volume")!.State.IsFocused);
    }

    [Fact]
    public void VisibleStrip_ExposesMiniIslandsAndDotsAsButtons()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.RunToRest();

        var root = source.BuildSnapshot();
        var strip = root.Find(SkiaIslandUiaElementIds.Strip);
        var tab = root.Find(SkiaIslandUiaElementIds.Page(0));

        Assert.NotNull(strip);
        Assert.True(strip!.Children.Count >= 2);
        Assert.All(strip.Children, child => Assert.Equal(UiaElementRole.Button, child.Role));
        Assert.Contains(strip.Children, child => child.Id == "island.strip.current.0");
        Assert.NotNull(tab);
        Assert.False(tab!.State.IsOffscreen);
        Assert.False(tab.BoundsDip.IsEmpty);
    }

    [Fact]
    public void NotificationRowsAndDismiss_DispatchTheRowActions()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.GoToPage(2);

        Assert.NotNull(source.BuildSnapshot().Find("island.page.2.row.0"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.2.row.0"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.2.row.0.dismiss"));

        Assert.Collection(actions.Calls,
            call => Assert.Equal(IslandPlatformActionKind.NotificationActivate, call.Kind),
            call => Assert.Equal(IslandPlatformActionKind.NotificationDismiss, call.Kind));
    }

    [Fact]
    public void ClipboardRowPinAndCopy_DispatchTheRowActions()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.GoToPage(3);

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.3.row.0"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Toggle, "island.page.3.row.0.pin"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.3.row.0.copy"));

        Assert.Collection(actions.Calls,
            call => Assert.Equal(IslandPlatformActionKind.ClipboardCopy, call.Kind),
            call => Assert.Equal(IslandPlatformActionKind.ClipboardPin, call.Kind),
            call => Assert.Equal(IslandPlatformActionKind.ClipboardCopy, call.Kind));
    }

    [Fact]
    public void WidgetsTasksAndCalendar_DispatchThePageActions()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.GoToPage(5);

        source.Perform(new UiaElementActionRequest(UiaElementAction.Toggle, "island.page.5.task.0"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.5.calendar.prev"));
        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.page.5.calendar.next"));

        Assert.Collection(actions.Calls,
            call => Assert.Equal(IslandPlatformActionKind.TaskToggle, call.Kind),
            call =>
            {
                Assert.Equal(IslandPlatformActionKind.CalendarNavigate, call.Kind);
                Assert.Equal(-1, call.Value);
            },
            call =>
            {
                Assert.Equal(IslandPlatformActionKind.CalendarNavigate, call.Kind);
                Assert.Equal(1, call.Value);
            });
    }

    [Fact]
    public void QuickToggle_DispatchUsesTheToggleSlot()
    {
        var (fixture, source, actions) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();

        source.Perform(new UiaElementActionRequest(UiaElementAction.Toggle, "island.page.0.toggle.3"));

        var call = Assert.Single(actions.Calls);
        Assert.Equal(IslandPlatformActionKind.QuickToggle, call.Kind);
        Assert.Equal(IslandQuickToggle.Theme, call.Toggle);
        Assert.Equal(3, call.Index);
    }

    [Fact]
    public void VisibleStripButton_NavigatesToItsPage()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.ExpandAndSettle();
        fixture.Controller.Archipelago.Reveal(fixture.Time.GetLocalNow());
        fixture.RunToRest();

        source.Perform(new UiaElementActionRequest(UiaElementAction.Invoke, "island.strip.next.1"));
        fixture.RunToRest();

        Assert.Equal(1, fixture.Controller.RenderState.PageIndex);
    }

    [Fact]
    public void HiddenIsland_ExposesOnlyAnOffscreenRoot()
    {
        var (fixture, source, _) = Create();
        using var _fixture = fixture;
        using var _source = source;
        fixture.Settle();
        fixture.Controller.HandleHideHotkey();
        fixture.RunToRest();

        var root = source.BuildSnapshot();

        Assert.True(root.State.IsOffscreen);
        Assert.Empty(root.Children);
        Assert.Equal(UiaExpandCollapseState.Collapsed, root.State.ExpandCollapse);
    }
}
