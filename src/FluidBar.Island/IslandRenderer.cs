using System.Diagnostics;
using FluidBar.Core.Layout;
using SkiaSharp;
using ActivityKind = FluidBar.Core.Activities.ActivityKind;

namespace FluidBar.Island;

/// <summary>Renderer tuning knobs.</summary>
public sealed record IslandRendererOptions
{
    /// <summary>Bounded text layout cache capacity (entries; default 200).</summary>
    public int TextCacheCapacity { get; init; } = TextEngine.DefaultCacheCapacity;

    /// <summary>
    /// Bounded shadow image cache capacity (entries; default 4). One expanded shadow image is up to
    /// ~2.7 MB at 150 % DPI, so the old capacity of 8 could retain ~21 MB; 4 still covers every
    /// settled size plus the morph buckets that are actually revisited.
    /// </summary>
    public int ShadowCacheCapacity { get; init; } = 4;

    /// <summary>Bounded squircle path cache capacity (entries; default 32).</summary>
    public int PathCacheCapacity { get; init; } = 32;

    /// <summary>Draw the baked soft shadow (disable for pixel-overflow tests).</summary>
    public bool EnableShadow { get; init; } = true;

    /// <summary>Record frame timings in <see cref="IslandRenderer.Stats"/>.</summary>
    public bool TrackTimings { get; init; } = true;
}

/// <summary>Frame timing snapshot (milliseconds, wall clock).</summary>
public readonly record struct IslandRenderStats(
    long Frames,
    long DirtyFrames,
    double LastMilliseconds,
    double MeanMilliseconds,
    double LastDirtyMilliseconds,
    double MeanDirtyMilliseconds);

/// <summary>
/// Production Skia island renderer (MVP): renders the shell, compact surfaces, expanded home/media
/// pages with the continuous page strip, and the detached transient mini-island strip into an
/// offscreen surface. The renderer is UI-framework-free (no Avalonia/Win32), uses host-local DIP and
/// applies the DPI scale once.
/// <para>
/// Geometry comes from Core (<see cref="LayoutTokens"/>, <see cref="HostSurface"/>,
/// <see cref="ShellGeometry"/>, <see cref="PagePagerPolicy"/>); this project only draws. The shadow is
/// baked into a cached image (never a live <c>SKImageFilter</c> per frame), text/icon/path caches are
/// bounded, and warm frames do not allocate.
/// </para>
/// </summary>
public sealed class IslandRenderer : IDisposable
{
    private readonly IslandRendererOptions _options;
    private readonly TextEngine _text;
    private readonly bool _ownsText;
    private readonly AccentPalette _accents = new();
    private readonly ArtShaderCache _art;
    private readonly ArtworkImageCache _artwork;
    private readonly NumberTextCache _numbers = new();
    private readonly SquirclePathCache _paths;
    private readonly ShadowCache _shadows;
    private readonly CompactRenderer _compact;
    private readonly PageRenderer _pages;
    private readonly TransientStripRenderer _strip;

    private readonly SKPaint _fill = new() { IsAntialias = true };
    private readonly SKPaint _icon = new() { IsAntialias = true };
    private readonly SKPaint _stroke = new() { IsAntialias = true };
    private readonly SKPaint _clear = new() { IsAntialias = false, BlendMode = SKBlendMode.Src };
    private readonly SKPaint _glassPaint = new() { IsAntialias = true };

    private IslandTheme _theme;
    private SKShader? _glassShader;
    private float _glassHeight;
    private ShellGeometry? _hitGeometry;
    private HitGeometryKey _hitGeometryKey;
    private bool _disposed;

    private long _frames;
    private long _dirtyFrames;
    private double _lastMs;
    private double _totalMs;
    private double _lastDirtyMs;
    private double _totalDirtyMs;

    /// <summary>Creates a renderer. The text engine is owned by the caller when supplied.</summary>
    /// <param name="theme">Default theme (also used by <see cref="Measure"/> / diagnostics).</param>
    /// <param name="textEngine">Shared text cache; a private one is created when null.</param>
    /// <param name="options">Tuning knobs.</param>
    public IslandRenderer(IslandTheme? theme = null, TextEngine? textEngine = null, IslandRendererOptions? options = null)
    {
        _theme = theme ?? IslandTheme.Dark;
        _options = options ?? new IslandRendererOptions();
        _ownsText = textEngine is null;
        _text = textEngine ?? new TextEngine(_options.TextCacheCapacity);
        _art = new ArtShaderCache();
        _artwork = new ArtworkImageCache();
        _paths = new SquirclePathCache(_options.PathCacheCapacity);
        _shadows = new ShadowCache(_options.ShadowCacheCapacity);
        _compact = new CompactRenderer(_text, _accents, _art, _artwork, _numbers);
        _pages = new PageRenderer(_text, _accents, _art, _artwork, _numbers);
        _strip = new TransientStripRenderer(_text);
    }

