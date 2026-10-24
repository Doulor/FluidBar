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
    public const int RefreshMs = 120;

    /// <summary>Hard floor between two captures, even while the window is being dragged (ms).</summary>
    public const int MinCaptureIntervalMs = 60;

    /// <summary>Downsample factor of the blur bake.</summary>
    private const int Downsample = 6;

    /// <summary>
    /// Backdrop saturation. Every reference implementation (rdev, archisvaze, Apple's .regular) lifts
    /// the saturation of the blurred backdrop — glass concentrates colour instead of greying it out.
    /// </summary>
    private const float BackdropSaturation = 1.60f;

    /// <summary>Gaussian sigma at the 1/6 bake scale (2.5 ≈ a 10 DIP CSS blur at 150 % DPI).</summary>
    private const float BakeSigma = 2.5f;

    private const int MaxEntries = 2;

    /// <summary>
    /// Frames a replaced image is kept alive before it is disposed: in-flight draws in the current frame
    /// may still reference it. This is a *safety margin*, not an ownership guarantee — callers must copy
    /// a frame they intend to draw later (see <c>SkiaIslandHost.CopyImage</c>). Counting it as ownership
    /// is what freed a host-held backdrop and crashed the process on 2026-10-24.
    /// </summary>
    private const int RetireFrames = 12;

    private readonly object _gate = new();
    private readonly Dictionary<(int X, int Y, int Width, int Height), (SKImage Image, double Luminance, long At)> _frames = new(MaxEntries);
    private readonly Queue<(SKImage Image, long RetiredAt)> _retired = new();

    private Task? _pending;
    private long _frame;
    private long _lastCaptureTicks;
    private bool _disposed;

    /// <summary>Raised after a capture lands, so the host can paint one frame with the new backdrop.</summary>
    public event Action? Updated;

    /// <summary>
    /// Test seam: replaces the real screen grab so frame ownership and retirement can be exercised
    /// without a capturable desktop (headless CI). Null in production.
    /// </summary>
    internal Func<(int X, int Y, int Width, int Height), BackdropFrame?>? CaptureOverride { get; set; }

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
    public SKImage? Get(
        (int X, int Y, int Width, int Height) screenRect,
        (int X, int Y, int Width, int Height)[]? islandMasks = null,
        bool allowComposedCapture = true,
        bool force = false,
        (int X, int Y, int Width, int Height)? luminanceRect = null)
    {
        lock (_gate)
        {
            _frame++;
            DrainRetired();

            if (_disposed || screenRect.Width <= 0 || screenRect.Height <= 0)
            {
                return null;
            }

            if (_frames.TryGetValue(screenRect, out var cached))
            {
                var ageMs = (Stopwatch.GetTimestamp() - cached.At) * 1000.0 / Stopwatch.Frequency;
                if (force || ageMs >= RefreshMs)
                {
                    ScheduleCapture(screenRect, islandMasks, allowComposedCapture, force, luminanceRect);
                }

                // Only this rectangle's own capture may be returned. Returning the newest frame overall
                // painted one monitor's desktop into the other island and handed callers a frame they had
                // no claim on; the host keeps its last adopted copy while a new capture bakes.
                return cached.Image;
            }

            ScheduleCapture(screenRect, islandMasks, allowComposedCapture, force, luminanceRect);
            return null;
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

    private void ScheduleCapture(
        (int X, int Y, int Width, int Height) screenRect,
        (int X, int Y, int Width, int Height)[]? islandMasks,
        bool allowComposedCapture,
        bool force,
        (int X, int Y, int Width, int Height)? luminanceRect)
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
        _pending = Task.Run(() => CaptureAndBake(screenRect, islandMasks, allowComposedCapture, luminanceRect));
    }

    private void CaptureAndBake(
        (int X, int Y, int Width, int Height) screenRect,
        (int X, int Y, int Width, int Height)[]? islandMasks,
        bool allowComposedCapture,
        (int X, int Y, int Width, int Height)? luminanceRect)
    {
        var frame = CaptureOverride is { } capture
            ? capture(screenRect)
            : ScreenBackdropCapture.TryCapture(
                screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height, allowComposedCapture);
        if (frame is not { } captured)
        {
            lock (_gate)
            {
                CaptureFailures++;
            }

            return;
        }

        if (islandMasks is { Length: > 0 } masks)
        {
            foreach (var mask in masks)
            {
                MaskIslandArea(captured, mask);
            }
        }

        var baked = Bake(captured, out var luminance, luminanceRect);
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

        // Outside the lock: the host posts one frame so an idle island actually shows the new backdrop.
        try
        {
            Updated?.Invoke();
        }
        catch (Exception)
        {
            // A subscriber failure must never break the capture loop.
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

    /// <summary>
    /// Replaces the island's own pixels in a composed capture with a horizontal edge-extend fill: the
    /// capture is taken with the window visible (so the island stays visible in screen shares), and the
    /// smear disappears under the 1/6 downsample + Gaussian. Every island surface is masked — the shell
    /// *and* the split capsules: skipping the capsules baked their rotating agent ring into the glass and
    /// made the main pill blink with it (reported 2026-10-23).
    /// </summary>
    internal static void MaskIslandArea(BackdropFrame frame, in (int X, int Y, int Width, int Height) mask)
    {
        var x0 = Math.Clamp(mask.X, 0, frame.Width - 1);
        var y0 = Math.Clamp(mask.Y, 0, frame.Height - 1);
        var x1 = Math.Clamp(mask.X + mask.Width, 0, frame.Width);
        var y1 = Math.Clamp(mask.Y + mask.Height, 0, frame.Height);
        if (x1 - x0 < 2 || y1 - y0 < 2)
        {
            return;
        }

        var stride = frame.Stride;
        var pixels = frame.Pixels;
        var leftX = Math.Max(0, x0 - 4);
        var rightX = Math.Min(frame.Width - 1, x1 + 4);
        var span = (float)Math.Max(1, x1 - x0);
        for (var y = y0; y < y1; y++)
        {
            var row = y * stride;
            var li = row + (leftX * 4);
            var ri = row + (rightX * 4);
            var lb = pixels[li];
            var lg = pixels[li + 1];
            var lr = pixels[li + 2];
            var rb = pixels[ri];
            var rg = pixels[ri + 1];
            var rr = pixels[ri + 2];
            for (var x = x0; x < x1; x++)
            {
                var t = (x - x0) / span;
                var i = row + (x * 4);
                pixels[i] = (byte)(lb + ((rb - lb) * t));
                pixels[i + 1] = (byte)(lg + ((rg - lg) * t));
                pixels[i + 2] = (byte)(lr + ((rr - lr) * t));
            }
        }
    }

    private static SKImage? Bake(
        in BackdropFrame frame,
        out double luminance,
        (int X, int Y, int Width, int Height)? luminanceRect = null)
    {
        var width = Math.Max(1, frame.Width / Downsample);
        var height = Math.Max(1, frame.Height / Downsample);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Mean luminance of the capture (sRGB-weighted, sampled every 4th pixel). When the caller passes
        // the island footprint, only those pixels count: a bright window merely *near* the island must not
        // recolour the glass (reported 2026-10-23).
        double sum = 0;
        var count = 0;
        var pixels = frame.Pixels;
        var x0 = 0;
        var y0 = 0;
        var x1 = frame.Width;
        var y1 = frame.Height;
        if (luminanceRect is { } window)
        {
            x0 = Math.Clamp(window.X, 0, frame.Width);
            y0 = Math.Clamp(window.Y, 0, frame.Height);
            x1 = Math.Clamp(window.X + window.Width, x0, frame.Width);
            y1 = Math.Clamp(window.Y + window.Height, y0, frame.Height);
        }

        for (var y = y0; y < y1; y++)
        {
            var row = y * frame.Stride;
            for (var x = x0; x < x1; x++)
            {
                var i = row + (x * 4);
                sum += (0.2126 * pixels[i + 2]) + (0.7152 * pixels[i + 1]) + (0.0722 * pixels[i]);
                count++;
            }
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
                paint.ImageFilter = SKImageFilter.CreateBlur(BakeSigma, BakeSigma);
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
