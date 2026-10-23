using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Paints the Tier 2 "Liquid Glass" finish on a shell silhouette.
/// <para>
/// A layered window is not composited by DWM's GPU path, so no real <c>backdrop-filter</c> blur is
/// available behind the island. The material is therefore drawn as the optical *result* of glass:
/// a lensing rim band that bends towards the light, a specular cap on the lit edge, a caustic bounce on
/// the far edge, a low-alpha chromatic fringe (cool top / warm bottom), a static sheen and a
/// pointer-following illumination.
/// </para>
/// <para>
/// The static layers are baked once per (shell size × DPI × corner set) into a cached premultiplied
/// image — the same budget rule the shadow cache follows — so a settled frame costs one clipped blit
/// instead of six full-surface gradient passes. Only the interactive illumination is drawn live.
/// </para>
/// </summary>
internal sealed class LiquidGlassPainter : IDisposable
{
    private const int MaxOverlays = 4;

    /// <summary>Specular cap band: 14 DIP on the panel, proportionally less on a 36 DIP pill.</summary>
    private static float SpecularBandFor(float height) => Math.Clamp(height * 0.16f, 4f, 14f);

    /// <summary>Caustic band height at the far edge.</summary>
    private static float CausticBandFor(float height) => Math.Clamp(height * 0.14f, 3.5f, 12f);

    /// <summary>Chromatic fringe band height.</summary>
    private static float FringeBandFor(float height) => Math.Clamp(height * 0.09f, 2f, 5f);

    /// <summary>Uniform edge key line alpha (9 %) — the wall of the glass, independent of direction.</summary>
    private const byte EdgeKeyAlpha = 0x16;

