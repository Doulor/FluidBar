using FluidBar.Core.Activities;

namespace FluidBar.Core.Tests;

/// <summary>Stable activity-key tokens must include every kind the plugin SDK can publish (M2 Status).</summary>
public sealed class ActivityKindTokenTests
{
    [Fact]
    public void StatusHasAStableTokenAndRoundTrips()
    {
        Assert.Equal("status", ActivityKind.Status.ToToken());
        Assert.True(ActivityKindExtensions.TryParseToken("status", out var parsed));
        Assert.Equal(ActivityKind.Status, parsed);
        Assert.Equal("status:fluidbar-agent", new ActivityKey(ActivityKind.Status, "fluidbar-agent").ToString());
    }
}

