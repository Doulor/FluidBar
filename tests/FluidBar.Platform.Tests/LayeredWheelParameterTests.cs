using System.Reflection;
using FluidBar.Platform.Windows.Hosting;

namespace FluidBar.Platform.Tests;

public sealed class LayeredWheelParameterTests
{
    [Theory]
    [InlineData(0xFF880009L)]
    [InlineData(-7864311L)]
    public void Negative_wheel_delta_does_not_overflow_mouse_button_or_modifier_decoding(long packed)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var buttons = (LayeredPointerButtons)typeof(LayeredWindowHost).GetMethod("DecodeButtons", flags)!.Invoke(null, [new IntPtr(packed)])!;
        Assert.True(buttons.HasFlag(LayeredPointerButtons.Left));
        var modifiers = (LayeredKeyModifiers)typeof(LayeredWindowHost).GetMethod("DecodeModifiers", flags)!.Invoke(null, [new IntPtr(packed)])!;
        Assert.True(modifiers.HasFlag(LayeredKeyModifiers.Control));
    }
}
