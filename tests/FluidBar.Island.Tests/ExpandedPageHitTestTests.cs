using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Hit-test coverage for the expanded notifications/clipboard/agent/widgets controls.</summary>
public sealed class ExpandedPageHitTestTests
{
    private static IslandHitTarget Hit(in IslandRenderState state, float x, float y)
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        return renderer.HitTest(state, x, y);
    }

    [Fact]
    public void Notifications_rows_dismiss_buttons_and_scroll_region_are_targets()
    {
        var baseState = IslandTestFixtures.Expanded(2);
        var state = baseState with
        {
            Pages = baseState.Pages with
            {
                Notifications = baseState.Pages.Notifications with
                {
                    Items = [.. baseState.Pages.Notifications.Items.Take(3)],
                },
            },
        };
        var page = IslandMetrics.PageRect(state, 2);
        var metrics = FontScaleMetrics.From(state.FontScale);

        var row = PageMetricsLayout.NotificationRow(page, 0, 0, metrics);
        var rowHit = Hit(state, row.MidX, row.MidY);
        Assert.Equal(IslandHitKind.PageControl, rowHit.Kind);
        Assert.Equal(IslandPageControl.Row, rowHit.Control);
        Assert.Equal(0, rowHit.Index);
        Assert.Equal(2, rowHit.PageIndex);

        var dismiss = PageMetricsLayout.NotificationDismiss(page, 1, 0, metrics);
        var dismissHit = Hit(state, dismiss.MidX, dismiss.MidY);
        Assert.Equal(IslandPageControl.Dismiss, dismissHit.Control);
        Assert.Equal(1, dismissHit.Index);

        var scroll = PageLayout.ListBody(page);
        var scrollHit = Hit(state, scroll.MidX, scroll.Bottom - 2);
        Assert.Equal(IslandPageControl.Scroll, scrollHit.Control);
    }

    [Fact]
    public void Notification_scroll_offset_moves_the_hit_targets()
    {
        var baseState = IslandTestFixtures.Expanded(2);
        var items = new List<NotificationItemModel>(baseState.Pages.Notifications.Items);
        items.Add(new NotificationItemModel { AppName = "Store", Title = "Update available", Body = "FluidBar 2.0.1", TimeText = "1 hr" });
        items.Add(new NotificationItemModel { AppName = "System", Title = "Backup complete", Body = "Saved to OneDrive", TimeText = "2 hr" });
        var state = baseState with
        {
            Pages = baseState.Pages with
            {
                Notifications = baseState.Pages.Notifications with { Items = items, ScrollOffset = 70 },
            },
        };

        var page = IslandMetrics.PageRect(state, 2);
        var metrics = FontScaleMetrics.From(state.FontScale);
        var row1 = PageMetricsLayout.NotificationRow(page, 1, 70, metrics);
        var hit = Hit(state, row1.MidX, row1.MidY);
        Assert.Equal(IslandPageControl.Row, hit.Control);
        Assert.Equal(1, hit.Index);

        // Row 0 is scrolled above the body band and is no longer clickable.
        var row0 = PageMetricsLayout.NotificationRow(page, 0, 70, metrics);
        var above = Hit(state, row0.MidX, row0.MidY);
        Assert.NotEqual(IslandPageControl.Row, above.Control);
    }

    [Fact]
    public void Clipboard_rows_pin_copy_and_scroll_region_are_targets()
    {
        var state = IslandTestFixtures.Expanded(3);
        var page = IslandMetrics.PageRect(state, 3);

        var row = PageLayout.ClipboardRow(page, 0);
        var rowHit = Hit(state, row.MidX, row.MidY);
        Assert.Equal(IslandPageControl.Row, rowHit.Control);
        Assert.Equal(0, rowHit.Index);

        var pin = PageLayout.ClipboardPin(page, 2);
        var pinHit = Hit(state, pin.MidX, pin.MidY);
        Assert.Equal(IslandPageControl.Pin, pinHit.Control);
        Assert.Equal(2, pinHit.Index);

        var copy = PageLayout.ClipboardCopy(page, 3);
        var copyHit = Hit(state, copy.MidX, copy.MidY);
        Assert.Equal(IslandPageControl.Copy, copyHit.Control);
        Assert.Equal(3, copyHit.Index);

        var body = PageLayout.ListBody(page);
        var scrollHit = Hit(state, body.MidX, body.Bottom - 2);
        Assert.Equal(IslandPageControl.Scroll, scrollHit.Control);
    }

    [Fact]
    public void Clipboard_scroll_offset_moves_the_hit_targets()
    {
        var baseState = IslandTestFixtures.Expanded(3);
        var items = new List<ClipboardItemModel>(baseState.Pages.Clipboard.Items)
        {
            new() { Preview = "extra entry", Kind = ClipboardContentKind.Text, TimeText = "1 hr" },
        };
        var state = baseState with
        {
            Pages = baseState.Pages with
            {
                Clipboard = baseState.Pages.Clipboard with { Items = items, ScrollOffset = 12 },
            },
        };

        var page = IslandMetrics.PageRect(state, 3);
        var pin = PageLayout.ClipboardPin(page, 1, 12);
        var hit = Hit(state, pin.MidX, pin.MidY);
        Assert.Equal(IslandPageControl.Pin, hit.Control);
        Assert.Equal(1, hit.Index);
    }

    [Fact]
    public void Agent_rows_and_scroll_region_are_targets()
    {
        var state = IslandTestFixtures.Expanded(4);
        var page = IslandMetrics.PageRect(state, 4);

        var row = PageLayout.AgentRow(page, 2);
        var hit = Hit(state, row.MidX, row.MidY);
        Assert.Equal(IslandPageControl.Row, hit.Control);
        Assert.Equal(2, hit.Index);
        Assert.Equal(4, hit.PageIndex);

        var body = PageLayout.ListBody(page);
        var scroll = Hit(state, body.MidX, body.Bottom - 2);
        Assert.Equal(IslandPageControl.Scroll, scroll.Control);
    }

    [Fact]
    public void Widgets_calendar_nav_and_task_rows_are_targets()
    {
        var state = IslandTestFixtures.Expanded(5);
        var page = IslandMetrics.PageRect(state, 5);

        var previous = PageLayout.WidgetsCalendarNav(page, previous: true);
        Assert.Equal(IslandPageControl.CalendarPrevious, Hit(state, previous.MidX, previous.MidY).Control);

        var next = PageLayout.WidgetsCalendarNav(page, previous: false);
        Assert.Equal(IslandPageControl.CalendarNext, Hit(state, next.MidX, next.MidY).Control);

        for (var i = 0; i < 3; i++)
        {
            var task = PageLayout.WidgetsTaskRow(page, i);
            var hit = Hit(state, task.MidX, task.MidY);
            Assert.Equal(IslandPageControl.TaskToggle, hit.Control);
            Assert.Equal(i, hit.Index);
        }

        var calendar = PageLayout.WidgetsCalendar(page);
        var calendarHit = Hit(state, calendar.MidX, calendar.MidY);
        Assert.Equal(IslandHitKind.Page, calendarHit.Kind);
        Assert.Equal(5, calendarHit.PageIndex);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Page_strip_positions_2_to_5_hit_the_visible_page(int pageIndex)
    {
        var state = IslandTestFixtures.Expanded(pageIndex);
        var page = IslandMetrics.PageRect(state, pageIndex);
        var hit = Hit(state, page.Left + 200, page.Top + 150);
        Assert.Equal(pageIndex, hit.PageIndex);
        Assert.True(hit.IsShell);
        Assert.True(hit.Kind is IslandHitKind.Page or IslandHitKind.PageControl);
    }

    [Fact]
    public void Empty_lists_expose_the_scroll_region_but_no_rows()
    {
        var state = IslandTestFixtures.Expanded(2) with
        {
            Pages = IslandTestFixtures.Content() with
            {
                Notifications = new NotificationsPageModel(),
            },
        };

        var page = IslandMetrics.PageRect(state, 2);
        var body = PageLayout.ListBody(page);
        var hit = Hit(state, body.MidX, body.MidY);
        Assert.Equal(IslandPageControl.Scroll, hit.Control);
        Assert.Equal(-1, hit.Index);
    }
}