    /// <summary>Default theme supplied to the constructor.</summary>
    public IslandTheme Theme => _theme;

    /// <summary>Shared text engine (layouts, shaping, cache counters).</summary>
    public TextEngine Text => _text;

    /// <summary>Tuning knobs.</summary>
    public IslandRendererOptions Options => _options;

    /// <summary>Bounded squircle path cache (diagnostics).</summary>
    public int PathCacheCount => _paths.Count;

    /// <summary>Bounded shadow image cache (diagnostics).</summary>
    public int ShadowCacheCount => _shadows.Count;

    /// <summary>
    /// Retained-cache accounting for the working-set audit: image caches, text layouts and paths.
    /// Cheap pointer reads except the text approximation (bounded by the cache capacity).
    /// </summary>
    public RenderResourceStats ResourceStats => new(
        _paths.Count,
        _paths.Builds,
        _shadows.Count,
        _shadows.PixelBytes,
        _shadows.Builds,
        _art.Count,
        _artwork.Count,
        _artwork.PixelBytes,
        _artwork.Builds,
        _text.CacheCount,
        _text.CacheCapacity,
        _text.ApproxCacheBytes,
        _numbers.Count);

    /// <summary>Frame timing snapshot.</summary>
    public IslandRenderStats Stats => new(
        _frames,
        _dirtyFrames,
        _lastMs,
        _frames == 0 ? 0 : _totalMs / _frames,
        _lastDirtyMs,
        _dirtyFrames == 0 ? 0 : _totalDirtyMs / _dirtyFrames);

    /// <summary>
    /// Fixed host surface size in DIP (488×384 float/flush, 488×444 dock). The host creates its DIB at
    /// this size (× DPI) and never resizes it per frame.
    /// </summary>
    public VisualSize Measure(in IslandRenderState state) => IslandMetrics.SurfaceSize(state.Mode);

    /// <summary>Host surface size in physical pixels at a DPI scale.</summary>
    public PixelRect MeasurePixels(in IslandRenderState state, float dpiScale)
        => IslandMetrics.SurfacePixels(state.Mode, dpiScale);

    /// <summary>
    /// Measures the detached strip (host-local DIP). Returns empty rects when the strip is hidden.
    /// Intended for host integration (UIA/click-through) and tests; not part of the frame path.
    /// </summary>
    public IslandStripLayout MeasureStrip(in IslandRenderState state)
    {
        var shell = IslandMetrics.ShellRect(state);
        _strip.Update(state, shell);
        return _strip.ToLayout();
    }

    /// <summary>Resets the timing counters.</summary>
    public void ResetStats()
    {
        _frames = 0;
        _dirtyFrames = 0;
        _lastMs = 0;
        _totalMs = 0;
        _lastDirtyMs = 0;
        _totalDirtyMs = 0;
    }

