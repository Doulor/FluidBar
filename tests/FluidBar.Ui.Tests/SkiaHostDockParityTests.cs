using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Dock placement parity between Core's authoritative <see cref="HostSurface.Dock"/> geometry, the
/// Skia renderer's host-local rectangles and the layered host's window origin. Headless: no HWND is
/// created, so these run on every machine and pin the "no 386 DIP dock shift" contract.
/// </summary>
public sealed class SkiaHostDockParityTests
{
    private const int PageCount = 6;

    [Fact]
    public void Dock_CoreHostSurface_LocalRects_ArePanel24To308_Strip316To368_Arch376To408()
    {
        var dock = HostSurface.Dock;

        Assert.Equal(new VisualRect(24, 24, 440, 284), dock.ShellEnvelopeLocal);
        Assert.Equal(new VisualRect(24, 316, 440, 52), dock.DockStripLocal);
        Assert.Equal(new VisualRect(68, 376, 352, 34), dock.ArchipelagoStripLocal);
        Assert.Equal(446, dock.Height);
        Assert.Equal(488, dock.Width);
    }

    [Fact]
    public void Dock_RendererState_UsesTheSameLocalRectsAsCore()
    {
        var dock = HostSurface.Dock;

        var expanded = IslandRenderState.Expanded(
            IslandMode.Dock,
            PagerRenderState.At(0, PageCount),
            ExpandedPageContent.Empty,
            IslandTheme.Dark);
        Assert.Equal(dock.ShellLocalOffset, expanded.ShellOrigin);
        Assert.Equal(dock.ShellEnvelopeLocal.Width, expanded.ShellWidth);
        Assert.Equal(dock.ShellEnvelopeLocal.Height, expanded.ShellHeight);

        var compact = IslandRenderState.ForCompact(
            IslandMode.Dock,
            new CompactActivity(ActivityKind.Idle, new CompactModel()),
            IslandTheme.Dark);
        Assert.Equal(new VisualPoint(dock.DockStripLocal.X, dock.DockStripLocal.Y), compact.ShellOrigin);
        Assert.Equal(dock.DockStripLocal.Width, compact.ShellWidth);
        Assert.Equal(dock.DockStripLocal.Height, compact.ShellHeight);

        var stripState = expanded with { Archipelago = ArchipelagoState.Shown(0) };
        using var renderer = new IslandRenderer();
        var strip = renderer.MeasureStrip(stripState);
        Assert.Equal((float)dock.ArchipelagoTopLocal, strip.Strip.Top, 3);
        Assert.Equal((float)dock.ArchipelagoStripLocal.Height, strip.Strip.Height, 3);
        Assert.Equal((float)dock.ArchipelagoStripLocal.CenterX, strip.Strip.MidX, 3);
    }

    [Fact]
    public void Float_RendererStrip_SitsAt316NotBelowTheShellInDockTerms()
    {
        var floatSurface = HostSurface.Float;
        Assert.Equal(new VisualRect(24, 24, 440, 284), floatSurface.ShellEnvelopeLocal);
        Assert.Equal(new VisualRect(68, 316, 352, 34), floatSurface.ArchipelagoStripLocal);

        var state = IslandRenderState.Expanded(
            IslandMode.Float,
            PagerRenderState.At(0, PageCount),
            ExpandedPageContent.Empty,
            IslandTheme.Dark) with
        {
            Archipelago = ArchipelagoState.Shown(0),
        };
        using var renderer = new IslandRenderer();
        var strip = renderer.MeasureStrip(state);

        // Prototype2: rail top = shell bottom + 8 DIP (24 + 284 + 8 = 316), 32 DIP tall.
        Assert.Equal(316f, strip.Strip.Top, 3);
        Assert.Equal(34f, strip.Strip.Height, 3);
        Assert.Equal(284f, strip.Strip.MidX, 3);
    }

    [Theory]
    [InlineData(96u, 1.0, 488, 386, 488, 446)]
    [InlineData(144u, 1.5, 732, 519, 732, 666)]
    [InlineData(192u, 2.0, 976, 768, 976, 812)]
    public void SurfacePixels_CeilAt150And200Percent(
        uint dpi, double scale, int floatWidth, int floatHeight, int dockWidth, int dockHeight)
    {
        _ = dpi;
        var floatPixels = IslandMetrics.SurfacePixels(IslandMode.Float, (float)scale);
        var dockPixels = IslandMetrics.SurfacePixels(IslandMode.Dock, (float)scale);
        var hostFloat = LayeredSurfaceSize.Float.ToPixels(scale);
        var hostDock = LayeredSurfaceSize.Dock.ToPixels(scale);

        Assert.Equal(floatWidth, floatPixels.Width);
        Assert.Equal(floatHeight, floatPixels.Height);
        Assert.Equal(dockWidth, dockPixels.Width);
        Assert.Equal(dockHeight, dockPixels.Height);
        Assert.Equal(floatWidth, hostFloat.Width);
        Assert.Equal(floatHeight, hostFloat.Height);
        Assert.Equal(dockWidth, hostDock.Width);
        Assert.Equal(dockHeight, hostDock.Height);
    }

