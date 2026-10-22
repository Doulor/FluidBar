using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Tests;

/// <summary>Foreground fullscreen detection: rectangle coverage must be tolerant of one-pixel DWM rounding.</summary>
public sealed class FullscreenDetectorTests
{
    private static NativeMethods.RECT Monitor => new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

    [Fact]
    public void WindowCoveringTheMonitor_IsFullscreen()
    {
        var window = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        Assert.True(FullscreenDetector.CoversMonitor(window, Monitor));
    }

    [Fact]
    public void WindowOffByOnePixel_IsStillFullscreen()
    {
        // DWM and borderless maximized windows commonly round by a pixel.
        var window = new NativeMethods.RECT { Left = -1, Top = 0, Right = 1920, Bottom = 1081 };
        Assert.True(FullscreenDetector.CoversMonitor(window, Monitor));
    }

    [Fact]
    public void WindowLeavingTaskbarVisible_IsNotFullscreen()
    {
        var window = new NativeMethods.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        Assert.False(FullscreenDetector.CoversMonitor(window, Monitor));
    }

    [Fact]
    public void WindowOnAnotherMonitor_IsNotFullscreen()
    {
        var window = new NativeMethods.RECT { Left = 1920, Top = 0, Right = 3840, Bottom = 1080 };
        Assert.False(FullscreenDetector.CoversMonitor(window, Monitor));
    }

    [Fact]
    public void NormalMaximizedWindowStyle_IsNotFullscreen()
    {
        // WS_CAPTION | WS_THICKFRAME | WS_MAXIMIZE (a regular maximized desktop window).
        const long style = 0x00C00000L | 0x00040000L | 0x01000000L;
        Assert.False(FullscreenDetector.IsFullscreenStyle(style));
    }

    [Fact]
    public void PopupWindowStyle_IsFullscreen()
    {
        Assert.True(FullscreenDetector.IsFullscreenStyle(0x80000000L));
    }

    [Fact]
    public void CaptionlessWindowStyle_IsFullscreen()
    {
        Assert.True(FullscreenDetector.IsFullscreenStyle(0x00000000L));
    }
}
