using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Tier 2 "liquid glass" coverage: the material must stay translucent, keep a lit top / shaded bottom
/// rim, stay neutral (no accent tint), react to the pointer, keep its overlay cache bounded and stay
/// inside the frame budget. The layered host cannot ask DWM for a backdrop blur, so these assertions
/// guard the *drawn* optical simulation that replaces it.
/// </summary>
public sealed class LiquidGlassTests
{
    private static IslandTheme Liquid => IslandTheme.Dark.WithMaterial(IslandMaterial.LiquidGlass);

    private static IslandRenderState CompactState(IslandTheme theme)
        => IslandTestFixtures.MediaCompact() with { Theme = theme };

    [Fact]
    public void Compact_and_expanded_shells_render_with_the_liquid_material()
    {
        using var renderer = new IslandRenderer(Liquid);
        using var compact = renderer.RenderToBitmap(CompactState(Liquid), IslandTestFixtures.Scale);
        var expanded = IslandTestFixtures.Expanded(1) with { Theme = Liquid };
        using var panel = renderer.RenderToBitmap(expanded, IslandTestFixtures.Scale);

        Assert.True(OpaquePixels(compact) > 1000, "compact liquid shell painted nothing");
        Assert.True(OpaquePixels(panel) > 10000, "expanded liquid shell painted nothing");
        Assert.True(renderer.LiquidGlassOverlayCount is > 0 and <= 4,
            $"overlay cache should be bounded, got {renderer.LiquidGlassOverlayCount}");
        Assert.True(renderer.LiquidGlassOverlayBytes <= 12L * 1024 * 1024,
            $"overlay cache retained {renderer.LiquidGlassOverlayBytes} bytes");
    }

    [Fact]
    public void Liquid_body_is_more_translucent_than_obsidian_and_acrylic()
    {
        var alpha = new Dictionary<IslandMaterial, byte>();
        foreach (var material in new[]
                 {
                     IslandMaterial.Obsidian, IslandMaterial.SimulatedGlass, IslandMaterial.LiquidGlass,
                 })
        {
            var theme = IslandTheme.Dark.WithMaterial(material);
            using var renderer = new IslandRenderer(theme);
            using var bitmap = renderer.RenderToBitmap(CompactState(theme), IslandTestFixtures.Scale);
            var state = CompactState(theme);
            var shell = IslandMetrics.ShellRect(state);
            var x = (int)Math.Round((shell.Left + (shell.Width / 2)) * IslandTestFixtures.Scale);
            var y = (int)Math.Round((shell.Top + (shell.Height * 0.62)) * IslandTestFixtures.Scale);
            alpha[material] = bitmap.GetPixel(x, y).Alpha;
        }

        Assert.True(alpha[IslandMaterial.Obsidian] > 240, $"obsidian should stay opaque, got {alpha[IslandMaterial.Obsidian]}");
        Assert.True(alpha[IslandMaterial.LiquidGlass] < alpha[IslandMaterial.SimulatedGlass],
            $"liquid glass ({alpha[IslandMaterial.LiquidGlass]}) should be clearer than acrylic ({alpha[IslandMaterial.SimulatedGlass]})");
        Assert.True(alpha[IslandMaterial.LiquidGlass] < 200,
            $"liquid glass body should let the desktop through, got alpha {alpha[IslandMaterial.LiquidGlass]}");
    }

    [Fact]
    public void Liquid_rim_is_lit_on_the_top_edge_and_shaded_at_the_bottom()
    {
        var state = CompactState(Liquid);
        using var renderer = new IslandRenderer(Liquid);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var shell = IslandMetrics.ShellRect(state);
        var inset = 1;

        var x = (int)Math.Round((shell.Left + (shell.Width / 2)) * IslandTestFixtures.Scale);
        var top = (int)Math.Round((shell.Top + inset) * IslandTestFixtures.Scale);
        var middle = (int)Math.Round((shell.Top + (shell.Height / 2)) * IslandTestFixtures.Scale);
        var bottom = (int)Math.Round((shell.Bottom - inset) * IslandTestFixtures.Scale);

        var topLuma = Luma(bitmap.GetPixel(x, top));
        var midLuma = Luma(bitmap.GetPixel(x, middle));
        var bottomLuma = Luma(bitmap.GetPixel(x, bottom));

        Assert.True(topLuma > midLuma * 2.5,
            $"specular edge should lift the top rim (top {topLuma:0.0} vs centre {midLuma:0.0})");
        Assert.True(bottomLuma > midLuma * 1.8,
            $"caustic bounce should lift the bottom rim (bottom {bottomLuma:0.0} vs centre {midLuma:0.0})");
        Assert.True(bottomLuma < topLuma,
            $"the lit edge must stay the brightest (top {topLuma:0.0} vs bottom {bottomLuma:0.0})");
    }

