using FluidBar.Island;
using Xunit;

namespace FluidBar.Island.Tests;

/// <summary>
/// The big island's agent spinner and the split-satellite ring must be the same primitive: identical
/// sweep, identical stroke:radius ratio and identical period, all read from <see cref="AgentSpinner"/>.
/// </summary>
public sealed class AgentSpinnerTests
{
    [Fact]
    public void Satellite_and_big_island_share_sweep_ratio_and_speed()
    {
        // Sweep
        Assert.Equal(AgentSpinner.SweepDegrees, 180f, 0.001f);
        Assert.Equal(AgentSpinner.SweepDegrees, AgentMotion.ArcSweepDegrees, 0.001f);

        // Expanded-row spinner: 1.5 DIP on a 4.75 DIP radius = 0.3158.
        Assert.Equal(1.5f / 4.75f, AgentSpinner.StrokeToRadiusRatio, 0.0001f);
        Assert.Equal(AgentSpinner.StrokeForRadius(4.75f), AgentMotion.RingStrokeWidth, 0.001f);

        // Satellite ring: the primary pill's ratio (1.5 DIP on a 9.25 DIP radius) -> the same thin circle.
        Assert.Equal(1.5f / 9.25f, AgentSpinner.RingStrokeToRadiusRatio, 0.0001f);
        Assert.Equal(IslandMetrics.SplitRingStrokeDip,
            AgentSpinner.RingStrokeForRadius(IslandMetrics.SplitRingRadiusDip), 0.001f);
        Assert.InRange(IslandMetrics.SplitRingStrokeDip, 1.7f, 1.9f);

        // Speed
        Assert.Equal(0.9, AgentSpinner.PeriodSeconds, 0.0001);
        Assert.Equal(AgentSpinner.PeriodSeconds, IslandMetrics.SplitRingPeriodSeconds, 0.0001);
        Assert.Equal((float)AgentSpinner.RotationDegrees(IslandMetrics.SplitRingPeriodSeconds), 0f, 0.01f);
    }

    [Fact]
    public void Big_island_spinner_radius_matches_the_reference_ring()
    {
        const float referenceRadius = (11f - AgentMotion.RingStrokeWidth) / 2f;
        Assert.Equal(4.75f, referenceRadius, 0.001f);
        Assert.Equal(1.5f, AgentSpinner.StrokeForRadius(referenceRadius), 0.001f);
    }

    [Fact]
    public void Satellite_ring_keeps_a_cap_like_the_big_island()
    {
        Assert.Equal(SkiaSharp.SKStrokeCap.Butt, AgentSpinner.Cap);
    }
}
