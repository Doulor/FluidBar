using System.Diagnostics;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Supplies the liquid-glass material with a blurred snapshot of the desktop behind the island.
/// <para>
/// The window is marked <c>WDA_EXCLUDEFROMCAPTURE</c> by the layered host, so a GDI capture of its
/// screen rectangle returns what is *behind* it. The frame is downsampled 1/6 and blurred once, then
/// cached: captures are throttled to <see cref="RefreshMs"/> and re-taken when the window moves, so a
/// settled island pays ~3 ms every 400 ms and nothing per frame.
/// </para>
/// </summary>
internal sealed class IslandBackdropProvider : IDisposable
{
    /// <summary>Minimum time between two captures (ms).</summary>
    public const int RefreshMs = 400;

    /// <summary>Hard floor between two captures, even while the window is being dragged (ms).</summary>
    public const int MinCaptureIntervalMs = 120;

    /// <summary>Downsample factor of the blur bake.</summary>
    private const int Downsample = 6;

    private const int MaxEntries = 2;

    private readonly Dictionary<(int X, int Y, int Width, int Height), (SKImage Image, double Luminance, long At)> _frames = new(MaxEntries);
    private long _lastCaptureTicks;
    private bool _disposed;

    /// <summary>Number of successful captures (diagnostics).</summary>
    public long CaptureCount { get; private set; }

    /// <summary>Number of failed capture attempts (diagnostics).</summary>
    public long CaptureFailures { get; private set; }

    /// <summary>
    /// Returns the blurred backdrop for a window rectangle, refreshing it at most every
    /// <see cref="RefreshMs"/>. Returns the previous image when a capture fails.
    /// </summary>
    /// <param name="screenRect">Window rectangle in physical screen pixels.</param>
    /// <param name="force">Bypass the throttle (material switch, explicit refresh).</param>
    public SKImage? Get((int X, int Y, int Width, int Height) screenRect, bool force = false)
    {
        if (_disposed || screenRect.Width <= 0 || screenRect.Height <= 0)
        {
            return Latest();
        }

        var now = Stopwatch.GetTimestamp();

        // A drag moves the window every frame; capturing per frame would cost ~3 ms each. The floor
        // keeps the backdrop at most one drag-frame stale instead of turning a drag into 60 captures/s.
        var sinceLast = _lastCaptureTicks == 0
            ? double.MaxValue
            : (now - _lastCaptureTicks) * 1000.0 / Stopwatch.Frequency;
        if (!force && sinceLast < MinCaptureIntervalMs)
        {
            return _frames.TryGetValue(screenRect, out var recent) ? recent.Image : Latest();
        }

        if (_frames.TryGetValue(screenRect, out var cached))
        {
            var elapsedMs = (now - cached.At) * 1000.0 / Stopwatch.Frequency;
            if (!force && elapsedMs < RefreshMs)
            {
                return cached.Image;
            }

            if (force && elapsedMs < RefreshMs / 2)
            {
                return cached.Image;
            }
        }

        var frame = ScreenBackdropCapture.TryCapture(
            screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
        if (frame is not { } captured)
        {
            CaptureFailures++;
            return Latest();
        }

        var baked = Bake(captured, out var luminance);
        if (baked is null)
        {
            CaptureFailures++;
            return Latest();
        }

        if (_frames.TryGetValue(screenRect, out var previous))
        {
            previous.Image.Dispose();
        }
        else if (_frames.Count >= MaxEntries)
        {
            // Keep the two most recent monitor rects only.
            var oldest = _frames.OrderBy(pair => pair.Value.At).First();
            _frames.Remove(oldest.Key);
            oldest.Value.Image.Dispose();
        }

        _frames[screenRect] = (baked, luminance, now);
        _lastCaptureTicks = now;
        CaptureCount++;
        return baked;
    }

    /// <summary>Drops every cached capture (the next frame re-captures).</summary>
    public void Invalidate()
    {
        foreach (var entry in _frames.Values)
        {
            entry.Image.Dispose();
        }

        _frames.Clear();
    }

    private SKImage? Latest()
        => _frames.Count == 0
            ? null
            : _frames.OrderBy(pair => pair.Value.At).Last().Value.Image;

    /// <summary>Mean luminance 0..1 of the cached capture for a rectangle (0.5 when unknown).</summary>
    public double LuminanceFor((int X, int Y, int Width, int Height) screenRect)
        => _frames.TryGetValue(screenRect, out var frame) ? frame.Luminance : 0.5;

    private static SKImage? Bake(in BackdropFrame frame, out double luminance)
    {
        luminance = 0.5;
        var width = Math.Max(1, frame.Width / Downsample);
        var height = Math.Max(1, frame.Height / Downsample);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Mean luminance of the capture (sRGB-weighted, sampled every 4th pixel): the tint adapts to it.
        double sum = 0;
        var count = 0;
        var pixels = frame.Pixels;
        for (var i = 0; i < pixels.Length; i += 16)
        {
            sum += (0.2126 * pixels[i + 2]) + (0.7152 * pixels[i + 1]) + (0.0722 * pixels[i]);
            count++;
        }

        luminance = count == 0 ? 0.5 : Math.Clamp(sum / (count * 255.0), 0, 1);

        try
        {
            var sourceInfo = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            using var source = SKImage.FromPixelCopy(sourceInfo, frame.Pixels, frame.Stride);

            using var small = new SKBitmap(info);
            using (var canvas = new SKCanvas(small))
            using (var paint = new SKPaint { IsAntialias = false })
            {
                // Box-downsample + a small Gaussian at the reduced resolution: sigma 2 at 1/6 scale is
                // roughly a 12-18 px blur at device pixels, i.e. the 20-30 px CSS blur of the prototype.
                paint.ImageFilter = SKImageFilter.CreateBlur(2.0f, 2.0f);
                paint.FilterQuality = SKFilterQuality.Medium;
                canvas.DrawImage(source, new SKRect(0, 0, width, height), paint);
                canvas.Flush();
            }

            return SKImage.FromBitmap(small);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _frames.Values)
        {
            entry.Image.Dispose();
        }

        _frames.Clear();
    }
}
