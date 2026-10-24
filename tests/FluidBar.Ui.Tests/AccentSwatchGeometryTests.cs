using FluidBar.App.Views.Settings;
using Xunit;

namespace FluidBar.Ui.Tests;

/// <summary>
/// The accent swatch draws three concentric rings inside one small control. Every radius is derived from the
/// control's half extent; before that the selected ring (radius 16, 2 px pen) and the dashed focus ring
/// (radius 18, 1 px pen) both reached past the 16 px half width, so all four sides were clipped and the clipped
/// dashes read as noise dots (user report 2026-10-24).
/// </summary>
public sealed class AccentSwatchGeometryTests
{
    private static double ControlSize => SettingsMetrics.SwatchSize + (2 * (SettingsMetrics.SwatchRingInset + 2));

    [Fact]
    public void Rings_never_reach_the_clip_edge()
    {
        var rings = AccentSwatch.RingGeometry(ControlSize, ControlSize);

        Assert.True(rings.SelectedOuter <= rings.Half - 0.5,
            "selected ring outer edge " + rings.SelectedOuter + " must stay inside " + rings.Half);
        Assert.True(rings.FocusOuter <= rings.Half - 0.5,
            "focus ring outer edge " + rings.FocusOuter + " must stay inside " + rings.Half);
        Assert.True(rings.Hover + (rings.HoverPen / 2) <= rings.Half - 0.5);
    }

    [Fact]
    public void Rings_are_distinct_and_clear_of_the_colour_disc()
    {
        var rings = AccentSwatch.RingGeometry(ControlSize, ControlSize);

        Assert.True(rings.Disc > 1, "the colour disc must keep a real radius");
        Assert.True(rings.Selected - (rings.SelectedPen / 2) > rings.Disc,
            "the selected ring must sit outside the disc, with a gap");
        Assert.True(rings.Focus > rings.Selected, "the focus ring must be the outermost one");
        Assert.True(rings.Focus - (rings.FocusPen / 2) >= rings.Selected + (rings.SelectedPen / 2),
            "the focus ring must not overlap the selected ring");
    }

    [Fact]
    public void A_tiny_control_still_keeps_every_ring_inside()
    {
        // Guards the clamp: a squeezed layout must not resurrect the clipping.
        var rings = AccentSwatch.RingGeometry(16, 16);
        Assert.True(rings.SelectedOuter <= rings.Half);
        Assert.True(rings.FocusOuter <= rings.Half);
        Assert.True(rings.Disc >= 1);
    }
}
