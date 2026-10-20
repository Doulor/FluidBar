using System;
using System.Linq;
using System.Reflection;
using FluidBar.Island;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// Locks the split-satellite material recipe to the ranges taken from the iOS study
/// (docs/ios-design-study.md, layer table L0-L4). If someone drifts a constant outside
/// the band, the build fails instead of the island quietly looking off.
/// </summary>
public sealed class SatelliteVisualQualityTests
{
    [Fact]
    public void Rim_highlight_stays_inside_the_ios_band()
    {
        var percent = SplitSatelliteStyle.BallRimAlpha / 255.0;
        Assert.InRange(percent, 0.06, 0.10);
        Assert.InRange(SplitSatelliteStyle.BallRimWidthDip, 1f, 2f);
        Assert.InRange(SplitSatelliteStyle.BallRimFade, 0.3f, 0.7f);
    }

    [Fact]
    public void Cover_well_keeps_the_hairline_and_eight_dip_inset()
    {
        Assert.Equal(4f, SplitSatelliteStyle.InnerFrameInsetDip);
        Assert.InRange(SplitSatelliteStyle.InnerFrameRadiusFactor, 0.20f, 0.25f);
        Assert.Equal(1f, SplitSatelliteStyle.InnerFrameStrokeWidthDip);
        var stroke = SplitSatelliteStyle.InnerFrameStrokeAlpha / 255.0;
        Assert.InRange(stroke, 0.05, 0.10);
    }

    [Fact]
    public void Ball_carries_a_soft_bottom_shade_for_volume()
    {
        var shade = SplitSatelliteStyle.BallShadeAlpha / 255.0;
        Assert.InRange(shade, 0.15, 0.25);
        Assert.InRange(SplitSatelliteStyle.BallShadeStart, 0.4f, 0.65f);
        Assert.InRange(SplitSatelliteStyle.BallShadowAlpha / 255.0, 0.10, 0.25);
    }

    [Fact]
    public void Press_shade_matches_the_main_island_press_depth()
    {
        Assert.Equal(0.18f * 255f, SplitSatelliteStyle.PressShadeAlpha, 3);
    }

    [Fact]
    public void Agent_ring_turns_at_the_contracted_speed()
    {
        // docs/satellite-render-contract.md pins one third of a turn per second.
        Assert.InRange(SplitSatelliteStyle.RingDegreesPerSecond, 110, 130);
        Assert.Equal(2f, SplitSatelliteStyle.RingWidthDip);
        Assert.InRange(SplitSatelliteStyle.RingSweepDegrees, 90f, 270f);
    }

    [Fact]
    public void Style_surface_exposes_no_accent_colour()
    {
        var accentMembers = typeof(SplitSatelliteStyle)
            .GetMembers(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.Contains("Accent", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToArray();
        Assert.Empty(accentMembers);
    }
}
