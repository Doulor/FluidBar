using FluidBar.App.Hosting.Skia;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Ownership contract of the liquid-glass backdrop.
/// <para>
/// Regression guard for the 2026-10-24 crashes: an access violation inside
/// <c>sk_image_get_width</c> reached from <c>SkiaIslandHost.CopyImage</c>, because the host kept a
/// <em>borrow</em> of the provider's cache and rendered it after the provider had retired and freed it.
/// The provider disposes on a capture counter that the idle palette watch keeps advancing even when no
/// frame renders, so the retire window is a safety margin, never an ownership guarantee.
/// </para>
/// <para>
/// Two invariants are pinned here: a caller that wants to draw a frame later must copy it, and the
/// provider may only hand out the frame belonging to the requested rectangle.
/// </para>
/// </summary>
public sealed class IslandBackdropOwnershipTests
{
    private static readonly (int X, int Y, int Width, int Height) RectA = (0, 0, 360, 120);
    private static readonly (int X, int Y, int Width, int Height) RectB = (1920, 0, 360, 120);

    /// <summary>A synthetic capture: a flat grey frame of the given value.</summary>
    private static BackdropFrame Frame(byte value)
    {
        const int width = 36;
        const int height = 12;
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

    /// <summary>A tiny stand-in for a provider frame.</summary>
    private static SKImage SourceImage(byte value)
    {
        const int width = 6;
        const int height = 4;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 255;
        }

        return SKImage.FromPixelCopy(info, pixels, width * 4);
    }

    private static SKImage? WaitForFrame(
        IslandBackdropProvider provider,
        (int X, int Y, int Width, int Height) rect,
        Func<SKImage?, bool> until,
        bool force = false,
        int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var image = provider.Get(rect, force: force);
            if (until(image))
            {
                return image;
            }

            Thread.Sleep(10);
        }

        return provider.Get(rect, force: force);
    }

    [Fact]
    public void A_copied_frame_outlives_the_provider_frame()
    {
        var source = SourceImage(70);
        var copy = SkiaIslandHost.CopyImage(source);
        Assert.NotNull(copy);

        // The provider is free to release its frame at any moment; the host keeps drawing its own copy.
        source.Dispose();

        using (copy)
        {
            Assert.Equal(6, copy!.Width);
            Assert.Equal(4, copy.Height);

            using var readback = new SKBitmap(new SKImageInfo(6, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
            Assert.True(copy.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));
            Assert.Equal(70, readback.GetPixel(0, 0).Red);
        }
    }

    [Fact]
    public void A_host_copy_survives_the_provider_retire_window()
    {
        // The exact 2026-10-24 crash: the host adopts a frame, the provider later retires and frees that
        // same frame, and the host renders on. Copying at adoption is what keeps the render legal.
        using var provider = new IslandBackdropProvider();
        provider.CaptureOverride = _ => Frame(60);

        var borrowed = WaitForFrame(provider, RectA, image => image is not null);
        Assert.NotNull(borrowed);
        var owned = SkiaIslandHost.CopyImage(borrowed!);
        Assert.NotNull(owned);
        var ownedWidth = owned!.Width;

        // A newer capture replaces the cached frame, then idle polls burn the retire counter.
        provider.CaptureOverride = _ => Frame(180);
        var replacement = WaitForFrame(
            provider, RectA, image => image is not null && !ReferenceEquals(image, borrowed), force: true);
        Assert.NotNull(replacement);
        for (var i = 0; i < 20; i++)
        {
            provider.Get(RectA);
        }

        // The provider's frame is gone ...
        Assert.Equal(IntPtr.Zero, borrowed.Handle);

        // ... while the host's copy is still a valid, readable image (pre-fix this read was the AV).
        using (owned)
        {
            Assert.NotEqual(IntPtr.Zero, owned.Handle);
            Assert.Equal(ownedWidth, owned.Width);
            using var readback = new SKBitmap(new SKImageInfo(owned.Width, owned.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            Assert.True(owned.ReadPixels(readback.Info, readback.GetPixels(), readback.RowBytes, 0, 0));
        }
    }

    [Fact]
    public void The_provider_never_returns_another_rectangles_frame()
    {
        using var provider = new IslandBackdropProvider();
        provider.CaptureOverride = rect => rect == RectA ? Frame(90) : null;

        var own = WaitForFrame(provider, RectA, image => image is not null);
        Assert.NotNull(own);

        // RectB has no capture of its own: it must get nothing rather than RectA's desktop.
        Assert.Null(provider.Get(RectB));
    }

    [Fact]
    public void A_retired_frame_is_disposed_within_the_retire_window()
    {
        using var provider = new IslandBackdropProvider();
        provider.CaptureOverride = _ => Frame(40);
        var first = WaitForFrame(provider, RectA, image => image is not null);
        Assert.NotNull(first);
        Assert.NotEqual(IntPtr.Zero, first!.Handle);

        // A newer capture replaces the cached frame: the old one is retired, not reused.
        provider.CaptureOverride = _ => Frame(120);
        var second = WaitForFrame(
            provider, RectA, image => image is not null && !ReferenceEquals(image, first), force: true);
        Assert.NotNull(second);
        Assert.NotSame(first, second);

        // Idle palette polls advance the same counter the retire window is measured in — which is exactly
        // how a host-held borrow ended up freed underneath the renderer.
        for (var i = 0; i < 20; i++)
        {
            provider.Get(RectA);
        }

        Assert.Equal(IntPtr.Zero, first.Handle);
    }
}
