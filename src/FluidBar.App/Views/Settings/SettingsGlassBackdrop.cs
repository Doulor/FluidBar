using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.App.Views.Settings;

/// <summary>One finished glass backdrop: BGRA premultiplied pixels plus the luminance they were baked from.</summary>
/// <param name="Pixels">Tinted backdrop pixels, top-down BGRA premultiplied.</param>
/// <param name="Width">Pixel width (the 1/6 bake).</param>
/// <param name="Height">Pixel height (the 1/6 bake).</param>
/// <param name="RowBytes">Bytes per row.</param>
internal readonly record struct GlassFrame(byte[] Pixels, int Width, int Height, int RowBytes);

/// <summary>
/// Blurred-desktop material for the settings panel, built for zero-latency tracking.
/// <para>
/// Capturing per window position can never be zero latency: the screen read always happens *after* the window
/// has moved, so the blur trails the pointer by at least a frame. This pipeline therefore captures a region
/// <em>larger</em> than the window once, bakes it (1/6 downsample → gaussian blur → saturation lift) and keeps
/// the raw pixels. Moving the window then only re-samples an offset inside that baked region and re-applies the
/// tint gradient in memory — a few hundred microseconds, no screen read, no allocation, same frame as the move.
/// A new region is captured only when the window travels out of the cached one, and once more (composed desktop)
/// after the drag settles so content that GDI cannot see, like other windows underneath, is corrected.
/// </para>
/// <para>
/// Deliberately not <c>IslandBackdropProvider</c>: that provider retires frames by counting calls to its own
/// <c>Get</c> method, so a second consumer calling into it would free images the island is still drawing — the
/// use-after-free that crashed the process on 2026-10-24.
/// </para>
/// </summary>
internal sealed class SettingsGlassBackdrop : IDisposable
{
    /// <summary>Downsample factor of the bake (identical to the island's so both materials match).</summary>
    private const int Downsample = 6;

    /// <summary>
    /// Gaussian sigma at the bake scale. 4.2 at 1/6 downsample ≈ a 25 px physical blur (≈ 17 DIP at 150 % DPI),
    /// noticeably frostier than the island's 2.5. This is the single knob for "more/less blur".
    /// </summary>
    private const float BakeSigma = 4.2f;

    /// <summary>Backdrop saturation: glass concentrates colour instead of greying it out.</summary>
    private const float Saturation = 1.60f;

    /// <summary>Minimum gap between two region captures (ms).</summary>
    private const int RegionIntervalMs = 60;

    private readonly object _gate = new();
    private Task? _pending;
    private long _lastRegionTicks;
    private bool _disposed;

    private byte[]? _regionPixels;
    private int _regionWidth;
    private int _regionHeight;
    private int _regionRowBytes;
    private double _regionLuminance = 0.5;
    private byte[]? _composeBuffer;
    private int _composeRowBytes;

    /// <summary>
    /// Test seam: replaces the real screen grab so the material can be exercised without a capturable desktop
    /// (headless CI). Null in product.
    /// </summary>
    internal Func<(int X, int Y, int Width, int Height), BackdropFrame?>? CaptureOverride { get; set; }

    /// <summary>Screen rectangle the cached region was captured from (physical pixels).</summary>
    public (int X, int Y, int Width, int Height) RegionRect { get; private set; }

    /// <summary>Raised on a pool thread after a fresh region lands; the caller composes and uploads.</summary>
    public event Action? RegionReady;

    /// <summary>Successful region captures (diagnostics).</summary>
    public long CaptureCount { get; private set; }

    /// <summary>Failed capture or bake attempts (diagnostics).</summary>
    public long CaptureFailures { get; private set; }

    /// <summary>Duration of the last region capture (ms).</summary>
    public double LastCaptureMs { get; private set; }

    /// <summary>Duration of the last blur bake (ms).</summary>
    public double LastBakeMs { get; private set; }

    /// <summary>True when a usable region is cached.</summary>
    public bool HasRegion => _regionPixels is not null;

    /// <summary>
    /// Captures and bakes a region around <paramref name="windowRect"/>. Asynchronous: the result arrives through
    /// <see cref="RegionReady"/>. Callers keep drawing from the previous region until then.
    /// </summary>
    /// <param name="windowRect">Window rectangle in physical pixels.</param>
    /// <param name="allowComposedCapture">True for the composed desktop (WGC), false for the cheap GDI read.</param>
    /// <param name="force">Ignore the throttle and any in-flight capture (window shown, drag settled).</param>
    /// <returns>True when a capture was queued.</returns>
    public bool RequestRegion((int X, int Y, int Width, int Height) windowRect, bool allowComposedCapture,
        bool force = false)
    {
        lock (_gate)
        {
            if (_disposed || windowRect.Width <= 0 || windowRect.Height <= 0)
            {
                return false;
            }

            var now = Stopwatch.GetTimestamp();
            if (!force && _lastRegionTicks != 0
                && (now - _lastRegionTicks) * 1000.0 / Stopwatch.Frequency < RegionIntervalMs)
            {
                return false;
            }

            if (!force && _pending is { IsCompleted: false })
            {
                return false;
            }

            // Half a window of slack on every side: a drag of up to that distance needs no new capture at all.
            var marginX = Math.Max(220, windowRect.Width / 2);
            var marginY = Math.Max(160, windowRect.Height / 2);
            var region = (
                X: windowRect.X - marginX,
                Y: windowRect.Y - marginY,
                Width: windowRect.Width + (marginX * 2),
                Height: windowRect.Height + (marginY * 2));

            _lastRegionTicks = now;
            _pending = Task.Run(() => CaptureRegion(region, allowComposedCapture, windowRect));
            return true;
        }
    }

