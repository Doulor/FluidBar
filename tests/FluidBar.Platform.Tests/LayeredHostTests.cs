using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Pure tests for the layered-host geometry, hit region, DIB surface and pacing math. No window is
/// created here: the HWND/DIB integration is covered by
/// <see cref="PlatformIntegrationTests"/> on a real desktop.
/// </summary>
public sealed class LayeredHostTests
{
    [Fact]
    public void SurfaceSize_ForMode_ReturnsTheFixedPerModeSurfaces()
    {
        var floatSize = LayeredSurfaceSize.ForMode(LayeredHostMode.Float);
        var dockSize = LayeredSurfaceSize.ForMode(LayeredHostMode.Dock);

        Assert.Equal(488, floatSize.WidthDip);
        Assert.Equal(384, floatSize.HeightDip);
        Assert.Equal(488, dockSize.WidthDip);
        Assert.Equal(444, dockSize.HeightDip);

        // The surface must be wide enough for the 440 DIP shell plus 24 DIP bleed on both sides.
        Assert.Equal(LayeredSurfaceSize.PanelWidth + (2 * LayeredSurfaceSize.Padding), floatSize.WidthDip);

        // Float: 24 bleed + 284 panel + 10 gap + 30 archipelago + 36 bottom bleed (Core composition).
        Assert.Equal(
            LayeredSurfaceSize.Padding + LayeredSurfaceSize.PanelHeight + LayeredSurfaceSize.ArchipelagoGap
                + LayeredSurfaceSize.ArchipelagoStripHeight + LayeredSurfaceSize.BottomPadding,
            floatSize.HeightDip);
        Assert.Equal(LayeredSurfaceSize.DetachedLaneHeight,
            LayeredSurfaceSize.ArchipelagoGap + LayeredSurfaceSize.ArchipelagoStripHeight + LayeredSurfaceSize.BottomPadding);

        // Dock: 24 bleed + 284 panel + 8 gap + 52 strip + 10 gap + 30 archipelago + 36 bottom bleed.
        Assert.Equal(
            LayeredSurfaceSize.Padding + LayeredSurfaceSize.PanelHeight + LayeredSurfaceSize.DockGap
                + LayeredSurfaceSize.DockStripHeight + LayeredSurfaceSize.ArchipelagoGap
                + LayeredSurfaceSize.ArchipelagoStripHeight + LayeredSurfaceSize.BottomPadding,
            dockSize.HeightDip);
        Assert.Equal(dockSize.HeightDip - LayeredSurfaceSize.Padding - LayeredSurfaceSize.PanelHeight,
            LayeredSurfaceSize.DockBottomAllowance);
    }

    [Theory]
    [InlineData(1.0, 488, 384)]
    [InlineData(1.25, 610, 480)]
    [InlineData(1.5, 732, 576)]
    [InlineData(2.0, 976, 768)]
    public void SurfaceSize_ToPixels_CeilsAtTheGivenScale(double scale, int expectedWidth, int expectedHeight)
    {
        var pixels = LayeredSurfaceSize.Float.ToPixels(scale);

        Assert.Equal(expectedWidth, pixels.Width);
        Assert.Equal(expectedHeight, pixels.Height);
    }

    [Theory]
    [InlineData(1.0, 488, 444)]
    [InlineData(1.25, 610, 555)]
    [InlineData(1.5, 732, 666)]
    [InlineData(2.0, 976, 888)]
    public void SurfaceSize_DockToPixels_UsesTheCeilRule(double scale, int expectedWidth, int expectedHeight)
    {
        var pixels = LayeredSurfaceSize.Dock.ToPixels(scale);

        Assert.Equal(expectedWidth, pixels.Width);
        Assert.Equal(expectedHeight, pixels.Height);
        Assert.True(LayeredSurfaceSize.Dock.ToPixels(1.5).Height > LayeredSurfaceSize.Float.ToPixels(1.5).Height);
    }

