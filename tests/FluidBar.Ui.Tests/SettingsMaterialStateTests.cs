using System.Linq;
using System.Reflection;
using Avalonia.Media;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Material interaction-state contract: the pressed state used by every card must be the documented L4
/// inset (black 18 %, blur 4, y offset 8, negative spread), and the hover state must be the soft glow.
/// </summary>
public sealed class SettingsMaterialStateTests
{
    private static BoxShadows Shadow(string property) =>
        (BoxShadows)typeof(SettingsWindow).Assembly.GetTypes()
            .Single(type => type.Name == "SettingsMaterialStates")
            .GetProperty(property, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void Pressed_state_is_the_documented_L4_inset()
    {
        var shadow = Shadow("PressedShadow")[0];
        Assert.True(shadow.IsInset);
        Assert.Equal(4, shadow.Blur);
        Assert.Equal(8, shadow.OffsetY);
        Assert.Equal(-8, shadow.Spread);
        Assert.Equal(Color.FromArgb(46, 0, 0, 0), shadow.Color);
    }

    [Fact]
    public void Hover_state_is_a_soft_outer_glow()
    {
        var shadow = Shadow("HoverShadow")[0];
        Assert.False(shadow.IsInset);
        Assert.Equal(10, shadow.Blur);
        Assert.Equal(0, shadow.OffsetY);
        Assert.Equal(Color.FromArgb(20, 255, 255, 255), shadow.Color);
    }
}
