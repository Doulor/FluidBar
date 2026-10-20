using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Prototype2 rail interaction visuals: the two neighbour glasses are buttons, so they carry a hover
/// fill, a pressed fill and a 'focus-visible' accent outline. The prototype rules are
/// '.mini-bg:hover { background: rgba(40,40,48,.88) }',
/// '.mini-bg:active { background: rgba(52,52,62,.94) }' and
/// ':focus-visible { outline: 2px solid var(--accent); outline-offset: 2px }'.
/// </summary>
public sealed class MiniRailInteractionTests
{
    [Fact]
    public void GlassPalette_MatchesThePrototypeRules()
    {
        Assert.Equal(new SKColor(26, 26, 31, 184), MiniRailLayout.Glass);
        Assert.Equal(new SKColor(40, 40, 48, 224), MiniRailLayout.HoverGlass);
        Assert.Equal(new SKColor(52, 52, 62, 240), MiniRailLayout.PressedGlass);

        // Resting, hovered and pressed neighbour glasses.
        Assert.Equal(MiniRailLayout.Glass,
            MiniRailLayout.GlassFor(MiniRailSlot.Next, MiniRailSlot.None, MiniRailSlot.None));
        Assert.Equal(MiniRailLayout.HoverGlass,
            MiniRailLayout.GlassFor(MiniRailSlot.Next, MiniRailSlot.Next, MiniRailSlot.None));
        Assert.Equal(MiniRailLayout.PressedGlass,
            MiniRailLayout.GlassFor(MiniRailSlot.Next, MiniRailSlot.None, MiniRailSlot.Next));

        // ':active' is declared after ':hover', so pressed wins while the pointer is over the pill.
        Assert.Equal(MiniRailLayout.PressedGlass,
            MiniRailLayout.GlassFor(MiniRailSlot.Previous, MiniRailSlot.Previous, MiniRailSlot.Previous));

        // The current capsule is a plain <span> in the prototype: hover/press never change its fill.
        Assert.Equal(MiniRailLayout.Glass,
            MiniRailLayout.GlassFor(MiniRailSlot.Current, MiniRailSlot.Current, MiniRailSlot.Current));

        // A state resolved for a different slot never tints this one.
        Assert.Equal(MiniRailLayout.Glass,
            MiniRailLayout.GlassFor(MiniRailSlot.Next, MiniRailSlot.Previous, MiniRailSlot.Previous));
    }

    [Fact]
    public void RenderedNeighbourGlass_GetsLighterAndMoreOpaque_WhenHoveredAndPressed()
    {
        var basePixel = SampleNextGlass(MiniRailSlot.None, MiniRailSlot.None);
        var hoverPixel = SampleNextGlass(MiniRailSlot.Next, MiniRailSlot.None);
        var pressedPixel = SampleNextGlass(MiniRailSlot.Next, MiniRailSlot.Next);

        Assert.True(hoverPixel.Alpha > basePixel.Alpha, $"hover {hoverPixel} must be more opaque than {basePixel}");
        Assert.True(pressedPixel.Alpha > hoverPixel.Alpha, $"pressed {pressedPixel} must be more opaque than {hoverPixel}");
        Assert.True(hoverPixel.Red > basePixel.Red, $"hover {hoverPixel} must be lighter than {basePixel}");
        Assert.True(pressedPixel.Red > hoverPixel.Red, $"pressed {pressedPixel} must be lighter than {hoverPixel}");
    }

    [Fact]
    public void RenderedCurrentCapsule_KeepsItsGlass_WhenHovered()
    {
        var basePixel = SampleCurrentGlass(MiniRailSlot.None);
        var hoverPixel = SampleCurrentGlass(MiniRailSlot.Current);
        Assert.Equal(basePixel, hoverPixel);
    }

    [Fact]
    public void FocusRing_IsATwoDipAccentOutlineAroundTheFocusedGlass()
    {
        var state = WithInteraction(IslandTestFixtures.RailState(2), MiniRailSlot.None, MiniRailSlot.None, MiniRailSlot.Next);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var frame = Measure(state);

        // The stroke covers [right + 1, right + 3] DIP: sample its centre line.
        var ringX = (int)MathF.Round((frame.Next.Right + 2f) * IslandTestFixtures.Scale);
        var ringY = (int)MathF.Round(frame.Next.MidY * IslandTestFixtures.Scale);
        AssertColor(IslandTestFixtures.Theme.Accent, bitmap.GetPixel(ringX, ringY));

        // The 2 DIP outline offset stays clear: a sample just outside the pill is still background.
        var gapX = (int)MathF.Round((frame.Next.Right + 0.3f) * IslandTestFixtures.Scale);
        Assert.True(bitmap.GetPixel(gapX, ringY).Alpha < 200,
            "the outline offset must leave a gap between the glass and the ring");

        // A focused current capsule gets the same outline.
        var currentState = WithInteraction(IslandTestFixtures.RailState(2), MiniRailSlot.None, MiniRailSlot.None, MiniRailSlot.Current);
        using var currentBitmap = renderer.RenderToBitmap(currentState, IslandTestFixtures.Scale);
        var currentFrame = Measure(currentState);
        AssertColor(IslandTestFixtures.Theme.Accent, currentBitmap.GetPixel(
            (int)MathF.Round((currentFrame.Current.Right + 2f) * IslandTestFixtures.Scale),
            (int)MathF.Round(currentFrame.Current.MidY * IslandTestFixtures.Scale)));

        // Page 01 has no previous glass, so a stale focus slot must not paint a ring there.
        var firstState = WithInteraction(IslandTestFixtures.RailState(0), MiniRailSlot.None, MiniRailSlot.None, MiniRailSlot.Previous);
        using var firstBitmap = renderer.RenderToBitmap(firstState, IslandTestFixtures.Scale);
        var firstFrame = Measure(firstState);
        Assert.True(firstBitmap.GetPixel(
            (int)MathF.Round((firstFrame.Previous.Right + 2f) * IslandTestFixtures.Scale),
            (int)MathF.Round(firstFrame.Previous.MidY * IslandTestFixtures.Scale)).Alpha < 32,
            "page 01 has no previous glass; no ring may be painted for it");
    }

