using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Detached transient strip below the expanded panel: previous 26×26, current 64×28 (icon + page
/// label) and next 26×26 mini-islands, flanked by up to three 3 DIP far-page dots per side. The strip
/// is drawn outside the shell, has no persistent chrome, and reveals/hides with the host-driven
/// opacity/translate/scale. Layout is recomputed into fixed buffers (no per-frame allocations) and the
/// same rectangles are used for drawing and hit-testing.
/// </summary>
internal sealed class TransientStripRenderer : IDisposable
{
    private const int MaxDots = IslandMetrics.MaxDotsPerSide;
    private const float DotPitch = (float)(IslandMetrics.DotSize + IslandMetrics.DotGap);
    private const float BlockGap = 8f;

    private readonly TextEngine _text;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true };
    private readonly SKPaint _icon = new() { IsAntialias = true };
    private readonly SKRect[] _dotsLeft = new SKRect[MaxDots];
    private readonly SKRect[] _dotsRight = new SKRect[MaxDots];

    private IslandTheme _theme = IslandTheme.Dark;
    private SKRect _strip;
    private SKRect _prev;
    private SKRect _current;
    private SKRect _next;
    private int _leftDots;
    private int _rightDots;
    private int _page;
    private string _label = string.Empty;
    private float _opacity = 1;

    public TransientStripRenderer(TextEngine text)
    {
        _text = text;
    }

    /// <summary>Current strip envelope in host-local DIP (diagnostics/tests).</summary>
    public SKRect Strip => _strip;

    /// <summary>Current mini-island page indices (diagnostics/tests).</summary>
    public int Page => _page;

    /// <summary>Copies the current fixed-buffer layout into a public snapshot (allocates; not per frame).</summary>
    public IslandStripLayout ToLayout()
    {
        var left = new SKRect[_leftDots];
        var right = new SKRect[_rightDots];
        Array.Copy(_dotsLeft, left, _leftDots);
        Array.Copy(_dotsRight, right, _rightDots);
        return new IslandStripLayout
        {
            Strip = _strip,
            Previous = _page > 0 ? _prev : SKRect.Empty,
            Current = _current,
            Next = _page < PagerConstants.PageCount - 1 ? _next : SKRect.Empty,
            Page = _page,
            DotsLeft = left,
            DotsRight = right,
        };
    }

    /// <summary>Recomputes the strip layout for a shell rectangle and state.</summary>
    public void Update(in IslandRenderState state, SKRect shell)
    {
        var arch = state.Archipelago;
        _page = PagePagerPolicy.OffsetToPage(
            (double.IsFinite(arch.PagePosition) ? Math.Clamp(arch.PagePosition, 0, PagerConstants.PageCount - 1) : 0)
            * PagerConstants.PagePitchDip);
        _leftDots = arch.ResolvedFarLeftDots;
        _rightDots = arch.ResolvedFarRightDots;
        _label = state.Pages?.ShortTitle(_page)
            ?? ExpandedPageContent.DefaultPageTitles[Math.Clamp(_page, 0, PagerConstants.PageCount - 1)];

        var leftBlock = _leftDots > 0 ? (_leftDots * DotPitch) - (float)IslandMetrics.DotGap : 0;
        var rightBlock = _rightDots > 0 ? (_rightDots * DotPitch) - (float)IslandMetrics.DotGap : 0;
        var islands = (float)((IslandMetrics.MiniNeighbourWidth * 2) + IslandMetrics.MiniCurrentWidth + (IslandMetrics.MiniGap * 2));
        var total = leftBlock + (_leftDots > 0 ? BlockGap : 0) + islands + (_rightDots > 0 ? BlockGap : 0) + rightBlock;

        // The strip lives in the host's bottom band, not immediately below the shell: in dock mode
        // the 52 DIP dock strip sits between the panel and the archipelago. Core's HostSurface owns
        // the local top (318 float, 378 dock) and the App hit-test reads the same value.
        var top = (float)HostSurface.ForMode(state.Mode).ArchipelagoTopLocal;
        var left = shell.MidX - (total / 2);
        _strip = new SKRect(left, top, left + total, top + (float)IslandMetrics.StripHeight);

        var cursor = left;
        var dotY = top + ((float)IslandMetrics.StripHeight - (float)IslandMetrics.DotSize) / 2;
        for (var i = 0; i < _leftDots; i++)
        {
            _dotsLeft[i] = new SKRect(cursor, dotY, cursor + (float)IslandMetrics.DotSize, dotY + (float)IslandMetrics.DotSize);
            cursor += DotPitch;
        }

        if (_leftDots > 0)
        {
            cursor += BlockGap - (float)IslandMetrics.DotGap;
        }

        var neighbourWidth = (float)IslandMetrics.MiniNeighbourWidth;
        var neighbourHeight = (float)IslandMetrics.MiniNeighbourHeight;
        var neighbourY = top + (((float)IslandMetrics.StripHeight - neighbourHeight) / 2);
        _prev = new SKRect(cursor, neighbourY, cursor + neighbourWidth, neighbourY + neighbourHeight);
        cursor += neighbourWidth + (float)IslandMetrics.MiniGap;

        var currentHeight = (float)IslandMetrics.MiniCurrentHeight;
        var currentY = top + (((float)IslandMetrics.StripHeight - currentHeight) / 2);
        _current = new SKRect(cursor, currentY, cursor + (float)IslandMetrics.MiniCurrentWidth, currentY + currentHeight);
        cursor += (float)IslandMetrics.MiniCurrentWidth + (float)IslandMetrics.MiniGap;

        _next = new SKRect(cursor, neighbourY, cursor + neighbourWidth, neighbourY + neighbourHeight);
        cursor += neighbourWidth;

        if (_rightDots > 0)
        {
            cursor += BlockGap;
            for (var i = 0; i < _rightDots; i++)
            {
                _dotsRight[i] = new SKRect(cursor, dotY, cursor + (float)IslandMetrics.DotSize, dotY + (float)IslandMetrics.DotSize);
                cursor += DotPitch;
            }
        }
    }

    /// <summary>
    /// Draws the page indicator below the shell (outside the shell clip): the always-on faint dot
    /// preview (current page as a capsule) plus, while revealed, the transient 3-island filmstrip.
    /// </summary>
    public void Draw(SKCanvas canvas, in IslandRenderState state, SKRect shell)
    {
        var arch = state.Archipelago;
        var reveal = (float)Math.Clamp(arch.Opacity, 0, 1);
        var expanded = state.Form == ShellForm.Expanded;
        if (!expanded && (!arch.Visible || reveal <= 0.01f))
        {
            return;
        }

        _theme = state.Theme;
        if (expanded && arch.ShowIdlePreview)
        {
            DrawIdlePreview(canvas, state, shell, reveal);
        }

        if (!arch.Visible || reveal <= 0.01f)
        {
            return;
        }

        Update(state, shell);
        _opacity = reveal;

        // Staggered reveal: the current island leads, the neighbours follow with a short outward
        // slide so the strip reads as one element opening up rather than three islands popping.
        var currentAlpha = Math.Min(1f, reveal * 1.5f);
        var neighbourAlpha = Math.Max(0f, Math.Min(1f, (reveal - 0.35f) * 1.54f));
        var neighbourOffset = (1f - reveal) * 3f;

        canvas.Save();
        var scale = (float)Math.Clamp(arch.Scale, 0.5, 1.5);
        canvas.Translate(0, (float)arch.TranslateYDip);
        if (Math.Abs(scale - 1) > 0.0001f)
        {
            canvas.Translate(_strip.MidX, _strip.MidY);
            canvas.Scale(scale, scale);
            canvas.Translate(-_strip.MidX, -_strip.MidY);
        }

        DrawDots(canvas, neighbourAlpha);
        if (_page > 0 && neighbourAlpha > 0.01f)
        {
            DrawNeighbour(canvas, _prev, _page - 1, neighbourAlpha, -neighbourOffset);
        }

        if (_page < PagerConstants.PageCount - 1 && neighbourAlpha > 0.01f)
        {
            DrawNeighbour(canvas, _next, _page + 1, neighbourAlpha, neighbourOffset);
        }

        DrawCurrent(canvas, state, currentAlpha);
        canvas.Restore();
    }

    /// <summary>
    /// Always-on page preview: one faint dot per page at its relative position with the current page
    /// drawn as a slightly larger capsule. It cross-fades out while the filmstrip is revealed so the
    /// two indicators never compete.
    /// </summary>
    private void DrawIdlePreview(SKCanvas canvas, in IslandRenderState state, SKRect shell, float reveal)
    {
        // The preview leads the cross-fade: gone by the time the filmstrip is half revealed.
        var alpha = Math.Clamp(1f - (reveal * 2.5f), 0, 1);
        if (alpha <= 0.02f)
        {
            return;
        }

        var top = (float)HostSurface.ForMode(state.Mode).ArchipelagoTopLocal;
        var centerY = top + ((float)IslandMetrics.StripHeight / 2f);
        const float pitch = 14f;
        const float dotSize = 4f;
        const float capsuleWidth = 14f;
        var count = PagerConstants.PageCount;
        var position = double.IsFinite(state.Pager.PagePosition)
            ? Math.Clamp(state.Pager.PagePosition, 0, count - 1)
            : 0;
        var firstX = shell.MidX - (((count - 1) * pitch) / 2f);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.ForegroundSecondary, 0.20f * alpha);
        for (var page = 0; page < count; page++)
        {
            canvas.DrawCircle(firstX + (page * pitch), centerY, dotSize / 2f, _fill);
        }

        var indicatorX = firstX + ((float)position * pitch);
        var radius = dotSize / 2f;
        _fill.Color = Alpha(_theme.Foreground, 0.52f * alpha);
        canvas.DrawRoundRect(
            new SKRect(indicatorX - (capsuleWidth / 2f), centerY - radius,
                indicatorX + (capsuleWidth / 2f), centerY + radius),
            radius, radius, _fill);
    }

    /// <summary>Hit-tests the strip; returns None when hidden or outside every pill/dot.</summary>
    public IslandHitTarget HitTest(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var arch = state.Archipelago;
        if (!arch.Visible || arch.Opacity <= 0.01)
        {
            return default;
        }

        Update(state, shell);

        // Invert the reveal transform (translateY + scale about the strip centre).
        var scale = (float)Math.Clamp(arch.Scale, 0.5, 1.5);
        var px = ((x - _strip.MidX) / scale) + _strip.MidX;
        var py = ((y - (float)arch.TranslateYDip - _strip.MidY) / scale) + _strip.MidY;

        if (_page > 0 && _prev.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, _page - 1);
        }

        if (_current.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, _page);
        }

        if (_page < PagerConstants.PageCount - 1 && _next.Contains(px, py))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, _page + 1);
        }

        for (var i = 0; i < _leftDots; i++)
        {
            var target = _page - 2 - i;
            // Prototype hit area is 7×15 per dot: inflating by 6 in both axes made neighbouring hit
            // rects overlap (4 DIP gaps) and stole the far dot's click.
            var leftHit = _dotsLeft[i];
            leftHit.Inflate(2f, 6f);
            if (target >= 0 && leftHit.Contains(px, py))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, target);
            }
        }

        for (var i = 0; i < _rightDots; i++)
        {
            var target = _page + 2 + i;
            var rightHit = _dotsRight[i];
            rightHit.Inflate(2f, 6f);
            if (target < PagerConstants.PageCount && rightHit.Contains(px, py))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, target);
            }
        }

        return default;
    }

    private void DrawDots(SKCanvas canvas, float alpha)
    {
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.ForegroundSecondary, 0.45f * alpha);
        for (var i = 0; i < _leftDots; i++)
        {
            canvas.DrawCircle(_dotsLeft[i].MidX, _dotsLeft[i].MidY, (float)IslandMetrics.DotSize / 2, _fill);
        }

        for (var i = 0; i < _rightDots; i++)
        {
            canvas.DrawCircle(_dotsRight[i].MidX, _dotsRight[i].MidY, (float)IslandMetrics.DotSize / 2, _fill);
        }
    }

    private void DrawNeighbour(SKCanvas canvas, SKRect rect, int page, float alpha, float offsetX)
    {
        var shifted = new SKRect(rect.Left + offsetX, rect.Top, rect.Right + offsetX, rect.Bottom);
        DrawIsland(canvas, shifted, active: false, alpha);
        IconSet.Draw(canvas, IconFor(page), shifted.Deflate(7),
            Alpha(_theme.ForegroundSecondary, 0.90f * alpha), _icon);
    }

    private void DrawCurrent(SKCanvas canvas, in IslandRenderState state, float alpha)
    {
        // Page-change pulse: the rounded page flips at 0.5 of the pager travel, so scaling by the
        // fractional distance gives a pop exactly when the card changes and settles to 1 at rest.
        var pagePosition = double.IsFinite(state.Pager.PagePosition)
            ? Math.Clamp(state.Pager.PagePosition, 0, PagerConstants.PageCount - 1)
            : _page;
        var pulse = (float)Math.Clamp(Math.Abs(pagePosition - _page) / 0.5, 0, 1);
        var scale = 1f + (0.06f * pulse);

        canvas.Save();
        if (pulse > 0.001f)
        {
            canvas.Translate(_current.MidX, _current.MidY);
            canvas.Scale(scale, scale);
            canvas.Translate(-_current.MidX, -_current.MidY);
        }

        DrawIsland(canvas, _current, active: true, alpha);
        var labelAlpha = alpha * (1f - (0.35f * pulse));
        var labelArea = new SKRect(_current.Left + 20, _current.Top, _current.Right - 6, _current.Bottom);
        _text.Draw(canvas, _label, IslandType.MiniLabel, labelArea,
            Alpha(_theme.Foreground, labelAlpha), TextAlign.Left, labelAlpha);
        canvas.Restore();
    }

    private void DrawIsland(SKCanvas canvas, SKRect rect, bool active, float alpha)
    {
        var radius = rect.Height / 2;
        var shadowAlpha = active ? 0.32f : 0.22f;
        for (var layer = 2; layer >= 0; layer--)
        {
            var inflate = layer * 2f;
            var shadow = new SKRect(rect.Left - inflate, rect.Top - inflate + 3, rect.Right + inflate, rect.Bottom + inflate + 3);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = new SKColor(0, 0, 0, (byte)Math.Clamp((shadowAlpha / (layer + 1.5f)) * 255 * alpha, 0, 255));
            canvas.DrawRoundRect(shadow, radius + inflate, radius + inflate, _fill);
        }

        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(_theme.Background, (active ? 0.97f : 0.94f) * alpha);
        canvas.DrawRoundRect(rect, radius, radius, _fill);

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = active ? 1.2f : 1f;
        _stroke.Color = Alpha(active ? _theme.Accent : _theme.LineStrong, (active ? 0.65f : 0.90f) * alpha);
        canvas.DrawRoundRect(rect, radius, radius, _stroke);

        if (active)
        {
            IconSet.Draw(canvas, IconFor(_page), new SKRect(rect.Left + 7, rect.MidY - 5.5f, rect.Left + 18, rect.MidY + 5.5f),
                Alpha(_theme.Accent, 0.72f * alpha), _icon);
        }
    }

    private static IconKind IconFor(int page) => page switch
    {
        0 => IconKind.Home,
        1 => IconKind.Media,
        2 => IconKind.Bell,
        3 => IconKind.Clipboard,
        4 => IconKind.Agent,
        _ => IconKind.Grid,
    };

    private static SKColor Alpha(SKColor color, float opacity)
        => opacity >= 0.999f
            ? color
            : color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0, 255));

    public void Dispose()
    {
        _fill.Dispose();
        _stroke.Dispose();
        _icon.Dispose();
    }
}
