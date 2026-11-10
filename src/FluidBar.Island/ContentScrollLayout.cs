using SkiaSharp;

namespace FluidBar.Island;

/// <summary>Shared viewport and thumb geometry for painting, pointer input and page scrolling.</summary>
public readonly record struct ContentScrollGeometry(SKRect Viewport, SKRect Track, SKRect Thumb, double Offset, double Maximum)
{
    public bool CanScroll => Maximum > 0.5 && Track.Height > Thumb.Height;
    public double OffsetAt(float pointerY, float grabOffset)
        => CanScroll ? Math.Clamp((pointerY - Track.Top - grabOffset) / (Track.Height - Thumb.Height), 0, 1) * Maximum : 0;
}

public static class ContentScrollLayout
{
    public static ContentScrollGeometry Measure(ExpandedPageContent pages, int index, SKRect page, in FontScaleMetrics metrics)
    {
        var kind = pages.PageSlotAt(index)?.Kind;
        var viewport = new SKRect(page.Left + 16, page.Top + PageMetricsLayout.BodyTop, page.Right - 16, page.Bottom);
        double max = 0, offset = 0;
        if (pages.PluginPageAt(index) is { } plugin)
        {
            viewport = PluginListLayout.Body(page, plugin, metrics);
            max = PluginListLayout.MaxScroll(plugin, metrics);
            offset = plugin.ScrollOffset;
        }
        else switch (kind)
        {
            case PageSlotKind.Home:
                max = PageMetricsLayout.MaxScroll(page, kind.Value, metrics); offset = pages.Home.ScrollOffset; break;
            case PageSlotKind.Media:
                max = PageMetricsLayout.MaxScroll(page, kind.Value, metrics); offset = pages.Media.ScrollOffset; break;
            case PageSlotKind.Widgets:
                max = PageMetricsLayout.MaxScroll(page, kind.Value, metrics, pages.Widgets.Tasks.Count); offset = pages.Widgets.ScrollOffset; break;
            case PageSlotKind.Notifications:
                viewport = pages.Notifications.Detail is { } detail ? PageMetricsLayout.NotificationDetailBody(page) : PageMetricsLayout.NotificationBody(page);
                max = pages.Notifications.Detail is { } item ? PageMetricsLayout.NotificationDetailMaxScroll(page, item, metrics)
                    : PageMetricsLayout.NotificationMaxScroll(pages.Notifications.Items.Count, metrics);
                offset = pages.Notifications.ScrollOffset; break;
            case PageSlotKind.Clipboard:
                max = ContentCardLayout.ClipboardMaxScroll(pages.Clipboard, metrics); offset = pages.Clipboard.ScrollOffset; break;
            case PageSlotKind.Agent:
                max = Math.Max(0, pages.Agent.Items.Count * (PageMetricsLayout.AgentRowHeight(metrics) + 6) - 6 - viewport.Height);
                offset = pages.Agent.ScrollOffset; break;
        }
        offset = Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, max);
        var track = new SKRect(page.Right - 10, viewport.Top, page.Right, viewport.Bottom);
        var height = Math.Min(track.Height, Math.Max(28, track.Height * track.Height / (track.Height + (float)max)));
        var top = track.Top + (track.Height - height) * (float)(max <= 0 ? 0 : offset / max);
        return new(viewport, track, new SKRect(track.Left + 2, top, track.Right - 2, top + height), offset, max);
    }
}
