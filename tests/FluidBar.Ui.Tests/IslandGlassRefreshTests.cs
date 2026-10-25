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