    /// <summary>Renders one full frame into a host-local DIP canvas (the canvas is scaled by the DPI).</summary>
    public void Render(SKCanvas canvas, in IslandRenderState state, float dpiScale)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ValidateScale(dpiScale);
        var start = _options.TrackTimings ? Stopwatch.GetTimestamp() : 0;
        RenderCore(canvas, state, dpiScale, null);
        Record(start, dirty: false);
    }

    /// <summary>
    /// Renders a damage rectangle (host-local DIP). Pixels outside <paramref name="damageDip"/> are left
    /// untouched; the host uploads only this rectangle.
    /// </summary>
    public void Render(SKCanvas canvas, in IslandRenderState state, float dpiScale, SKRect damageDip)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ValidateScale(dpiScale);
        var start = _options.TrackTimings ? Stopwatch.GetTimestamp() : 0;
        RenderCore(canvas, state, dpiScale, damageDip);
        Record(start, dirty: true);
    }

    /// <summary>Renders to a new premultiplied BGRA bitmap (matching the layered host DIB).</summary>
    public SKBitmap RenderToBitmap(in IslandRenderState state, float dpiScale)
    {
        var pixels = MeasurePixels(state, dpiScale);
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        Render(canvas, state, dpiScale);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>Renders to a new immutable image (transparent, premultiplied BGRA).</summary>
    public SKImage RenderToImage(in IslandRenderState state, float dpiScale)
    {
        var pixels = MeasurePixels(state, dpiScale);
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Skia could not create the island surface.");
        Render(surface.Canvas, state, dpiScale);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>Creates a reusable surface at the state's host size (caller owns it).</summary>
    public SKSurface CreateSurface(in IslandRenderState state, float dpiScale)
    {
        var pixels = MeasurePixels(state, dpiScale);
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return SKSurface.Create(info)
            ?? throw new InvalidOperationException("Skia could not create the island surface.");
    }

    /// <summary>Hit-tests a client point in host-local DIP.</summary>
    public IslandHitTarget HitTest(in IslandRenderState state, VisualPoint clientDip)
        => HitTest(state, (float)clientDip.X, (float)clientDip.Y);

    /// <summary>Hit-tests a client point in host-local DIP.</summary>
    public IslandHitTarget HitTest(in IslandRenderState state, float x, float y)
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
                var metrics = FontScaleMetrics.From(state.FontScale);
                var pageScroll = PageScrollOffset(state, page, metrics, pageRect);
                var control = PageMetricsLayout.HitTest(
                    page,
                    pageRect,
                    x,
                    y,
                    metrics,
                    pageScroll,
                    PageItemCount(state, page),
                    out var index);
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
        else if (SplitIsletHit(state, x, y) is { } split)
        {
            return split;
        }
        else if (IslandMetrics.OverflowBadgeRect(state) is { Width: > 0 } badge && badge.Contains(x, y))
        {
            return new IslandHitTarget(IslandHitKind.OverflowBadge, state.PageIndex);
        }
        else if (ContainsShell(state, shell, x, y))
        {
            // The compact media pill has no play/pause button (removed on request): the whole pill is a
            // single expand target, so nothing in it can swallow the click.
            return new IslandHitTarget(IslandHitKind.Compact, state.PageIndex);
        }

        var stripHit = _strip.HitTest(state, shell, x, y);
        return stripHit.IsInteractive ? stripHit : default;
    }

    // ------------------------------------------------------------------ rendering

    private void RenderCore(SKCanvas canvas, in IslandRenderState state, float dpiScale, SKRect? damageDip)
    {
        var size = Measure(state);
        var surface = new SKRect(0, 0, (float)size.Width, (float)size.Height);
        var damage = damageDip ?? surface;

        canvas.Save();
        canvas.Scale(dpiScale);
        canvas.ClipRect(damage);
        _clear.Color = SKColors.Transparent;
        canvas.DrawRect(damage, _clear);

        if (state.IsVisible)
        {
            DrawScene(canvas, state, dpiScale);
        }

        canvas.Restore();
    }

    private void DrawScene(SKCanvas canvas, in IslandRenderState state, float dpiScale)
    {
        _theme = state.Theme ?? IslandTheme.Dark;
        var shell = IslandMetrics.ShellRect(state);
        var opacity = (float)Math.Clamp(state.Opacity, 0, 1);
        var press = (float)Math.Clamp(state.PressScale, 0.5, 1.5);

        if (Math.Abs(press - 1) > 0.0001f)
        {
            canvas.Save();
            canvas.Translate(shell.MidX, shell.MidY);
            canvas.Scale(press, press);
            canvas.Translate(-shell.MidX, -shell.MidY);
        }

        if (_options.EnableShadow && state.Form is not ShellForm.Expanded)
        {
            // The expanded host window is deliberately much larger than the 440x284 panel (the
            // transparent margin is click-through padding). Painting the large shell shadow into
            // that margin makes the layered window show a translucent grey block on the desktop,
            // so the expanded panel is shadowless; compact/peek keep the prototype shadow.
            DrawShadow(canvas, state, shell, dpiScale, opacity);
        }

        var path = _paths.Get(state.ShellWidth, state.ShellHeight, state.ShellRadii);

        // Multi-island stacking: with the "stacked" strategy the scheduler keeps up to two preempted
        // activities alive as ghost layers; they are painted as smaller copies of the compact pill above
        // the primary shell (LayoutTokens.Ghost* tokens). The "latest only" strategy reports
        // GhostCount = 0, so the renderer draws nothing extra.
        DrawGhostStack(canvas, state, shell, opacity);

        // Split islets are painted before the shell body so their birth bud is hidden behind the pill
        // edge and only the separated ball becomes visible (cell-division read).
        DrawSplitIslets(canvas, state, opacity, dpiScale);

        canvas.Save();
        canvas.Translate(shell.Left, shell.Top);

        DrawShellBody(canvas, state, path, opacity);

        // Prototype2 layers the compact surface and the expanded panel as siblings, each with its own
        // CSS transition; both are painted while a transition is in flight. The ghost stack is painted
        // above, before the shell body, so the primary pill always sits on top of it.
        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        var compactOpacity = (float)Math.Clamp(opacity * state.ResolvedCompactLayerOpacity, 0, 1);
        if (compactOpacity > 0.002f)
        {
            _compact.Draw(canvas, state, compactOpacity);
        }

        var expandedOpacity = (float)Math.Clamp(opacity * state.ResolvedExpandedLayerOpacity, 0, 1);
        if (expandedOpacity > 0.002f)
        {
            var chromeOpacity = (float)Math.Clamp(opacity * state.ResolvedChromeOpacity, 0, 1);
            DrawExpanded(canvas, state, expandedOpacity, chromeOpacity);
        }

        canvas.Restore();

        // The +N chip overlaps the shell's rounded top-right corner, so it is painted after the clip
        // (still in shell-local code space) instead of being cut by the squircle.
        if (state.Form != ShellForm.Expanded && state.OverflowCount > 0)
        {
            DrawOverflowBadge(canvas, state, opacity);
        }

        canvas.Restore();

        if (Math.Abs(press - 1) > 0.0001f)
        {
            canvas.Restore();
        }

        _strip.Draw(canvas, state, shell);
        DrawFocusRing(canvas, state);
    }

    /// <summary>
    /// Draws the logical keyboard/UIA focus ring (spec §2.9/§3.6): a 2 DIP informational outline
    /// around the focused element, in host-local DIP. Painted after the strip so it is never covered.
    /// </summary>
    private void DrawFocusRing(SKCanvas canvas, in IslandRenderState state)
    {
        if (state.FocusRing is not { } ring || ring.Width <= 0 || ring.Height <= 0)
        {
            return;
        }

        var rect = new SKRect((float)ring.X, (float)ring.Y, (float)ring.Right, (float)ring.Bottom);
        rect.Inflate(-1f, -1f);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 2f;
        _stroke.Shader = null;
        _stroke.Color = _theme.Info.WithAlpha((byte)Math.Clamp(230 * state.Opacity, 0, 255));
        canvas.DrawRoundRect(rect, 6f, 6f, _stroke);
    }

    /// <summary>
    /// Paints the ghost layers of the compact stack (behind/above the primary pill). Each layer is the
    /// shell silhouette scaled down, offset by the Core tokens and faded by its layer opacity.
    /// </summary>
    private void DrawGhostStack(SKCanvas canvas, in IslandRenderState state, SKRect shell, float opacity)
    {
        if (state.GhostCount <= 0 || state.Form == ShellForm.Expanded)
        {
            return;
        }

        var compactOpacity = (float)Math.Clamp(opacity * state.ResolvedCompactLayerOpacity, 0, 1);
        if (compactOpacity <= 0.01f)
        {
            return;
        }

        var materialGlass = _theme.Material == IslandMaterial.SimulatedGlass;
        var fillOpacity = materialGlass
            ? 0.72f
            : (float)Math.Clamp(_theme.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);
        var offsets = new[] { LayoutTokens.GhostOffsetY1, LayoutTokens.GhostOffsetY2 };
        var scales = new[] { LayoutTokens.GhostScale1, LayoutTokens.GhostScale2 };
        var opacities = new[] { LayoutTokens.GhostOpacity1, LayoutTokens.GhostOpacity2 };
        var count = Math.Min(state.GhostCount, LayoutTokens.MaxGhosts);
        // Wheel rotation: the layers slide down/fade while the new order flies in, so the exchange
        // reads as a stack shuffle instead of an instant content swap.
        var spin = (float)Math.Clamp(state.StackSpin, 0, 1);
        var spinWave = (float)Math.Sin(Math.PI * spin);
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        for (var i = 0; i < count; i++)
        {
            var scale = (float)scales[i] * (1f - (0.04f * spinWave));
            var ghostWidth = shell.Width * scale;
            var ghostHeight = shell.Height * scale;
            var ghost = new SKRect(
                shell.Left + (shell.Width - ghostWidth) / 2,
                shell.Top + (float)offsets[i] + (spinWave * 7f),
                shell.Left + (shell.Width + ghostWidth) / 2,
                shell.Top + (float)offsets[i] + (spinWave * 7f) + ghostHeight);
            var layerOpacity = (float)opacities[i] * (1f - (0.45f * spinWave));
            _fill.Color = Alpha(_theme.Background,
                fillOpacity * layerOpacity * compactOpacity);
            canvas.DrawRoundRect(ghost, ghostHeight / 2, ghostHeight / 2, _fill);
            // Key line: without it the ghost silhouette disappears into the dark shell/desktop
            // (prototype README: "softened fills and key lines"). Legacy Avalonia shell used the same
            // LineStrong hairline for its ghost pills.
            _stroke.Style = SKPaintStyle.Stroke;
            _stroke.StrokeWidth = (float)LayoutTokens.HairlineWidth;
            _stroke.Shader = null;
            _stroke.Color = Alpha(_theme.LineStrong, layerOpacity * compactOpacity);
            canvas.DrawRoundRect(ghost, ghostHeight / 2, ghostHeight / 2, _stroke);
        }
    }

    /// <summary>Paints the compact "+N" overflow chip (small rounded pill, 1 DIP key line, tabular digits).</summary>
    private void DrawOverflowBadge(SKCanvas canvas, in IslandRenderState state, float opacity)
    {
        var absolute = IslandMetrics.OverflowBadgeRect(state);
        if (absolute.Width <= 0 || absolute.Height <= 0)
        {
            return;
        }

        var shell = IslandMetrics.ShellRect(state);
        var rect = new SKRect(
            absolute.Left - shell.Left,
            absolute.Top - shell.Top,
            absolute.Right - shell.Left,
            absolute.Bottom - shell.Top);

        var alpha = Math.Clamp(opacity, 0, 1);
        var radius = rect.Height / 2;
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.Color = Alpha(_theme.BackgroundRaised, 0.92f * alpha);
        canvas.DrawRoundRect(rect, radius, radius, _fill);
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = 1f;
        _stroke.Shader = null;
        _stroke.Color = Alpha(_theme.LineStrong, alpha);
        canvas.DrawRoundRect(rect, radius, radius, _stroke);
        _text.Draw(canvas, "+" + state.OverflowCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IslandType.OverflowBadge, rect, _theme.ForegroundSecondary, TextAlign.Center, alpha);
    }

    /// <summary>
    /// Paints the split previews as dark glass capsules on the same baseline as the primary pill:
    /// left circular thumbnail/icon, middle rounded-square status slot with optional red count badge,
    /// optional circular action on the right. No text lives outside the capsule.
    /// </summary>
    private void DrawSplitIslets(SKCanvas canvas, in IslandRenderState state, float opacity, float dpiScale)
    {
        if (state.SplitIslets.Length == 0 || state.SplitProgress <= 0.01)
        {
            return;
        }

        var finals = IslandMetrics.SplitIsletRects(state);
        var shell = IslandMetrics.ShellRect(state);
        // Exactly the primary pill's material: same palette background, same fill opacity, no tint.
        var fillOpacity = _theme.Material == IslandMaterial.SimulatedGlass
            ? 0.72f
            : (float)Math.Clamp(_theme.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);
        for (var i = 0; i < finals.Length && i < state.SplitIslets.Length; i++)
        {
            var progress = (float)IslandMetrics.SplitIsletProgress(state, i);
            if (progress <= 0.01f)
            {
                continue;
            }

            var islet = state.SplitIslets[i];
            var rect = IslandMetrics.SplitIsletAnimatedRect(state, finals, i);
            var alpha = Math.Clamp(progress * 1.35f, 0, 1) * opacity;
            // Bud: the capsule edge bulges out of the pill before the capsule separates.
            if (progress < 0.6f)
            {
                var bud = Math.Clamp(progress / 0.6f, 0, 1);
                var edgeX = finals[i].MidX >= shell.MidX ? shell.Right : shell.Left;
                var budWidth = finals[i].Width * 0.45f * bud;
                var budHeight = finals[i].Height * (0.55f + (0.45f * bud));
                var budRect = new SKRect(
                    edgeX - (budWidth / 2),
                    shell.MidY - (budHeight / 2),
                    edgeX + (budWidth / 2),
                    shell.MidY + (budHeight / 2));
                _fill.Style = SKPaintStyle.Fill;
                _fill.Shader = null;
                _fill.Color = Alpha(_theme.Background, fillOpacity * bud * opacity);
                canvas.DrawRoundRect(budRect, budHeight / 2, budHeight / 2, _fill);
            }

            var radius = rect.Height / 2;
            // Capsule shadow is intentionally omitted: the primary pill's shadow already grounds the
            // row, and the capsule fill stays byte-identical to the pill interior.


            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            // Src inside the capsule: the opaque slot replaces the shadow beneath, so the capsule
            // interior is exactly the same palette colour as the primary pill even though the two
            // shadows have different shapes.
            _fill.BlendMode = SKBlendMode.Src;
            _fill.Color = SplitFillColor(fillOpacity, alpha);
            canvas.DrawRoundRect(rect, radius, radius, _fill);
            _fill.BlendMode = SKBlendMode.SrcOver;
            _stroke.Style = SKPaintStyle.Stroke;
            _stroke.StrokeWidth = 1f;
            _stroke.Shader = null;
            _stroke.Color = Alpha(_theme.LineStrong, alpha);
            canvas.DrawRoundRect(rect, radius, radius, _stroke);

            // Content is clipped to the capsule so the birth animation can never leak glyphs outside.
            canvas.Save();
            canvas.ClipRoundRect(new SKRoundRect(rect, radius, radius), SKClipOperation.Intersect, antialias: true);

            var disc = IslandMetrics.SplitIsletDiscHeight(state);
            var discCentre = new SKPoint(rect.Left + 4 + (disc / 2), rect.MidY);
            // Thumbnail fallback: neutral dark disc + white glyph (no accent ring).
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = Alpha(_theme.LineStrong, 0.16f * alpha);
            canvas.DrawCircle(discCentre.X, discCentre.Y, disc / 2, _fill);
            var iconSize = disc * 0.5f;
            IconSet.Draw(canvas, islet.Icon,
                new SKRect(discCentre.X - (iconSize / 2), discCentre.Y - (iconSize / 2),
                    discCentre.X + (iconSize / 2), discCentre.Y + (iconSize / 2)),
                Alpha(SKColors.White, alpha), _icon);

            // Middle slot: a red semantic count badge when the activity carries one, otherwise a
            // neutral rounded square with a white/grey status dot. No accent decoration anywhere.
            var slotWidth = IslandMetrics.SplitIsletSlotWidth(islet);
            var slot = new SKRect(
                rect.Left + 4 + disc + 3,
                rect.MidY - 7,
                rect.Left + 4 + disc + 3 + slotWidth,
                rect.MidY + 7);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            var badgeText = islet.Badge;
            if (!string.IsNullOrWhiteSpace(badgeText))
            {
                _fill.Color = Alpha(new SKColor(0xFF, 0x45, 0x3A), alpha);
                canvas.DrawRoundRect(slot, 7, 7, _fill);
                _text.Draw(canvas, badgeText,
                    new TextStyle(FontRole.Mono, 8.5f), slot, SKColors.White, TextAlign.Center, alpha);
            }
            else
            {
                _fill.Color = Alpha(_theme.LineStrong, 0.22f * alpha);
                canvas.DrawRoundRect(slot, 5, 5, _fill);
                _fill.Color = Alpha(_theme.ForegroundSecondary, alpha);
                canvas.DrawCircle(slot.MidX, slot.MidY, 2.8f, _fill);
            }

            if (islet.ShowAction)
            {
                var action = new SKRect(rect.Right - 20, rect.MidY - 8, rect.Right - 4, rect.MidY + 8);
                _fill.Style = SKPaintStyle.Fill;
                _fill.Shader = null;
                _fill.Color = Alpha(_theme.LineStrong, 0.22f * alpha);
                canvas.DrawCircle(action.MidX, action.MidY, 8, _fill);
                _stroke.Style = SKPaintStyle.Stroke;
                _stroke.StrokeWidth = 1f;
                _stroke.Shader = null;
                _stroke.Color = Alpha(_theme.LineStrong, alpha);
                canvas.DrawCircle(action.MidX, action.MidY, 8, _stroke);
                IconSet.Draw(canvas, IconKind.Pause,
                    new SKRect(action.MidX - 4, action.MidY - 4, action.MidX + 4, action.MidY + 4),
                    Alpha(SKColors.White, alpha), _icon);
            }

            canvas.Restore();
        }
    }

    /// <summary>Opaque split-surface fill: the primary pill and every capsule use the same byte values
    /// so a pixel sample of their backgrounds is identical while split previews are visible.</summary>
    private SKColor SplitFillColor(float fillOpacity, float opacity)
        => new(
            (byte)Math.Clamp(_theme.Background.Red * fillOpacity, 0, 255),
            (byte)Math.Clamp(_theme.Background.Green * fillOpacity, 0, 255),
            (byte)Math.Clamp(_theme.Background.Blue * fillOpacity, 0, 255),
            (byte)Math.Clamp(255f * opacity, 0, 255));

    private IslandHitTarget? SplitIsletHit(in IslandRenderState state, float x, float y)
    {
        if (state.Form == ShellForm.Expanded || state.SplitIslets.Length == 0)
        {
            return null;
        }

        var finals = IslandMetrics.SplitIsletRects(state);
        for (var i = 0; i < finals.Length && i < state.SplitIslets.Length; i++)
        {
            if (finals[i].Contains(x, y))
            {
                return new IslandHitTarget(IslandHitKind.SplitIslet, (int)state.SplitIslets[i].Page, Index: i);
            }
        }

        return null;
    }

    private void DrawShellBody(SKCanvas canvas, in IslandRenderState state, SKPath path, float opacity)
    {
        var materialGlass = _theme.Material == IslandMaterial.SimulatedGlass;
        var fillOpacity = materialGlass
            ? 0.72f
            : (float)Math.Clamp(_theme.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);

        // While split capsules are visible they share the primary's exact material. Using Src for
        // both interiors means the capsule and the pill have byte-identical background colour/alpha
        // (the two shadows have different shapes and would otherwise bleed through differently).
        var splitVisible = state.SplitIslets.Length > 0 && state.SplitProgress > 0.01;
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.BlendMode = splitVisible ? SKBlendMode.Src : SKBlendMode.SrcOver;
        _fill.Color = splitVisible
            ? SplitFillColor(fillOpacity, opacity)
            : Alpha(_theme.Background, fillOpacity * opacity);
        canvas.DrawPath(path, _fill);
        _fill.BlendMode = SKBlendMode.SrcOver;

        if (materialGlass)
        {
            var shader = GetGlassShader((float)state.ShellHeight);
            _glassPaint.Style = SKPaintStyle.Fill;
            _glassPaint.Shader = shader;
            _glassPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(0x20 * opacity, 0, 255));
            canvas.DrawPath(path, _glassPaint);
            _glassPaint.Shader = null;
        }

        // Prototype2 declares `.island { border: 0 }`: the shell has no rim/outline at all. The old
        // LineStrong hairline is gone so the silhouette is pure fill + shadow (see ShellOutlineTests,
        // which still guards against an accent-tinted rim ever coming back).
    }

    private SKShader GetGlassShader(float height)
    {
        if (_glassShader is not null && Math.Abs(_glassHeight - height) < 0.5f)
        {
            return _glassShader;
        }

        _glassShader?.Dispose();
        _glassHeight = height;
        _glassShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            [new SKColor(0xFF, 0xFF, 0xFF, 0x20), SKColors.Transparent],
            [0f, 0.35f],
            SKShaderTileMode.Clamp);
        return _glassShader;
    }

    private void DrawShadow(SKCanvas canvas, in IslandRenderState state, SKRect shell, float dpiScale, float opacity)
    {
        var dock = state.Mode == IslandMode.Dock;
        ShadowLayer primary;
        ShadowLayer secondary;
        if (dock)
        {
            // Dock keeps its compact token shadow (single layer, spec 3.5).
            primary = new ShadowLayer(
                LayoutTokens.DockShadowBlur / 2,
                LayoutTokens.DockShadowOffsetY,
                0,
                (byte)Math.Clamp(LayoutTokens.DockShadowOpacity * 255, 0, 255));
            secondary = default;
        }
        else
        {
            // Prototype2's two-layer shell shadow; CSS blur / 2 = Gaussian sigma.
            primary = new ShadowLayer(
                _theme.ShadowBlur / 2,
                _theme.ShadowOffsetY,
                _theme.ShadowSpread,
                (byte)Math.Clamp(_theme.ShadowOpacity * 255, 0, 255));
            secondary = new ShadowLayer(
                _theme.ShadowBlur2 / 2,
                _theme.ShadowOffsetY2,
                _theme.ShadowSpread2,
                (byte)Math.Clamp(_theme.ShadowOpacity2 * 255, 0, 255));
        }

        var image = _shadows.Get(
            state.ShellWidth,
            state.ShellHeight,
            state.ShellRadii,
            dpiScale,
            _theme.ShadowColor,
            primary,
            secondary,
            state.ShellMorphing ? ShadowCache.MorphBucketDip : 0);
        _shadows.Draw(canvas, image, shell, opacity);
    }

    private void DrawExpanded(SKCanvas canvas, in IslandRenderState state, float layerOpacity, float chromeOpacity)
    {
        var content = new SKRect(
            (float)IslandMetrics.ContentLeft,
            (float)IslandMetrics.ContentTop,
            (float)(IslandMetrics.ContentLeft + IslandMetrics.ContentWidth),
            (float)(IslandMetrics.ContentTop + IslandMetrics.ContentHeight));

        var pagePosition = double.IsFinite(state.Pager.PagePosition)
            ? Math.Clamp(state.Pager.PagePosition, 0, Math.Max(1, state.Pages.PageCount) - 1)
            : 0;
        var pitch = FluidBar.Core.Interaction.PagerConstants.PagePitchDip;
        var offset = (float)(pagePosition * pitch);

        // Prototype2 moves a single flex track with translate3d(-offset * 400px); the pages themselves
        // are never scaled, faded or parallaxed. Keep the transition a pure horizontal translation.
        canvas.Save();
        canvas.ClipRect(content);
        for (var page = 0; page < state.Pages.PageCount; page++)
        {
            var left = content.Left + (page * (float)pitch) - offset;
            var pageRect = new SKRect(left, content.Top, left + (float)pitch, content.Bottom);
            if (!pageRect.Intersects(content))
            {
                continue;
            }

            canvas.Save();
            canvas.ClipRect(pageRect);
            if (page is 0 or 1 or 5)
            {
                // Home/media/widgets overflow the 232 DIP body at large font scales: the page scrolls
                // inside that body (the wheel host writes the model offset, clamped by the metric layout).
                var metrics = FontScaleMetrics.From(state.FontScale);
                var scroll = page switch
                {
                    0 => state.Pages.Home.ScrollOffset,
                    1 => state.Pages.Media.ScrollOffset,
                    _ => state.Pages.Widgets.ScrollOffset,
                };
                var itemCount = page == 5 ? state.Pages.Widgets.Tasks?.Count ?? 0 : 0;
                var clamped = PageMetricsLayout.ClampScroll(pageRect, page, scroll, metrics, itemCount);
                var body = PageMetricsLayout.Body(pageRect);
                canvas.Save();
                canvas.ClipRect(body);
                canvas.Translate(0, -(float)clamped);
                _pages.DrawPage(canvas, state, page, pageRect, layerOpacity);
                canvas.Restore();
            }
            else
            {
                _pages.DrawPage(canvas, state, page, pageRect, layerOpacity);
            }

            canvas.Restore();
        }

        canvas.Restore();
        _pages.DrawChrome(canvas, state, content, chromeOpacity);
    }

    // ------------------------------------------------------------------ hit-test helpers

    private bool ContainsShell(in IslandRenderState state, SKRect shell, float x, float y)
    {
        var key = new HitGeometryKey(
            (long)Math.Round(shell.Left * 2),
            (long)Math.Round(shell.Top * 2),
            (long)Math.Round(shell.Width * 2),
            (long)Math.Round(shell.Height * 2),
            (long)Math.Round(state.ShellRadii.TopLeft * 2),
            (long)Math.Round(state.ShellRadii.TopRight * 2),
            (long)Math.Round(state.ShellRadii.BottomRight * 2),
            (long)Math.Round(state.ShellRadii.BottomLeft * 2));
        if (_hitGeometry is null || !key.Equals(_hitGeometryKey))
        {
            _hitGeometry = ShellGeometry.Create(
                new VisualRect(shell.Left, shell.Top, shell.Width, shell.Height),
                state.ShellRadii);
            _hitGeometryKey = key;
        }

        return _hitGeometry.Contains(x, y);
    }

    private static int PageUnder(in IslandRenderState state, SKRect content, float x)
    {
        var pitch = FluidBar.Core.Interaction.PagerConstants.PagePitchDip;
        var offset = state.Pager.PagePosition * pitch;
        var local = x - content.Left + offset;
        var page = (int)Math.Floor(local / pitch);
        return Math.Clamp(page, 0, FluidBar.Core.Interaction.PagerConstants.PageCount - 1);
    }

    private static float PageScrollOffset(in IslandRenderState state, int page, in FontScaleMetrics metrics, SKRect pageRect)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return page switch
        {
            0 => PageMetricsLayout.ClampScroll(pageRect, 0, pages.Home.ScrollOffset, metrics),
            1 => PageMetricsLayout.ClampScroll(pageRect, 1, pages.Media.ScrollOffset, metrics),
            2 => (float)pages.Notifications.ScrollOffset,
            3 => (float)pages.Clipboard.ScrollOffset,
            4 => (float)pages.Agent.ScrollOffset,
            // The widgets card grows with the task rows, so the clamp needs the row count.
            5 => PageMetricsLayout.ClampScroll(pageRect, 5, pages.Widgets.ScrollOffset, metrics,
                pages.Widgets.Tasks?.Count ?? 0),
            _ => 0f,
        };
    }

    private static int PageItemCount(in IslandRenderState state, int page)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return page switch
        {
            0 => 4,
            1 => 1,
            2 => pages.Notifications.Items?.Count ?? 0,
            3 => pages.Clipboard.Items?.Count ?? 0,
            4 => pages.Agent.Items?.Count ?? 0,
            5 => pages.Widgets.Tasks?.Count ?? 0,
            _ => int.MaxValue,
        };
    }

    private static void ValidateScale(float dpiScale)
    {
        if (!float.IsFinite(dpiScale) || dpiScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "DPI scale must be finite and positive.");
        }
    }

    private void Record(long start, bool dirty)
    {
        if (!_options.TrackTimings)
        {
            _frames++;
            if (dirty)
            {
                _dirtyFrames++;
            }

            return;
        }

        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        _frames++;
        _totalMs += ms;
        _lastMs = ms;
        if (dirty)
        {
            _dirtyFrames++;
            _totalDirtyMs += ms;
            _lastDirtyMs = ms;
        }
    }

    private static SKColor Alpha(SKColor color, float opacity)
        => opacity >= 0.999f
            ? color
            : color.WithAlpha((byte)Math.Clamp(color.Alpha * opacity, 0, 255));

    /// <summary>Disposes caches and paints (the injected text engine is not disposed).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _paths.Dispose();
        _shadows.Dispose();
        _art.Dispose();
        _artwork.Dispose();
        _compact.Dispose();
        _pages.Dispose();
        _strip.Dispose();
        _glassShader?.Dispose();
        _glassShader = null;
        _fill.Dispose();
        _stroke.Dispose();
        _clear.Dispose();
        _glassPaint.Dispose();
        if (_ownsText)
        {
            _text.Dispose();
        }
    }

    private readonly record struct HitGeometryKey(
        long Left,
        long Top,
        long Width,
        long Height,
        long TopLeft,
        long TopRight,
        long BottomRight,
        long BottomLeft);
}
