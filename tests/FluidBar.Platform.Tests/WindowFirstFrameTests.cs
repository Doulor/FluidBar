using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Guards the ordering of the first-frame preparation: the window class background must be replaced
/// before the rounded region is applied, and both happen before the window is composited
/// (<c>WM_SHOWWINDOW</c>). A fake <see cref="IWindowFirstFrameOps"/> records the call order so the
/// sequence is asserted without a real compositor.
/// </summary>
public sealed class WindowFirstFrameTests
{
    [Fact]
    public void Prepare_ReplacesTheClassBackgroundBeforeClippingTheRegion()
    {
        var ops = new RecordingOps();
        var preparation = WindowFirstFrame.Prepare(new IntPtr(0x1234), 33, 0xFF0C0C0F, ops);

        Assert.True(preparation.Succeeded, preparation.Error);
        Assert.Equal(["class-background", "window-region"], preparation.Steps);
        Assert.Equal(["class-background", "window-region"], ops.Calls);
        Assert.Equal(new IntPtr(0x1234), ops.LastHandle);
        Assert.Equal(0xFF0C0C0Fu, ops.LastColor);
        Assert.Equal(33, ops.LastRadius);
    }

    [Fact]
    public void Prepare_StopsWhenTheClassBackgroundFails_AndReportsTheReason()
    {
        var ops = new RecordingOps { ClassBackgroundResult = false, ClassBackgroundError = "denied" };
        var preparation = WindowFirstFrame.Prepare(new IntPtr(1), 22, 0xFF101014, ops);

        Assert.False(preparation.Succeeded);
        Assert.Equal("denied", preparation.Error);
        Assert.Equal(["class-background"], ops.Calls);
    }

    [Fact]
    public void Prepare_ReportsTheRegionFailure_AfterTheClassBackgroundSucceeded()
    {
        var ops = new RecordingOps { RegionResult = false, RegionError = "rgn failed" };
        var preparation = WindowFirstFrame.Prepare(new IntPtr(1), 22, 0xFF101014, ops);

        Assert.False(preparation.Succeeded);
        Assert.Equal("rgn failed", preparation.Error);
        Assert.Equal(["class-background", "window-region"], ops.Calls);
    }

    [Fact]
    public void Prepare_RejectsAZeroHandleWithoutTouchingThePlatform()
    {
        var ops = new RecordingOps();
        var preparation = WindowFirstFrame.Prepare(IntPtr.Zero, 22, 0xFF101014, ops);

        Assert.False(preparation.Succeeded);
        Assert.Empty(ops.Calls);
        Assert.Empty(preparation.Steps);
    }

    [Fact]
    public void Prepare_NeverAppliesTheRegionBeforeTheClassBackground_OnAnyCodePath()
    {
        // Regression guard for the reported grey plate: the region alone cannot hide the default erase.
        foreach (var classBackgroundOk in new[] { true, false })
        {
            var ops = new RecordingOps { ClassBackgroundResult = classBackgroundOk };
            _ = WindowFirstFrame.Prepare(new IntPtr(7), 16, 0xFF000000, ops);
            var regionIndex = ops.Calls.IndexOf("window-region");
            if (regionIndex >= 0)
            {
                Assert.True(ops.Calls.IndexOf("class-background") < regionIndex);
            }
        }
    }

    [Fact]
    public async Task Prepare_OnARealWindow_ReplacesTheClassBackgroundAndClipsTheRegion()
    {
        await using var host = new PlatformMessageHost(new CapturingLogger(), new PlatformCounters());
        await host.StartAsync();
        var window = await host.CreateWindowAsync("first-frame-test", PlatformWindowKind.HiddenTopLevel,
            static (_, _, _, _) => null);
        try
        {
            // A hidden test window has no client size yet; give it one so the region has a rect to clip.
            _ = NativeMethods.User32.SetWindowPos(window.Handle, IntPtr.Zero, 0, 0, 320, 200,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            var preparation = WindowFirstFrame.Prepare(window.Handle, 16, 0xFF0C0C0F);
            Assert.True(preparation.Succeeded, preparation.Error);
            Assert.Equal(["class-background", "window-region"], preparation.Steps);

            var brush = NativeMethods.User32.GetClassLongPtr(window.Handle, NativeMethods.User32.GCLP_HBRBACKGROUND);
            Assert.NotEqual(IntPtr.Zero, brush);

            var rgnType = NativeMethods.User32.GetWindowRgnBox(window.Handle, out var box);
            Assert.Equal(3, rgnType); // COMPLEXREGION: the rounded rectangle.
            Assert.True(box.Right > 0 && box.Bottom > 0);
        }
        finally
        {
            await window.DisposeAsync();
            await host.DisposeAsync();
        }
    }

    private sealed class RecordingOps : IWindowFirstFrameOps
    {
        public List<string> Calls { get; } = [];

        public bool ClassBackgroundResult { get; init; } = true;

        public string? ClassBackgroundError { get; init; }

        public bool RegionResult { get; init; } = true;

        public string? RegionError { get; init; }

        public IntPtr LastHandle { get; private set; }

        public uint LastColor { get; private set; }

        public int LastRadius { get; private set; }

        public bool SetClassBackground(IntPtr hwnd, uint argb, out string? failure)
        {
            Calls.Add("class-background");
            LastHandle = hwnd;
            LastColor = argb;
            failure = ClassBackgroundError;
            return ClassBackgroundResult;
        }

        public bool SetWindowRegion(IntPtr hwnd, int radiusPx, out string? failure)
        {
            Calls.Add("window-region");
            LastHandle = hwnd;
            LastRadius = radiusPx;
            failure = RegionError;
            return RegionResult;
        }
    }
}
