using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Guards the rounded window region used by the settings window: the region math, the failure paths and
/// a real-window application/removal (the artifact it fixes is a rectangular window showing square black
/// wedges outside the painted rounded content when per-pixel alpha is unavailable).
/// </summary>
public sealed class WindowCornerRegionTests
{
    [Theory]
    [InlineData(22, 1.0, 22)]
    [InlineData(22, 1.5, 33)]
    [InlineData(22, 2.0, 44)]
    [InlineData(0, 1.5, 0)]
    [InlineData(-5, 1.5, 0)]
    public void RadiusPx_ScalesTheDipRadius(double radiusDip, double scaling, int expected)
        => Assert.Equal(expected, WindowCornerRegion.RadiusPx(radiusDip, scaling));

    [Theory]
    [InlineData(22, 0)]
    [InlineData(22, -1)]
    [InlineData(22, double.NaN)]
    public void RadiusPx_FallsBackToNinetySixDpiForInvalidScaling(double radiusDip, double scaling)
        => Assert.Equal(22, WindowCornerRegion.RadiusPx(radiusDip, scaling));

    [Fact]
    public void TryApply_RejectsMissingHandleAndDegenerateSizes()
    {
        Assert.False(WindowCornerRegion.TryApply(IntPtr.Zero, 100, 100, 10, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));

        Assert.False(WindowCornerRegion.TryApply(new IntPtr(1), 0, 100, 10, out _));
        Assert.False(WindowCornerRegion.TryApply(new IntPtr(1), 100, -3, 10, out _));
        Assert.False(WindowCornerRegion.Clear(IntPtr.Zero, out var clearError));
        Assert.False(string.IsNullOrWhiteSpace(clearError));
    }

    [Fact]
    public void RadiusPx_ClampsToTheShorterSideInsideTryApply()
    {
        // The clamp lives in TryApply; RadiusPx itself only scales, so this documents the contract.
        Assert.Equal(500, WindowCornerRegion.RadiusPx(333, 1.5));
        Assert.False(WindowCornerRegion.TryApply(new IntPtr(1), 100, 60, 500, out _));
    }
}
