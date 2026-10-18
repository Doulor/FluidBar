using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Policies;

namespace FluidBar.Platform.Tests;

public sealed class DisplayMetricsTests
{
    private static MonitorInfo Monitor(int dpi = 96, int width = 1920, int height = 1080, int workHeight = 1040)
        => new("\\\\.\\DISPLAY1", 0,
            new PixelRect(0, 0, width, height),
            new PixelRect(0, 0, width, workHeight),
            (uint)dpi, (uint)dpi, true);

    [Theory]
    [InlineData(96, 0.0, 0.0, 132.0, 36.0, 0, 0, 132, 36)]
    [InlineData(120, 0.0, 0.0, 132.0, 36.0, 0, 0, 165, 45)]
    [InlineData(144, 10.0, 20.0, 100.0, 40.0, 15, 30, 150, 60)]
    [InlineData(192, 2.5, 4.5, 33.0, 17.0, 5, 9, 66, 34)]
    public void ToPhysical_ConvertsDipWorkAreaRelative(int dpi, double x, double y, double w, double h,
        int expectedX, int expectedY, int expectedW, int expectedH)
    {
        var monitor = Monitor(dpi);
        var rect = DisplayMetrics.ToPhysical(new DipRect(x, y, w, h), monitor);
        Assert.Equal(new PixelRect(expectedX, expectedY, expectedW, expectedH), rect);
    }

    [Fact]
    public void ToDips_RoundTripsApproximately()
    {
        var monitor = Monitor(144);
        var dip = new DipRect(12, 24, 440, 284);
        var physical = DisplayMetrics.ToPhysical(dip, monitor);
        var back = DisplayMetrics.ToDips(physical, monitor);
        Assert.InRange(back.X, 11.9, 12.1);
        Assert.InRange(back.Y, 23.9, 24.1);
        Assert.InRange(back.Width, 439.5, 440.5);
        Assert.InRange(back.Height, 283.5, 284.5);
    }

    [Fact]
    public void ToPhysical_UsesWorkAreaOrigin()
    {
        var monitor = new MonitorInfo("m", 0, new PixelRect(1920, 0, 1920, 1080),
            new PixelRect(1920, 0, 1920, 1040), 96, 96, false);
        var rect = DisplayMetrics.ToPhysical(new DipRect(0, 0, 10, 10), monitor);
        Assert.Equal(1920, rect.X);
        Assert.Equal(0, rect.Y);
    }

    [Theory]
    [InlineData(IslandAnchor.TopCenter, 96, 894, 0)]
    [InlineData(IslandAnchor.TopLeft, 96, 0, 0)]
    [InlineData(IslandAnchor.TopRight, 96, 1788, 0)]
    [InlineData(IslandAnchor.BottomCenter, 96, 894, 1004)]
    [InlineData(IslandAnchor.BottomLeft, 96, 0, 1004)]
    [InlineData(IslandAnchor.BottomRight, 96, 1788, 1004)]
    public void PlaceAtAnchor_AnchorsInsideWorkArea(IslandAnchor anchor, uint dpi, int expectedX, int expectedY)
    {
        var monitor = Monitor((int)dpi);
        var rect = DisplayMetrics.PlaceAtAnchor(monitor, anchor, 132, 36);
        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(132, rect.Width);
        Assert.Equal(36, rect.Height);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void PlaceAtAnchor_AllAnchorsStayInsideWorkArea(int dpi)
    {
        var monitor = Monitor(dpi);
        foreach (var anchor in Enum.GetValues<IslandAnchor>())
        {
            var rect = DisplayMetrics.PlaceAtAnchor(monitor, anchor, 488, 332);
            Assert.True(rect.X >= monitor.WorkArea.X, $"{anchor}: x {rect.X}");
            Assert.True(rect.Y >= monitor.WorkArea.Y, $"{anchor}: y {rect.Y}");
            Assert.True(rect.Right <= monitor.WorkArea.Right, $"{anchor}: right {rect.Right}");
            Assert.True(rect.Bottom <= monitor.WorkArea.Bottom, $"{anchor}: bottom {rect.Bottom}");
        }
    }

    [Fact]
    public void ClampToWorkArea_ShrinksOversizeAndKeepsOriginInside()
    {
        var monitor = Monitor();
        var clamped = DisplayMetrics.ClampToWorkArea(new PixelRect(-500, -500, 4000, 3000), monitor);
        Assert.Equal(monitor.WorkArea, clamped);
    }

    [Fact]
    public void ClampToWorkArea_NegativeOriginRemainsForWindowBleedOffset()
    {
        // The padded host may sit at -24 DIP (negative) — clamping only applies to work-area placement.
        var monitor = Monitor();
        var rect = new PixelRect(-24, -24, 488, 332);
        var clamped = DisplayMetrics.ClampToWorkArea(rect, monitor);
        Assert.True(clamped.X >= monitor.WorkArea.X);
        Assert.True(clamped.Y >= monitor.WorkArea.Y);
    }

    [Fact]
    public void ReanchorForDpiChange_RecomputesFromAnchorInsteadOfScalingOldRect()
    {
        var monitor100 = Monitor(96);
        var monitor150 = Monitor(144);
        var at100 = DisplayMetrics.PlaceAtAnchor(monitor100, IslandAnchor.TopCenter, 440, 284);
        var dip = DisplayMetrics.ToDips(at100, monitor100);

        var reanchored = DisplayMetrics.ReanchorForDpiChange(monitor150, IslandAnchor.TopCenter, dip.Width, dip.Height);
        Assert.InRange(reanchored.Width, 439.5, 440.5);
        var physical = DisplayMetrics.ToPhysical(reanchored, monitor150);
        Assert.Equal(660, physical.Width);
        Assert.Equal(426, physical.Height);
    }

    [Theory]
    [InlineData(96u, 96u)]
    [InlineData(120u, 144u)]
    [InlineData(192u, 192u)]
    public void DecodeDpiChangedWParam_DecodesLowAndHighWords(uint dpiX, uint dpiY)
    {
        var wParam = new IntPtr((long)(dpiX | (dpiY << 16)));
        var (decodedX, decodedY) = DisplayMetrics.DecodeDpiChangedWParam(wParam);
        Assert.Equal(dpiX, decodedX);
        Assert.Equal(dpiY, decodedY);
    }

    [Fact]
    public void DecodeDpiChangedWParam_ZeroFallsBackTo96()
    {
        var (dpiX, dpiY) = DisplayMetrics.DecodeDpiChangedWParam(IntPtr.Zero);
        Assert.Equal(96u, dpiX);
        Assert.Equal(96u, dpiY);
    }

    [Fact]
    public void SelectMonitor_ChoosesLargestIntersection()
    {
        var set = new MonitorSet(System.Collections.Immutable.ImmutableArray.Create(
            new MonitorInfo("left", 0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, 96, false),
            new MonitorInfo("right", 1, new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1040), 96, 96, true)));

        var selected = DisplayMetrics.SelectMonitor(set, new PixelRect(1800, 0, 400, 100));
        Assert.Equal("right", selected.Id);
    }