    private readonly SKPaint _paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _stroke = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeJoin = SKStrokeJoin.Round,
        StrokeCap = SKStrokeCap.Round,
    };

    private readonly Dictionary<(int Width, int Height, int Dpi, int Shape), Overlay> _overlays = new(MaxOverlays);
    private readonly SKPath _scratch = new();
    private SKBitmap? _scratchBitmap;
    private int _scratchWidth;
    private int _scratchHeight;
    private SKShader? _pointerGlow;
    private ShaderSet? _edgeSet;
    private (int Width, int Height) _edgeKey;
    private bool _disposed;

    /// <summary>Number of baked overlays currently retained (diagnostics).</summary>
    public int OverlayCount => _overlays.Count;

    /// <summary>Retained bytes of the baked overlays (diagnostics).</summary>
    public long OverlayBytes
    {
        get
        {
            long bytes = 0;
            foreach (var overlay in _overlays.Values)
            {
                bytes += (long)overlay.Image.Width * overlay.Image.Height * 4;
            }

            return bytes;
        }
    }

    /// <summary>Paints the full liquid-glass body treatment inside <paramref name="path"/>.</summary>
    /// <param name="canvas">Target canvas already translated to the shell origin.</param>
    /// <param name="path">Shell silhouette in the current (shell-local) space.</param>
    /// <param name="bounds">Shell bounds in the current space.</param>
    /// <param name="opacity">Layer opacity 0..1.</param>
    /// <param name="pointerX">Pointer X in the current space (NaN when the pointer is away).</param>
    /// <param name="pointerY">Pointer Y in the current space (NaN when the pointer is away).</param>
    /// <param name="hasPointer">Whether the pointer is over the island.</param>
    /// <param name="shapeKey">Hash of the corner radii (keeps the bake cache honest).</param>
    /// <param name="morphing">
    /// True while the shell size is animating: the overlay is then baked at half resolution in 4 DIP
    /// buckets (a rebake per frame at full resolution would cost ~5 ms during expand/collapse).
    /// </param>
    /// <param name="blend">Blend mode used while the material cross-fade paints this pass.</param>
    public void Draw(
        SKCanvas canvas,
        SKPath path,
        SKRect bounds,
        float opacity,
        float pointerX,
        float pointerY,
        bool hasPointer,
        int shapeKey = 0,
        bool morphing = false,
        SKBlendMode blend = SKBlendMode.SrcOver)
    {
        if (bounds.Width < 4 || bounds.Height < 4 || opacity <= 0.002f)
        {
            return;
        }

        var dpi = canvas.TotalMatrix.ScaleX;
        if (!float.IsFinite(dpi) || dpi <= 0.2f)
        {
            dpi = 1f;
        }

        // Quantise the scale: the press animation scales the whole canvas by ~±3 %, and an exact
        // scale would force a fresh bake on every press frame. 0.05 steps keep 125/150/175/200 % exact
        // and absorb the press delta.
        dpi = MathF.Round(dpi * 20f) / 20f;

        var overlay = GetOverlay(path, bounds, dpi, shapeKey, morphing);
        if (overlay is null)
        {
            return;
        }

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        _paint.Shader = null;
        _paint.BlendMode = blend;
        _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255f, 0, 255));
        var sampling = overlay.Image.Width == (int)Math.Round(bounds.Width * dpi)
            ? new SKSamplingOptions(SKFilterMode.Nearest)
            : new SKSamplingOptions(SKFilterMode.Linear);
        canvas.DrawImage(overlay.Image, overlay.Source, bounds, sampling, _paint);

        if (hasPointer && float.IsFinite(pointerX) && float.IsFinite(pointerY)
            && bounds.Contains(pointerX, pointerY))
        {
            DrawPointerGlow(canvas, bounds, pointerX, pointerY, opacity);
        }

        canvas.Restore();

        _paint.BlendMode = SKBlendMode.SrcOver;

        // Transient (morph) overlays are per-frame; release the wrapper now that the blit has landed.
        if (overlay.Transient)
        {
            overlay.Dispose();
        }
    }

    /// <summary>
    /// Edge-only finish used when a blurred desktop capture carries the body: a 1 DIP key line, the
    /// specular top edge, the caustic bounce and a cool fringe — no veil, no rim bands, no sheen.
    /// </summary>
    /// <param name="canvas">Target canvas already translated to the shell origin.</param>
    /// <param name="path">Shell silhouette in the current space.</param>
    /// <param name="bounds">Shell bounds in the current space.</param>
    /// <param name="opacity">Layer opacity 0..1.</param>
    public void DrawEdgeOnly(SKCanvas canvas, SKPath path, SKRect bounds, float opacity)
    {
        if (bounds.Width < 4 || bounds.Height < 4 || opacity <= 0.002f)
        {
            return;
        }

        var set = EdgeSetRef(bounds);
        var layer = (byte)Math.Clamp(opacity * 255f, 0, 255);

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

        _stroke.StrokeWidth = 1f;
        _stroke.Shader = null;
        _stroke.Color = SKColors.White.WithAlpha((byte)(0.09f * layer));
        canvas.DrawPath(path, _stroke);

        _stroke.StrokeWidth = 1.6f;
        _stroke.Shader = set.Specular;
        _stroke.Color = SKColors.White.WithAlpha(layer);
        canvas.DrawPath(path, _stroke);

        _stroke.Shader = set.Caustic;
        canvas.DrawPath(path, _stroke);

        _stroke.Shader = set.FringeCool;
        canvas.DrawPath(path, _stroke);

        _stroke.Shader = null;
        canvas.Restore();
    }

    /// <summary>Paints the liquid-glass rim on a small round surface (split islet / secondary pill).</summary>
    /// <param name="canvas">Target canvas in absolute host-local DIP.</param>
    /// <param name="center">Circle centre.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="opacity">Layer opacity 0..1.</param>
    public void DrawCircle(SKCanvas canvas, SKPoint center, float radius, float opacity)
    {
        if (radius < 2 || opacity <= 0.002f)
        {
            return;
        }

        _scratch.Reset();
        _scratch.AddCircle(center.X, center.Y, radius);
        var bounds = new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
        Draw(canvas, _scratch, bounds, opacity, float.NaN, float.NaN, hasPointer: false, shapeKey: -1);
    }

    /// <summary>Paints the liquid-glass rim on a ghost pill (rounded rect).</summary>
    /// <param name="canvas">Target canvas in absolute host-local DIP.</param>
    /// <param name="rect">Pill bounds.</param>
    /// <param name="cornerRadius">Corner radius (height/2 for a pill).</param>
    /// <param name="opacity">Layer opacity 0..1.</param>
    public void DrawRoundRect(SKCanvas canvas, SKRect rect, float cornerRadius, float opacity)
    {
        if (rect.Width < 4 || rect.Height < 4 || opacity <= 0.002f)
        {
            return;
        }

        _scratch.Reset();
        using var round = new SKRoundRect(rect, cornerRadius, cornerRadius);
        _scratch.AddRoundRect(round);
        Draw(canvas, _scratch, rect, opacity, float.NaN, float.NaN, hasPointer: false,
            shapeKey: -(2 + (int)Math.Round(cornerRadius * 10)));
    }

    private void DrawPointerGlow(SKCanvas canvas, SKRect bounds, float x, float y, float opacity)
    {
        _pointerGlow ??= SKShader.CreateRadialGradient(
            SKPoint.Empty,
            (float)LiquidGlassTokens.PointerGlowRadiusDip,
            [SKColors.White.WithAlpha(LiquidGlassTokens.PointerGlowAlpha), SKColors.White.WithAlpha(0)],
            [0f, 1f],
            SKShaderTileMode.Clamp);

        using var local = _pointerGlow.WithLocalMatrix(SKMatrix.CreateTranslation(x, y));
        _paint.Shader = local;
        _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255f, 0, 255));
        canvas.DrawRect(bounds, _paint);
        _paint.Shader = null;
    }

    /// <summary>Shared shader set for the edge-only finish (rebuilt only when the shell size changes).</summary>
    private ShaderSet EdgeSetRef(SKRect bounds)
    {
        var key = ((int)Math.Round(bounds.Width * 4), (int)Math.Round(bounds.Height * 4));
        if (_edgeSet is not null && _edgeKey == key)
        {
            return _edgeSet;
        }

        _edgeSet?.Dispose();
        _edgeKey = key;
        _edgeSet = new ShaderSet(bounds.Width, bounds.Height);
        return _edgeSet;
    }

    private Overlay? GetOverlay(SKPath path, SKRect bounds, float dpi, int shapeKey, bool morphing)
    {
        // Settled frames bake once at device resolution; morph frames bake at quarter resolution in
        // 8 DIP buckets so the expand/collapse spring never pays a full-surface rebake per frame.
        var bucket = morphing ? 0.125f : 4f;
        var bakeScale = morphing ? dpi * 0.25f : dpi;
        var key = (
            (int)Math.Round(bounds.Width * bucket),
            (int)Math.Round(bounds.Height * bucket),
            (int)Math.Round(bakeScale * 100),
            shapeKey);
        if (_overlays.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * bakeScale));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * bakeScale));
        var source = new SKRect(0, 0, width, height);

        if (morphing)
        {
            // Transient overlay: reuse one scratch surface, never retain it.
            EnsureScratch(width, height);
            PaintInto(_scratchBitmap!, bakeScale, path, bounds);
            return new Overlay(SKImage.FromBitmap(_scratchBitmap!), source, transient: true);
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        PaintInto(bitmap, bakeScale, path, bounds);
        var image = SKImage.FromBitmap(bitmap);
        if (_overlays.Count >= MaxOverlays)
        {
            foreach (var entry in _overlays.Values)
            {
                entry.Dispose();
            }

            _overlays.Clear();
        }

        var overlay = new Overlay(image, source, bitmap);
        _overlays[key] = overlay;
        return overlay;
    }

    private void EnsureScratch(int width, int height)
    {
        if (_scratchBitmap is not null && _scratchWidth >= width && _scratchHeight >= height)
        {
            return;
        }

        _scratchBitmap?.Dispose();
        _scratchWidth = Math.Max(width, _scratchWidth);
        _scratchHeight = Math.Max(height, _scratchHeight);
        var info = new SKImageInfo(_scratchWidth, _scratchHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _scratchBitmap = new SKBitmap(info);
    }

    private void PaintInto(SKBitmap bitmap, float scale, SKPath path, SKRect bounds)
    {
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        PaintOverlay(canvas, path, bounds);
        canvas.Flush();
    }

    private void PaintOverlay(SKCanvas canvas, SKPath path, SKRect bounds)
    {
        var band = LiquidGlassTokens.RimBandFor(bounds.Height);
        var specularBand = SpecularBandFor(bounds.Height);
        var causticBand = CausticBandFor(bounds.Height);
        var fringeBand = FringeBandFor(bounds.Height);
        using var shaders = new ShaderSet(bounds.Width, bounds.Height);

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

        // 1. Body veil: light pools at the top, the glass thickens towards the bottom.
        _paint.Shader = shaders.Body;
        _paint.Color = SKColors.White;
        canvas.DrawRect(bounds, _paint);

        // 2. Lensing rim: a thick stroke clipped to the inside edge reads as the wall of the glass.
        _stroke.StrokeWidth = band * 2f;
        _stroke.Shader = shaders.RimLight;
        _stroke.Color = SKColors.White;
        canvas.DrawPath(path, _stroke);

        _stroke.Shader = shaders.RimShade;
        canvas.DrawPath(path, _stroke);

        // 3. Specular cap and caustic bounce: horizontal bands clipped by the silhouette, so light is
        //    even across the lit edge instead of following a stroke's curvature.
        _paint.Shader = shaders.Specular;
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + specularBand), _paint);

        _paint.Shader = shaders.Caustic;
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Bottom - causticBand, bounds.Right, bounds.Bottom), _paint);

        // 4. Chromatic dispersion: cool fringe on the lit edge, warm fringe where light exits.
        _paint.Shader = shaders.FringeCool;
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + fringeBand), _paint);

        _paint.Shader = shaders.FringeWarm;
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Bottom - fringeBand, bounds.Right, bounds.Bottom), _paint);

        // 5. Uniform edge key: without it the left/right walls read as flat fill at mid height.
        _stroke.StrokeWidth = 1f;
        _stroke.Shader = null;
        _stroke.Color = SKColors.White.WithAlpha(EdgeKeyAlpha);
        canvas.DrawPath(path, _stroke);

        // 6. Static sheen band, angled across the surface.
        _paint.Shader = shaders.Sheen;
        _paint.Color = SKColors.White;
        canvas.DrawRect(bounds, _paint);

        _paint.Shader = null;
        _stroke.Shader = null;
        canvas.Restore();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _overlays.Values)
        {
            entry.Dispose();
        }

        _overlays.Clear();
        _scratchBitmap?.Dispose();
        _scratchBitmap = null;
        _pointerGlow?.Dispose();
        _pointerGlow = null;
        _edgeSet?.Dispose();
        _edgeSet = null;
        _scratch.Dispose();
        _paint.Dispose();
        _stroke.Dispose();
    }

    /// <summary>One baked overlay image plus the pixels that own it (transient overlays own nothing).</summary>
    private sealed class Overlay : IDisposable
    {
        private readonly bool _transient;

        public Overlay(SKImage image, SKRect source, SKBitmap? owner = null, bool transient = false)
        {
            Image = image;
            Source = source;
            Owner = owner;
            _transient = transient;
        }

        public SKImage Image { get; }

        public SKRect Source { get; }

        public SKBitmap? Owner { get; }

        public bool Transient => _transient;

        public void Dispose()
        {
            if (!_transient)
            {
                Image.Dispose();
            }

            Owner?.Dispose();
        }
    }

    /// <summary>Gradient set for one shell size; disposed right after the bake.</summary>
    private sealed class ShaderSet : IDisposable
    {
        public ShaderSet(float w, float h)
        {
            // Body veil: white lift at the top, neutral shade at the bottom.
            Body = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, h),
                [
                    SKColors.White.WithAlpha(LiquidGlassTokens.VeilTopAlpha),
                    SKColors.White.WithAlpha(0x08),
                    SKColors.Transparent,
                    SKColors.Black.WithAlpha(LiquidGlassTokens.BodyShadeAlpha),
                ],
                [0f, 0.38f, 0.62f, 1f],
                SKShaderTileMode.Clamp);

            // Rim light: brightest at the top, gone by the lower half.
            RimLight = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, h),
                [
                    SKColors.White.WithAlpha(LiquidGlassTokens.RimLightTopAlpha),
                    SKColors.White.WithAlpha(0x12),
                    SKColors.Transparent,
                ],
                [0f, 0.26f, 0.55f],
                SKShaderTileMode.Clamp);

            // Rim shade: the far wall of the glass, dark near the bottom edge.
            RimShade = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, h),
                [
                    SKColors.Transparent,
                    SKColors.Transparent,
                    SKColors.Black.WithAlpha(0x22),
                    SKColors.Black.WithAlpha(LiquidGlassTokens.RimShadeBottomAlpha),
                ],
                [0f, 0.55f, 0.84f, 1f],
                SKShaderTileMode.Clamp);

            // Specular cap: a hot 1 DIP line plus a soft bloom under it.
            Specular = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, SpecularBandFor(h)),
                [
                    SKColors.White.WithAlpha(LiquidGlassTokens.SpecularTopAlpha),
                    SKColors.White.WithAlpha(0x3A),
                    SKColors.White.WithAlpha(0x10),
                    SKColors.Transparent,
                ],
                [0f, 0.08f, 0.35f, 1f],
                SKShaderTileMode.Clamp);

            // Caustic: light concentrating on the inner face of the far edge, peaking just above it.
            Caustic = SKShader.CreateLinearGradient(
                new SKPoint(0, h - CausticBandFor(h)),
                new SKPoint(0, h),
                [
                    SKColors.Transparent,
                    SKColors.White.WithAlpha(0x0C),
                    SKColors.White.WithAlpha(LiquidGlassTokens.CausticBottomAlpha),
                    SKColors.White.WithAlpha(0x1C),
                ],
                [0f, 0.35f, 0.82f, 1f],
                SKShaderTileMode.Clamp);

            // Dispersion: cool fringe on the lit edge, warm fringe where light exits.
            FringeCool = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, FringeBandFor(h)),
                [
                    new SKColor(0x8F, 0xE8, 0xFF, LiquidGlassTokens.DispersionCoolAlpha),
                    SKColors.Transparent,
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);

            FringeWarm = SKShader.CreateLinearGradient(
                new SKPoint(0, h - FringeBandFor(h)),
                new SKPoint(0, h),
                [
                    SKColors.Transparent,
                    new SKColor(0xFF, 0xA6, 0xD8, LiquidGlassTokens.DispersionWarmAlpha),
                ],
                [0f, 1f],
                SKShaderTileMode.Clamp);

            // Sheen: one soft diagonal band, fixed (no per-frame animation → no idle frames).
            Sheen = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(w * 0.72f, h),
                [
                    SKColors.Transparent,
                    SKColors.White.WithAlpha(LiquidGlassTokens.SheenAlpha),
                    SKColors.Transparent,
                ],
                [0.08f, 0.44f, 0.78f],
                SKShaderTileMode.Clamp);
        }

        public SKShader Body { get; }

        public SKShader RimLight { get; }

        public SKShader RimShade { get; }

        public SKShader Specular { get; }

        public SKShader Caustic { get; }

        public SKShader FringeCool { get; }

        public SKShader FringeWarm { get; }

        public SKShader Sheen { get; }

        public void Dispose()
        {
            Body.Dispose();
            RimLight.Dispose();
            RimShade.Dispose();
            Specular.Dispose();
            Caustic.Dispose();
            FringeCool.Dispose();
            FringeWarm.Dispose();
            Sheen.Dispose();
        }
    }
}
