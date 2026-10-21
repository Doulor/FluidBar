using System.Diagnostics;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
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

    /// <summary>
    /// Freezes the split satellites' agent ring at this angle (degrees) instead of spinning it from
    /// the renderer's monotonic clock. Deterministic golden/evidence captures only.
    /// </summary>
    public double? SplitRingAngleDegrees { get; init; }
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

    // Split satellites: one monotonic clock for the legacy ring spin (the contract path carries its
    // own RingAngleDegrees) and two cached blur filters for the ball/cover contact shadows, so a warm
    // frame allocates nothing beyond the small gradient shaders.
    private readonly Stopwatch _ringClock = Stopwatch.StartNew();
    private SKMaskFilter? _splitBallBlur;
    private float _splitBallBlurScale;
    private SKMaskFilter? _splitCoverBlur;
    private float _splitCoverBlurScale;
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
                var kind = state.Pages.PageSlotAt(page)?.Kind ?? PageSlotKind.Plugin;
                var pageScroll = PageScrollOffset(state, kind, metrics, pageRect);
                var control = PageMetricsLayout.HitTest(
                    kind,
                    pageRect,
                    x,
                    y,
                    metrics,
                    pageScroll,
                    PageItemCount(state, kind),
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
    /// Paints the split previews as pure circles on the primary pill's baseline. The pose comes from
    /// <see cref="IslandRenderState.SatelliteFrames"/> when the host drives the satellites
    /// (docs/satellite-render-contract.md); otherwise the legacy SplitProgress birth path runs
    /// unchanged, so settled snapshots keep their pixels.
    /// <para>
    /// Every circle carries exactly one element: an agent gets only its blue round-capped ring, media
    /// gets a rounded-square contain-fit cover inside a hairline well (or a neutral note), everything
    /// else one white icon. The polish is pure light modelling (SplitSatelliteStyle): 1 DIP top rim
    /// highlight, a barely visible bottom shade, a soft contact shadow, and a 1 DIP cover hairline with
    /// a recessed top shade. No labels, badges, status dots and never an accent-tinted body.
    /// </para>
    /// </summary>
    private void DrawSplitIslets(SKCanvas canvas, in IslandRenderState state, float opacity, float dpiScale)
    {
        if (state.SplitIslets.Length == 0)
        {
            return;
        }

        var framed = state.SatelliteFrames.Length == state.SplitIslets.Length;
        if (!framed && state.SplitProgress <= 0.01)
        {
            return;
        }

        var fillOpacity = _theme.Material == IslandMaterial.SimulatedGlass
            ? 0.72f
            : (float)Math.Clamp(_theme.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);

        if (framed)
        {
            DrawSplitFrames(canvas, state, opacity, dpiScale, fillOpacity);
            return;
        }

        var finals = IslandMetrics.SplitIsletRects(state);
        var ringAngle = _options.SplitRingAngleDegrees
            ?? SplitSatelliteStyle.RingAngleDegrees(_ringClock.Elapsed.TotalSeconds);
        for (var i = 0; i < finals.Length && i < state.SplitIslets.Length; i++)
        {
            var progress = (float)IslandMetrics.SplitIsletProgress(state, i);
            if (progress <= 0.01f)
            {
                continue;
            }

            var rect = IslandMetrics.SplitIsletAnimatedRect(state, finals, i);
            var alpha = Math.Clamp(progress * 1.35f, 0, 1) * opacity;
            DrawSplitSurface(canvas, state.SplitIslets[i], rect, new SplitSurfaceStyle(
                alpha, 0f, 0f, 0f, 1f, ringAngle, fillOpacity, dpiScale));
        }
    }

    /// <summary>
    /// Contract path: one frame per satellite, already carrying the composed scale (birth/merge ×
    /// press × hover × breathing), the directional stretch about the centre and the ring angle.
    /// </summary>
    private void DrawSplitFrames(SKCanvas canvas, in IslandRenderState state, float opacity,
        float dpiScale, float fillOpacity)
    {
        var shell = IslandMetrics.ShellRect(state);
        var baseDiameter = IslandMetrics.SplitIsletDiameter(state);
        for (var i = 0; i < state.SplitIslets.Length; i++)
        {
            var frame = state.SatelliteFrames[i];
            if (frame.State is SplitSatelliteState.Hidden or SplitSatelliteState.Merged)
            {
                continue;
            }

            if (!double.IsFinite(frame.Scale) || !double.IsFinite(frame.X) || !double.IsFinite(frame.Y))
            {
                continue;
            }

            var radius = (float)(baseDiameter * 0.5 * Math.Clamp(frame.Scale, 0.1, 2.0));
            if (radius < 0.5f)
            {
                continue;
            }

            // Directional stretch: scale about the centre, then compensate the stroke weight so the
            // rim/hairline/ring keep their DIP width instead of being stretched into ellipses.
            var sx = 1f + (float)Math.Clamp(frame.StretchX, -0.4, 0.4);
            var sy = 1f + (float)Math.Clamp(frame.StretchY, -0.4, 0.4);
            var strokeScale = 1f / Math.Max(0.5f, Math.Max(sx, sy));
            var surface = new SplitSurfaceStyle(
                Math.Clamp(opacity, 0f, 1f),
                (float)Math.Clamp(frame.PressDepth, 0, 1),
                (float)Math.Clamp(frame.Highlight, 0, 1),
                (float)Math.Clamp(frame.Brightness, -1, 0),
                strokeScale,
                frame.RingAngleDegrees,
                fillOpacity,
                dpiScale);

            canvas.Save();
            canvas.Translate(shell.MidX + (float)frame.X, shell.MidY + (float)frame.Y);
            canvas.Scale(sx, sy);
            DrawSplitSurface(canvas, state.SplitIslets[i], new SKRect(-radius, -radius, radius, radius), surface);
            canvas.Restore();
        }
    }

    /// <summary>Paints one satellite ball (body, light modelling, content) into <paramref name="ball"/>.</summary>
    private void DrawSplitSurface(SKCanvas canvas, in IslandSplitIslet islet, SKRect ball, in SplitSurfaceStyle surface)
    {
        var radius = ball.Width / 2;
        var alpha = surface.Alpha;

        // 1) Soft contact shadow: lifts the ball off the desktop without a visible halo.
        DrawSplitBlurredCircle(canvas, ball.MidX, ball.MidY + SplitSatelliteStyle.BallShadowOffsetYDip,
            radius * 0.98f, SplitSatelliteStyle.BallShadowAlpha, alpha, surface.DpiScale, cover: false);

        // 1b) Hover glow: white 6 % blurred halo just outside the ball. Drawn before the body so only
        // the outer half of the stroke survives; the ball's size and layout never change.
        if (surface.Highlight > 0.01f)
        {
            _stroke.Style = SKPaintStyle.Stroke;
            _stroke.StrokeWidth = SplitSatelliteStyle.HoverGlowWidthDip;
            _stroke.StrokeCap = SKStrokeCap.Butt;
            _stroke.Shader = null;
            _stroke.MaskFilter = SplitBlur(surface.DpiScale, cover: false);
            // Highlight arrives normalised to SatelliteMotionTokens.HoverHighlight (0.35); the glow is
            // specified as 6 % white, so rescale it to 0..1 before applying the alpha.
            var glow = Math.Clamp(surface.Highlight / SatelliteMotionTokens.HoverHighlight, 0, 1);
            _stroke.Color = SKColors.White.WithAlpha((byte)Math.Clamp(
                SplitSatelliteStyle.HoverGlowAlpha * glow * alpha, 0, 255));
            canvas.DrawCircle(ball.MidX, ball.MidY, radius, _stroke);
            _stroke.MaskFilter = null;
        }

        // 2) Body: byte-identical to the primary pill while split previews are visible (Src).
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.BlendMode = SKBlendMode.Src;
        _fill.Color = SplitFillColor(surface.FillOpacity, alpha);
        canvas.DrawCircle(ball.MidX, ball.MidY, radius, _fill);
        _fill.BlendMode = SKBlendMode.SrcOver;

        // 3) Light modelling: bottom shade, top rim highlight, press inner shadow, brightness lift.
        DrawSplitBallLight(canvas, ball, surface);

        // 4) Content: exactly one element per ball.
        if (islet.Kind == ActivityKind.Agent)
        {
            DrawSplitAgentRing(canvas, ball, surface);
            return;
        }

        if (islet.Kind == ActivityKind.Media)
        {
            DrawSplitCover(canvas, islet, ball, surface);
            return;
        }

        var iconSize = radius * 0.9f;
        IconSet.Draw(canvas, islet.Icon,
            new SKRect(ball.MidX - (iconSize / 2), ball.MidY - (iconSize / 2),
                ball.MidX + (iconSize / 2), ball.MidY + (iconSize / 2)),
            Alpha(SKColors.White, alpha), _icon);
    }

    /// <summary>Bottom volume shade + 1 DIP top rim highlight + press inner shadow + press darkening.</summary>
    private void DrawSplitBallLight(SKCanvas canvas, SKRect ball, in SplitSurfaceStyle surface)
    {
        var radius = ball.Width / 2;
        var alpha = surface.Alpha;
        var strokeScale = surface.StrokeScale;

        if (surface.Brightness < -0.001f)
        {
            // Pressed: a neutral darkening (never a hue shift), clipped to the ball.
            canvas.Save();
            using var clip = new SKPath();
            clip.AddCircle(ball.MidX, ball.MidY, radius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = SKColors.Black.WithAlpha(
                (byte)Math.Clamp(-surface.Brightness * alpha * 255f, 0, 255));
            canvas.DrawRect(ball, _fill);
            canvas.Restore();
        }

        if (surface.PressDepth > 0.01f)
        {
            // Contract §4.4: top inner shadow, alpha = 0.18 × depth at full press.
            canvas.Save();
            using var clip = new SKPath();
            clip.AddCircle(ball.MidX, ball.MidY, radius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
            using var pressShade = SKShader.CreateLinearGradient(
                new SKPoint(0, ball.Top),
                new SKPoint(0, ball.Top + (ball.Height * 0.55f)),
                [
                    SKColors.Black.WithAlpha((byte)Math.Clamp(
                        SplitSatelliteStyle.PressShadeAlpha * surface.PressDepth * alpha, 0, 255)),
                    SKColors.Transparent,
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = SKColors.White;
            _fill.Shader = pressShade;
            canvas.DrawRect(ball, _fill);
            _fill.Shader = null;
            canvas.Restore();
        }

        // Bottom volume shade: transparent above the midline, faint black at the very bottom.
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(ball.MidX, ball.MidY, radius);
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
            using var shade = SKShader.CreateLinearGradient(
                new SKPoint(0, ball.Top + (ball.Height * SplitSatelliteStyle.BallShadeStart)),
                new SKPoint(0, ball.Bottom),
                [
                    SKColors.Transparent,
                    SKColors.Black.WithAlpha(SplitSatelliteStyle.Alpha(
                        SplitSatelliteStyle.BallShadeAlpha, alpha)),
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = SKColors.White;
            _fill.Shader = shade;
            canvas.DrawRect(ball, _fill);
            _fill.Shader = null;
        }

        canvas.Restore();

        // Top rim highlight: one DIP of white 8.6 % on the top edge, fading out by 55 % of the height.
        // Hover adds to the same light (never a hue), as the contract asks.
        var rimAlpha = (byte)(SplitSatelliteStyle.BallRimAlpha
            + (byte)Math.Clamp(surface.Highlight * SplitSatelliteStyle.HoverRimBoost, 0, 255 - SplitSatelliteStyle.BallRimAlpha));
        using var rim = SKShader.CreateLinearGradient(
            new SKPoint(0, ball.Top),
            new SKPoint(0, ball.Top + (ball.Height * SplitSatelliteStyle.BallRimFade)),
            [
                SKColors.White.WithAlpha(SplitSatelliteStyle.Alpha(rimAlpha, alpha)),
                SKColors.Transparent,
            ],
            [0f, 1f],
            SKShaderTileMode.Clamp);
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = SplitSatelliteStyle.BallRimWidthDip * strokeScale;
        _stroke.StrokeCap = SKStrokeCap.Butt;
        _stroke.Color = SKColors.White;
        _stroke.Shader = rim;
        canvas.DrawCircle(ball.MidX, ball.MidY, radius - (_stroke.StrokeWidth / 2), _stroke);
        _stroke.Shader = null;
    }

    /// <summary>Rounded-square cover well: contain-fit artwork, recessed top shade and a 1 DIP hairline.</summary>
    private void DrawSplitCover(SKCanvas canvas, in IslandSplitIslet islet, SKRect ball, in SplitSurfaceStyle surface)
    {
        var alpha = surface.Alpha;
        var strokeScale = surface.StrokeScale;
        var frame = SplitSatelliteStyle.InnerFrameRect(ball);
        var frameRadius = SplitSatelliteStyle.InnerFrameRadius(frame);
        if (frame.Width <= 0)
        {
            return;
        }

        var cover = _artwork.Get(islet.ArtworkKey, islet.Artwork);
        if (cover is null)
        {
            // No artwork: the fallback is a disc, not the rounded square well. Without artwork the
            // well would become the silhouette of the whole ball, and a satellite has to read as a
            // circle no matter what it carries.
            var discRadius = (ball.Width / 2f) - (SplitSatelliteStyle.InnerFrameInsetDip * surface.DpiScale);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.MaskFilter = null;
            _fill.Color = Alpha(_theme.BackgroundInset, alpha);
            canvas.DrawCircle(ball.MidX, ball.MidY, discRadius, _fill);
            var noteSize = ball.Width * 0.40f;
            IconSet.Draw(canvas, IconKind.Media,
                new SKRect(ball.MidX - (noteSize / 2), ball.MidY - (noteSize / 2),
                    ball.MidX + (noteSize / 2), ball.MidY + (noteSize / 2)),
                Alpha(SKColors.White, alpha), _icon);
            return;
        }

        // Contact shadow of the cover inside the ball: a soft dark rim under the well.
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.MaskFilter = SplitBlur(surface.DpiScale, cover: true);
        _fill.Color = SKColors.Black.WithAlpha(SplitSatelliteStyle.Alpha(SplitSatelliteStyle.CoverShadowAlpha, alpha));
        canvas.DrawRoundRect(
            frame.Left, frame.Top + SplitSatelliteStyle.CoverShadowOffsetYDip,
            frame.Width, frame.Height, frameRadius, frameRadius, _fill);
        _fill.MaskFilter = null;

        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddRoundRect(new SKRoundRect(frame, frameRadius, frameRadius));
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);

            if (cover is not null)
            {
                // Contain: the whole cover stays visible, any spare space is the well's black fill.
                var imageRect = IslandMetrics.SplitIsletContainRect(frame, cover.Width, cover.Height);
                _icon.Color = Alpha(SKColors.White, alpha);
                canvas.DrawImage(cover, imageRect, _icon);
            }

            // Recessed inner shade: the top of the cover sits in shadow.
            using var inner = SKShader.CreateLinearGradient(
                new SKPoint(0, frame.Top),
                new SKPoint(0, frame.Top + SplitSatelliteStyle.CoverInnerShadeDepthDip),
                [
                    SKColors.Black.WithAlpha(SplitSatelliteStyle.Alpha(
                        SplitSatelliteStyle.CoverInnerShadeAlpha, alpha)),
                    SKColors.Transparent,
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            _fill.Style = SKPaintStyle.Fill;
            _fill.Color = SKColors.White;
            _fill.Shader = inner;
            canvas.DrawRect(frame, _fill);
            _fill.Shader = null;
        }

        canvas.Restore();

        // 1 DIP inner stroke on the well edge, as a vertical bevel: white 7 % on the top edge (the
        // light catches the rim), black 25 % on the bottom edge (the well sits in shadow). A flat
        // white hairline disappeared into bright artwork, which is what made the cover read as a
        // full-bleed circle instead of a square well. Hover lifts the white half only.
        var hairline = (byte)(SplitSatelliteStyle.InnerFrameStrokeAlpha
            + (byte)Math.Clamp(surface.Highlight * SplitSatelliteStyle.HoverRimBoost, 0, 255 - SplitSatelliteStyle.InnerFrameStrokeAlpha));
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = SplitSatelliteStyle.InnerFrameStrokeWidthDip * strokeScale;
        _stroke.StrokeCap = SKStrokeCap.Butt;
        using var bevel = SKShader.CreateLinearGradient(
            new SKPoint(0, frame.Top),
            new SKPoint(0, frame.Bottom),
            [
                SKColors.White.WithAlpha(SplitSatelliteStyle.Alpha(hairline, alpha)),
                SKColors.White.WithAlpha(SplitSatelliteStyle.Alpha((byte)(hairline / 3), alpha)),
                SKColors.Black.WithAlpha(SplitSatelliteStyle.Alpha(
                    SplitSatelliteStyle.InnerFrameBevelShadeAlpha, alpha)),
            ],
            [0f, 0.45f, 1f],
            SKShaderTileMode.Clamp);
        _stroke.Shader = bevel;
        _stroke.Color = SKColors.White;
        var inset = _stroke.StrokeWidth / 2;
        canvas.DrawRoundRect(
            frame.Left + inset, frame.Top + inset, frame.Width - (inset * 2), frame.Height - (inset * 2),
            Math.Max(0f, frameRadius - inset), Math.Max(0f, frameRadius - inset), _stroke);
        _stroke.Shader = null;
    }

    /// <summary>Agent ball content: a 2 DIP round-capped ring, the only chromatic element.</summary>
    private void DrawSplitAgentRing(SKCanvas canvas, SKRect ball, in SplitSurfaceStyle surface)
    {
        var ring = SplitSatelliteStyle.RingRect(ball);
        if (ring.Width <= 1f)
        {
            return;
        }

        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = SplitSatelliteStyle.RingWidthDip * surface.StrokeScale;
        _stroke.StrokeCap = SKStrokeCap.Round;
        _stroke.Shader = null;
        _stroke.Color = Alpha(_theme.Info, surface.Alpha);
        canvas.DrawArc(ring, (float)surface.RingAngleDegrees, SplitSatelliteStyle.RingSweepDegrees, false, _stroke);
        _stroke.StrokeCap = SKStrokeCap.Butt;
    }

    /// <summary>Soft black disc used for the ball/cover contact shadows (cached blur, no per-frame filter).</summary>
    private void DrawSplitBlurredCircle(SKCanvas canvas, float centreX, float centreY, float radius, byte tokenAlpha, float alpha, float dpiScale, bool cover)
    {
        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.MaskFilter = SplitBlur(dpiScale, cover);
        _fill.Color = SKColors.Black.WithAlpha(SplitSatelliteStyle.Alpha(tokenAlpha, alpha));
        canvas.DrawCircle(centreX, centreY, radius, _fill);
        _fill.MaskFilter = null;
    }

    private SKMaskFilter SplitBlur(float dpiScale, bool cover)
    {
        var sigma = (cover ? SplitSatelliteStyle.CoverShadowBlurDip : SplitSatelliteStyle.BallShadowBlurDip) * dpiScale;
        if (cover)
        {
            if (_splitCoverBlur is null || Math.Abs(_splitCoverBlurScale - dpiScale) > 0.01f)
            {
                _splitCoverBlur?.Dispose();
                _splitCoverBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
                _splitCoverBlurScale = dpiScale;
            }

            return _splitCoverBlur;
        }

        if (_splitBallBlur is null || Math.Abs(_splitBallBlurScale - dpiScale) > 0.01f)
        {
            _splitBallBlur?.Dispose();
            _splitBallBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
            _splitBallBlurScale = dpiScale;
        }

        return _splitBallBlur;
    }

    /// <summary>One satellite draw: composed opacity plus the per-frame pose modifiers.</summary>
    private readonly record struct SplitSurfaceStyle(
        float Alpha,
        float PressDepth,
        float Highlight,
        float Brightness,
        float StrokeScale,
        double RingAngleDegrees,
        float FillOpacity,
        float DpiScale);

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
            if (IslandMetrics.SplitIsletContains(finals[i], x, y))
            {
                return new IslandHitTarget(IslandHitKind.SplitIslet, state.Pages.IndexOfPage(state.SplitIslets[i].Page), Index: i);
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

        // Telegram/MioIsland-style press feedback: a small brightness lift plus a top inset shadow.
        // Both are proportional to the press spring so release rebounds instead of snapping.
        var press = (float)Math.Clamp(state.PressDepth, 0, 1);
        if (press > 0.001f)
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = SKColors.White.WithAlpha(
                (byte)Math.Clamp(MotionTokens.PressBrightness * press * opacity * 255f, 0, 255));
            canvas.DrawPath(path, _fill);

            canvas.Save();
            canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
            var shadowHeight = Math.Min(10f, (float)state.ShellHeight * 0.35f);
            using var inset = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, shadowHeight),
                [
                    SKColors.Black.WithAlpha(
                        (byte)Math.Clamp(MotionTokens.PressInnerShadow * press * opacity * 255f, 0, 255)),
                    SKColors.Transparent,
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            _fill.Color = SKColors.White;
            _fill.Shader = inset;
            canvas.DrawRect(new SKRect(0, 0, (float)state.ShellWidth, shadowHeight), _fill);
            _fill.Shader = null;
            canvas.Restore();
        }

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

        // The flex track still moves horizontally, but each page gets a tiny directional depth:
        // the incoming/outgoing page dips in scale and alpha at the halfway point and returns to
        // identity when committed. PagePosition is continuous, so rapid switches resume from the
        // current visual offset instead of snapping back to the old page.
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

            var distance = Math.Min(1.0, Math.Abs(page - pagePosition));
            var meld = (float)Math.Sin(Math.PI * distance);
            var pageScale = 1f - ((float)MotionTokens.PageScaleDrop * meld);
            var pageOpacity = layerOpacity * (1f - ((float)MotionTokens.PageFadeDrop * meld));
            canvas.Save();
            canvas.ClipRect(pageRect);
            if (meld > 0.001f)
            {
                canvas.Translate(pageRect.MidX, pageRect.Top);
                canvas.Scale(pageScale, pageScale);
                canvas.Translate(-pageRect.MidX, -pageRect.Top);
            }

            var slot = state.Pages.PageSlotAt(page);
            if (slot is { Kind: PageSlotKind.Home or PageSlotKind.Media or PageSlotKind.Widgets })
            {
                // Home/media/widgets overflow the 232 DIP body at large font scales: the page scrolls
                // inside that body (the wheel host writes the model offset, clamped by the metric layout).
                var metrics = FontScaleMetrics.From(state.FontScale);
                var scroll = slot.Kind switch
                {
                    PageSlotKind.Home => state.Pages.Home.ScrollOffset,
                    PageSlotKind.Media => state.Pages.Media.ScrollOffset,
                    _ => state.Pages.Widgets.ScrollOffset,
                };
                var itemCount = slot.Kind == PageSlotKind.Widgets ? state.Pages.Widgets.Tasks?.Count ?? 0 : 0;
                var clamped = PageMetricsLayout.ClampScroll(pageRect, slot.Kind, scroll, metrics, itemCount);
                var body = PageMetricsLayout.Body(pageRect);
                canvas.Save();
                canvas.ClipRect(body);
                canvas.Translate(0, -(float)clamped);
                _pages.DrawPage(canvas, state, page, pageRect, pageOpacity);
                canvas.Restore();
            }
            else
            {
                _pages.DrawPage(canvas, state, page, pageRect, pageOpacity);
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
        return Math.Clamp(page, 0, state.Pages.PageCount - 1);
    }

    private static float PageScrollOffset(in IslandRenderState state, PageSlotKind kind, in FontScaleMetrics metrics, SKRect pageRect)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return kind switch
        {
            PageSlotKind.Home => PageMetricsLayout.ClampScroll(pageRect, PageSlotKind.Home, pages.Home.ScrollOffset, metrics),
            PageSlotKind.Media => PageMetricsLayout.ClampScroll(pageRect, PageSlotKind.Media, pages.Media.ScrollOffset, metrics),
            PageSlotKind.Notifications => (float)pages.Notifications.ScrollOffset,
            PageSlotKind.Clipboard => (float)pages.Clipboard.ScrollOffset,
            // The widgets card grows with the task rows, so the clamp needs the row count.
            PageSlotKind.Widgets => PageMetricsLayout.ClampScroll(pageRect, PageSlotKind.Widgets, pages.Widgets.ScrollOffset, metrics,
                pages.Widgets.Tasks?.Count ?? 0),
            _ => 0f,
        };
    }

    private static int PageItemCount(in IslandRenderState state, PageSlotKind kind)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return kind switch
        {
            PageSlotKind.Home => 4,
            PageSlotKind.Media => 1,
            PageSlotKind.Notifications => pages.Notifications.Items?.Count ?? 0,
            PageSlotKind.Clipboard => pages.Clipboard.Items?.Count ?? 0,
            PageSlotKind.Widgets => pages.Widgets.Tasks?.Count ?? 0,
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
        _splitBallBlur?.Dispose();
        _splitBallBlur = null;
        _splitCoverBlur?.Dispose();
        _splitCoverBlur = null;
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

