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
    /// <summary>Enter/exit slide of a page, in DIP, along the travel direction.</summary>
    public const float PageEnterSlideDip = 6f;

    /// <summary>Enter/exit shrink of a page at the far edge of the transition (1.8 %).</summary>
    public const float PageEnterScaleDelta = 0.018f;

    /// <summary>
    /// Page enter/exit pose while the pager moves. The sign of <paramref name="pagePosition"/> - page
    /// encodes the direction: an incoming page sits 6 DIP further along the travel direction and 1.8 %
    /// smaller, so it slides/scales into place; the outgoing page does the opposite. The pose is a pure
    /// function of the continuous page position, so a rapid second switch continues from the current
    /// visual pose instead of snapping back.
    /// </summary>
    public static (float SlideDip, float Scale) PageEnterPose(double pagePosition, int page, bool reducedMotion = false)
    {
        if (reducedMotion || !double.IsFinite(pagePosition))
        {
            return (0f, 1f);
        }

        var relative = (float)Math.Clamp(pagePosition - page, -1.0, 1.0);
        var distance = Math.Abs(relative);
        return (-PageEnterSlideDip * relative, 1f - (PageEnterScaleDelta * distance));
    }

    /// <summary>Hover progress of the satellite glass under the pointer (0..1; diagnostics/tests).</summary>
    public float SatelliteHoverProgress => _strip.HoverProgress;

    /// <summary>Scale the hovered satellite glass is painted at (1..1.06; diagnostics/tests).</summary>
    public float SatelliteHoverScale => _strip.HoverScale;

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
    /// Paints the split previews as pure circles on the primary pill's baseline. Every circle has
    /// exactly one element: an agent gets only its blue spinning 180° arc, media gets only its
    /// circular album cover (or a neutral music-note fallback), and everything else gets one icon.
    /// No labels, badges, status dots, action buttons or decorative rings.
    /// </summary>
    /// <summary>
    /// Static end states of an agent satellite: a neutral check for Completed, a neutral cross for
    /// Failed and a neutral dot for Cancelled. Never blue, never spinning.
    /// </summary>
    private void DrawAgentSettledState(SKCanvas canvas, SKRect rect, float radius, CompactStatus status, float alpha)
    {
        var ink = Alpha(_theme.ForegroundSecondary, alpha);
        var thickness = Math.Max(IslandMetrics.SplitRingEndMarkStrokeDip, radius * 0.11f);
        _stroke.Style = SKPaintStyle.Stroke;
        _stroke.StrokeWidth = thickness;
        _stroke.StrokeCap = SKStrokeCap.Round;
        _stroke.Shader = null;
        _stroke.Color = ink;
        var c = radius * 0.42f;
        if (status is CompactStatus.Success or CompactStatus.Warning)
        {
            using var path = new SKPath();
            path.MoveTo(rect.MidX - c, rect.MidY);
            path.LineTo(rect.MidX - (c * 0.15f), rect.MidY + (c * 0.75f));
            path.LineTo(rect.MidX + c, rect.MidY - (c * 0.8f));
            canvas.DrawPath(path, _stroke);
        }
        else if (status == CompactStatus.Danger)
        {
            canvas.DrawLine(rect.MidX - c, rect.MidY - c, rect.MidX + c, rect.MidY + c, _stroke);
            canvas.DrawLine(rect.MidX + c, rect.MidY - c, rect.MidX - c, rect.MidY + c, _stroke);
        }
        else
        {
            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.Color = ink;
            canvas.DrawCircle(rect.MidX, rect.MidY, radius * 0.16f, _fill);
        }

        _stroke.StrokeCap = SKStrokeCap.Butt;
    }

    private void DrawSplitIslets(SKCanvas canvas, in IslandRenderState state, float opacity, float dpiScale)
    {
        if (state.SplitIslets.Length == 0 || state.SplitProgress <= 0.01)
        {
            return;
        }

        var finals = IslandMetrics.SplitIsletRects(state);
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
            var radius = rect.Width / 2;

            _fill.Style = SKPaintStyle.Fill;
            _fill.Shader = null;
            _fill.BlendMode = SKBlendMode.Src;
            _fill.Color = SplitFillColor(fillOpacity, alpha);
            canvas.DrawCircle(rect.MidX, rect.MidY, radius, _fill);
            _fill.BlendMode = SKBlendMode.SrcOver;

            if (islet.Kind == ActivityKind.Agent)
            {
                if (islet.Status == CompactStatus.Info)
                {
                    // Running/Waiting: 2 DIP, 180° arc at 120 deg/s, centred at 0.575 x radius.
                    var phase = (float)((state.AgentRingFrozenDegrees
                        ?? IslandMetrics.SplitRingDegrees(state.AgentAnimationSeconds)) / 360.0);
                    if (phase < 0) { phase += 1f; }
                    var ringInset = Math.Max(1f, radius * (1f - IslandMetrics.SplitRingRadiusFactor));
                    var ringRect = new SKRect(
                        rect.Left + ringInset,
                        rect.Top + ringInset,
                        rect.Right - ringInset,
                        rect.Bottom - ringInset);
                    _stroke.Style = SKPaintStyle.Stroke;
                    _stroke.StrokeWidth = IslandMetrics.SplitRingStrokeDip;
                    _stroke.StrokeCap = SKStrokeCap.Round;
                    _stroke.Shader = null;
                    _stroke.Color = Alpha(_theme.Info, alpha);
                    canvas.DrawArc(ringRect, phase * 360f, 180f, false, _stroke);
                    _stroke.StrokeCap = SKStrokeCap.Butt;
                }
                else
                {
                    DrawAgentSettledState(canvas, rect, radius, islet.Status, alpha);
                }

                continue;
            }

            if (islet.Kind == ActivityKind.Media)
            {
                var cover = _artwork.Get(islet.ArtworkKey, islet.Artwork);
                if (cover is not null)
                {
                    var thumbnail = IslandMetrics.SplitIsletThumbnailRect(rect);
                    var thumbRadius = IslandMetrics.SplitIsletThumbnailRadius(thumbnail);
                    canvas.Save();
                    using var ballClip = new SKPath();
                    ballClip.AddCircle(rect.MidX, rect.MidY, radius);
                    canvas.ClipPath(ballClip, SKClipOperation.Intersect, antialias: true);
                    using var squircle = new SKPath();
                    squircle.AddRoundRect(new SKRoundRect(thumbnail, thumbRadius, thumbRadius));
                    canvas.ClipPath(squircle, SKClipOperation.Intersect, antialias: true);
                    _icon.Color = Alpha(SKColors.White, alpha);
                    // Contain: the whole cover is visible; spare space stays the black ball fill.
                    var imageRect = IslandMetrics.SplitIsletContainRect(
                        thumbnail, cover.Width, cover.Height);
                    canvas.DrawImage(cover, imageRect, _icon);
                    canvas.Restore();
                    continue;
                }
            }

            // One glyph only, painted white over the neutral circle (media without cover: note).
            var iconKind = islet.Kind == ActivityKind.Media ? IconKind.Media : islet.Icon;
            var iconSize = radius * 0.9f;
            IconSet.Draw(canvas, iconKind,
                new SKRect(rect.MidX - (iconSize / 2), rect.MidY - (iconSize / 2),
                    rect.MidX + (iconSize / 2), rect.MidY + (iconSize / 2)),
                Alpha(SKColors.White, alpha), _icon);
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

            // Enter/exit pose: 6 DIP slide along the travel direction plus a 1.8 % shrink, derived from the
            // continuous page position (direction = sign of the pagePosition delta, continuity for free).
            var (pageSlide, pageScale) = PageEnterPose(pagePosition, page, state.ReducedMotion);
            if (pageSlide != 0 || pageScale != 1)
            {
                canvas.Translate(pageRect.MidX, pageRect.MidY);
                canvas.Scale(pageScale, pageScale);
                canvas.Translate(-pageRect.MidX, -pageRect.MidY);
                canvas.Translate(pageSlide, 0);
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
            PageSlotKind.Agent => (float)pages.Agent.ScrollOffset,
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
            PageSlotKind.Agent => pages.Agent.Items?.Count ?? 0,
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

