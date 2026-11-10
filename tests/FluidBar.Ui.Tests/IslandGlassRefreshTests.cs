using FluidBar.App.Hosting.Skia;
using FluidBar.Platform.Windows.Hosting;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The island's idle glass watch must repaint when the desktop behind the island changes, not only when the mean
/// brightness changes: another window, a video or scrolling can keep the average luminance identical while the
/// material is completely different (report 2026-10-25: the glass stayed stale until some activity repainted).
/// </summary>
public sealed class IslandGlassRefreshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public async Task Backdrop_bake_blurs_text_sized_stripes_instead_of_only_resizing_them(int captureAlpha)
    {
        using var provider = new IslandBackdropProvider();
        var rect = (X: 0, Y: 0, Width: 480, Height: 240);
        provider.CaptureOverride = _ =>
        {
            var pixels = new byte[rect.Width * rect.Height * 4];
            for (var y = 0; y < rect.Height; y++)
            for (var x = 0; x < rect.Width; x++)
            {
                var value = (byte)((x / 12) % 2 == 0 ? 0 : 255);
                var i = (y * rect.Width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = (byte)captureAlpha;
            }
            return new BackdropFrame(rect.Width, rect.Height, rect.Width * 4, pixels);
        };
        Assert.True(await CaptureAsync(provider, rect));
        using var bitmap = SkiaSharp.SKBitmap.FromImage(provider.Get(rect)!);
        var levels = Enumerable.Range(16, bitmap.Width - 32).Select(x => (int)bitmap.GetPixel(x, bitmap.Height / 2).Red).ToArray();
        Assert.True(levels.Max() - levels.Min() < 30, $"Backdrop still contains sharp stripes: contrast {levels.Max() - levels.Min()}.");
        Assert.InRange(levels.Average(), 110, 145); // Preserve desktop RGB instead of replacing it with an opaque black image.
        foreach (var point in new[] { (bitmap.Width / 2, bitmap.Height / 2), (0, 0), (bitmap.Width - 1, bitmap.Height - 1) })
            Assert.True(bitmap.GetPixel(point.Item1, point.Item2).Alpha >= 250,
                $"Capture alpha {captureAlpha} leaked into the blurred desktop at {point}: {bitmap.GetPixel(point.Item1, point.Item2).Alpha}.");
    }

    [Fact]
    public void Another_window_at_the_same_brightness_still_wakes_the_loop()
    {
        Assert.True(IslandGlassRefreshPolicy.ShouldWake(
            previousSignature: 1234, signature: 9876, previousLuminance: 0.5, luminance: 0.5));
    }

    [Fact]
    public void A_static_desktop_keeps_the_loop_asleep()
    {
        Assert.False(IslandGlassRefreshPolicy.ShouldWake(
            previousSignature: 1234, signature: 1234, previousLuminance: 0.5, luminance: 0.505));
    }

    [Fact]
    public void A_luminance_jump_alone_also_wakes_the_loop()
    {
        Assert.True(IslandGlassRefreshPolicy.ShouldWake(
            previousSignature: 1234, signature: 1234, previousLuminance: 0.2,
            luminance: 0.2 + IslandGlassRefreshPolicy.LuminanceThreshold + 0.01));
    }

    [Fact]
    public void First_capture_wakes_the_loop_and_an_unknown_signature_does_not()
    {
        Assert.True(IslandGlassRefreshPolicy.ShouldWake(
            previousSignature: -1, signature: 42, previousLuminance: -1, luminance: 0.5));
        Assert.False(IslandGlassRefreshPolicy.ShouldWake(
            previousSignature: 42, signature: -1, previousLuminance: 0.5, luminance: 0.5));
    }

    [Fact]
    public void A_landed_capture_forces_a_full_frame_without_controller_invalidation()
    {
        Assert.True(IslandGlassRefreshPolicy.RequiresFullFrame(
            backdropChanged: true, luminanceAnimating: false, awaitingFirstGlass: false));
        Assert.False(IslandGlassRefreshPolicy.RequiresFullFrame(
            backdropChanged: false, luminanceAnimating: false, awaitingFirstGlass: false));
    }

    [Fact]
    public void The_idle_loop_cannot_stop_before_the_capture_and_its_fade_are_painted()
    {
        Assert.False(IslandGlassRefreshPolicy.CanStopIdleLoop(
            backdropChanged: true, luminanceAnimating: false, awaitingFirstGlass: false));
        Assert.False(IslandGlassRefreshPolicy.CanStopIdleLoop(
            backdropChanged: false, luminanceAnimating: true, awaitingFirstGlass: false));
        Assert.False(IslandGlassRefreshPolicy.CanStopIdleLoop(
            backdropChanged: false, luminanceAnimating: false, awaitingFirstGlass: true));
        Assert.True(IslandGlassRefreshPolicy.CanStopIdleLoop(
            backdropChanged: false, luminanceAnimating: false, awaitingFirstGlass: false));
    }

    [Fact]
    public async Task Provider_signature_tracks_content_not_only_mean_luminance()
    {
        using var provider = new IslandBackdropProvider();
        var rect = (0, 0, 480, 320);

        provider.CaptureOverride = _ => Uniform(rect.Item3, rect.Item4, 120);
        Assert.True(await CaptureAsync(provider, rect));
        var uniformSignature = provider.SignatureFor(rect);
        var uniformLuminance = provider.LuminanceFor(rect);

        // Same average brightness (half bright, half dark), completely different content.
        provider.CaptureOverride = _ => Checker(rect.Item3, rect.Item4);
        provider.Invalidate();
        Assert.True(await CaptureAsync(provider, rect));
        var checkerSignature = provider.SignatureFor(rect);

        Assert.Equal(uniformLuminance, provider.LuminanceFor(rect), 2);
        Assert.NotEqual(uniformSignature, checkerSignature);
        Assert.True(IslandGlassRefreshPolicy.ShouldWake(
            uniformSignature, checkerSignature, uniformLuminance, provider.LuminanceFor(rect)));
    }

    [Fact]
    public async Task Identical_captures_keep_the_same_signature()
    {
        using var provider = new IslandBackdropProvider();
        var rect = (0, 0, 480, 320);
        provider.CaptureOverride = _ => Uniform(rect.Item3, rect.Item4, 90);

        Assert.True(await CaptureAsync(provider, rect));
        var first = provider.SignatureFor(rect);
        provider.Invalidate();
        Assert.True(await CaptureAsync(provider, rect));

        Assert.Equal(first, provider.SignatureFor(rect));
    }

    private static Task<bool> CaptureAsync(IslandBackdropProvider provider,
        (int X, int Y, int Width, int Height) rect)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUpdated() => completion.TrySetResult(true);
        provider.Updated += OnUpdated;
        try
        {
            _ = provider.Get(rect, force: true);
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _ = completion.Task.ContinueWith(_ => provider.Updated -= OnUpdated, TaskScheduler.Default);
        }
    }

    private static BackdropFrame Uniform(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        return new BackdropFrame(width, height, width * 4, pixels);
    }

    private static BackdropFrame Checker(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var blockWidth = Math.Max(1, width / 4);
        var blockHeight = Math.Max(1, height / 4);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bright = (((x / blockWidth) + (y / blockHeight)) % 2) == 0;
                var value = (byte)(bright ? 240 : 0);
                var i = ((y * width) + x) * 4;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        return new BackdropFrame(width, height, width * 4, pixels);
    }
}