    [Theory]
    [InlineData(96u, 1.0)]
    [InlineData(144u, 1.5)]
    [InlineData(192u, 2.0)]
    public void DockPlacement_CompactStripAndArchipelago_KeepTheirCoreLocalOffsets(uint dpi, double scale)
    {
        _ = scale;
        var monitor = MonitorInfo.Create(
            "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1040),
            dpi,
            isPrimary: true);
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

        // The compact dock shell *is* the strip and sits at local y=316 inside the window.
        Assert.Equal(placement.DockStrip, placement.Shell);
        Assert.Equal(placement.WindowVisual.Y + HostSurface.Dock.DockStripTopLocal, placement.DockStrip.Y, 3);
        Assert.Equal(placement.WindowVisual.Y + HostSurface.Dock.ArchipelagoTopLocal, placement.ArchipelagoStrip.Y, 3);

        // Core's WindowPixel is the host window origin verbatim (no 386 DIP dock correction).
        var computed = LayeredWindowHost.ComputePlacement(placement, LayeredHostMode.Dock);
        Assert.Equal(placement.WindowPixel.X, computed.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, computed.WindowPixel.Y);
        Assert.Equal((int)Math.Ceiling(488 * scale), computed.WindowPixel.Width);
        Assert.Equal((int)Math.Ceiling(446 * scale), computed.WindowPixel.Height);
    }

    [Theory]
    [InlineData(96u, 716, 592, 488, 446)]
    [InlineData(144u, 594, 368, 732, 666)]
    [InlineData(192u, 472, 144, 976, 812)]
    public void DockPlacement_MeasuredWindowOrigin_ForA1920x1040WorkArea(
        uint dpi, int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var monitor = MonitorInfo.Create(
            "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1040),
            dpi,
            isPrimary: true);
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

        Assert.Equal(expectedX, placement.WindowPixel.X);
        Assert.Equal(expectedY, placement.WindowPixel.Y);
        Assert.Equal(expectedWidth, placement.WindowPixel.Width);
        Assert.Equal(expectedHeight, placement.WindowPixel.Height);

        // The dock strip bottom is always 80 DIP above the work-area bottom (Core contract).
        Assert.Equal(
            monitor.WorkAreaHeightDip - 4 - HostSurface.Dock.Height,
            placement.WindowVisual.Y,
            6);
        Assert.Equal(
            placement.WindowVisual.Y + HostSurface.Dock.DockStripTopLocal + HostSurface.Dock.DockStripHeight,
            placement.DockStrip.Bottom,
            6);
    }

    [Theory]
    [InlineData(96u, 1.0)]
    [InlineData(144u, 1.5)]
    [InlineData(192u, 2.0)]
    public void DockPlacement_ExpandedPanelAndArchipelago_KeepTheirCoreLocalOffsets(uint dpi, double scale)
    {
        _ = scale;
        var monitor = MonitorInfo.Create(
            "\\\\.\\DISPLAY1",
            new PixelRect(0, 0, 1920, 1040),
            dpi,
            isPrimary: true);
        var shell = IslandLayout.MeasureExpanded(
            new IslandLayoutRequest
            {
                Form = ShellForm.Expanded,
                Mode = IslandMode.Dock,
                Anchor = IslandAnchor.BottomCenter,
            },
            monitor.WorkAreaDip,
            ExpandDirection.Up);
        var placement = WindowPlacementConverter.PlaceWindow(
            shell, monitor, HostSurface.Dock, ShellForm.Expanded, IslandAnchor.BottomCenter);

        Assert.Equal(placement.WindowVisual.Y + HostSurface.Dock.ShellLocalOffset.Y, placement.Shell.Y, 3);
        Assert.Equal(placement.WindowVisual.Y + HostSurface.Dock.ArchipelagoTopLocal, placement.ArchipelagoStrip.Y, 3);

        var computed = LayeredWindowHost.ComputePlacement(placement, LayeredHostMode.Dock);
        Assert.Equal(placement.WindowPixel.X, computed.WindowPixel.X);
        Assert.Equal(placement.WindowPixel.Y, computed.WindowPixel.Y);
    }
}