    [Theory]
    [InlineData(1.1, 537, 423)] // 488*1.1=536.8, 384*1.1=422.4: both must ceil
    [InlineData(1.75, 854, 672)] // 488*1.75=854, 384*1.75=672
    public void SurfaceSize_ToPixels_CeilsFractionalPixels(double scale, int expectedWidth, int expectedHeight)
    {
        var pixels = LayeredSurfaceSize.Float.ToPixels(scale);

        Assert.Equal(expectedWidth, pixels.Width);
        Assert.Equal(expectedHeight, pixels.Height);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.Width * scale), pixels.Width);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.FloatHeight * scale), pixels.Height);
    }

    [Fact]
    public void SurfaceSize_ShellLocalOffset_IsPanelTopForAllModes()
    {
        Assert.Equal(new VisualPoint(24, 24), LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Float));

        // Core dock composition keeps the panel at the top (24..308); the dock strip lives below
        // it (316..368) and the archipelago strip below that (378..408). The host must not shift
        // the window for dock mode; Core's WindowPixel is authoritative.
        var dockOffset = LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Dock);
        Assert.Equal(24, dockOffset.X);
        Assert.Equal(24, dockOffset.Y);
        Assert.Equal(444, LayeredSurfaceSize.Dock.HeightDip);
    }

    [Fact]
    public void SurfaceSize_FormAwareOffset_AnchorsDockCompactOnTheStrip()
    {
        Assert.Equal(new VisualPoint(24, 316),
            LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Dock, ShellForm.Compact));
        Assert.Equal(new VisualPoint(24, 316),
            LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Dock, ShellForm.Peek));
        Assert.Equal(new VisualPoint(24, 24),
            LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Dock, ShellForm.Expanded));
        Assert.Equal(new VisualPoint(24, 24),
            LayeredSurfaceSize.ShellLocalOffset(LayeredHostMode.Float, ShellForm.Compact));

        // Local rects the renderer paints in dock mode: panel 24..308, strip 316..368, arch 378..408.
        Assert.Equal(24, LayeredSurfaceSize.Padding);
        Assert.Equal(308, LayeredSurfaceSize.Padding + LayeredSurfaceSize.PanelHeight);
        Assert.Equal(316, LayeredSurfaceSize.DockStripTop);
        Assert.Equal(368, LayeredSurfaceSize.DockStripTop + LayeredSurfaceSize.DockStripHeight);
        Assert.Equal(378, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Dock));
        Assert.Equal(408, LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Dock) + LayeredSurfaceSize.ArchipelagoStripHeight);
    }

    [Fact]
    public void SurfaceSize_MatchesCoreHostSurface_ForBothModes()
    {
        var floatSurface = HostSurface.Float;
        var dockSurface = HostSurface.Dock;

        Assert.Equal(LayeredSurfaceSize.Width, floatSurface.Width);
        Assert.Equal(LayeredSurfaceSize.FloatHeight, floatSurface.Height);
        Assert.Equal(LayeredSurfaceSize.Width, dockSurface.Width);
        Assert.Equal(LayeredSurfaceSize.DockHeight, dockSurface.Height);

        Assert.Equal(LayeredSurfaceSize.Padding, dockSurface.ShellLocalOffset.X);
        Assert.Equal(LayeredSurfaceSize.Padding, dockSurface.ShellLocalOffset.Y);
        Assert.Equal(LayeredSurfaceSize.PanelHeight, dockSurface.ShellEnvelopeLocal.Height);
        Assert.Equal(LayeredSurfaceSize.DockStripTop, dockSurface.DockStripTopLocal);
        Assert.Equal(LayeredSurfaceSize.DockStripHeight, dockSurface.DockStripLocal.Height);
        Assert.Equal(LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Dock), dockSurface.ArchipelagoTopLocal);
        Assert.Equal(LayeredSurfaceSize.ArchipelagoStripHeight, dockSurface.ArchipelagoStripLocal.Height);
        Assert.Equal(LayeredSurfaceSize.ArchipelagoTop(LayeredHostMode.Float), floatSurface.ArchipelagoTopLocal);
    }

    [Theory]
    [InlineData(96u, 1.0, 24, 316)]
    [InlineData(144u, 1.5, 36, 474)]
    [InlineData(192u, 2.0, 48, 632)]
    public void ComputePlacement_Dock_UsesCoreWindowPixel_AndCeilSurface(
        uint dpi, double scale, int expectedOffsetX, int expectedOffsetY)
    {
        var monitor = FluidBar.Core.Layout.MonitorInfo.Create(
            "\\\\.\\DISPLAY1",
            new PixelRect(1920, 0, 2560, 1400),
            dpi,
            new PixelRect(1920, 0, 2560, 1440),
            isPrimary: false);
        var shell = IslandLayout.MeasureCompact(
            new IslandLayoutRequest
            {
                Form = ShellForm.Compact,
                Mode = IslandMode.Dock,
                Anchor = IslandAnchor.BottomCenter,
            },
            monitor.WorkAreaDip);
        var placement = WindowPlacementConverter.PlaceWindow(
            shell, monitor, HostSurface.Dock, ShellForm.Compact, IslandAnchor.BottomCenter);

        var computed = LayeredWindowHost.ComputePlacement(placement, LayeredHostMode.Dock);

        // No 344 DIP dock shift: Core's WindowPixel is the host window origin verbatim.
        Assert.Equal(placement.WindowPixel.X, computed.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, computed.WindowPixel.Y);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.Width * scale), computed.SurfaceSizePx.Width);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.DockHeight * scale), computed.SurfaceSizePx.Height);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.Width * scale), computed.WindowPixel.Width);
        Assert.Equal((int)Math.Ceiling(LayeredSurfaceSize.DockHeight * scale), computed.WindowPixel.Height);
        Assert.Equal(scale, computed.Scale, 4);
        Assert.Equal(expectedOffsetX, computed.ShellLocalOffsetPx.Width);
        Assert.Equal(expectedOffsetY, computed.ShellLocalOffsetPx.Height);
    }

    [Fact]
    public void ComputePlacement_Float_UsesCoreWindowPixel()
    {
        var monitor = FluidBar.Core.Layout.MonitorInfo.Create(
            "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 2560, 1400),
            dpi: 144,
            isPrimary: true);
        var shell = IslandLayout.MeasureCompact(
            new IslandLayoutRequest { Form = ShellForm.Compact, Mode = IslandMode.Float, Anchor = IslandAnchor.TopCenter },
            monitor.WorkAreaDip);
        var placement = WindowPlacementConverter.PlaceWindow(
            shell, monitor, HostSurface.Float, ShellForm.Compact, IslandAnchor.TopCenter);

        var computed = LayeredWindowHost.ComputePlacement(placement, LayeredHostMode.Float);

        Assert.Equal(placement.WindowPixel.X, computed.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, computed.WindowPixel.Y);
        Assert.Equal(732, computed.SurfaceSizePx.Width);
        Assert.Equal(576, computed.SurfaceSizePx.Height);
        Assert.Equal(
            (int)Math.Round(placement.ShellLocalOffset.X * 1.5, MidpointRounding.AwayFromZero),
            computed.ShellLocalOffsetPx.Width);
        Assert.Equal(36, computed.ShellLocalOffsetPx.Height);
    }

    [Fact]
    public void Rect_UnionAndIntersectAndClamp_Behave()
    {
        var a = new LayeredRect(10, 20, 30, 40);
        var b = new LayeredRect(60, 20, 30, 40);

        var union = a.Union(b);
        Assert.Equal(new LayeredRect(10, 20, 80, 40), union);

        Assert.True(a.Intersect(b).IsEmpty);
        Assert.Equal(new LayeredRect(20, 20, 20, 40), a.Intersect(new LayeredRect(20, 0, 100, 100)));

        var clamped = new LayeredRect(-10, -10, 100, 100).ClampTo(50, 50);
        Assert.Equal(new LayeredRect(0, 0, 50, 50), clamped);
        Assert.True(clamped.Covers(50, 50));
        Assert.False(new LayeredRect(0, 0, 49, 50).Covers(50, 50));
    }

    [Fact]
    public void Rect_FromDip_RoundsTheOriginAndCeilsTheSize()
    {
        var rect = LayeredRect.FromDip(new VisualRect(24.4, 24.6, 440, 284), 1.5);

        Assert.Equal(37, rect.X); // round(36.6)
        Assert.Equal(37, rect.Y); // round(36.9)
        Assert.Equal(660, rect.Width);
        Assert.Equal(426, rect.Height);
        Assert.Equal(rect.X + rect.Width, rect.Right);
    }

    [Fact]
    public void HitRegion_ShellSquircleIsInteractiveButCornersAreNot()
    {
        var region = new LayeredHitRegion();
        region.SetShell(new VisualRect(24, 24, 440, 284), CornerRadii.Uniform(28));

        Assert.True(region.IsInteractive(244, 166)); // panel centre
        Assert.True(region.IsInteractive(52, 52));   // inside the top-left radius centre
        Assert.False(region.IsInteractive(24.5, 24.5)); // squircle corner cut-out
        Assert.False(region.IsInteractive(10, 10));     // outside the shell rectangle
        Assert.False(region.IsInteractive(244, 380));   // detached-island lane (no pill here)
    }

    [Fact]
    public void HitRegion_DetachedPillsAreInteractiveButTheGapsAreNot()
    {
        var region = new LayeredHitRegion();
        region.SetShell(new VisualRect(24, 24, 440, 284), CornerRadii.Uniform(28));
        region.SetInteractiveRects(
        [
            new VisualRect(24, 316, 140, 26),
            new VisualRect(174, 316, 140, 26),
        ]);

        Assert.True(region.IsInteractive(94, 329));   // first pill
        Assert.True(region.IsInteractive(244, 329));  // second pill
        Assert.False(region.IsInteractive(169, 329)); // 10 DIP gap between them
        Assert.False(region.IsInteractive(94, 350));  // below the pills (second row is empty)
        Assert.Equal(2, region.InteractiveRectCount);
    }

    [Fact]
    public void HitRegion_PixelQueriesUseTheScale()
    {
        var region = new LayeredHitRegion();
        region.SetShell(new VisualRect(24, 24, 440, 284), CornerRadii.Uniform(28));

        Assert.True(region.IsInteractivePixels(366, 249, 1.5));   // (244,166) DIP
        Assert.False(region.IsInteractivePixels(6, 6, 1.5));      // (4,4) DIP corner
        Assert.False(region.IsInteractivePixels(366, 249, 0));    // invalid scale
    }

    [Fact]
    public void HitRegion_HasInteractiveContentFalseMakesEverythingClickThrough()
    {
        var region = new LayeredHitRegion();
        region.SetShell(new VisualRect(24, 24, 440, 284), CornerRadii.Uniform(28));
        region.HasInteractiveContent = false;

        Assert.False(region.IsInteractive(244, 166));
    }

    [Fact]
    public void HitRegion_RejectsMoreThanTheFixedCapacity()
    {
        var region = new LayeredHitRegion();
        var rects = Enumerable.Range(0, LayeredHitRegion.MaxInteractiveRects + 1)
            .Select(i => new VisualRect(i, 0, 1, 1))
            .ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => region.SetInteractiveRects(rects));
    }

    [Fact]
    public void HitRegion_EmptyShellAndEmptyRectsAreClickThrough()
    {
        var region = new LayeredHitRegion();
        Assert.True(region.IsEmpty);

        region.SetShell(VisualRect.Empty, CornerRadii.Uniform(12));
        region.SetInteractiveRects([VisualRect.Empty]);

        Assert.True(region.IsEmpty);
        Assert.False(region.IsInteractive(0, 0));
    }

    [Fact]
    public void FramePacer_ComputesTheDeadlinePeriod()
    {
        using var sixty = new LayeredFramePacer(LayeredPacingMode.TargetFrameRate, 60);
        using var hundredTwenty = new LayeredFramePacer(LayeredPacingMode.TargetFrameRate, 120);
        using var composition = new LayeredFramePacer(LayeredPacingMode.Composition);

        Assert.Equal(60, sixty.TargetFramesPerSecond);
        Assert.Equal(16.67, sixty.FramePeriodMilliseconds, 1);
        Assert.Equal(8.33, hundredTwenty.FramePeriodMilliseconds, 1);
        Assert.Equal(0, composition.FramePeriodMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void FramePacer_RejectsImpossibleTargetRates(double fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayeredFramePacer(LayeredPacingMode.TargetFrameRate, fps));
    }

    [Fact]
    public void FramePacer_NoneModeNeverWaits()
    {
        using var pacer = new LayeredFramePacer(LayeredPacingMode.None);

        var started = TimeProvider.System.GetTimestamp();
        for (var i = 0; i < 100; i++)
        {
            pacer.WaitForNextFrame();
        }

        Assert.Equal(0, pacer.GetStats().Frames);
        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CommitResult_NoneIsNotUpdated()
    {
        Assert.False(LayeredCommitResult.None.Updated);
        Assert.Equal(0, LayeredCommitResult.None.UploadedRectCount);
        Assert.Equal(0, LayeredCommitResult.None.Win32Error);
    }

    [Fact]
    public async Task Host_IsThreadAffine()
    {
        using var host = new LayeredWindowHost();
        var monitor = new FluidBar.Platform.Windows.Display.MonitorInfo("test", 0,
            new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1080),
            new FluidBar.Platform.Windows.Display.PixelRect(0, 0, 1920, 1040), 96, 96, true);

        var exception = await Record.ExceptionAsync(() => Task.Run(() => host.Create(monitor)));

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("thread-affine", exception!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Surface_IsTopDownPremultipliedBgraAndReusable()
    {
        using var surface = LayeredSurface.Create(16, 8);

        Assert.Equal(16, surface.WidthPx);
        Assert.Equal(8, surface.HeightPx);
        Assert.Equal(64, surface.Stride);
        Assert.Equal(512, surface.ByteCount);
        Assert.NotEqual(IntPtr.Zero, surface.Bits);
        Assert.NotEqual(IntPtr.Zero, surface.Hdc);

        surface.Clear();
        Assert.Equal(0u, surface.GetPixel(0, 0));
        Assert.Equal(0u, surface.GetPixel(15, 7));

        // Row 0 is the top row: writing (3,0) must not appear at (3,7).
        surface.SetPixel(3, 0, 0xFF336699);
        Assert.Equal(0xFF336699u, surface.GetPixel(3, 0));
        Assert.Equal(0u, surface.GetPixel(3, 7));

        var row = new byte[surface.Stride];
        row[0] = 0x11;
        row[1] = 0x22;
        row[2] = 0x33;
        row[3] = 0x44;
        surface.CopyRow(1, row);
        Assert.Equal(0x44332211u, surface.GetPixel(0, 1));

        var originalHandle = surface.DibHandle;
        surface.Recreate(32, 16);
        Assert.Equal(32, surface.WidthPx);
        Assert.Equal(128, surface.Stride);
        Assert.NotEqual(originalHandle, surface.DibHandle);
    }
}
