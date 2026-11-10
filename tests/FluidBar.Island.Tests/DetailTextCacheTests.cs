using FluidBar.Island;

namespace FluidBar.Island.Tests;

public sealed class DetailTextCacheTests
{
    [Fact]
    public void Equal_text_from_a_refreshed_snapshot_reuses_the_measured_lines()
    {
        var original = string.Concat(Enumerable.Repeat("通知详情中的完整正文，包含一段需要换行的说明。", 120));
        var first = PageMetricsLayout.NotificationDetailLines(original, 352, FontScaleMetrics.One);
        var refreshed = new string(original.ToCharArray());
        Assert.NotSame(original, refreshed);
        Assert.Same(first, PageMetricsLayout.NotificationDetailLines(refreshed, 352, FontScaleMetrics.One));
    }

    [Fact]
    public void Warm_long_detail_layout_does_not_allocate_during_scroll_frames()
    {
        var text = string.Concat(Enumerable.Repeat("Long notification body with full content. ", 500));
        for (var i = 0; i < 10; i++) PageMetricsLayout.NotificationDetailLines(text, 352, FontScaleMetrics.One);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) PageMetricsLayout.NotificationDetailLines(text, 352, FontScaleMetrics.One);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"Cached scroll frames allocated {allocated} bytes.");
    }
}
