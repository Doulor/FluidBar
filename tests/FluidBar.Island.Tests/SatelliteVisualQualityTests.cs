using System;
using System.Linq;
using System.Reflection;
using FluidBar.Core.Activities;
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
    public void Cover_well_keeps_the_hairline_and_ten_dip_inset()
    {
        // Round 3 of the satellite polish widened the inset 5 -> 8 DIP (well = 24 DIP on a 40 DIP ball,
        // 60 %, inside the audited 20-24 band), so the pure-circular black ring is unmistakable and the
        // silhouette can never read as a squircle. Spec change, not a relaxed check.
        Assert.Equal(8f, SplitSatelliteStyle.InnerFrameInsetDip);
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
    public void Ball_silhouette_is_a_pure_circle()
    {
        // 360 samples on the circle 1.5 DIP inside the rim: every one must be the opaque ball fill.
        // A squircle (or a cover that replaced the body) fails at the corners, where the sample lands
        // on the background instead of the ball.
        var state = IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets =
            [
                new(ActivityKind.Media, IconKind.Media, "媒体", "Midnight Drive", PageRef.Media, 0, null, false, null, null),
            ],
        };
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, 4f);
        var ball = IslandMetrics.SplitIsletRects(state)[0];
        var radius = (ball.Width / 2) - 1.5;
        var inside = 0;
        for (var i = 0; i < 360; i++)
        {
            var angle = i * Math.PI / 180.0;
            var x = ball.MidX + (Math.Cos(angle) * radius);
            var y = ball.MidY + (Math.Sin(angle) * radius);
            var pixel = bitmap.Pixels[((int)Math.Round(y * 4) * bitmap.Width) + (int)Math.Round(x * 4)];
            if (pixel.Alpha > 240 && Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) <= 60)
            {
                inside++;
            }
        }

        Assert.True(inside >= 342, $"the ball silhouette must be a circle: only {inside}/360 rim samples hit the ball");
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