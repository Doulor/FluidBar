using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Guards the settings window chrome: borderless rounded shell and a title bar that can actually start a
/// move drag. The title bar used to be a background-less Grid, which is not hit-testable, so clicks on
/// its empty area fell through to the shell and <c>BeginMoveDrag</c> never ran.
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
                var shell = Assert.IsType<Border>(window.Content);
                var layout = Assert.IsType<Grid>(shell.Child);
                var titleBar = Assert.IsType<Border>(layout.Children[0]);
                var bar = Assert.IsType<Grid>(titleBar.Child);

                // Press in the empty part of the title bar and see whether the event routes through it.
                // Avalonia 12 has no public InputHitTest, so the tunnel route is the honest probe: a press
                // that lands on the shell (the pre-fix behaviour) never passes the title bar grid.
                var received = 0;
                bar.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, _) => received++,
                    Avalonia.Interactivity.RoutingStrategies.Tunnel);
                var point = new Point(window.Bounds.Width / 2, 24);
                window.MouseDown(point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);

                return (
                    titleBar.Background is not null,
                    bar.Background is not null,
                    received > 0,
                    received.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(result.Item1, "the title bar border must be hit-testable");
        Assert.True(result.Item2, "the title bar grid must be hit-testable (drag target)");
        Assert.True(result.Item3, $"a click in the empty title bar must route through the drag target (presses seen: {result.Item4})");
    }
}
