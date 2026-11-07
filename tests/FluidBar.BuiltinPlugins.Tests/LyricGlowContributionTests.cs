using FluidBar.Sdk.Contributions;

namespace FluidBar.BuiltinPlugins.Tests;

public sealed class LyricGlowContributionTests
{
    [Fact]
    public void Media_detail_exposes_percentage_slider_with_reduced_default()
    {
        var page = Assert.Single(FluidBar.Plugins.Builtin.MediaPlugin.Contributions.SettingsPages!);
        var control = Assert.Single(page.Controls!, control => control.Key == "media.lyricGlowStrength");
        Assert.Equal(SettingControlType.Slider, control.Type);
        Assert.Equal(0, control.Min);
        Assert.Equal(100, control.Max);
        Assert.Equal(5, control.Step);
        Assert.Equal("%", control.Unit);
        Assert.Equal(70, control.Default!.Value.GetDouble());
        Assert.NotNull(control.Description);
    }
}
