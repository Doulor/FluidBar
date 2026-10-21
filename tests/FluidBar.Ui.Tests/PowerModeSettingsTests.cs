using FluidBar.App.Settings;
using FluidBar.Core.Configuration;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Power-mode setting: the shared contract keeps <c>IslandPowerMode</c> in AppSettings, serialises it as
/// <c>powerMode</c> (case-insensitive, performance/saver) and defaults to Performance.
/// </summary>
public sealed class PowerModeSettingsTests
{
    [Fact]
    public void Default_is_performance()
    {
        Assert.Equal(IslandPowerMode.Performance, AppSettings.Default.PowerMode);
    }

    [Fact]
    public void Json_round_trips_the_power_mode()
    {
        var saver = AppSettings.Default with { PowerMode = IslandPowerMode.Saver };
        var json = saver.ToJson();
        Assert.Contains("\"powerMode\":\"saver\"", json.Replace(" ", string.Empty), StringComparison.Ordinal);

        var parsed = AppSettings.TryParse(json, out var error);
        Assert.NotNull(parsed);
        Assert.Null(error);
        Assert.Equal(IslandPowerMode.Saver, parsed!.PowerMode);
    }

    [Theory]
    [InlineData("performance", IslandPowerMode.Performance)]
    [InlineData("Performance", IslandPowerMode.Performance)]
    [InlineData("saver", IslandPowerMode.Saver)]
    [InlineData("SAVER", IslandPowerMode.Saver)]
    public void Json_parses_the_token_case_insensitively(string token, IslandPowerMode expected)
    {
        var json = AppSettings.Default.ToJson().Replace("performance", token, StringComparison.OrdinalIgnoreCase);
        var parsed = AppSettings.TryParse(json, out _);
        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed!.PowerMode);
    }
}