    [Fact]
    public void Liquid_rim_stays_neutral_and_is_never_the_accent_colour()
    {
        var state = CompactState(Liquid);
        using var renderer = new IslandRenderer(Liquid);
        using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        var shell = IslandMetrics.ShellRect(state);
        var accent = Liquid.Accent;

        var maxSpread = 0;
        var minAccentDistance = double.MaxValue;
        var left = (int)Math.Round(shell.Left * IslandTestFixtures.Scale);
        var right = (int)Math.Round(shell.Right * IslandTestFixtures.Scale);
        var top = (int)Math.Round(shell.Top * IslandTestFixtures.Scale);
        var bottom = (int)Math.Round(shell.Bottom * IslandTestFixtures.Scale);
        for (var y = top + 6; y <= bottom - 6; y += 2)
        {
            foreach (var dx in new[] { 2, 3, 4 })
            {
                var colour = bitmap.GetPixel(left + dx, y);
                if (colour.Alpha < 120)
                {
                    continue;
                }

                var spread = Math.Max(colour.Red, Math.Max(colour.Green, colour.Blue))
                    - Math.Min(colour.Red, Math.Min(colour.Green, colour.Blue));
                maxSpread = Math.Max(maxSpread, spread);
                minAccentDistance = Math.Min(minAccentDistance, Distance(colour, accent));
            }
        }

        Assert.True(maxSpread <= 26, $"the liquid rim must stay neutral, got channel spread {maxSpread}");
        Assert.True(minAccentDistance > 40, $"the liquid rim is too close to the accent colour ({minAccentDistance:0})");
    }

    [Fact]
    public void Pointer_illumination_brightens_the_shell_under_the_pointer()
    {
        var state = CompactState(Liquid);
        using var renderer = new IslandRenderer(Liquid);
        using var withoutPointer = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var pointerX = shell.Left + (shell.Width * 0.5);
        var pointerY = shell.Top + (shell.Height * 0.72);
        var withPointer = state with
        {
            HasPointer = true,
            PointerX = pointerX,
            PointerY = pointerY,
        };
        using var lit = renderer.RenderToBitmap(withPointer, IslandTestFixtures.Scale);

        var px = (int)Math.Round(pointerX * IslandTestFixtures.Scale);
        var py = (int)Math.Round(pointerY * IslandTestFixtures.Scale);
        var plain = Luma(withoutPointer.GetPixel(px, py));
        var glow = Luma(lit.GetPixel(px, py));

        Assert.True(glow > plain + 4,
            $"pointer illumination should lift the shell (plain {plain:0.0} vs lit {glow:0.0})");
    }

    [Fact]
    public void Liquid_glass_settled_frames_stay_in_budget_and_allocate_nothing()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(1) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);
        using var surface = renderer.CreateSurface(state, IslandTestFixtures.Scale);
        for (var i = 0; i < 24; i++)
        {
            renderer.Render(surface.Canvas, state, IslandTestFixtures.Scale);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 40; i++)
        {
            renderer.Render(surface.Canvas, state, IslandTestFixtures.Scale);
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 40.0;
        Assert.True(perFrame < 1024, $"liquid-glass warm frames allocated {perFrame:0.0} B/frame");

        // Generous ceiling: the reported budget is ~0.55-0.61 ms for a full prototype frame on the
        // maintainer's machine; CI machines are slower and noisier.
        Assert.True(renderer.Stats.MeanMilliseconds < 4.0,
            $"liquid-glass frame cost {renderer.Stats.MeanMilliseconds:0.00} ms exceeds the ceiling");
    }

    [Fact]
    public void Overlay_cache_is_bounded_across_shell_sizes()
    {
        using var renderer = new IslandRenderer(Liquid);
        for (var i = 0; i < 12; i++)
        {
            var width = 120 + (i * 12);
            var state = CompactState(Liquid) with
            {
                ShellWidth = width,
                ShellHeight = 36,
                ShellRadii = LayoutTokens.RadiiFor(ShellForm.Compact, IslandMode.Float, 36),
            };
            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        }

        Assert.True(renderer.LiquidGlassOverlayCount <= 4,
            $"overlay cache grew to {renderer.LiquidGlassOverlayCount} entries");
    }

    private static int OpaquePixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (bitmap.GetPixel(x, y).Alpha > 8)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double Distance(SKColor a, SKColor b)
    {
        var dr = a.Red - b.Red;
        var dg = a.Green - b.Green;
        var db = a.Blue - b.Blue;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static double Luma(SKColor colour)
        => (0.2126 * colour.Red) + (0.7152 * colour.Green) + (0.0722 * colour.Blue);
}
