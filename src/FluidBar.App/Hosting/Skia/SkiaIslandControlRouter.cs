using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Pure hit-target → semantic-action mapping for the expanded pages. Kept separate from the
/// controller so the mapping (including slider fractions and row validation) can be unit-tested
/// without a window, a service or a frame. Returns <see langword="null"/> for non-actions
/// (<see cref="IslandPageControl.Scroll"/>, the page title, the home media card navigation).
/// </summary>
public static class SkiaIslandControlRouter
{
    /// <summary>Maps one hit target to an action, or null when the hit is not a platform action.</summary>
    /// <param name="hit">Hit target from <see cref="SkiaIslandHitTest"/>.</param>
    /// <param name="state">Current render state (page models + geometry).</param>
    /// <param name="pointerXDip">Pointer X in host-local DIP (drives slider fractions).</param>
    public static IslandPlatformAction? Map(in IslandHitTarget hit, in IslandRenderState state, double pointerXDip)
    {
        if (hit.Kind != IslandHitKind.PageControl)
        {
            return null;
        }

        var page = IslandMetrics.PageRect(state, hit.PageIndex);
        switch (hit.Control)
        {
            case IslandPageControl.PlayPause:
                return new IslandPlatformAction(IslandPlatformActionKind.MediaTogglePlay);
            case IslandPageControl.Previous:
                return new IslandPlatformAction(IslandPlatformActionKind.MediaPrevious);
            case IslandPageControl.Next:
                return new IslandPlatformAction(IslandPlatformActionKind.MediaNext);
            case IslandPageControl.Seek:
                return new IslandPlatformAction(
                    IslandPlatformActionKind.MediaSeek,
                    Value: Fraction(PageLayout.MediaSeekTrack(page), pointerXDip));
            case IslandPageControl.VolumeSlider:
                return new IslandPlatformAction(
                    IslandPlatformActionKind.VolumeSet,
                    Value: Fraction(PageLayout.HomeVolumeTrack(page), pointerXDip));
            case IslandPageControl.VolumeMute:
                return new IslandPlatformAction(IslandPlatformActionKind.VolumeMute);
            case IslandPageControl.BrightnessSlider:
                return new IslandPlatformAction(
                    IslandPlatformActionKind.BrightnessSet,
                    Value: Fraction(PageLayout.HomeBrightnessTrack(page), pointerXDip));
            case IslandPageControl.QuickToggle:
                return hit.Index is >= 0 and <= (int)IslandQuickToggle.Theme
                    ? new IslandPlatformAction(
                        IslandPlatformActionKind.QuickToggle,
                        Index: hit.Index,
                        Toggle: (IslandQuickToggle)hit.Index)
                    : null;
            case IslandPageControl.Output:
                return new IslandPlatformAction(IslandPlatformActionKind.MediaOutput);
            case IslandPageControl.Queue:
                return new IslandPlatformAction(IslandPlatformActionKind.MediaQueue);
            case IslandPageControl.Dismiss:
                return HasRow(state.Pages.Notifications.Items, hit.Index)
                    ? new IslandPlatformAction(IslandPlatformActionKind.NotificationDismiss, hit.Index)
                    : null;
            case IslandPageControl.Pin:
                return HasRow(state.Pages.Clipboard.Items, hit.Index)
                    ? new IslandPlatformAction(IslandPlatformActionKind.ClipboardPin, hit.Index)
                    : null;
            case IslandPageControl.Copy:
                return TryClipboardCopy(state, hit.Index);
            case IslandPageControl.Row:
                return MapRow(state, hit);
            case IslandPageControl.TaskToggle:
                return HasRow(state.Pages.Widgets.Tasks, hit.Index)
                    ? new IslandPlatformAction(IslandPlatformActionKind.TaskToggle, hit.Index)
                    : null;
            case IslandPageControl.CalendarPrevious:
                return new IslandPlatformAction(IslandPlatformActionKind.CalendarNavigate, Value: -1);
            case IslandPageControl.CalendarNext:
                return new IslandPlatformAction(IslandPlatformActionKind.CalendarNavigate, Value: 1);
            default:
                return null;
        }
    }

    /// <summary>Clamps a pointer X against a slider track into a 0..1 fraction.</summary>
    /// <param name="track">Track rectangle in host-local DIP.</param>
    /// <param name="pointerXDip">Pointer X in host-local DIP.</param>
    public static double Fraction(SKRect track, double pointerXDip)
    {
        if (track.Width <= 0 || !double.IsFinite(pointerXDip))
        {
            return 0;
        }

        return Math.Clamp((pointerXDip - track.Left) / track.Width, 0, 1);
    }

    private static IslandPlatformAction? MapRow(in IslandRenderState state, in IslandHitTarget hit)
    {
        switch (hit.PageIndex)
        {
            case 2:
                return HasRow(state.Pages.Notifications.Items, hit.Index)
                    ? new IslandPlatformAction(IslandPlatformActionKind.NotificationActivate, hit.Index)
                    : null;
            case 3:
                // A clipboard row click copies the row (Windows clipboard-history behaviour).
                return TryClipboardCopy(state, hit.Index);
            case 4:
                return HasRow(state.Pages.Agent.Items, hit.Index)
                    ? new IslandPlatformAction(IslandPlatformActionKind.AgentActivate, hit.Index)
                    : null;
            default:
                return null;
        }
    }

    private static IslandPlatformAction? TryClipboardCopy(in IslandRenderState state, int index)
    {
        var items = state.Pages.Clipboard.Items;
        if (!HasRow(items, index))
        {
            return null;
        }

        var item = items[index];
        // Image/file rows carry a localized label, not text; the seam reports them unsupported
        // instead of copying the label.
        return new IslandPlatformAction(
            IslandPlatformActionKind.ClipboardCopy,
            index,
            Text: item.Kind == ClipboardContentKind.Image ? null : item.Preview);
    }

    private static bool HasRow<T>(IReadOnlyList<T>? items, int index)
        => items is not null && index >= 0 && index < items.Count;
}
