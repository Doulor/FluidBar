using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Measured detached-strip geometry in host-local DIP (mirrors the renderer's layout).</summary>
/// <param name="Strip">Strip envelope.</param>
/// <param name="Previous">Previous mini-island (empty on page 0).</param>
/// <param name="Current">Current mini-island.</param>
/// <param name="Next">Next mini-island (empty on the last page).</param>
/// <param name="Page">Committed page.</param>
/// <param name="CapsuleWidth">Resolved current capsule width in DIP (64..104).</param>
/// <param name="DotsLeft">Left far-page dots (nearest first).</param>
/// <param name="DotsRight">Right far-page dots (nearest first).</param>
public readonly record struct SkiaStripGeometry(
    SKRect Strip,
    SKRect Previous,
    SKRect Current,
    SKRect Next,
    int Page,
    float CapsuleWidth,
    SKRect[] DotsLeft,
    SKRect[] DotsRight,
    int PageCount = 0,
    SKRect OverflowLeft = default,
    SKRect OverflowRight = default)
{
    /// <summary>
    /// Target page for a left dot index (nearest dot first). Dot 0 is the page just before the previous
    /// pill, so the cluster maps onto the pages it actually stands for instead of every dot jumping to
    /// page 01 — three dots that all did the same thing read as "点不了、毫无作用" (user report 2026-10-22).
    /// </summary>
    public int LeftDotPage(int index)
        => index >= 0 && index < DotsLeft.Length ? Math.Max(0, Page - 2 - index) : -1;

    /// <summary>Target page for a right dot index (nearest dot first, mirror of <see cref="LeftDotPage"/>).</summary>
    public int RightDotPage(int index)
        => index >= 0 && index < DotsRight.Length ? Math.Min(PageCount - 1, Page + 2 + index) : -1;
}

/// <summary>
/// Hit-testing for the Skia island, replicating <see cref="IslandRenderer.HitTest"/> without owning a
/// renderer. The host uses it for pointer input and to build the layered window's interactive region
/// (only the shell squircle, the mini-island pills and the dots are clickable). A parity test pins the
/// strip geometry against <see cref="IslandRenderer.MeasureStrip"/>.
/// </summary>
public static class SkiaIslandHitTest
{
    /// <summary>Hit-tests a host-local DIP point.</summary>
    /// <param name="state">Current render state.</param>
    /// <param name="x">Client X in DIP.</param>
    /// <param name="y">Client Y in DIP.</param>
    public static IslandHitTarget Hit(in IslandRenderState state, double x, double y)
        => Hit(state, (float)x, (float)y);

    /// <summary>Hit-tests a host-local DIP point.</summary>
    /// <param name="state">Current render state.</param>
    /// <param name="x">Client X in DIP.</param>
    /// <param name="y">Client Y in DIP.</param>
    public static IslandHitTarget Hit(in IslandRenderState state, float x, float y)
    {
        if (!state.IsVisible)
        {
            return default;
        }

        var shell = IslandMetrics.ShellRect(state);
        var content = IslandMetrics.ContentRect(shell, state.RailSide);

        if (state.Form != ShellForm.Expanded && state.SplitIslets.Length > 0)
        {
            var islets = IslandMetrics.SplitIsletRects(state);
            for (var i = 0; i < islets.Length && i < state.SplitIslets.Length; i++)
            {
                if (IslandMetrics.SplitIsletContains(islets[i], x, y))
                {
                    return new IslandHitTarget(
                        IslandHitKind.SplitIslet, state.Pages.IndexOfPage(state.SplitIslets[i].Page), Index: i);
                }
            }
        }

        if (state.Form != ShellForm.Expanded
            && IslandMetrics.OverflowBadgeRect(state) is { Width: > 0 } badge
            && badge.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.OverflowBadge, state.PageIndex);
        }

        var secondary = IslandMetrics.SecondaryShellRect(state);
        if (!secondary.IsEmpty && secondary.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.SecondaryCompact, state.PageIndex);
        }

        if (state.Form == ShellForm.Expanded)
        {
            if (PageLayout.CollapseRect(content).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.Collapse, state.PageIndex);
            }

            if (PageLayout.SettingsRect(content).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.Settings, state.PageIndex);
            }

            var tab = SideRailLayout.HitTest(content, state.Pages.PageCount, x, y, state.Pages.RailScrollOffset, state.RailSide);
            if (tab >= 0)
            {
                return new IslandHitTarget(IslandHitKind.PageTab, tab, Index: tab);
            }

            if (ContainsShell(state, shell, x, y))
            {
                var page = PageUnder(state, content, x);
                var pageRect = IslandMetrics.PageRect(state, page);
                var control = HitPageControl(state, page, pageRect, x, y, out var index);
                if (control != IslandPageControl.None)
                {
                    return new IslandHitTarget(IslandHitKind.PageControl, page, control, index);
                }

                if (PageLayout.PageTitleRect(content).Contains(x, y))
                {
                    return new IslandHitTarget(IslandHitKind.Title, page);
                }

                return content.Contains(x, y)
                    ? new IslandHitTarget(IslandHitKind.Page, page)
                    : new IslandHitTarget(IslandHitKind.Shell, page);
            }
        }
        else if (ContainsShell(state, shell, x, y))
        {
            // The compact media pill carries no play/pause button (removed on request): every point in
            // the pill is the expand target, so the old right-hand control area is gone with it.
            return new IslandHitTarget(IslandHitKind.Compact, state.PageIndex);
        }

        return default;
    }

    /// <summary>Computes the strip geometry for a state (empty when the strip is hidden).</summary>
    /// <param name="state">Current render state.</param>
    public static SkiaStripGeometry MeasureStrip(in IslandRenderState state)
    {
        var shell = IslandMetrics.ShellRect(state);
        Span<SKRect> dotsLeft = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];
        Span<SKRect> dotsRight = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];

        // Same measurement the renderer paints: the rail is centred on the shell, sits 8 DIP below
        // it, is 32 DIP tall and morphs the current capsule between the neighbouring label widths.
        var frame = MiniRailLayout.Measure(state, shell, dotsLeft, dotsRight);
        return new SkiaStripGeometry(
            frame.Strip,
            frame.Page > 0 ? frame.Previous : SKRect.Empty,
            frame.Current,
            frame.Page < frame.PageCount - 1 ? frame.Next : SKRect.Empty,
            frame.Page,
            frame.CapsuleWidth,
            frame.LeftDots > 0 ? dotsLeft[..frame.LeftDots].ToArray() : [],
            frame.RightDots > 0 ? dotsRight[..frame.RightDots].ToArray() : [],
            frame.PageCount,
            frame.OverflowLeft,
            frame.OverflowRight);
    }

    /// <summary>
    /// Page-control hit testing with the scroll offset and row count the page models actually paint,
    /// so list rows can never be hit (or clicked) beyond the data.
    /// </summary>
    private static IslandPageControl HitPageControl(
        in IslandRenderState state,
        int page,
        SKRect pageRect,
        float x,
        float y,
        out int index)
    {
        var pages = state.Pages;
        var metrics = FontScaleMetrics.From(state.FontScale);
        if (pages.PluginPageAt(page) is { } pluginPage)
        {
            var body = PageLayout.ListBody(pageRect);
            for (var i = 0; i < pluginPage.Rows.Count; i++)
            {
                var row = PageMetricsLayout.NotificationRow(pageRect, i, 0, metrics);
                if (!row.Contains(x, y) || !body.Contains(x, y))
                {
                    continue;
                }

                index = i;
                var model = pluginPage.Rows[i];
                var trailing = new SKRect(row.Right - 96, row.MidY - 12, row.Right - 8, row.MidY + 12);
                return trailing.Contains(x, y) && !string.IsNullOrWhiteSpace(model.TrailingCommandId)
                    ? IslandPageControl.PluginRowButton
                    : !string.IsNullOrWhiteSpace(model.RowCommandId) || !string.IsNullOrWhiteSpace(model.TrailingCommandId)
                        ? IslandPageControl.PluginRow
                        : IslandPageControl.None;
            }

            index = -1;
            return IslandPageControl.None;
        }

        var kind = pages.PageSlotAt(page)?.Kind ?? PageSlotKind.Home;
        var count = kind switch
        {
            PageSlotKind.Home => 4,
            PageSlotKind.Media => 1,
            PageSlotKind.Notifications => pages.Notifications.Items?.Count ?? 0,
            PageSlotKind.Clipboard => pages.Clipboard.Items?.Count ?? 0,
            PageSlotKind.Agent => pages.Agent.Items?.Count ?? 0,
            PageSlotKind.Widgets => pages.Widgets.Tasks?.Count ?? 0,
            _ => 0,
        };
        var scroll = kind switch
        {
            PageSlotKind.Home => (float)pages.Home.ScrollOffset,
            PageSlotKind.Media => (float)pages.Media.ScrollOffset,
            PageSlotKind.Notifications => (float)pages.Notifications.ScrollOffset,
            PageSlotKind.Clipboard => (float)pages.Clipboard.ScrollOffset,
            PageSlotKind.Agent => (float)pages.Agent.ScrollOffset,
            PageSlotKind.Widgets => (float)pages.Widgets.ScrollOffset,
            _ => 0f,
        };
        var contentRect = IslandMetrics.ContentRect(IslandMetrics.ShellRect(state), state.RailSide);
        if (kind == PageSlotKind.Clipboard
            && !string.IsNullOrWhiteSpace(pages.Clipboard.SearchLabel)
            && PageLayout.ClipboardSearchRect(contentRect).Contains(x, y))
        {
            index = -1;
            return IslandPageControl.ClipboardSearch;
        }

        if (kind == PageSlotKind.Clipboard
            && !string.IsNullOrWhiteSpace(pages.Clipboard.FilterLabel)
            && PageLayout.ClipboardFilterRect(contentRect).Contains(x, y))
        {
            index = -1;
            return IslandPageControl.ClipboardFilter;
        }

        if (kind == PageSlotKind.Notifications
            && !string.IsNullOrWhiteSpace(pages.Notifications.ClearLabel)
            && PageLayout.NotificationClearRect(contentRect).Contains(x, y))
        {
            index = -1;
            return IslandPageControl.NotificationClear;
        }

        var control = PageMetricsLayout.HitTest(kind, pageRect, x, y, metrics, scroll, count, out index,
            state.Pages.Home.Toggles.Count);

        // Declared-but-unavailable controls are inert: no click target, so nothing can look clickable
        // and then do nothing. The renderer greys them and prints the localized reason instead.
        if (kind == PageSlotKind.Media
            && ((control == IslandPageControl.Output && !pages.Media.OutputAvailable
                    && string.IsNullOrWhiteSpace(pages.Media.OutputPlaceholder))
                || (control == IslandPageControl.Queue && !pages.Media.QueueAvailable
                    && string.IsNullOrWhiteSpace(pages.Media.QueuePlaceholder))
                || (control == IslandPageControl.VolumeMute && !pages.Media.HasVolume)))
        {
            index = -1;
            return IslandPageControl.None;
        }

        // A volume mute button with no audio endpoint is greyed and must not swallow a click.
        if (kind is PageSlotKind.Home or PageSlotKind.Media
            && control == IslandPageControl.VolumeMute
            && (kind == PageSlotKind.Home ? !pages.Home.HasVolume : !pages.Media.HasVolume))
        {
            index = -1;
            return IslandPageControl.None;
        }

        // An unavailable quick toggle is painted greyed; refusing its hit target keeps that visual
        // honest (no dead button that swallows the click).
        if (kind == PageSlotKind.Home
            && control == IslandPageControl.QuickToggle
            && (index < 0 || index >= pages.Home.Toggles.Count || !pages.Home.Toggles[index].IsEnabled))
        {
            index = -1;
            return IslandPageControl.None;
        }

        return control;
    }

    private static IslandHitTarget HitStrip(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var arch = state.Archipelago;
        if (!arch.Visible || arch.Opacity <= 0.01)
        {
            return default;
        }

        // The prototype reveals the rail with an opacity fade only: geometry is never transformed, so
        // the painted pills and these targets share one coordinate space.
        var geometry = MeasureStrip(state);
        if (geometry.Page > 0 && geometry.Previous.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page - 1);
        }

        if (geometry.Current.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page);
        }

        if (!geometry.OverflowLeft.IsEmpty && geometry.OverflowLeft.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailOverflow, Index: 0);
        }

        if (!geometry.OverflowRight.IsEmpty && geometry.OverflowRight.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailOverflow, Index: 1);
        }

        if (geometry.Page < geometry.PageCount - 1 && geometry.Next.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page + 1);
        }

        for (var i = 0; i < geometry.DotsLeft.Length; i++)
        {
            if (IslandStripLayout.DotHitRect(geometry.DotsLeft[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, geometry.LeftDotPage(i));
            }
        }

        for (var i = 0; i < geometry.DotsRight.Length; i++)
        {
            if (IslandStripLayout.DotHitRect(geometry.DotsRight[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, geometry.RightDotPage(i));
            }
        }

        return default;
    }

    private static int LegacyPageIndex(PageSlotKind kind) => kind switch
    {
        PageSlotKind.Home => 0,
        PageSlotKind.Media => 1,
        PageSlotKind.Notifications => 2,
        PageSlotKind.Clipboard => 3,
        PageSlotKind.Agent => 4,
        PageSlotKind.Widgets => 5,
        _ => -1,
    };

    private static bool ContainsShell(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var geometry = ShellGeometry.Create(
            new VisualRect(shell.Left, shell.Top, shell.Width, shell.Height),
            state.ShellRadii);
        return geometry.Contains(x, y);
    }

    private static int PageUnder(in IslandRenderState state, SKRect content, float x)
    {
        var pitch = PagerConstants.PagePitchDip;
        var offset = state.Pager.PagePosition * pitch;
        var local = x - content.Left + offset;
        var page = (int)Math.Floor(local / pitch);
        return Math.Clamp(page, 0, Math.Max(0, state.Pager.PageCount - 1));
    }
}