    /// <summary>
    /// Captures and bakes the region right now, on the calling thread. Costs ~2 ms with GDI, which is worth
    /// paying once when the window opens: the alternative is an async first capture, which leaves the panel
    /// painting a flat tint for the first few frames — the state that reads as "the second window looked worse"
    /// because it depends on how quickly the capture happened to land.
    /// </summary>
    /// <param name="windowRect">Window rectangle in physical pixels.</param>
    /// <param name="allowComposedCapture">True for the composed desktop (WGC), false for GDI.</param>
    /// <returns>True when a region is cached afterwards.</returns>
    public bool TryCaptureNow((int X, int Y, int Width, int Height) windowRect, bool allowComposedCapture)
    {
        var marginX = Math.Max(220, windowRect.Width / 2);
        var marginY = Math.Max(160, windowRect.Height / 2);
        var region = (
            X: windowRect.X - marginX,
            Y: windowRect.Y - marginY,
            Width: windowRect.Width + (marginX * 2),
            Height: windowRect.Height + (marginY * 2));
        CaptureRegion(region, allowComposedCapture, windowRect);
        return HasRegion;
    }

    /// <summary>
    /// Samples the cached region for <paramref name="windowRect"/> and applies the tint gradient in memory.
    /// This is the zero-latency path: no screen read, no allocation after the first call.
    /// </summary>
    /// <param name="windowRect">Current window rectangle in physical pixels.</param>
    /// <param name="tint">Panel tint colour.</param>
    /// <param name="topAlpha">Tint alpha at the top edge (0..1).</param>
    /// <param name="bottomAlpha">Tint alpha at the bottom edge (0..1).</param>
    /// <param name="scrimThreshold">Backdrop luminance above which the tint densifies.</param>
    /// <param name="scrimMaxAlpha">Extra tint at full backdrop brightness (dark themes need this).</param>
    /// <param name="liftThreshold">Backdrop luminance below which the tint lightens (light themes need this).</param>
    /// <param name="liftMaxAlpha">Extra tint at a black backdrop.</param>
    /// <param name="frame">
    /// The composed frame. Its pixel buffer is reused by the next call, so consume it (upload it) before composing
    /// again — the window does exactly that, one upload per position event.
    /// </param>
    /// <returns>False when the window is outside the cached region (a new capture is needed).</returns>
    public bool TryCompose(
        (int X, int Y, int Width, int Height) windowRect,
        SKColor tint,
        double topAlpha,
        double bottomAlpha,
        double scrimThreshold,
        double scrimMaxAlpha,
        double liftThreshold,
        double liftMaxAlpha,
        out GlassFrame frame)
    {
        frame = default;
        var region = _regionPixels;
        if (region is null || windowRect.Width <= 0 || windowRect.Height <= 0 || RegionRect.Width <= 0)
        {
            return false;
        }

        var scale = (double)RegionRect.Width / _regionWidth;
        var offsetX = (int)Math.Round((windowRect.X - RegionRect.X) / scale);
        var offsetY = (int)Math.Round((windowRect.Y - RegionRect.Y) / scale);
        var width = Math.Max(1, (int)Math.Ceiling(windowRect.Width / scale));
        var height = Math.Max(1, (int)Math.Ceiling(windowRect.Height / scale));
        if (offsetX < 0 || offsetY < 0 || offsetX + width > _regionWidth || offsetY + height > _regionHeight)
        {
            return false;
        }

        var rowBytes = width * 4;
        if (_composeBuffer is null || _composeRowBytes != rowBytes || _composeBuffer.Length < rowBytes * height)
        {
            _composeBuffer = new byte[rowBytes * height];
            _composeRowBytes = rowBytes;
        }

        // Two-sided adaptation: densify on a bright backdrop (white text needs it), lighten on a dark one
        // (a light panel that lets a dark desktop drag it to mid-grey loses every grey label it draws).
        var densify = scrimMaxAlpha <= 0
            ? 0
            : Math.Clamp((_regionLuminance - scrimThreshold) / Math.Max(1e-6, 1 - scrimThreshold), 0, 1)
              * scrimMaxAlpha;
        var lift = liftMaxAlpha <= 0
            ? 0
            : Math.Clamp((liftThreshold - _regionLuminance) / Math.Max(1e-6, liftThreshold), 0, 1) * liftMaxAlpha;
        var scrim = densify + lift;
        var target = _composeBuffer;
        for (var y = 0; y < height; y++)
        {
            var alphaTop = (height <= 1) ? 1.0 : (double)y / (height - 1);
            var alpha = Math.Min(0.95, topAlpha + ((bottomAlpha - topAlpha) * alphaTop) + scrim);
            var inverse = 1 - alpha;
            var destination = y * rowBytes;
            var source = ((offsetY + y) * _regionRowBytes) + (offsetX * 4);
            for (var x = 0; x < width; x++)
            {
                var s = source + (x * 4);
                var d = destination + (x * 4);
                // BGRA premultiplied in, straight-alpha tint blended per row.
                target[d] = (byte)((region[s] * inverse) + (tint.Blue * alpha));
                target[d + 1] = (byte)((region[s + 1] * inverse) + (tint.Green * alpha));
                target[d + 2] = (byte)((region[s + 2] * inverse) + (tint.Red * alpha));
                target[d + 3] = 255;
            }
        }

        frame = new GlassFrame(target, width, height, rowBytes);
        return true;
    }

