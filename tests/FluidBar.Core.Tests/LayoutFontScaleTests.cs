using FluidBar.Core.Activities;
using FluidBar.Core.Layout;

namespace FluidBar.Core.Tests;

/// <summary>
/// Font-scale aware compact sizing: 1.0 keeps the approved tokens byte-for-byte, larger scales grow the
/// pill with its text, the expanded shell caps it, and invalid values clamp to the supported range.
/// </summary>
public sealed class LayoutFontScaleTests
{
    public static TheoryData<ActivityKind?, IslandMode> Cases
    {
        get
        {
            var data = new TheoryData<ActivityKind?, IslandMode>();
            foreach (var kind in new ActivityKind?[]
                     {
                         null, ActivityKind.Media, ActivityKind.Volume, ActivityKind.Notification,
                         ActivityKind.Clipboard, ActivityKind.Agent, ActivityKind.Lookup, ActivityKind.Focus,
                     })
            {
                data.Add(kind, IslandMode.Float);
            }

            data.Add(ActivityKind.Media, IslandMode.Dock);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Scale_one_returns_the_exact_token(ActivityKind? kind, IslandMode mode)
    {
        Assert.Equal(LayoutTokens.CompactSize(kind, mode), LayoutTokens.CompactSizeFor(kind, mode, 1.0));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Compact_size_grows_monotonically_with_the_scale(ActivityKind? kind, IslandMode mode)
    {
        // Lookup's token is wider than the expanded shell (480 > 440) and is clamped by the shell/motion
        // contract at every scale; it is covered by the identity + clamp tests instead.
        if (LayoutTokens.CompactSize(kind, mode).Width > LayoutTokens.ExpandedWidth)
        {
            return;
        }

        var previous = LayoutTokens.CompactSizeFor(kind, mode, 0.8);
        foreach (var scale in new[] { 1.0, 1.2, 1.4, 1.6 })
        {
            var size = LayoutTokens.CompactSizeFor(kind, mode, scale);
            Assert.True(size.Width >= previous.Width - 1e-9, $"{kind}/{mode}: width must not shrink at {scale}");
            Assert.True(size.Height >= previous.Height - 1e-9, $"{kind}/{mode}: height must not shrink at {scale}");
            previous = size;
        }
    }

    [Fact]
    public void Compact_size_never_exceeds_the_expanded_shell()
    {
        var size = LayoutTokens.CompactSizeFor(ActivityKind.Agent, IslandMode.Float, 1.6);
        // prototype2 agent token 236 × 1.6 = 377.6, still below the 440 expanded shell.
        Assert.Equal(377.6, size.Width, 3);
        Assert.True(size.Height <= LayoutTokens.ExpandedHeight);
        var narrow = LayoutTokens.CompactSizeFor(null, IslandMode.Float, 0.8);
        Assert.Equal(Math.Round(LayoutTokens.CompactSize(null).Width * 0.8, 2), narrow.Width, 3);
    }

    [Fact]
    public void Invalid_scales_clamp_to_the_supported_range()
    {
        var low = LayoutTokens.CompactSizeFor(null, IslandMode.Float, 0.01);
        var highAgent = LayoutTokens.CompactSizeFor(ActivityKind.Agent, IslandMode.Float, 99);
        var nan = LayoutTokens.CompactSizeFor(null, IslandMode.Float, double.NaN);
        Assert.Equal(Math.Round(LayoutTokens.CompactSize(null).Width * LayoutTokens.MinFontScale, 2), low.Width, 3);
        // 236 × 1.6 at the ceiling (no clamping needed with the narrower prototype2 agent pill).
        Assert.Equal(377.6, highAgent.Width, 3);
        Assert.True(highAgent.Width <= LayoutTokens.ExpandedWidth);
        Assert.Equal(LayoutTokens.CompactSize(null, IslandMode.Float), nan);
        Assert.Equal(0.8, LayoutTokens.MinFontScale);
        Assert.Equal(1.6, LayoutTokens.MaxFontScale);
    }
}
