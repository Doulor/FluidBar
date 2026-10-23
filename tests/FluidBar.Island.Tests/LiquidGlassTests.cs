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
    public void Bright_backdrops_get_a_vibrancy_scrim_behind_the_top_text_band()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(1) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var white = new SKBitmap(info);
        using (var canvas = new SKCanvas(white))
        {
            canvas.Clear(SKColors.White);
        }

        using var image = SKImage.FromBitmap(white);
        using var bright = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.98 }, IslandTestFixtures.Scale);
        using var dark = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.0 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var x = (int)Math.Round((shell.Left + (shell.Width / 2)) * IslandTestFixtures.Scale);
        var topY = (int)Math.Round((shell.Top + 40) * IslandTestFixtures.Scale);
        var bottomY = (int)Math.Round((shell.Bottom - 30) * IslandTestFixtures.Scale);

        var brightDelta = Luma(bright.GetPixel(x, topY)) - Luma(bright.GetPixel(x, bottomY));
        var darkDelta = Luma(dark.GetPixel(x, topY)) - Luma(dark.GetPixel(x, bottomY));

        Assert.True(
            brightDelta < darkDelta - 10,
            $"a bright backdrop must darken the top band (bright {brightDelta:0.0} vs dark {darkDelta:0.0})");
    }

    [Fact]
    public void Refraction_band_refracts_the_backdrop_near_the_rim()
    {
        var theme = Liquid;
        var state = CompactState(theme);
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var backdropBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(backdropBitmap))
        {
            canvas.Clear(new SKColor(0xF2, 0xE6, 0xD0));
            using var dark = new SKPaint { Color = new SKColor(0x10, 0x20, 0x40) };
            var cell = 40;
            for (var by = 0; by < info.Height; by += cell)
            {
                for (var bx = 0; bx < info.Width; bx += cell)
                {
                    if (((bx / cell) + (by / cell)) % 2 == 0)
                    {
                        canvas.DrawRect(new SKRect(bx, by, bx + cell, by + cell), dark);
                    }
                }
            }
        }

        using var image = SKImage.FromBitmap(backdropBitmap);
        using var flat = renderer.RenderToBitmap(
            state with { Backdrop = image, RefractionScale = 1.0 }, IslandTestFixtures.Scale);
        using var lens = renderer.RenderToBitmap(
            state with { Backdrop = image, RefractionScale = 1.25, RefractionBandDip = 18 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        double total = 0;
        var samples = 0;
        for (var dy = 3; dy <= 9; dy += 2)
        {
            for (var dx = (int)(shell.Width * 0.2); dx <= (int)(shell.Width * 0.8); dx += 7)
            {
                var px = (int)Math.Round((shell.Left + dx) * IslandTestFixtures.Scale);
                var py = (int)Math.Round((shell.Top + dy) * IslandTestFixtures.Scale);
                total += Math.Abs(Luma(lens.GetPixel(px, py)) - Luma(flat.GetPixel(px, py)));
                samples++;
            }
        }

        var meanDiff = total / Math.Max(1, samples);
        Assert.True(
            meanDiff > 1.5,
            $"the rim band must sample different backdrop content (mean |diff| {meanDiff:0.00} over {samples} samples)");
    }

    [Fact]
    public void Backdrop_mode_paints_the_captured_desktop_instead_of_the_flat_tint()
    {
        var theme = Liquid;
        var state = CompactState(theme);
        using var renderer = new IslandRenderer(theme);

        // Synthetic "desktop": a coarse checkerboard (the host hands over a blurred capture; a blocky
        // pattern makes the sampled gradient obvious even after the tint).
        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var backdropBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(backdropBitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x40, 0x80));
            using var light = new SKPaint { Color = new SKColor(0xF0, 0xE0, 0xC0) };
            var cell = 60;
            for (var by = 0; by < info.Height; by += cell)
            {
                for (var bx = 0; bx < info.Width; bx += cell)
                {
                    if (((bx / cell) + (by / cell)) % 2 == 0)
                    {
                        canvas.DrawRect(new SKRect(bx, by, bx + cell, by + cell), light);
                    }
                }
            }
        }

        using var image = SKImage.FromBitmap(backdropBitmap);
        using var flat = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var withBackdrop = renderer.RenderToBitmap(
            state with { Backdrop = image }, IslandTestFixtures.Scale);

        // Sample well inside the shell: the rim band and the key line make the outer 8 DIP non-flat.
        var shell = IslandMetrics.ShellRect(state);
        var left = (int)Math.Round((shell.Left + (shell.Width * 0.30)) * IslandTestFixtures.Scale);
        var right = (int)Math.Round((shell.Left + (shell.Width * 0.70)) * IslandTestFixtures.Scale);
        var y = (int)Math.Round((shell.Top + (shell.Height * 0.50)) * IslandTestFixtures.Scale);

        var flatLeft = Luma(flat.GetPixel(left, y));
        var flatRight = Luma(flat.GetPixel(right, y));
        var glassLeft = Luma(withBackdrop.GetPixel(left, y));
        var glassRight = Luma(withBackdrop.GetPixel(right, y));

        var flatDelta = Math.Abs(flatLeft - flatRight);
        var glassDelta = Math.Abs(glassLeft - glassRight);
        Assert.True(glassDelta > flatDelta + 8,
            $"the backdrop must add spatial variation (flat delta {flatDelta:0.0}, glass delta {glassDelta:0.0})");
        Assert.True(
            Math.Abs(glassLeft - flatLeft) > 8 || Math.Abs(glassRight - flatRight) > 8,
            $"the backdrop must change the shell pixels (flat {flatLeft:0.0}/{flatRight:0.0}, glass {glassLeft:0.0}/{glassRight:0.0})");
    }

    [Fact]
    public void Material_swap_cross_fades_between_the_two_materials()
    {
        var obsidian = IslandTheme.Dark.WithMaterial(IslandMaterial.Obsidian);
        var liquid = IslandTheme.Dark.WithMaterial(IslandMaterial.LiquidGlass);
        using var renderer = new IslandRenderer();

        var baseState = IslandTestFixtures.MediaCompact();
        var obsidianState = baseState with { Theme = obsidian };
        var liquidState = baseState with { Theme = liquid };

        using var obsidianBitmap = renderer.RenderToBitmap(obsidianState, IslandTestFixtures.Scale);
        using var liquidBitmap = renderer.RenderToBitmap(liquidState, IslandTestFixtures.Scale);
        using var startBitmap = renderer.RenderToBitmap(
            liquidState with { PreviousTheme = obsidian, MaterialBlend = 0 }, IslandTestFixtures.Scale);
        using var endBitmap = renderer.RenderToBitmap(
            liquidState with { PreviousTheme = obsidian, MaterialBlend = 1 }, IslandTestFixtures.Scale);
        using var midBitmap = renderer.RenderToBitmap(
            liquidState with { PreviousTheme = obsidian, MaterialBlend = 0.5 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(liquidState);
        var px = (int)Math.Round((shell.Left + (shell.Width / 2)) * IslandTestFixtures.Scale);
        var py = (int)Math.Round((shell.Top + (shell.Height * 0.62)) * IslandTestFixtures.Scale);

        var start = startBitmap.GetPixel(px, py);
        var end = endBitmap.GetPixel(px, py);
        var mid = midBitmap.GetPixel(px, py);
        var plainObsidian = obsidianBitmap.GetPixel(px, py);
        var plainLiquid = liquidBitmap.GetPixel(px, py);

        Assert.True(Math.Abs(start.Alpha - plainObsidian.Alpha) <= 3,
            $"blend 0 must equal the old material (alpha {start.Alpha} vs {plainObsidian.Alpha})");
        Assert.True(Math.Abs(end.Alpha - plainLiquid.Alpha) <= 3,
            $"blend 1 must equal the new material (alpha {end.Alpha} vs {plainLiquid.Alpha})");

        var expected = (plainObsidian.Alpha + plainLiquid.Alpha) / 2.0;
        Assert.True(Math.Abs(mid.Alpha - expected) <= 10,
            $"midpoint alpha {mid.Alpha} should sit between {plainObsidian.Alpha} and {plainLiquid.Alpha}");
        Assert.True(mid.Alpha < plainObsidian.Alpha && mid.Alpha > plainLiquid.Alpha,
            "the cross-fade must be monotonic between the two materials");
    }

    [Fact]
    public void Liquid_glass_settled_frames_stay_in_budget_and_allocate_nothing()
    {
        var liquid = Liquid;
        var obsidian = IslandTheme.Dark.WithMaterial(IslandMaterial.Obsidian);
        var liquidState = IslandTestFixtures.Expanded(1) with { Theme = liquid };
        var obsidianState = IslandTestFixtures.Expanded(1) with { Theme = obsidian };

        using var liquidRenderer = new IslandRenderer(liquid);
        using var obsidianRenderer = new IslandRenderer(obsidian);
        using var surface = liquidRenderer.CreateSurface(liquidState, IslandTestFixtures.Scale);

        // Warm every cache, then measure both materials in the same process: an absolute budget is
        // meaningless on a loaded CI box, so the gate is the ratio against the opaque baseline.
        for (var i = 0; i < 24; i++)
        {
            liquidRenderer.Render(surface.Canvas, liquidState, IslandTestFixtures.Scale);
            obsidianRenderer.Render(surface.Canvas, obsidianState, IslandTestFixtures.Scale);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 40; i++)
        {
            liquidRenderer.Render(surface.Canvas, liquidState, IslandTestFixtures.Scale);
        }

        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 40.0;
        Assert.True(perFrame < 1024, $"liquid-glass warm frames allocated {perFrame:0.0} B/frame");

        var liquidMean = Measure(liquidRenderer, surface, liquidState);
        var obsidianMean = Measure(obsidianRenderer, surface, obsidianState);
        Assert.True(liquidMean < 8.0,
            $"liquid-glass frame cost {liquidMean:0.00} ms exceeds the absolute ceiling");
        Assert.True(liquidMean < (obsidianMean * 3.0) + 1.5,
            $"liquid-glass ({liquidMean:0.00} ms) is more than 3x the opaque baseline ({obsidianMean:0.00} ms)");

        static double Measure(IslandRenderer renderer, SKSurface surface, IslandRenderState state)
        {
            renderer.ResetStats();
            for (var i = 0; i < 40; i++)
            {
                renderer.Render(surface.Canvas, state, IslandTestFixtures.Scale);
            }

            return renderer.Stats.MeanMilliseconds;
        }
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
