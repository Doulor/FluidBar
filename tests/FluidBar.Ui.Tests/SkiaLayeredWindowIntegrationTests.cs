using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;
using SkiaSharp;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guarded real-HWND integration test for the Skia island host path. It is skipped unless
/// <c>FLUIDBAR_HWND_TESTS=1</c> (it needs an interactive Windows desktop and creates a real layered
/// window on a dedicated STA thread). It proves the exact production frame path: Skia renders into
/// the host DIB, the commit succeeds, the island pixels are opaque and the shadow bleed stays
/// transparent (per-pixel alpha), plus Core placement parity for the float surface.
/// </summary>
public sealed class SkiaLayeredWindowIntegrationTests
{
    [Fact]
    public void LayeredWindow_RendersTransparentIsland_AndCommits()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_HWND_TESTS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Exception? failure = null;
        using var completed = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try
            {
                RunLayeredFrame();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            Name = "FluidBar.SkiaHwndTest",
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)), "layered host test thread timed out");
        Assert.Null(failure);
    }

    private static void RunLayeredFrame()
    {
        var monitor = FluidBar.Platform.Windows.Display.MonitorInfo.Create(
            "\\\\.\\DISPLAY_TEST",
            index: 0,
            bounds: new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1080),
            workArea: new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1040),
            dpi: 96,
            isPrimary: true);

        using var host = new LayeredWindowHost(new LayeredHostOptions
        {
            Pacing = LayeredPacingMode.None,
            ReassertTopmost = false,
        });
        host.Create(monitor, LayeredHostMode.Float);
        host.Show();

        Assert.Equal(LayeredSurfaceSize.Width, host.SurfaceSizeDip.WidthDip, 1);
        Assert.Equal(LayeredSurfaceSize.FloatHeight, host.SurfaceSizeDip.HeightDip, 1);
        Assert.Equal(488, host.SurfaceSizePx.Width);
        Assert.Equal(384, host.SurfaceSizePx.Height);

        // Core placement parity for the float surface (the production placement seam).
        var coreMonitor = FluidBar.Core.Layout.MonitorInfo.Create(
            "test",
            new FluidBar.Core.Layout.PixelRect(0, 0, 1920, 1040),
            dpi: 96,
            isPrimary: true);
        var placement = WindowPlacementConverter.PlaceWindow(
            new VisualRect(188, 12, 132, 36),
            coreMonitor,
            HostSurface.Float,
            ShellForm.Compact,
            IslandAnchor.TopCenter);
        host.PlaceForWindowPlacement(placement, LayeredHostMode.Float);
        Assert.Equal(placement.WindowPixel.X, host.Placement.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, host.Placement.WindowPixel.Y);

        using var renderer = new IslandRenderer();
        var state = IslandRenderState.ForCompact(
            IslandMode.Float,
            new CompactActivity(ActivityKind.Idle, new CompactModel { ClockText = "19:16", DateText = "Sun, Oct 18" }),
            IslandTheme.Dark);

        var frame = host.BeginFrame();
        var info = new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surface = SKSurface.Create(info, frame.Bits, frame.Stride))
        {
            Assert.NotNull(surface);
            renderer.Render(surface!.Canvas, state, (float)frame.Scale);
            surface.Canvas.Flush();
        }

        var full = host.CommitFrame();
        Assert.True(full.Updated, $"full-frame commit failed (Win32 error {full.Win32Error})");
        Assert.True(full.WasFullFrame);

        var dib = host.Surface!;
        var shell = IslandMetrics.ShellRect(state);
        var centerX = (int)(shell.MidX * frame.Scale);
        var centerY = (int)(shell.MidY * frame.Scale);
        var center = dib.GetPixel(centerX, centerY);
        Assert.True((center >> 24) > 200, $"island centre alpha was {center >> 24}");
        Assert.Equal(0u, dib.GetPixel(0, 0) >> 24);
        Assert.Equal(0u, dib.GetPixel(dib.WidthPx - 1, dib.HeightPx - 1) >> 24);

        // Dirty-rect commit (progress/lyrics path) must also succeed without a full upload.
        host.BeginFrame();
        var dirty = host.CommitFrame(new LayeredRect(centerX - 4, centerY - 4, 8, 8));
        Assert.True(dirty.Updated, $"dirty commit failed (Win32 error {dirty.Win32Error})");
        Assert.False(dirty.WasFullFrame);

        host.Hide();
        host.Dispose();

        RunDockFrame(monitor, renderer);
    }

    private static void RunDockFrame(
        FluidBar.Platform.Windows.Display.MonitorInfo monitor,
        IslandRenderer renderer)
    {
        using var dock = new LayeredWindowHost(new LayeredHostOptions
        {
            Pacing = LayeredPacingMode.None,
            ReassertTopmost = false,
        });
        dock.Create(monitor, LayeredHostMode.Dock);
        dock.Show();
        Assert.Equal(LayeredSurfaceSize.Width, dock.SurfaceSizeDip.WidthDip, 1);
        Assert.Equal(LayeredSurfaceSize.DockHeight, dock.SurfaceSizeDip.HeightDip, 1);
        Assert.Equal(488, dock.SurfaceSizePx.Width);
        Assert.Equal(406, dock.SurfaceSizePx.Height);

        var coreMonitor = FluidBar.Core.Layout.MonitorInfo.Create(
            "test",
            new FluidBar.Core.Layout.PixelRect(0, 0, 1920, 1040),
            dpi: 96,
            isPrimary: true);
        var placement = WindowPlacementConverter.PlaceWindow(
            new VisualRect(24, 936, 440, 52),
            coreMonitor,
            HostSurface.Dock,
            ShellForm.Compact,
            IslandAnchor.BottomCenter);
        // Core's WindowPlacement is authoritative for dock too: no PlacePhysical workaround.
        dock.PlaceForWindowPlacement(placement, LayeredHostMode.Dock);
        Assert.Equal(placement.WindowPixel.X, dock.Placement.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, dock.Placement.WindowPixel.Y);
        Assert.Equal(488, dock.Placement.SurfaceSizePx.Width);
        Assert.Equal(406, dock.Placement.SurfaceSizePx.Height);

        var state = IslandRenderState.ForCompact(
            IslandMode.Dock,
            new CompactActivity(ActivityKind.Idle, new CompactModel { ClockText = "19:16", DateText = "Sun, Oct 18" }),
            IslandTheme.Dark);

        var frame = dock.BeginFrame();
        var info = new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surface = SKSurface.Create(info, frame.Bits, frame.Stride))
        {
            Assert.NotNull(surface);
            renderer.Render(surface!.Canvas, state, (float)frame.Scale);
            surface.Canvas.Flush();
        }

        var commit = dock.CommitFrame();
        Assert.True(commit.Updated, $"dock commit failed (Win32 error {commit.Win32Error})");

        var dib = dock.Surface!;
        var shell = IslandMetrics.ShellRect(state);
        var stripCenter = dib.GetPixel((int)(shell.MidX * frame.Scale), (int)(shell.MidY * frame.Scale));
        Assert.True((stripCenter >> 24) > 200, $"dock strip alpha was {stripCenter >> 24}");
        Assert.Equal(0u, dib.GetPixel(0, 0) >> 24);
        dock.Hide();
    }
}


