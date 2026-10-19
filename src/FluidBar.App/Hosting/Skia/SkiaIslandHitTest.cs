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
    SKRect[] DotsRight)
{
    /// <summary>Target page for a left dot index (every dot in the cluster jumps to page 01).</summary>
    public int LeftDotPage(int index) => index >= 0 && index < DotsLeft.Length ? 0 : -1;

    /// <summary>Target page for a right dot index (every dot in the cluster jumps to the last page).</summary>
    public int RightDotPage(int index)
        => index >= 0 && index < DotsRight.Length ? PagerConstants.PageCount - 1 : -1;
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

            if (PageLayout.SettingsRect(content).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.Settings, state.PageIndex);
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
        Span<SKRect> dotsLeft = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];
        Span<SKRect> dotsRight = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];

        // Same measurement the renderer paints: the rail is centred on the shell, sits 8 DIP below
        // it, is 32 DIP tall and morphs the current capsule between the neighbouring label widths.
        var frame = MiniRailLayout.Measure(state, shell, dotsLeft, dotsRight);
        var last = PagerConstants.PageCount - 1;
        return new SkiaStripGeometry(
            frame.Strip,
            frame.Page > 0 ? frame.Previous : SKRect.Empty,
            frame.Current,
            frame.Page < last ? frame.Next : SKRect.Empty,
            frame.Page,
            frame.CapsuleWidth,
            frame.LeftDots > 0 ? dotsLeft[..frame.LeftDots].ToArray() : [],
            frame.RightDots > 0 ? dotsRight[..frame.RightDots].ToArray() : []);
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
        var count = page switch
        {
            0 => 4,
            1 => 1,
            2 => pages.Notifications.Items?.Count ?? 0,
            3 => pages.Clipboard.Items?.Count ?? 0,
            4 => pages.Agent.Items?.Count ?? 0,
            5 => pages.Widgets.Tasks?.Count ?? 0,
            _ => 0,
        };
        var scroll = page switch
        {
            0 => (float)pages.Home.ScrollOffset,
            1 => (float)pages.Media.ScrollOffset,
            2 => (float)pages.Notifications.ScrollOffset,
            3 => (float)pages.Clipboard.ScrollOffset,
            4 => (float)pages.Agent.ScrollOffset,
            5 => (float)pages.Widgets.ScrollOffset,
            _ => 0f,
        };
        return PageMetricsLayout.HitTest(page, pageRect, x, y, metrics, scroll, count, out index);
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

        if (geometry.Page < PagerConstants.PageCount - 1 && geometry.Next.Contains(x, y))
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
