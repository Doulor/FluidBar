using FluidBar.Platform.Windows.Policies;
using FluidBar.Platform.Windows.Shell;

namespace FluidBar.Platform.Tests;

public sealed class TopmostPolicyTests
{
    private static readonly TimeSpan NotThrottled = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("Xaml_WindowedPopupClass")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("#32768")]
    [InlineData("tooltips_class32")]
    public void ShellClasses_AreRecognisedCaseInsensitively(string className)
    {
        Assert.True(ShellWindowClasses.IsShellWindowClass(className));
        Assert.True(ShellWindowClasses.IsShellWindowClass(className.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("Notepad")]
    [InlineData("")]
    [InlineData(null)]
    public void NonShellClasses_AreNotRecognised(string? className)
    {
        Assert.False(ShellWindowClasses.IsShellWindowClass(className));
    }

    [Fact]
    public void Evaluate_ReassertsForNormalForeground()
    {
        var decision = TopmostPolicy.Evaluate(new TopmostContext("Chrome_WidgetWin_1", true, null, NotThrottled));
        Assert.Equal(TopmostDecision.Reassert, decision);
    }

    [Fact]
    public void Evaluate_SkipsWhenHidden()
    {
        Assert.Equal(TopmostDecision.SkipHidden,
            TopmostPolicy.Evaluate(new TopmostContext("Notepad", false, null, NotThrottled)));
    }

    [Fact]
    public void Evaluate_SkipsWhenShellOwnsForeground()
    {
        Assert.Equal(TopmostDecision.SkipShellForeground,
            TopmostPolicy.Evaluate(new TopmostContext("Shell_TrayWnd", true, null, NotThrottled)));
        Assert.Equal(TopmostDecision.SkipShellForeground,
            TopmostPolicy.Evaluate(new TopmostContext("#32768", true, null, NotThrottled)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Evaluate_SkipsWhileSuppressed(int userNotificationState)
    {
        Assert.Equal(TopmostDecision.SkipSuppressed,
            TopmostPolicy.Evaluate(new TopmostContext("game", true, userNotificationState, NotThrottled)));
    }

    [Fact]
    public void Evaluate_ThrottlesToOncePerSecond()
    {
        Assert.Equal(TopmostDecision.SkipThrottled,
            TopmostPolicy.Evaluate(new TopmostContext("Notepad", true, null, TimeSpan.FromMilliseconds(200))));
        Assert.Equal(TopmostDecision.Reassert,
            TopmostPolicy.Evaluate(new TopmostContext("Notepad", true, null, TimeSpan.FromMilliseconds(1000))));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(null, false)]
    public void IsFullScreenState(int? state, bool expected)
    {
        Assert.Equal(expected, TopmostPolicy.IsFullScreenState(state));
    }

    [Fact]
    public void IsShellForeground_ClassifiesShellClasses()
    {
        Assert.False(ShellWindowClasses.IsShellWindowClass("HwndWrapper[FluidBar.exe;]"));
    }
}

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44u)]
    [InlineData("ctrl+alt+d", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44u)]
    [InlineData("Win+Shift+S", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x53u)]
    [InlineData("Ctrl+F12", HotkeyModifiers.Control, 0x7Bu)]
    [InlineData("Alt+Space", HotkeyModifiers.Alt, 0x20u)]
    [InlineData("Ctrl+Alt+1", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x31u)]
    [InlineData("Control+Shift+Left", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x25u)]
    public void TryParse_ValidGestures(string text, HotkeyModifiers modifiers, uint virtualKey)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(virtualKey, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Ctrl+D")]
    [InlineData("Ctrl+Alt+D+E")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+F25")]
    [InlineData(null)]
    public void TryParse_InvalidGesturesFail(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ToString_RoundTripsCanonicalForm()
    {
        Assert.True(HotkeyGesture.TryParse("Ctrl+Alt+D", out var gesture, out _));
        Assert.Equal("Ctrl+Alt+D", gesture.ToString());
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var reparsed, out _));
        Assert.Equal(gesture, reparsed);
    }

    [Fact]
    public void ToNativeModifiers_MirrorsWin32Flags()
    {
        var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x44);
        Assert.Equal(3u, gesture.ToNativeModifiers());
    }

    [Fact]
    public void Parse_ThrowsWithReasonOnInvalidInput()
    {
        var exception = Assert.Throws<FormatException>(() => HotkeyGesture.Parse("Ctrl+"));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    [Fact]
    public void HotkeyKeyFormatter_FormatsKnownKeys()
    {
        Assert.Equal("D", HotkeyKeyFormatter.Format(0x44));
        Assert.Equal("F1", HotkeyKeyFormatter.Format(0x70));
        Assert.Equal("F24", HotkeyKeyFormatter.Format(0x87));
        Assert.Equal("VolumeUp", HotkeyKeyFormatter.Format(0xAF));
        Assert.StartsWith("VK(0x", HotkeyKeyFormatter.Format(0xFE));
    }
}
