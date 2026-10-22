using FluidBar.Platform.Windows.Calendar;

namespace FluidBar.Platform.Tests;

/// <summary>Calendar event dates are bucketed by local day and counted, never invented.</summary>
public sealed class CalendarEventServiceTests
{
    [Fact]
    public void CountByDay_buckets_and_counts_local_dates()
    {
        var first = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(8));
        var second = new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(8));
        var next = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(8));

        var counts = CalendarEventService.CountByDay([first, second, next]);

        Assert.Equal(2, counts[new DateOnly(2026, 10, 22)]);
        Assert.Equal(1, counts[new DateOnly(2026, 10, 23)]);
    }

    [Fact]
    public void CountsEqual_ignores_dictionary_order_and_detects_changes()
    {
        var first = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(8));
        var second = new DateTimeOffset(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(8));
        var forward = CalendarEventService.CountByDay([first, second]);
        var reverse = CalendarEventService.CountByDay([second, first]);

        Assert.True(CalendarEventService.CountsEqual(forward, reverse));
        Assert.False(CalendarEventService.CountsEqual(forward, CalendarEventService.CountByDay([first])));
        Assert.False(CalendarEventService.CountsEqual(
            forward,
            CalendarEventService.CountByDay([first, first, second])));
    }

    [Fact]
    public void CountByDay_is_empty_for_no_events()
    {
        Assert.Empty(CalendarEventService.CountByDay([]));
    }
}
