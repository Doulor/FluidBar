using System.Diagnostics;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Supplies the liquid-glass material with a blurred snapshot of the desktop behind the island.
/// <para>
/// The window is marked <c>WDA_EXCLUDEFROMCAPTURE</c> by the layered host, so a GDI capture of its screen
/// rectangle returns what is *behind* it. A screen read costs ~6.5 ms (GPU→CPU sync in the compositor),
/// which does not fit a frame budget, so captures run on the thread pool and the renderer always paints
/// the newest finished image: no frame ever waits for the capture. Frames are downsampled 1/6, blurred
/// and saturated once, then cached per window rectangle (≤2) and throttled to <see cref="RefreshMs"/>.
/// </para>
/// </summary>
internal sealed class IslandBackdropProvider : IDisposable
{
    /// <summary>Minimum time between two captures of the same rectangle (ms).</summary>
    public const int RefreshMs = 400;

    /// <summary>Hard floor between two captures, even while the window is being dragged (ms).</summary>
    public const int MinCaptureIntervalMs = 120;

    /// <summary>Downsample factor of the blur bake.</summary>
    private const int Downsample = 6;

    /// <summary>
    /// Backdrop saturation. Every reference implementation (rdev, archisvaze, Apple's .regular) lifts
    /// the saturation of the blurred backdrop — glass concentrates colour instead of greying it out.
    /// </summary>
    private const float BackdropSaturation = 1.55f;

    private const int MaxEntries = 2;

    /// <summary>Frames a replaced image is kept alive before it is disposed (a draw may still use it).</summary>
    private const int RetireFrames = 2;

    private readonly object _gate = new();
    private readonly Dictionary<(int X, int Y, int Width, int Height), (SKImage Image, double Luminance, long At)> _frames = new(MaxEntries);
    private readonly Queue<(SKImage Image, long RetiredAt)> _retired = new();

    private Task? _pending;
    private long _frame;
    private long _lastCaptureTicks;
    private bool _disposed;

    /// <summary>Number of successful captures (diagnostics).</summary>
    public long CaptureCount { get; private set; }

    /// <summary>Number of failed capture attempts (diagnostics).</summary>
    public long CaptureFailures { get; private set; }

    /// <summary>
    /// Returns the newest blurred backdrop for a window rectangle and schedules a refresh when it is due.
    /// Never blocks on the screen read; call once per rendered frame.
    /// </summary>
    /// <param name="screenRect">Window rectangle in physical screen pixels.</param>
    /// <param name="force">Ignore the throttle (material switch, explicit refresh).</param>
    public SKImage? Get((int X, int Y, int Width, int Height) screenRect, bool force = false)
    {
        lock (_gate)
        {
            _frame++;
            DrainRetired();

            if (_disposed || screenRect.Width <= 0 || screenRect.Height <= 0)
            {
                return Latest();
            }

            if (!force && _frames.TryGetValue(screenRect, out var cached))
            {
                var ageMs = (Stopwatch.GetTimestamp() - cached.At) * 1000.0 / Stopwatch.Frequency;
                if (ageMs < RefreshMs)
                {
                    return cached.Image;
                }
            }

            ScheduleCapture(screenRect, force);
            return Latest();
        }
    }

    /// <summary>Mean luminance 0..1 of the cached capture for a rectangle (0.5 when unknown).</summary>
    public double LuminanceFor((int X, int Y, int Width, int Height) screenRect)
    {
        lock (_gate)
        {
            return _frames.TryGetValue(screenRect, out var frame) ? frame.Luminance : 0.5;
        }
    }

    /// <summary>Drops every cached capture (the next frame re-captures).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            foreach (var entry in _frames.Values)
            {
                _retired.Enqueue((entry.Image, _frame));
            }

            _frames.Clear();
            _lastCaptureTicks = 0;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
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
            while (_retired.Count > 0)
            {
                _retired.Dequeue().Image.Dispose();
            }
        }
    }

    private void ScheduleCapture((int X, int Y, int Width, int Height) screenRect, bool force)
    {
        if (_pending is { IsCompleted: false })
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var sinceLastMs = _lastCaptureTicks == 0
            ? double.MaxValue
            : (now - _lastCaptureTicks) * 1000.0 / Stopwatch.Frequency;
        if (!force && sinceLastMs < MinCaptureIntervalMs)
        {
            return;
        }

        _lastCaptureTicks = now;
        _pending = Task.Run(() => CaptureAndBake(screenRect));
    }

    private void CaptureAndBake((int X, int Y, int Width, int Height) screenRect)
    {
        var frame = ScreenBackdropCapture.TryCapture(screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height);
        if (frame is not { } captured)
        {
            lock (_gate)
            {
                CaptureFailures++;
            }

            return;
        }

        var baked = Bake(captured, out var luminance);
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
                baked.Dispose();
                return;
            }

            if (_frames.TryGetValue(screenRect, out var previous))
            {
                _retired.Enqueue((previous.Image, _frame));
                _frames[screenRect] = (baked, luminance, Stopwatch.GetTimestamp());
            }
            else
            {
                if (_frames.Count >= MaxEntries)
                {
                    var oldest = _frames.OrderBy(pair => pair.Value.At).First();
                    _retired.Enqueue((oldest.Value.Image, _frame));
                    _frames.Remove(oldest.Key);
                }

                _frames[screenRect] = (baked, luminance, Stopwatch.GetTimestamp());
            }

            CaptureCount++;
        }
    }

    /// <summary>Disposes images that no in-flight frame can still reference (UI thread, start of a frame).</summary>
    private void DrainRetired()
    {
        while (_retired.Count > 0 && _frame - _retired.Peek().RetiredAt >= RetireFrames)
        {
            _retired.Dequeue().Image.Dispose();
        }
    }

    private SKImage? Latest()
        => _frames.Count == 0
            ? null
            : _frames.OrderBy(pair => pair.Value.At).Last().Value.Image;

    private static SKImage? Bake(in BackdropFrame frame, out double luminance)
    {
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
            using (var saturation = SKColorFilter.CreateColorMatrix(SaturationMatrix(BackdropSaturation)))
            {
                // Box-downsample + a small Gaussian at the reduced resolution: sigma 2 at 1/6 scale is
                // roughly a 12-18 px blur at device pixels, i.e. the 20-30 px CSS blur of the prototype.
                paint.ImageFilter = SKImageFilter.CreateBlur(2.0f, 2.0f);
                paint.ColorFilter = saturation;
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

    /// <summary>Standard luminance-preserving saturation matrix (Skia row-major 4x5).</summary>
    private static float[] SaturationMatrix(float s)
    {
        var inv = 1f - s;
        var r = 0.213f * inv;
        var g = 0.715f * inv;
        var b = 0.072f * inv;
        return
        [
            r + s, g, b, 0, 0,
            r, g + s, b, 0, 0,
            r, g, b + s, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }
}
