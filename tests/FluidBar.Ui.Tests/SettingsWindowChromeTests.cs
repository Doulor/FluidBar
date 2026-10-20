using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards the settings window chrome: borderless rounded shell, a title bar that can start a move drag and
/// the window buttons (minimize next to close, same hit box, localized tooltips).
/// </summary>
public sealed class SettingsWindowChromeTests
{
    [Fact]
    public async Task Window_UsesBorderlessRoundedChrome()
    {
        var chrome = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.General);
            window.Show();
            try
            {
                var shell = Assert.IsType<Border>(window.Content);
                return (
                    window.WindowDecorations,
                    window.Background is ISolidColorBrush { Color.A: 0 },
                    window.TransparencyLevelHint.Contains(WindowTransparencyLevel.Transparent),
                    shell.CornerRadius.TopLeft,
                    shell.ClipToBounds);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(WindowDecorations.None, chrome.WindowDecorations);
        Assert.True(chrome.Item2, "the window surface must stay transparent so the rounded shell can breathe");
        Assert.True(chrome.Item3, "the window must request per-pixel transparency");
        Assert.True(chrome.TopLeft > 0, "the shell must paint the rounded corners");
        Assert.True(chrome.ClipToBounds, "the shell must clip its content to the rounded shape");
    }

    [Fact]
    public async Task TitleBar_IsHitTestable_SoTheWindowCanBeDragged()
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("en-US");
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.General);
            window.Show();
            try
            {
                var bar = FindTitleBar(window);
                // Press in the empty part of the title bar and see whether the event routes through it.
                // Avalonia 12 has no public InputHitTest, so the tunnel route is the honest probe: a press
                // that lands on the shell (the pre-fix behaviour) never passes the title bar grid.
                var received = 0;
                bar.AddHandler(InputElement.PointerPressedEvent, (_, _) => received++,
                    RoutingStrategies.Tunnel);
                var point = new Point(window.Bounds.Width / 2, 24);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);

                return (bar.Background is not null, received > 0, received);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.Item1, "the title bar grid must be hit-testable (drag target)");
        Assert.True(result.Item2, $"a click in the empty title bar must route through the drag target (presses seen: {result.Item3})");
    }

    [Theory]
    [InlineData("en-US", "Minimize")]
    [InlineData("zh-Hans", "最小化")]
    public async Task TitleBar_HasLocalizedMinimizeButton_ThatMinimizesTheWindow(string culture, string expectedTip)
    {
        var result = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer(culture);
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, SettingsWindowSection.General);
            window.Show();
            try
            {
                var bar = FindTitleBar(window);
                var tipped = bar.Children.OfType<Border>()
                    .Where(border => ToolTip.GetTip(border) is string)
                    .ToArray();
                Assert.Equal(2, tipped.Length);
                var minimize = tipped.Single(border =>
                    string.Equals(ToolTip.GetTip(border) as string, expectedTip, StringComparison.Ordinal));
                var close = tipped.Single(border => !ReferenceEquals(border, minimize));
                var tip = ToolTip.GetTip(minimize) as string;
                var closeTip = ToolTip.GetTip(close) as string;
                // Same hit box as the close button (32x32 DIP from the prototype).
                Assert.Equal(32, minimize.Width);
                Assert.Equal(32, minimize.Height);
                Assert.Equal(minimize.Width, close.Width);
                Assert.Equal(minimize.Height, close.Height);

                var before = window.WindowState;
                minimize.RaiseEvent(new PointerPressedEventArgs(
                    minimize,
                    new Pointer(0, PointerType.Mouse, true),
                    window,
                    default,
                    0,
                    new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
                    KeyModifiers.None));
                var after = window.WindowState;
                return (tip, closeTip, before, after);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(expectedTip, result.tip);
        Assert.False(string.IsNullOrWhiteSpace(result.closeTip));
        Assert.Equal(WindowState.Normal, result.before);
        Assert.Equal(WindowState.Minimized, result.after);
    }

    [Fact]
    public async Task MinimizeAndClose_AreLocalizedInBothCatalogs()
    {
        var (en, zh) = await AvaloniaTest.Run(() =>
        {
            foreach (var culture in new[] { "en-US", "zh-Hans" })
            {
                var localizer = TestFixtures.CreateLocalizer(culture);
                foreach (var key in new[] { LocKeys.Common.Minimize, LocKeys.Common.Close })
                {
                    var value = localizer.Get(key);
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{culture}: {key} must be localized");
                    Assert.DoesNotContain(key, value, StringComparison.Ordinal);
                }
            }

            return (TestFixtures.CreateLocalizer("en-US").Get(LocKeys.Common.Minimize),
                TestFixtures.CreateLocalizer("zh-Hans").Get(LocKeys.Common.Minimize));
        });

        Assert.NotEqual(en, zh);
    }

    private static Grid FindTitleBar(SettingsWindow window)
    {
        var shell = Assert.IsType<Border>(window.Content);
        var layout = Assert.IsType<Grid>(shell.Child);
        var titleBar = Assert.IsType<Border>(layout.Children[0]);
        return Assert.IsType<Grid>(titleBar.Child);
    }
}
