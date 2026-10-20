using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Detached transient preview rail below the expanded panel — the prototype2 `.mini-rail`:
/// previous 26×26 glass circle, current capsule (28 DIP tall, 64 DIP base width that grows with its
/// 9 DIP label) and next 26×26 glass circle, flanked by up to three 3 DIP far-page dots per side
/// (7 DIP pitch, 7 DIP from the nearest island).
/// <para>
/// The rail is drawn outside the shell and has no persistent chrome: it is only painted while the
/// host reveals it, and the prototype shows nothing at rest. Layout comes from
/// <see cref="MiniRailLayout.Measure"/>, the same function the host hit-test consumes, so the painted
/// pills and their click targets are identical by construction. The frame path is allocation-free:
/// geometry lands in fixed buffers.
/// </para>
/// </summary>
internal sealed class TransientStripRenderer : IDisposable
{
    private const int MaxDots = IslandMetrics.MaxDotsPerSide;
    private const int LastPage = PagerConstants.PageCount - 1;

    /// <summary>Glass fill of the mini islands: prototype `rgba(26, 26, 31, .72)`.</summary>
    private static readonly SKColor Glass = new(26, 26, 31, 184);

    private readonly ScaledText _text;
    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _icon = new() { IsAntialias = true };
    private readonly SKPaint _shadow = new() { IsAntialias = true };
    private readonly SKMaskFilter _shadowFar = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 12f);
    private readonly SKMaskFilter _shadowNear = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f);
    private readonly SKRect[] _dotsLeft = new SKRect[MaxDots];
    private readonly SKRect[] _dotsRight = new SKRect[MaxDots];

    private MiniRailFrame _frame;
    private string _label = string.Empty;
    private string _labelFrom = string.Empty;
    private int _trackedPage = -1;

    public TransientStripRenderer(TextEngine text)
    {
        _text = new ScaledText(text);
    }

    /// <summary>Current rail envelope in host-local DIP (diagnostics/tests).</summary>
    public SKRect Strip => _frame.Strip;

    /// <summary>Current committed page index (diagnostics/tests).</summary>
    public int Page => _frame.Page;

    /// <summary>Current capsule width in DIP (64..104, morphs while the pager moves).</summary>
    public float CapsuleWidth => _frame.CapsuleWidth;

    /// <summary>Copies the current fixed-buffer layout into a public snapshot (allocates; not per frame).</summary>
    public IslandStripLayout ToLayout()
    {
        var left = new SKRect[_frame.LeftDots];
        var right = new SKRect[_frame.RightDots];
        Array.Copy(_dotsLeft, left, _frame.LeftDots);
        Array.Copy(_dotsRight, right, _frame.RightDots);
        return new IslandStripLayout
        {
            Strip = _frame.Strip,
            Previous = _frame.Page > 0 ? _frame.Previous : SKRect.Empty,
            Current = _frame.Current,
            Next = _frame.Page < LastPage ? _frame.Next : SKRect.Empty,
            Page = _frame.Page,
            CapsuleWidth = _frame.CapsuleWidth,
            DotsLeft = left,
            DotsRight = right,
        };
    }

    /// <summary>Recomputes the rail layout for a shell rectangle and state.</summary>
    public void Update(in IslandRenderState state, SKRect shell)
    {
        _frame = MiniRailLayout.Measure(state, shell, _dotsLeft, _dotsRight);
        var label = state.Pages?.ShortTitle(_frame.Page)
            ?? ExpandedPageContent.DefaultPageTitles[Math.Clamp(_frame.Page, 0, LastPage)];
        if (_frame.Page != _trackedPage)
        {
            // The prototype cross-fades old → new label over 150 ms with a 3 DIP slide; the host's
            // LabelProgress drives how far that fade has travelled.
            if (_trackedPage >= 0)
            {
                _labelFrom = _label;
            }

            _trackedPage = _frame.Page;
        }

        _label = label;
        if (_labelFrom.Length == 0)
        {
            _labelFrom = label;
        }
    }

    /// <summary>
    /// Draws the transient preview rail below the shell (outside the shell clip). Nothing is painted
    /// at rest: the prototype has no idle dot row or capsule preview.
    /// </summary>
    public void Draw(SKCanvas canvas, in IslandRenderState state, SKRect shell)
    {
        var arch = state.Archipelago;
        var reveal = (float)Math.Clamp(arch.Opacity, 0, 1);
        if (!state.IsExpanded || !arch.Visible || reveal <= 0.01f)
        {
            return;
        }

        _text.SetScale(state.FontScale);
        Update(state, shell);

        canvas.Save();

        // Prototype2 reveals the rail with an opacity fade only (120 ms in / 220 ms out): no slide and
        // no scale, so the pills never move relative to the shell while they appear. Every slot then
        // carries its own spring alpha (neighbours fade at the first/last page) and content pulse.
        DrawShadows(canvas, reveal);
        DrawDots(canvas, reveal);

        var prevAlpha = reveal * _frame.PrevAlpha;
        if (_frame.Page > 0 && prevAlpha > 0.01f)
        {
            DrawNeighbour(canvas, _frame.Previous, _frame.Page - 1, prevAlpha, _frame.PulsePrev);
        }

        var nextAlpha = reveal * _frame.NextAlpha;
        if (_frame.Page < LastPage && nextAlpha > 0.01f)
        {
            DrawNeighbour(canvas, _frame.Next, _frame.Page + 1, nextAlpha, _frame.PulseNext);
        }

        DrawCurrent(canvas, reveal);
        canvas.Restore();
    }

    /// <summary>Hit-tests the rail; returns None when hidden or outside every pill/dot.</summary>
    public IslandHitTarget HitTest(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var arch = state.Archipelago;
        if (!state.IsExpanded || !arch.Visible || arch.Opacity <= 0.01)
        {
            return default;
        }

        Update(state, shell);
        var frame = _frame;

        if (frame.Page > 0 && frame.Previous.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, frame.Page - 1);
        }

        if (frame.Current.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, frame.Page);
        }

        if (frame.Page < LastPage && frame.Next.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.RailSlot, frame.Page + 1);
        }

        // Every dot in a cluster jumps to the farthest page in that direction (left → 01, right → 06),
        // exactly like the prototype's shared far-dot target. The 3 DIP dots keep the prototype's
        // 7×15 invisible hit area, so neighbouring targets never overlap.
        for (var i = 0; i < frame.LeftDots; i++)
        {
            if (DotHitRect(_dotsLeft[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotLeft, 0);
            }
        }

        for (var i = 0; i < frame.RightDots; i++)
        {
            if (DotHitRect(_dotsRight[i]).Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.DotRight, LastPage);
            }
        }

        return default;
    }

    /// <summary>Invisible per-dot hit area: 7 DIP wide and 15 DIP tall, centred on the 3 DIP dot.</summary>
    private static SKRect DotHitRect(SKRect dot)
    {
        var dx = ((float)MiniRailLayout.DotHitWidth - dot.Width) / 2f;
        var dy = ((float)MiniRailLayout.DotHitHeight - dot.Height) / 2f;
        return new SKRect(dot.Left - dx, dot.Top - dy, dot.Right + dx, dot.Bottom + dy);
    }

    private void DrawShadows(SKCanvas canvas, float alpha)
    {
        if (_frame.Page > 0)
        {
            DrawShadow(canvas, _frame.Previous, alpha);
        }

        DrawShadow(canvas, _frame.Current, alpha);

        if (_frame.Page < LastPage)
        {
            DrawShadow(canvas, _frame.Next, alpha);
        }
    }

    /// <summary>
    /// Two soft shadows per pill, matching the prototype's
    /// `0 10px 24px -12px rgba(0,0,0,.78), 0 2px 7px -3px rgba(0,0,0,.42)`.
    /// </summary>
    private void DrawShadow(SKCanvas canvas, SKRect rect, float alpha)
    {
        _shadow.Style = SKPaintStyle.Fill;

        var far = new SKRect(rect.Left + 12, rect.Top + 12, rect.Right - 12, rect.Bottom - 12);
        far.Offset(0, 10);
        if (far.Width > 0 && far.Height > 0)
        {
            _shadow.MaskFilter = _shadowFar;
            _shadow.Color = new SKColor(0, 0, 0, (byte)Math.Clamp(0.78f * alpha * 255, 0, 255));
            canvas.DrawRoundRect(far, far.Height / 2f, far.Height / 2f, _shadow);
        }

        var near = new SKRect(rect.Left + 3, rect.Top + 3, rect.Right - 3, rect.Bottom - 3);
        near.Offset(0, 2);
        if (near.Width > 0 && near.Height > 0)
        {
            _shadow.MaskFilter = _shadowNear;
            _shadow.Color = new SKColor(0, 0, 0, (byte)Math.Clamp(0.42f * alpha * 255, 0, 255));
            canvas.DrawRoundRect(near, near.Height / 2f, near.Height / 2f, _shadow);
        }
    }

    private void DrawDots(SKCanvas canvas, float alpha)
    {
        _fill.Style = SKPaintStyle.Fill;

        // Each cluster has its own spring alpha: the prototype fades a side out when it loses its
        // far pages and fades the other side in, while the positions spring to the new targets.
        var leftAlpha = alpha * _frame.LeftAlpha;
        if (leftAlpha > 0.01f)
        {
            _fill.Color = Alpha(SKColors.White, (float)IslandMetrics.DotAlpha * leftAlpha);
            for (var i = 0; i < _frame.LeftDots; i++)
            {
                DrawDot(canvas, _dotsLeft[i]);
            }
        }

        var rightAlpha = alpha * _frame.RightAlpha;
        if (rightAlpha > 0.01f)
        {
            _fill.Color = Alpha(SKColors.White, (float)IslandMetrics.DotAlpha * rightAlpha);
            for (var i = 0; i < _frame.RightDots; i++)
            {
                DrawDot(canvas, _dotsRight[i]);
            }
        }
    }

    private void DrawDot(SKCanvas canvas, SKRect dot)
    {
        if (dot.Width > dot.Height + 0.01f)
        {
            // Overflow dot: a 7×3 DIP pill with a 2 DIP radius (clamped to the 3 DIP height).
            var radius = Math.Min((float)IslandMetrics.DotWideRadius, dot.Height / 2f);
            canvas.DrawRoundRect(dot, radius, radius, _fill);
            return;
        }

        canvas.DrawCircle(dot.MidX, dot.MidY, dot.Height / 2f, _fill);
    }

    private void DrawNeighbour(SKCanvas canvas, SKRect rect, int page, float alpha, float pulse)
    {
        DrawGlass(canvas, rect, alpha);
        var size = (float)IslandMetrics.MiniNeighbourIconSize;
        var icon = new SKRect(rect.MidX - (size / 2f), rect.MidY - (size / 2f),
            rect.MidX + (size / 2f), rect.MidY + (size / 2f));
        // `.mini-content` colour rgba(255,255,255,.56) with the 0.9 icon opacity and the 150 ms
        // content pulse (0.35 → 1) the prototype plays when the icon changes.
        var content = alpha * MiniRailMotionValues.PulseOpacity(Ease(pulse));
        IconSet.Draw(canvas, IconFor(page), icon, Alpha(SKColors.White, 0.504f * content), _icon);
    }

    private void DrawCurrent(SKCanvas canvas, float alpha)
    {
        var rect = _frame.Current;
        DrawGlass(canvas, rect, alpha);

        // The content layer never scales with the capsule: measure the label with the text engine and
        // centre the icon + 4 DIP gap + label group, so 9 DIP text stays crisp at every width.
        var layout = _text.GetLayout(_label, IslandType.MiniLabel, float.PositiveInfinity);
        var iconSize = (float)IslandMetrics.MiniCurrentIconSize;
        var gap = (float)IslandMetrics.MiniContentGap;
        var groupWidth = iconSize + gap + layout.Width;
        var left = rect.MidX - (groupWidth / 2f);
        var content = alpha * MiniRailMotionValues.PulseOpacity(Ease(_frame.PulseCurrent));

        var icon = new SKRect(left, rect.MidY - (iconSize / 2f), left + iconSize, rect.MidY + (iconSize / 2f));
        IconSet.Draw(canvas, IconFor(_frame.Page), icon, Alpha(SKColors.White, 0.576f * content), _icon);

        var labelRect = new SKRect(
            left + iconSize + gap,
            rect.Top,
            left + iconSize + gap + layout.Width + 2f,
            rect.Bottom);

        var progress = Math.Clamp(_frame.LabelProgress, 0, 1);
        if (progress >= 0.999f || string.Equals(_labelFrom, _label, StringComparison.Ordinal))
        {
            _text.DrawLayout(canvas, layout, labelRect, Alpha(SKColors.White, 0.8f * content), TextAlign.Left, content);
            return;
        }

        // 150 ms label cross-fade: the old label slides up 3 DIP while the new one rises into place,
        // both eased with the prototype's smoothstep (p²(3−2p)).
        var eased = Ease(progress);
        var oldLayout = _text.GetLayout(_labelFrom, IslandType.MiniLabel, float.PositiveInfinity);
        var oldRect = new SKRect(labelRect.Left, labelRect.Top - (3f * eased), labelRect.Right, labelRect.Bottom - (3f * eased));
        var newRect = new SKRect(labelRect.Left, labelRect.Top + (3f * (1f - eased)), labelRect.Right, labelRect.Bottom + (3f * (1f - eased)));
        _text.DrawLayout(canvas, oldLayout, oldRect, Alpha(SKColors.White, 0.8f * content), TextAlign.Left, content * (1f - eased));
        _text.DrawLayout(canvas, layout, newRect, Alpha(SKColors.White, 0.8f * content), TextAlign.Left, content * eased);
    }

    /// <summary>Prototype smoothstep easing <c>p²(3−2p)</c> shared by the label fade and pulses.</summary>
    private static float Ease(float progress)
    {
        var p = Math.Clamp(progress, 0f, 1f);
        return p * p * (3f - (2f * p));
    }

    private void DrawGlass(SKCanvas canvas, SKRect rect, float alpha)
    {
        var radius = Math.Min(rect.Width, rect.Height) / 2f;
        _fill.Style = SKPaintStyle.Fill;
        _fill.Color = Alpha(Glass, alpha);
        canvas.DrawRoundRect(rect, radius, radius, _fill);
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
        _icon.Dispose();
        _shadow.Dispose();
        _shadowFar.Dispose();
        _shadowNear.Dispose();
    }
}
