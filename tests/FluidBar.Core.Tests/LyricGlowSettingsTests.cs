using FluidBar.Core.Configuration;

namespace FluidBar.Core.Tests;

public sealed class LyricGlowSettingsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(35, 35)]
    [InlineData(70, 70)]
    [InlineData(100, 100)]
    [InlineData(-20, 0)]
    [InlineData(200, 100)]
    [InlineData(double.NaN, 70)]
    [InlineData(double.PositiveInfinity, 70)]
    [InlineData(double.NegativeInfinity, 70)]
    public void Percentage_is_bounded_and_nonfinite_uses_default(double input, double expected)
    {
        Assert.Equal(expected, LyricGlowSettings.NormalizePercent(input));
        Assert.Equal((float)(expected / 100), LyricGlowSettings.ToStrength(input));
    }
}