    private void CaptureRegion((int X, int Y, int Width, int Height) region, bool allowComposedCapture,
        (int X, int Y, int Width, int Height) windowRect)
    {
        try
        {
            var captureStarted = Stopwatch.GetTimestamp();
            var captured = CaptureOverride is { } capture
                ? capture(region)
                : ScreenBackdropCapture.TryCapture(region.X, region.Y, region.Width, region.Height,
                    allowComposedCapture);
            var captureMs = (Stopwatch.GetTimestamp() - captureStarted) * 1000.0 / Stopwatch.Frequency;
            if (captured is not { } source)
            {
                lock (_gate)
                {
                    CaptureFailures++;
                }

                return;
            }

            var bakeStarted = Stopwatch.GetTimestamp();
            var baked = Bake(source, out var luminance);
            var bakeMs = (Stopwatch.GetTimestamp() - bakeStarted) * 1000.0 / Stopwatch.Frequency;
            if (baked is null)
            {
                lock (_gate)
                {
                    CaptureFailures++;
                }

                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _regionPixels = baked.Value.Pixels;
                _regionWidth = baked.Value.Width;
                _regionHeight = baked.Value.Height;
                _regionRowBytes = baked.Value.RowBytes;
                _regionLuminance = luminance;
                RegionRect = region;
                CaptureCount++;
                LastCaptureMs = captureMs;
                LastBakeMs = bakeMs;
            }

            System.Diagnostics.Trace.WriteLine("[fluidbar glass] region capture="
                + captureMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "ms bake="
                + bakeMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "ms composed="
                + allowComposedCapture + " rect=" + region.X + "," + region.Y + " " + region.Width + "x"
                + region.Height);
            RegionReady?.Invoke();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException
                                      or NotSupportedException)
        {
            lock (_gate)
            {
                CaptureFailures++;
            }
        }
    }

    /// <summary>Downsamples, blurs and desaturates the capture; no tint (that is applied per window position).</summary>
    private static (byte[] Pixels, int Width, int Height, int RowBytes)? Bake(BackdropFrame frame,
        out double luminance)
    {
        luminance = 0.5;
        var smallWidth = Math.Max(1, frame.Width / Downsample);
        var smallHeight = Math.Max(1, frame.Height / Downsample);
        var info = new SKImageInfo(smallWidth, smallHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var source = SKImage.FromPixelCopy(
            new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque),
            frame.Pixels,
            frame.Stride);
        if (source is null)
        {
            return null;
        }

        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);
        using (var blur = SKImageFilter.CreateBlur(BakeSigma, BakeSigma))
        using (var saturation = SKColorFilter.CreateColorMatrix(SaturationMatrix(Saturation)))
        using (var paint = new SKPaint { ImageFilter = blur, ColorFilter = saturation })
        {
            canvas.DrawImage(source, SKRect.Create(smallWidth, smallHeight),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }

        using var image = surface.Snapshot();
        if (image is null)
        {
            return null;
        }

        using var bitmap = SKBitmap.FromImage(image);
        if (bitmap is null)
        {
            return null;
        }

        var rowBytes = bitmap.RowBytes;
        var bytes = new byte[rowBytes * bitmap.Height];
        var pixels = bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
        {
            return null;
        }

        Marshal.Copy(pixels, bytes, 0, bytes.Length);

        var stepX = Math.Max(1, bitmap.Width / 24);
        var stepY = Math.Max(1, bitmap.Height / 24);
        var sum = 0.0;
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                sum += (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue);
                count++;
            }
        }

        luminance = count == 0 ? 0.5 : sum / count / 255.0;
        return (bytes, bitmap.Width, bitmap.Height, rowBytes);
    }

    private static float[] SaturationMatrix(float saturation)
    {
        const float lumR = 0.2126f;
        const float lumG = 0.7152f;
        const float lumB = 0.0722f;
        var inverse = 1 - saturation;
        return
        [
            (lumR * inverse) + saturation, lumG * inverse, lumB * inverse, 0, 0,
            lumR * inverse, (lumG * inverse) + saturation, lumB * inverse, 0, 0,
            lumR * inverse, lumG * inverse, (lumB * inverse) + saturation, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _regionPixels = null;
        }
    }
}