    [Fact]
    public void SelectMonitor_FallsBackToPrimaryWhenNoIntersection()
    {
        var set = new MonitorSet(System.Collections.Immutable.ImmutableArray.Create(
            new MonitorInfo("a", 0, new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 90), 96, 96, false),
            new MonitorInfo("b", 1, new PixelRect(500, 500, 100, 100), new PixelRect(500, 500, 100, 90), 96, 96, true)));
        var selected = DisplayMetrics.SelectMonitor(set, new PixelRect(10_000, 10_000, 10, 10));
        Assert.Equal("b", selected.Id);
    }

    [Fact]
    public void MonitorAtPoint_ReturnsContainingMonitor()
    {
        var set = new MonitorSet(System.Collections.Immutable.ImmutableArray.Create(
            new MonitorInfo("a", 0, new PixelRect(0, 0, 100, 100), new PixelRect(0, 0, 100, 90), 96, 96, true)));
        Assert.Equal("a", DisplayMetrics.MonitorAtPoint(set, 50, 50)?.Id);
        Assert.Null(DisplayMetrics.MonitorAtPoint(set, 500, 500));
    }

    [Fact]
    public void ScaleFromDpi_ClampsToSaneRange()
    {
        Assert.Equal(1.0, DisplayMetrics.ScaleFromDpi(96));
        Assert.Equal(1.5, DisplayMetrics.ScaleFromDpi(144));
        Assert.Equal(0.5, DisplayMetrics.ScaleFromDpi(1));
        Assert.Equal(4.0, DisplayMetrics.ScaleFromDpi(1000));
    }

    [Fact]
    public void MonitorSet_ValueEqualityComparesContent()
    {
        var a = new MonitorSet(System.Collections.Immutable.ImmutableArray.Create(Monitor()));
        var b = new MonitorSet(System.Collections.Immutable.ImmutableArray.Create(Monitor()));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void HitTestPolicy_InteractivePointInsideSquircleIsClient()
    {
        var geometry = new HitTestGeometry(220, 142, 440, 284);
        Assert.Equal(HitTestResult.Client, HitTestPolicy.Evaluate(geometry));
    }

    [Fact]
    public void HitTestPolicy_CornerIsTransparentButEdgeIsClient()
    {
        Assert.Equal(HitTestResult.Transparent,
            HitTestPolicy.Evaluate(new HitTestGeometry(1, 1, 440, 284)));
        Assert.Equal(HitTestResult.Client,
            HitTestPolicy.Evaluate(new HitTestGeometry(220, 2, 440, 284)));
    }

    [Fact]
    public void HitTestPolicy_NonInteractiveOrEmptyIsTransparent()
    {
        Assert.Equal(HitTestResult.Transparent,
            HitTestPolicy.Evaluate(new HitTestGeometry(220, 142, 440, 284, HasInteractiveContent: false)));
        Assert.Equal(HitTestResult.Transparent, HitTestPolicy.Evaluate(new HitTestGeometry(0, 0, 0, 0)));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(30, 30, true)]
    [InlineData(439, 283, false)]
    [InlineData(439, 142, true)]
    public void HitTestPolicy_SquircleMatrix(double x, double y, bool expected)
    {
        Assert.Equal(expected, HitTestPolicy.IsPointInsideShell(x, y, 440, 284));
    }

    [Fact]
    public void HitTestPolicy_ClickThroughStyleOnlyWhenNonInteractive()
    {
        Assert.True(HitTestPolicy.ShouldWindowBeClickThrough(isVisible: false, hasInteractiveContent: true));
        Assert.True(HitTestPolicy.ShouldWindowBeClickThrough(isVisible: true, hasInteractiveContent: false));
        Assert.False(HitTestPolicy.ShouldWindowBeClickThrough(isVisible: true, hasInteractiveContent: true));
    }

    [Fact]
    public void HitTestPolicy_MapsToNativeHitTestValues()
    {
        Assert.Equal(1, HitTestPolicy.ToNativeHitTest(HitTestResult.Client));
        Assert.Equal(-1, HitTestPolicy.ToNativeHitTest(HitTestResult.Transparent));
        Assert.Equal(0, HitTestPolicy.ToNativeHitTest(HitTestResult.Nowhere));
    }
}
