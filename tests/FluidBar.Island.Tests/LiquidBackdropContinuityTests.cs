using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class LiquidBackdropContinuityTests
{
    [Theory]
    [InlineData(0.1, false)]
    [InlineData(0.5, false)]
    [InlineData(0.9, false)]
    [InlineData(0.1, true)]
    [InlineData(0.5, true)]
    [InlineData(0.9, true)]
    public void Material_fade_keeps_the_captured_background_in_the_glass_body(double blend, bool outgoing)
    {
        var liquid = IslandTheme.Dark.WithMaterial(IslandMaterial.LiquidGlass);
        var state = IslandTestFixtures.MediaCompact() with { Theme = liquid };
        var size = IslandMetrics.SurfaceSize(state.Mode);
        using var backdrop = new SKBitmap((int)size.Width, (int)size.Height);
        backdrop.Erase(new SKColor(80, 120, 180));
        using var image = SKImage.FromBitmap(backdrop);
        var from = outgoing ? liquid : IslandTheme.Dark;
        var to = outgoing ? IslandTheme.Dark : liquid;
        state = state with { Theme = to, Backdrop = image, BackdropLuminance = 0.4, PreviousTheme = from, MaterialBlend = blend };
        using var renderer = new IslandRenderer();
        using var oldBody = renderer.RenderToBitmap(state with { Theme = from, PreviousTheme = null }, 1);
        using var newBody = renderer.RenderToBitmap(state with { PreviousTheme = null }, 1);
        using var bitmap = renderer.RenderToBitmap(state, 1);
        var shell = IslandMetrics.ShellRect(state);
        var pixel = bitmap.GetPixel((int)shell.MidX, (int)(shell.Top + shell.Height * 0.62));
        Assert.True(pixel.Alpha >= 240, $"Material blend {blend} lost the captured background: alpha {pixel.Alpha}.");
        var before = oldBody.GetPixel((int)shell.MidX, (int)(shell.Top + shell.Height * 0.62));
        var after = newBody.GetPixel((int)shell.MidX, (int)(shell.Top + shell.Height * 0.62));
        Assert.InRange(pixel.Red, before.Red * (1 - blend) + after.Red * blend - 8, before.Red * (1 - blend) + after.Red * blend + 8);
        Assert.InRange(pixel.Green, before.Green * (1 - blend) + after.Green * blend - 8, before.Green * (1 - blend) + after.Green * blend + 8);
        Assert.InRange(pixel.Blue, before.Blue * (1 - blend) + after.Blue * blend - 8, before.Blue * (1 - blend) + after.Blue * blend + 8);
        Assert.True(state.RequiresLiquidBackdrop);
        Assert.Equal(!outgoing, (state with { MaterialBlend = 1 }).RequiresLiquidBackdrop);
    }
}
