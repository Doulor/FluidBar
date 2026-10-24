using FluidBar.App.Views.Settings;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The glass material's two contract points: a dark-theme tint must be visibly darker than the light-theme one
/// ("深色模式感觉不够深色"), and moving the window must re-sample the cached region without a new capture
/// ("零延迟"). Both are checked against an injected backdrop, so the result does not depend on the desktop.
/// </summary>
public sealed class SettingsGlassBackdropTests
{
    [Fact]
    public async Task Dark_tint_makes_the_panel_darker_than_the_light_tint()
    {
        using var glass = new SettingsGlassBackdrop();
        // Flat mid grey: the tint is then the only thing that changes the result.
        glass.CaptureOverride = region => Flat(region.Width, region.Height, 128);

        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));

        var tint = new SKColor(0x08, 0x08, 0x0A); // palette window colour (dark theme)
        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, 0.34, 0.48, 0.42, 0.30, 0.0, 0.0, out var light));
        var lightMean = Mean(light);

        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, 0.62, 0.76, 0.42, 0.30, 0.0, 0.0, out var dark));
        var darkMean = Mean(dark);

        Assert.True(darkMean < lightMean - 10,
            "the dark-theme tint must be clearly darker (light " + lightMean.ToString("0.0")
            + " vs dark " + darkMean.ToString("0.0") + ")");
        Assert.True(darkMean < 60, "a dark panel must stay dark even over a mid-grey desktop");
    }

    [Fact]
    public async Task Bright_desktop_densifies_the_tint_through_the_scrim()
    {
        using var glass = new SettingsGlassBackdrop();
        glass.CaptureOverride = region => Flat(region.Width, region.Height, 255);

        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));

        var tint = new SKColor(0x08, 0x08, 0x0A);
        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, 0.34, 0.48, 0.42, 0.0, 0.0, 0.0, out var withoutScrim));
        var plain = Mean(withoutScrim);

        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, 0.34, 0.48, 0.42, 0.30, 0.0, 0.0, out var withScrim));
        Assert.True(Mean(withScrim) < plain,
            "a bright desktop must get the extra scrim so white text keeps its contrast");
    }

    [Fact]
    public async Task Moving_the_window_re_samples_the_cached_region_without_a_capture()
    {
        using var glass = new SettingsGlassBackdrop();
        // Horizontal ramp: every x has a different brightness, so an offset is measurable.
        glass.CaptureOverride = region => Ramp(region.Width, region.Height);

        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));
        var captures = glass.CaptureCount;

        var tint = new SKColor(0, 0, 0);
        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, 0.0, 0.0, 0.42, 0.0, 0.0, 0.0, out var left));
        // The frame buffer is reused by the next call, so read what is needed before composing again.
        var leftFirst = left.Pixels[0];

        // Half a window to the right: still inside the cached region (the capture has half a window of slack).
        Assert.True(glass.TryCompose((300, 100, 600, 420), tint, 0.0, 0.0, 0.42, 0.0, 0.0, 0.0, out var right));

        Assert.NotEqual(leftFirst, right.Pixels[0]);
        Assert.Equal(captures, glass.CaptureCount);
    }

    [Fact]
    public async Task Leaving_the_cached_region_reports_that_a_new_capture_is_needed()
    {
        using var glass = new SettingsGlassBackdrop();
        glass.CaptureOverride = region => Flat(region.Width, region.Height, 128);
        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));

        var tint = new SKColor(0, 0, 0);
        Assert.False(glass.TryCompose((9000, 9000, 600, 420), tint, 0.3, 0.4, 0.42, 0.3, 0.0, 0.0, out _));
    }

    [Fact]
    public async Task Light_material_keeps_a_dark_desktop_from_turning_the_panel_grey()
    {
        using var glass = new SettingsGlassBackdrop();
        // A dark desktop (luminance ≈ 0.20): without the lift the light panel lands on mid-grey and every grey
        // label disappears into it (user report 2026-10-24: "浅色非常丑，而且灰色字看不清").
        glass.CaptureOverride = region => Flat(region.Width, region.Height, 51);
        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));

        var light = SettingsWindow.GlassMaterialFor(isLight: true);
        var tint = new SKColor(0xF9, 0xFA, 0xFC);
        Assert.True(glass.TryCompose((0, 0, 600, 420), tint, light.TopAlpha, light.BottomAlpha,
            light.ScrimThreshold, light.ScrimMaxAlpha, light.LiftThreshold, light.LiftMaxAlpha, out var frame));
        Assert.True(Mean(frame) > 170,
            "the light panel must stay light over a dark desktop (measured " + Mean(frame).ToString("0.0") + ")");

        // The lift belongs to the light material only: the dark theme must stay dark on the same backdrop.
        var dark = SettingsWindow.GlassMaterialFor(isLight: false);
        Assert.True(glass.TryCompose((0, 0, 600, 420), new SKColor(0x08, 0x08, 0x0A), dark.TopAlpha,
            dark.BottomAlpha, dark.ScrimThreshold, dark.ScrimMaxAlpha, dark.LiftThreshold, dark.LiftMaxAlpha,
            out var darkFrame));
        Assert.True(Mean(darkFrame) < 40,
            "the dark theme must not be lifted by the light theme's rules (measured "
            + Mean(darkFrame).ToString("0.0") + ")");
    }

    [Fact]
    public async Task Light_material_does_not_over_darken_a_bright_desktop()
    {
        using var glass = new SettingsGlassBackdrop();
        glass.CaptureOverride = region => Flat(region.Width, region.Height, 235);
        Assert.True(await CaptureAsync(glass, (0, 0, 600, 420)));

        var light = SettingsWindow.GlassMaterialFor(isLight: true);
        Assert.True(glass.TryCompose((0, 0, 600, 420), new SKColor(0xF9, 0xFA, 0xFC), light.TopAlpha,
            light.BottomAlpha, light.ScrimThreshold, light.ScrimMaxAlpha, light.LiftThreshold, light.LiftMaxAlpha,
            out var frame));
        Assert.True(Mean(frame) > 200, "a bright desktop must stay airy in the light theme");
    }

    private static Task<bool> CaptureAsync(SettingsGlassBackdrop glass, (int X, int Y, int Width, int Height) rect)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady() => completion.TrySetResult(true);
        glass.RegionReady += OnReady;
        try
        {
            Assert.True(glass.RequestRegion(rect, allowComposedCapture: false, force: true));
            return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _ = completion.Task.ContinueWith(_ => glass.RegionReady -= OnReady,
                TaskScheduler.Default);
        }
    }

    private static BackdropFrame Flat(int width, int height, byte value)
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

    private static BackdropFrame Ramp(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                var value = (byte)Math.Clamp(x * 255 / Math.Max(1, width - 1), 0, 255);
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        return new BackdropFrame(width, height, width * 4, pixels);
    }

    private static double Mean(GlassFrame frame)
    {
        var sum = 0.0;
        var count = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var i = (y * frame.RowBytes) + (x * 4);
                sum += (0.2126 * frame.Pixels[i + 2]) + (0.7152 * frame.Pixels[i + 1]) + (0.0722 * frame.Pixels[i]);
                count++;
            }
        }

        return count == 0 ? 0 : sum / count;
    }
}