    [Theory]
    [InlineData(MiniRailSlot.None, MiniRailSlot.None, MiniRailSlot.None)]
    [InlineData(MiniRailSlot.Next, MiniRailSlot.None, MiniRailSlot.Next)]
    [InlineData(MiniRailSlot.Next, MiniRailSlot.Next, MiniRailSlot.Previous)]
    [InlineData(MiniRailSlot.Previous, MiniRailSlot.Previous, MiniRailSlot.Current)]
    public void InteractionStates_DoNotMoveThePaintedGeometry(MiniRailSlot hover, MiniRailSlot pressed, MiniRailSlot focus)
    {
        var plain = Measure(IslandTestFixtures.RailState(3));
        var interacted = Measure(WithInteraction(IslandTestFixtures.RailState(3), hover, pressed, focus));

        // Drawing and hit-testing share MiniRailLayout.Measure, so an interaction state may only change
        // paint, never a rectangle.
        Assert.Equal(plain.Page, interacted.Page);
        Assert.Equal(plain.CapsuleWidth, interacted.CapsuleWidth);
        Assert.Equal(plain.Strip, interacted.Strip);
        Assert.Equal(plain.Previous, interacted.Previous);
        Assert.Equal(plain.Current, interacted.Current);
        Assert.Equal(plain.Next, interacted.Next);
    }

    [Fact]
    public void StaleSlots_AreDroppedAtThePageEdges()
    {
        // Page 01 has no previous glass and page 06 has no next glass: forwarding a stale
        // hover/press/focus there must resolve to None so no unpainted pill is tinted.
        var first = Measure(WithInteraction(IslandTestFixtures.RailState(0),
            MiniRailSlot.Previous, MiniRailSlot.Previous, MiniRailSlot.Previous));
        Assert.Equal(MiniRailSlot.None, first.Hover);
        Assert.Equal(MiniRailSlot.None, first.Pressed);
        Assert.Equal(MiniRailSlot.None, first.Focus);

        var last = Measure(WithInteraction(IslandTestFixtures.RailState(5),
            MiniRailSlot.Next, MiniRailSlot.Next, MiniRailSlot.Next));
        Assert.Equal(MiniRailSlot.None, last.Hover);
        Assert.Equal(MiniRailSlot.None, last.Pressed);
        Assert.Equal(MiniRailSlot.None, last.Focus);
    }

    private static IslandRenderState WithInteraction(
        IslandRenderState state,
        MiniRailSlot hover = MiniRailSlot.None,
        MiniRailSlot pressed = MiniRailSlot.None,
        MiniRailSlot focus = MiniRailSlot.None)
        => state with
        {
            Archipelago = state.Archipelago with
            {
                RailHover = hover,
                RailPressed = pressed,
                RailFocus = focus,
            },
        };

    private static MiniRailFrame Measure(IslandRenderState state)
    {
        Span<SKRect> left = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];
        Span<SKRect> right = stackalloc SKRect[IslandMetrics.MaxDotsPerSide];
        return MiniRailLayout.Measure(state, IslandMetrics.ShellRect(state), left, right);
    }

    private static SKColor SampleNextGlass(MiniRailSlot hover, MiniRailSlot pressed)
    {
        var state = WithInteraction(IslandTestFixtures.RailState(2), hover, pressed);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var frame = Measure(state);
        return bitmap.GetPixel(
            (int)MathF.Round(frame.Next.MidX * IslandTestFixtures.Scale),
            (int)MathF.Round((frame.Next.Top + 2.5f) * IslandTestFixtures.Scale));
    }

    private static SKColor SampleCurrentGlass(MiniRailSlot hover)
    {
        var state = WithInteraction(IslandTestFixtures.RailState(2), hover);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var frame = Measure(state);
        return bitmap.GetPixel(
            (int)MathF.Round(frame.Current.MidX * IslandTestFixtures.Scale),
            (int)MathF.Round((frame.Current.Top + 2.5f) * IslandTestFixtures.Scale));
    }

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= 2
            && Math.Abs(expected.Green - actual.Green) <= 2
            && Math.Abs(expected.Blue - actual.Blue) <= 2
            && Math.Abs(expected.Alpha - actual.Alpha) <= 2,
            $"expected {expected}, got {actual}");
    }
}
