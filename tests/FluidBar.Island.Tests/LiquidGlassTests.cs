using FluidBar.Core.Activities;
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

    private static IslandTheme Obsidian => IslandTheme.Dark.WithMaterial(IslandMaterial.Obsidian);

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
    public void Preview_images_for_black_photo_and_white_backdrops()
    {
        var dir = Environment.GetEnvironmentVariable("FLUIDBAR_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var theme = Liquid;
        using var renderer = new IslandRenderer(theme);
        foreach (var (name, luminance) in new[] { ("black", 0.0), ("photo", 0.45), ("white", 0.98) })
        {
            var surface = IslandMetrics.SurfaceSize(IslandMode.Float);
            using var backdrop = new SKBitmap(new SKImageInfo(
                (int)(surface.Width * IslandTestFixtures.Scale),
                (int)(surface.Height * IslandTestFixtures.Scale),
                SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(backdrop))
            {
                var v = (byte)(luminance * 255);
                canvas.Clear(new SKColor(v, v, v));
            }

            using var image = SKImage.FromBitmap(backdrop);
            using var compact = renderer.RenderToBitmap(
                CompactState(theme) with { Backdrop = image, BackdropLuminance = luminance },
                IslandTestFixtures.Scale);
            IslandTestFixtures.SavePng(compact, Path.Combine(dir, $"compact-{name}.png"));

            // Edge profile: peak luma in a 4 DIP band along each side (shell coordinates, not the shadow).
            var shell = IslandMetrics.ShellRect(CompactState(theme));
            double Band(int fromX, int fromY, int toX, int toY)
            {
                var peak = 0d;
                for (var d = 0; d < 6; d++)
                {
                    var px = (int)Math.Round((shell.Left + fromX + ((toX - fromX) * d)) * IslandTestFixtures.Scale);
                    var py = (int)Math.Round((shell.Top + fromY + ((toY - fromY) * d)) * IslandTestFixtures.Scale);
                    peak = Math.Max(peak, Luma(compact.GetPixel(px, py)));
                }

                return Math.Round(peak, 1);
            }

            var metrics = new
            {
                name,
                left = Band(1, (int)(shell.Height / 2), 1, (int)(shell.Height / 2)),
                top = Band((int)(shell.Width / 2), 1, (int)(shell.Width / 2), 1),
                bottom = Band((int)(shell.Width / 2), (int)shell.Height - 1, (int)(shell.Width / 2), (int)shell.Height - 1),
                centre = Math.Round(Luma(compact.GetPixel(
                    (int)Math.Round((shell.Left + (shell.Width / 2)) * IslandTestFixtures.Scale),
                    (int)Math.Round((shell.Top + (shell.Height / 2)) * IslandTestFixtures.Scale))), 1),
            };
            File.AppendAllText(Path.Combine(dir, "metrics.jsonl"), System.Text.Json.JsonSerializer.Serialize(metrics) + Environment.NewLine);

            var expandedState = IslandTestFixtures.Expanded(1) with { Theme = theme };
            using var expanded = renderer.RenderToBitmap(
                expandedState with { Backdrop = image, BackdropLuminance = luminance },
                IslandTestFixtures.Scale);
            IslandTestFixtures.SavePng(expanded, Path.Combine(dir, $"expanded-{name}.png"));
        }
    }

    [Fact]
    public void Soften_does_not_clip_the_split_capsules()
    {
        var theme = Liquid;
        var islets = new List<IslandSplitIslet>
        {
            new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true),
        };
        var state = IslandTestFixtures.MediaCompact() with
        {
            Theme = theme,
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets = islets.ToArray(),
        };
        using var renderer = new IslandRenderer(theme);
        using var sharp = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        using var softened = renderer.RenderToBitmap(state with { YieldProgress = 1 }, IslandTestFixtures.Scale);

        var ball = IslandMetrics.SplitIsletRects(state)[0];
        var x = (int)Math.Round((ball.Left + (ball.Width * 0.25)) * IslandTestFixtures.Scale);
        var y = (int)Math.Round((ball.MidY + (ball.Height * 0.25)) * IslandTestFixtures.Scale);

        var sharpAlpha = sharp.GetPixel(x, y).Alpha;
        var softenedAlpha = softened.GetPixel(x, y).Alpha;

        Assert.True(sharpAlpha > 60, $"the capsule must be painted while normal, got {sharpAlpha}");
        Assert.True(softenedAlpha > 10, $"the soften layer must not clip the capsule away, got {softenedAlpha}");
    }



    [Fact]
    public void White_backdrops_flip_the_shell_to_light_glass_with_dark_ink()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        using var panel = RenderOnWhite(renderer, state);
        var shell = IslandMetrics.ShellRect(state);

        // Empty padding inside the pill's left end = the glass surface itself.
        var padX = (int)Math.Round((shell.Left + 6) * IslandTestFixtures.Scale);
        var padY = (int)Math.Round(shell.MidY * IslandTestFixtures.Scale);
        var glassLuma = Luma(panel.GetPixel(padX, padY)) / 255.0;

        // Darkest ink anywhere in the title band: the labels must be dark on light glass.
        var minInk = 1.0;
        for (var x = shell.Left + (shell.Width * 0.28); x < shell.Right - 8; x += 1.5)
        {
            for (var dy = -7.0; dy <= 7.0; dy += 1.5)
            {
                var pixel = panel.GetPixel(
                    (int)Math.Round(x * IslandTestFixtures.Scale),
                    (int)Math.Round((shell.MidY + dy) * IslandTestFixtures.Scale));
                if (pixel.Alpha < 120)
                {
                    continue;
                }

                minInk = Math.Min(minInk, Luma(pixel) / 255.0);
            }
        }

        Assert.True(glassLuma > 0.5, $"white pages need light glass, got {glassLuma:0.00}");
        Assert.True(minInk < 0.4, $"white pages need dark ink, darkest pixel was {minInk:0.00}");
    }

    [Fact]
    public void Expanded_media_panel_on_white_flips_to_light_glass()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(1) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        using var panel = RenderOnWhite(renderer, state);
        var shell = IslandMetrics.ShellRect(state);

        var padX = (int)Math.Round((shell.Left + 10) * IslandTestFixtures.Scale);
        var padY = (int)Math.Round((shell.Top + (shell.Height * 0.5)) * IslandTestFixtures.Scale);
        var glassLuma = Luma(panel.GetPixel(padX, padY)) / 255.0;

        Assert.True(glassLuma > 0.45, $"the expanded panel must stay light on white, got {glassLuma:0.00}");
    }

    private SKBitmap RenderOnWhite(IslandRenderer renderer, IslandRenderState state)
    {
        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var white = new SKBitmap(info);
        using (var canvas = new SKCanvas(white))
        {
            canvas.Clear(SKColors.White);
        }

        using var image = SKImage.FromBitmap(white);
        return renderer.RenderToBitmap(state with { Backdrop = image, BackdropLuminance = 1.0 }, IslandTestFixtures.Scale);
    }



    [Fact]
    public void The_rim_dissolves_with_the_interior_instead_of_jumping()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(0) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var darkBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(darkBitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x20, 0x20));
        }

        using var lightBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(lightBitmap))
        {
            canvas.Clear(new SKColor(0xE0, 0xE0, 0xE0));
        }

        using var dark = SKImage.FromBitmap(darkBitmap);
        using var light = SKImage.FromBitmap(lightBitmap);

        using var solidDark = renderer.RenderToBitmap(
            state with { Backdrop = dark, BackdropBlend = 1, BackdropLuminance = 0.5 }, IslandTestFixtures.Scale);
        using var solidLight = renderer.RenderToBitmap(
            state with { Backdrop = light, BackdropBlend = 1, BackdropLuminance = 0.5 }, IslandTestFixtures.Scale);
        using var mid = renderer.RenderToBitmap(
            state with { Backdrop = light, PreviousBackdrop = dark, BackdropBlend = 0.5, BackdropLuminance = 0.5 },
            IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var x = (int)Math.Round((shell.Left + 3) * IslandTestFixtures.Scale);
        var y = (int)Math.Round((shell.Top + (shell.Height / 2)) * IslandTestFixtures.Scale);

        var rimDark = Luma(solidDark.GetPixel(x, y));
        var rimLight = Luma(solidLight.GetPixel(x, y));
        var rimMid = Luma(mid.GetPixel(x, y));

        Assert.True(rimLight > rimDark + 8, $"the rim must react to the capture ({rimDark:0} vs {rimLight:0})");
        Assert.True(
            rimMid > rimDark + 2 && rimMid < rimLight - 2,
            $"the rim must dissolve with the interior ({rimDark:0} .. {rimMid:0} .. {rimLight:0})");
    }

    [Fact]
    public void SideRail_InheritsTheShellMaterialWithoutDetachedPreview()
    {
        var state = IslandTestFixtures.Expanded(0) with { Archipelago = new ArchipelagoState { Visible = true, Opacity = 1 } };
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var strip = renderer.MeasureStrip(state);
        var frame = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
        Assert.Equal(0, IslandTestFixtures.CountOpaqueInside(frame, strip.Strip, IslandTestFixtures.Scale, 0));
    }

    [Fact]
    public void Cross_fading_the_backdrop_never_makes_the_shell_transparent()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var darkBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(darkBitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x20, 0x20));
        }

        using var lightBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(lightBitmap))
        {
            canvas.Clear(new SKColor(0xE0, 0xE0, 0xE0));
        }

        using var dark = SKImage.FromBitmap(darkBitmap);
        using var light = SKImage.FromBitmap(lightBitmap);

        foreach (var blend in new[] { 0.25, 0.5, 0.75 })
        {
            using var frame = renderer.RenderToBitmap(
                state with
                {
                    Backdrop = light,
                    PreviousBackdrop = dark,
                    BackdropBlend = blend,
                    BackdropLuminance = 0.5,
                },
                IslandTestFixtures.Scale);

            var shell = IslandMetrics.ShellRect(state);
            var x = (int)Math.Round((shell.Left + 6) * IslandTestFixtures.Scale);
            var y = (int)Math.Round(shell.MidY * IslandTestFixtures.Scale);
            var alpha = frame.GetPixel(x, y).Alpha;

            // Drawing the old and new capture each at partial alpha leaves a hole: the composite alpha
            // drops below 255 and the desktop shows through sharp — the "island flashes" report.
            Assert.True(alpha > 240, $"blend {blend:0.00} left the shell at alpha {alpha}");
        }
    }

    [Fact]
    public void Backdrop_captures_cross_fade_instead_of_swapping()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var darkBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(darkBitmap))
        {
            canvas.Clear(new SKColor(0x20, 0x20, 0x20));
        }

        using var lightBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(lightBitmap))
        {
            canvas.Clear(new SKColor(0xE0, 0xE0, 0xE0));
        }

        using var dark = SKImage.FromBitmap(darkBitmap);
        using var light = SKImage.FromBitmap(lightBitmap);

        // Same luminance for all three so only the image blend is measured.
        using var solidDark = renderer.RenderToBitmap(
            state with { Backdrop = dark, BackdropBlend = 1, BackdropLuminance = 0.5 }, IslandTestFixtures.Scale);
        using var solidLight = renderer.RenderToBitmap(
            state with { Backdrop = light, BackdropBlend = 1, BackdropLuminance = 0.5 }, IslandTestFixtures.Scale);
        using var mid = renderer.RenderToBitmap(
            state with
            {
                Backdrop = light,
                PreviousBackdrop = dark,
                BackdropBlend = 0.5,
                BackdropLuminance = 0.5,
            },
            IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var x = (int)Math.Round((shell.Left + 6) * IslandTestFixtures.Scale);
        var y = (int)Math.Round(shell.MidY * IslandTestFixtures.Scale);
        var lumaDark = Luma(solidDark.GetPixel(x, y));
        var lumaLight = Luma(solidLight.GetPixel(x, y));
        var lumaMid = Luma(mid.GetPixel(x, y));

        Assert.True(lumaLight > lumaDark + 20, $"the two captures must differ ({lumaDark:0} vs {lumaLight:0})");
        Assert.True(
            lumaMid > lumaDark + 3 && lumaMid < lumaLight - 3,
            $"the blended frame must sit between them ({lumaDark:0} .. {lumaMid:0} .. {lumaLight:0})");
    }

    [Fact]
    public void Split_capsules_render_the_backdrop_like_the_shell()
    {
        var theme = Liquid;
        var islets = new List<IslandSplitIslet>
        {
            new(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true),
        };
        var state = IslandTestFixtures.MediaCompact() with
        {
            Theme = theme,
            SplitProgress = 1,
            SplitIsletHeight = 40,
            SplitIslets = islets.ToArray(),
        };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bright = new SKBitmap(info);
        using (var canvas = new SKCanvas(bright))
        {
            canvas.Clear(new SKColor(0xF0, 0xF0, 0xF0));
        }

        using var image = SKImage.FromBitmap(bright);
        using var withBackdrop = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.94 }, IslandTestFixtures.Scale);
        using var withoutBackdrop = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var ball = IslandMetrics.SplitIsletRects(state)[0];
        var x = (int)Math.Round((ball.Left + (ball.Width * 0.22)) * IslandTestFixtures.Scale);
        var y = (int)Math.Round((ball.MidY + (ball.Height * 0.22)) * IslandTestFixtures.Scale);

        var glass = Luma(withBackdrop.GetPixel(x, y));
        var opaque = Luma(opaquePixel(withoutBackdrop, x, y));

        Assert.True(
            glass > opaque + 40,
            $"the split capsule must show the blurred desktop (glass {glass:0} vs opaque {opaque:0})");

        static SKColor opaquePixel(SKBitmap bitmap, int px, int py) => bitmap.GetPixel(px, py);
    }

    [Fact]
    public void Stacked_ghost_layers_render_the_backdrop_like_the_shell()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.MediaCompact() with { Theme = theme, GhostCount = 1, StackSpin = 0 };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bright = new SKBitmap(info);
        using (var canvas = new SKCanvas(bright))
        {
            canvas.Clear(new SKColor(0xF0, 0xF0, 0xF0));
        }

        using var image = SKImage.FromBitmap(bright);
        using var withBackdrop = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.94 }, IslandTestFixtures.Scale);
        using var withoutBackdrop = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var ghostTop = shell.Top + LayoutTokens.GhostOffsetY1;
        var ghostHeight = shell.Height * LayoutTokens.GhostScale1;
        var x = (int)Math.Round((shell.Left + (shell.Width * 0.5)) * IslandTestFixtures.Scale);
        // The shell paints over the ghosts, so sample the strip only the first ghost occupies.
        var y = (int)Math.Round((shell.Top - 3) * IslandTestFixtures.Scale);

        var glass = Luma(withBackdrop.GetPixel(x, y));
        var opaque = Luma(withoutBackdrop.GetPixel(x, y));
        Assert.True(
            glass > opaque + 30,
            $"a stacked ghost must show the blurred desktop (glass {glass:0} vs opaque {opaque:0})");
    }

    [Fact]
    public void Refraction_band_fades_out_instead_of_stepping_at_its_inner_edge()
    {
        // Opt-in: see Refraction_band_refracts_the_backdrop_near_the_rim.
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(1) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var backdropBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(backdropBitmap))
        {
            canvas.Clear(new SKColor(0x30, 0x30, 0x30));
            using var light = new SKPaint { Color = new SKColor(0xE0, 0xE0, 0xE0) };
            canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height / 2f), light);
        }

        using var image = SKImage.FromBitmap(backdropBitmap);
        using var panel = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.6 }, IslandTestFixtures.Scale);

        // Walk inwards from the left rim across the band: no single step may exceed the threshold, which
        // is what a hard-clipped band produces at its inner edge.
        var shell = IslandMetrics.ShellRect(state);
        var y = (int)Math.Round((shell.Top + (shell.Height / 2)) * IslandTestFixtures.Scale);
        var previous = double.NaN;
        var maxStep = 0d;
        for (var d = 1.0; d <= 18.0; d += 1.0)
        {
            var px = (int)Math.Round((shell.Left + d) * IslandTestFixtures.Scale);
            var luma = Luma(panel.GetPixel(px, y));
            if (!double.IsNaN(previous))
            {
                maxStep = Math.Max(maxStep, Math.Abs(luma - previous));
            }

            previous = luma;
        }

        Assert.True(maxStep < 22, $"the rim band must fade out, max step was {maxStep:0.0}");
    }

    [Fact]
    public void Glass_edges_fade_out_on_a_black_backdrop()
    {
        var theme = Liquid;
        var state = IslandTestFixtures.Expanded(1) with { Theme = theme };
        using var renderer = new IslandRenderer(theme);

        var surface = IslandMetrics.SurfaceSize(state.Mode);
        var info = new SKImageInfo((int)(surface.Width * IslandTestFixtures.Scale),
            (int)(surface.Height * IslandTestFixtures.Scale), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var black = new SKBitmap(info);
        using (var canvas = new SKCanvas(black))
        {
            canvas.Clear(SKColors.Black);
        }

        using var image = SKImage.FromBitmap(black);
        using var dark = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 0.0 }, IslandTestFixtures.Scale);
        using var bright = renderer.RenderToBitmap(
            state with { Backdrop = image, BackdropLuminance = 1.0 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var y = (int)Math.Round((shell.Top + (shell.Height / 2)) * IslandTestFixtures.Scale);

        // Peak luma across the left rim band: that is where the 1 DIP key line sits.
        double PeakEdge(SKBitmap bitmap)
        {
            var peak = 0d;
            for (var d = 0; d <= 6; d++)
            {
                var px = (int)Math.Round((shell.Left + d) * IslandTestFixtures.Scale);
                peak = Math.Max(peak, Luma(bitmap.GetPixel(px, y)));
            }

            return peak;
        }

        var darkEdge = PeakEdge(dark);
        var brightEdge = PeakEdge(bright);

        Assert.True(
            darkEdge < 60,
            $"a black desktop must not get a bright outline (edge luma {darkEdge:0.0})");
        Assert.True(
            brightEdge - darkEdge > 25,
            $"the edge must scale with the backdrop (dark {darkEdge:0.0} vs bright {brightEdge:0.0})");
    }

    [Fact]
    public void Bright_backdrops_use_light_glass_and_dark_backdrops_dark_glass()
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

        using var black = new SKBitmap(info);
        using (var canvas = new SKCanvas(black))
        {
            canvas.Clear(SKColors.Black);
        }

        using var whiteImage = SKImage.FromBitmap(white);
        using var blackImage = SKImage.FromBitmap(black);
        using var bright = renderer.RenderToBitmap(
            state with { Backdrop = whiteImage, BackdropLuminance = 0.98 }, IslandTestFixtures.Scale);
        using var dark = renderer.RenderToBitmap(
            state with { Backdrop = blackImage, BackdropLuminance = 0.0 }, IslandTestFixtures.Scale);

        var shell = IslandMetrics.ShellRect(state);
        var x = (int)Math.Round((shell.Left + 12) * IslandTestFixtures.Scale);
        var y = (int)Math.Round((shell.Top + (shell.Height * 0.5)) * IslandTestFixtures.Scale);
        var brightLuma = Luma(bright.GetPixel(x, y));
        var darkLuma = Luma(dark.GetPixel(x, y));

        Assert.True(brightLuma > 140, $"a white page must give light glass, got {brightLuma:0.0}");
        Assert.True(brightLuma > darkLuma + 40, $"light/dark glass must differ ({brightLuma:0.0} vs {darkLuma:0.0})");
    }

    [Fact]
    public void Refraction_band_refracts_the_backdrop_near_the_rim()
    {
        // Opt-in: the band is disabled by default since it strobed the rim at capture rate.
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
        var left = (int)Math.Round((shell.Left + 8) * IslandTestFixtures.Scale);
        var right = (int)Math.Round((shell.Left + 68) * IslandTestFixtures.Scale);
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
