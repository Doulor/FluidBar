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

    /// <summary>Bounded shadow image cache capacity (entries; default 8).</summary>
    public int ShadowCacheCapacity { get; init; } = 8;

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
                var control = PageLayout.HitTest(
                    page,
                    pageRect,
                    x,
                    y,
                    out var index,
                    PageScrollOffset(state, page),
                    PageItemCount(state, page));
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
            if (state.Compact is { Kind: ActivityKind.Media } && IsMediaPlay(state, shell, x, y))
            {
                return new IslandHitTarget(IslandHitKind.Compact, state.PageIndex, IslandPageControl.PlayPause);
            }

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

        if (_options.EnableShadow)
        {
            DrawShadow(canvas, state, shell, dpiScale, opacity);
        }

        var path = _paths.Get(state.ShellWidth, state.ShellHeight, state.ShellRadii);

        canvas.Save();
        canvas.Translate(shell.Left, shell.Top);

        DrawShellBody(canvas, state, path, opacity);

        // Prototype2 layers the compact surface and the expanded panel as siblings, each with its own
        // CSS transition; both are painted while a transition is in flight. There is no ghost stack:
        // prototype2's stacking strategy has no visual counterpart, so nothing behind the shell is drawn.
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

    private void DrawShellBody(SKCanvas canvas, in IslandRenderState state, SKPath path, float opacity)
    {
        var materialGlass = _theme.Material == IslandMaterial.SimulatedGlass;
        var fillOpacity = materialGlass
            ? 0.72f
            : (float)Math.Clamp(_theme.FillOpacity, LayoutTokens.MinFillOpacity, 1.0);

        _fill.Style = SKPaintStyle.Fill;
        _fill.Shader = null;
        _fill.Color = Alpha(_theme.Background, fillOpacity * opacity);
        canvas.DrawPath(path, _fill);

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
            secondary);
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
            ? Math.Clamp(state.Pager.PagePosition, 0, FluidBar.Core.Interaction.PagerConstants.PageCount - 1)
            : 0;
        var pitch = FluidBar.Core.Interaction.PagerConstants.PagePitchDip;
        var offset = (float)(pagePosition * pitch);

        // Prototype2 moves a single flex track with translate3d(-offset * 400px); the pages themselves
        // are never scaled, faded or parallaxed. Keep the transition a pure horizontal translation.
        canvas.Save();
        canvas.ClipRect(content);
        for (var page = 0; page < FluidBar.Core.Interaction.PagerConstants.PageCount; page++)
        {
            var left = content.Left + (page * (float)pitch) - offset;
            var pageRect = new SKRect(left, content.Top, left + (float)pitch, content.Bottom);
            if (!pageRect.Intersects(content))
            {
                continue;
            }

            canvas.Save();
            canvas.ClipRect(pageRect);
            _pages.DrawPage(canvas, state, page, pageRect, layerOpacity);
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

    private static bool IsMediaPlay(in IslandRenderState state, SKRect shell, float x, float y)
        => x >= shell.Right - 34 && x < shell.Right - 8 && y >= shell.Top + 7 && y < shell.Top + 33;

    private static float PageScrollOffset(in IslandRenderState state, int page)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return page switch
        {
            2 => (float)pages.Notifications.ScrollOffset,
            3 => (float)pages.Clipboard.ScrollOffset,
            4 => (float)pages.Agent.ScrollOffset,
            _ => 0f,
        };
    }

    private static int PageItemCount(in IslandRenderState state, int page)
    {
        var pages = state.Pages ?? ExpandedPageContent.Empty;
        return page switch
        {
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
