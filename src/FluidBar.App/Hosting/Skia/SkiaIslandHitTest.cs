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
/// <param name="DotsLeft">Left far-page dots (nearest first).</param>
/// <param name="DotsRight">Right far-page dots (nearest first).</param>
public readonly record struct SkiaStripGeometry(
    SKRect Strip,
    SKRect Previous,
    SKRect Current,
    SKRect Next,
    int Page,
    SKRect[] DotsLeft,
    SKRect[] DotsRight)
{
    /// <summary>Target page for a left dot index.</summary>
    public int LeftDotPage(int index) => index >= 0 && index < DotsLeft.Length ? Page - 2 - index : -1;

    /// <summary>Target page for a right dot index.</summary>
    public int RightDotPage(int index) => index >= 0 && index < DotsRight.Length ? Page + 2 + index : -1;
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
        var content = IslandMetrics.ContentRect(shell);

        if (state.Form == ShellForm.Expanded)
        {
            if (PageLayout.CollapseRect(content).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.Collapse, state.PageIndex);
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
            if (state.Compact is { Kind: FluidBar.Core.Activities.ActivityKind.Media } && IsMediaPlay(state, shell, x, y))
            {
                return new IslandHitTarget(IslandHitKind.Compact, state.PageIndex, IslandPageControl.PlayPause);
            }

            return new IslandHitTarget(IslandHitKind.Compact, state.PageIndex);
        }

        return HitStrip(state, shell, x, y);
    }

    /// <summary>Computes the strip geometry for a state (empty when the strip is hidden).</summary>
    /// <param name="state">Current render state.</param>
    public static SkiaStripGeometry MeasureStrip(in IslandRenderState state)
    {
        var shell = IslandMetrics.ShellRect(state);
        var arch = state.Archipelago;
        var page = PagePagerPolicy.OffsetToPage(
            (double.IsFinite(arch.PagePosition) ? Math.Clamp(arch.PagePosition, 0, PagerConstants.PageCount - 1) : 0)
            * PagerConstants.PagePitchDip);
        var leftDots = arch.ResolvedFarLeftDots;
        var rightDots = arch.ResolvedFarRightDots;

        const float dotPitch = (float)(IslandMetrics.DotSize + IslandMetrics.DotGap);
        const float blockGap = 8f;
        var leftBlock = leftDots > 0 ? (leftDots * dotPitch) - (float)IslandMetrics.DotGap : 0;
        var rightBlock = rightDots > 0 ? (rightDots * dotPitch) - (float)IslandMetrics.DotGap : 0;
        var islands = (float)((IslandMetrics.MiniNeighbourSize * 2) + IslandMetrics.MiniCurrentWidth + (IslandMetrics.MiniGap * 2));
        var total = leftBlock + (leftDots > 0 ? blockGap : 0) + islands + (rightDots > 0 ? blockGap : 0) + rightBlock;

        // The strip lives in the host's bottom band, not immediately below the shell: in dock mode
        // the 52 DIP dock strip sits between the panel and the archipelago. Core's HostSurface owns
        // the local top (318 float, 378 dock) and the renderer reads the same value.
        var top = (float)HostSurface.ForMode(state.Mode).ArchipelagoTopLocal;
        var left = shell.MidX - (total / 2);
        var strip = new SKRect(left, top, left + total, top + (float)IslandMetrics.StripHeight);

        var dotsLeft = leftDots > 0 ? new SKRect[leftDots] : [];
        var dotsRight = rightDots > 0 ? new SKRect[rightDots] : [];
        var cursor = left;
        var dotY = top + (((float)IslandMetrics.StripHeight - (float)IslandMetrics.DotSize) / 2);
        for (var i = 0; i < leftDots; i++)
        {
            dotsLeft[i] = new SKRect(cursor, dotY, cursor + (float)IslandMetrics.DotSize, dotY + (float)IslandMetrics.DotSize);
            cursor += dotPitch;
        }

        if (leftDots > 0)
        {
            cursor += blockGap - (float)IslandMetrics.DotGap;
        }

        var neighbour = (float)IslandMetrics.MiniNeighbourSize;
        var neighbourY = top + (((float)IslandMetrics.StripHeight - neighbour) / 2);
        var previous = new SKRect(cursor, neighbourY, cursor + neighbour, neighbourY + neighbour);
        cursor += neighbour + (float)IslandMetrics.MiniGap;

        var currentHeight = (float)IslandMetrics.MiniCurrentHeight;
        var currentY = top + (((float)IslandMetrics.StripHeight - currentHeight) / 2);
        var current = new SKRect(cursor, currentY, cursor + (float)IslandMetrics.MiniCurrentWidth, currentY + currentHeight);
        cursor += (float)IslandMetrics.MiniCurrentWidth + (float)IslandMetrics.MiniGap;

        var next = new SKRect(cursor, neighbourY, cursor + neighbour, neighbourY + neighbour);
        cursor += neighbour;

        if (rightDots > 0)
        {
            cursor += blockGap;
            for (var i = 0; i < rightDots; i++)
            {
                dotsRight[i] = new SKRect(cursor, dotY, cursor + (float)IslandMetrics.DotSize, dotY + (float)IslandMetrics.DotSize);
                cursor += dotPitch;
            }
        }

        return new SkiaStripGeometry(strip, previous, current, next, page, dotsLeft, dotsRight);
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
        return page switch
        {
            2 => PageLayout.HitTest(page, pageRect, x, y, out index,
                (float)pages.Notifications.ScrollOffset, pages.Notifications.Items?.Count ?? 0),
            3 => PageLayout.HitTest(page, pageRect, x, y, out index,
                (float)pages.Clipboard.ScrollOffset, pages.Clipboard.Items?.Count ?? 0),
            4 => PageLayout.HitTest(page, pageRect, x, y, out index,
                (float)pages.Agent.ScrollOffset, pages.Agent.Items?.Count ?? 0),
            5 => PageLayout.HitTest(page, pageRect, x, y, out index,
                0, pages.Widgets.Tasks?.Count ?? 0),
            _ => PageLayout.HitTest(page, pageRect, x, y, out index),
        };
    }

    private static IslandHitTarget HitStrip(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var arch = state.Archipelago;
        if (!arch.Visible || arch.Opacity <= 0.01)
        {
            return default;
        }

        var geometry = MeasureStrip(state);
        var scale = (float)Math.Clamp(arch.Scale, 0.5, 1.5);
        var px = ((x - geometry.Strip.MidX) / scale) + geometry.Strip.MidX;
        var py = ((y - (float)arch.TranslateYDip - geometry.Strip.MidY) / scale) + geometry.Strip.MidY;

        if (geometry.Page > 0 && geometry.Previous.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page - 1);
        }

        if (geometry.Current.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page);
        }

        if (geometry.Page < PagerConstants.PageCount - 1 && geometry.Next.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, geometry.Page + 1);
        }

        for (var i = 0; i < geometry.DotsLeft.Length; i++)
        {
            var target = geometry.LeftDotPage(i);
            var hitSize = (float)IslandMetrics.DotHitSize / 2;
            var dot = geometry.DotsLeft[i];
            dot.Inflate(hitSize, hitSize);
            if (target >= 0 && dot.Contains(px, py))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, target);
            }
        }

        for (var i = 0; i < geometry.DotsRight.Length; i++)
        {
            var target = geometry.RightDotPage(i);
            var hitSize = (float)IslandMetrics.DotHitSize / 2;
            var dot = geometry.DotsRight[i];
            dot.Inflate(hitSize, hitSize);
            if (target < PagerConstants.PageCount && dot.Contains(px, py))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, target);
            }
        }

        return default;
    }

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
        return Math.Clamp(page, 0, PagerConstants.PageCount - 1);
    }

    private static bool IsMediaPlay(in IslandRenderState state, SKRect shell, float x, float y)
        => x >= shell.Right - 34 && x < shell.Right - 8 && y >= shell.Top + 7 && y < shell.Top + 33;
}
