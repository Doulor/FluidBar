using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Typography and contrast guards for the settings window (task-34 audit outcome).
/// <para>
/// Every pane is laid out for real and every visible text run is checked: no UI text below 8.5 px
/// (12.75 device px at 150 % DPI) and a WCAG contrast ratio of at least 4.5:1 against the surface it
/// is painted on. Disabled controls are exempt, matching the WCAG rule Fluent follows.
/// </para>
/// </summary>
public sealed class SettingsTypographyTests
{
    private const double MinimumFontSize = 8.5;
    private const double MinimumContrast = 4.5;

    [Theory]
    [InlineData(SettingsWindowSection.General, "zh-Hans")]
    [InlineData(SettingsWindowSection.General, "en-US")]
    [InlineData(SettingsWindowSection.Appearance, "zh-Hans")]
    [InlineData(SettingsWindowSection.Appearance, "en-US")]
    [InlineData(SettingsWindowSection.Island, "zh-Hans")]
    [InlineData(SettingsWindowSection.Island, "en-US")]
    [InlineData(SettingsWindowSection.Plugins, "zh-Hans")]
    [InlineData(SettingsWindowSection.Plugins, "en-US")]
    [InlineData(SettingsWindowSection.Marketplace, "zh-Hans")]
    [InlineData(SettingsWindowSection.Marketplace, "en-US")]
    [InlineData(SettingsWindowSection.Advanced, "zh-Hans")]
    [InlineData(SettingsWindowSection.Advanced, "en-US")]
    [InlineData(SettingsWindowSection.About, "zh-Hans")]
    [InlineData(SettingsWindowSection.About, "en-US")]
    [InlineData(SettingsWindowSection.AgentSupport, "zh-Hans")]
    [InlineData(SettingsWindowSection.AgentSupport, "en-US")]
    public async Task PaneText_MeetsMinimumSizeAndContrast(SettingsWindowSection section, string culture)
    {
        var issues = await AvaloniaTest.Run(() =>
        {
            var localizer = LocalizationService.CreateDefault(culture, showMissingKeys: false, enablePseudoLocale: false);
            var (controller, _) = TestFixtures.CreateSettings(AppSettings.Default);
            var window = new SettingsWindow(controller, localizer, section);
            window.Show();
            try
            {
                var pane = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .First(scroll => Math.Abs(scroll.Padding.Top - 24) < 0.1);
                var issues = new List<string>();
                foreach (var text in pane.GetVisualDescendants().OfType<TextBlock>())
                {
                    var value = text.Text;
                    if (string.IsNullOrWhiteSpace(value) || !text.IsVisible || text.Bounds.Width <= 0)
                    {
                        continue;
                    }

                    if (IsInsideDisabledControl(text))
                    {
                        continue;
                    }

                    if (text.FontSize < MinimumFontSize - 0.01)
                    {
                        issues.Add($"size {text.FontSize:0.0}px: {Shorten(value)}");
                    }

                    var foreground = (text.Foreground as ISolidColorBrush)?.Color;
                    if (foreground is null)
                    {
                        continue;
                    }

                    var background = ResolveBackground(text);
                    var ratio = ContrastRatio(foreground.Value, background);
                    if (ratio < MinimumContrast)
                    {
                        issues.Add($"contrast {ratio:0.00}:1 ({foreground} on {background}): {Shorten(value)}");
                    }
                }

                return issues;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(issues.Count == 0, $"{section}/{culture}: {string.Join(" | ", issues.Take(8))}");
    }

    private static string Shorten(string value) => value.Length > 40 ? value[..40] : value;

    private static bool IsInsideDisabledControl(Visual visual)
    {
        for (var current = visual.GetVisualParent(); current is not null; current = current.GetVisualParent())
        {
            if (current is Control { IsEnabled: false })
            {
                return true;
            }
        }

        return false;
    }

    private static Color ResolveBackground(Visual visual)
    {
        for (var current = visual.GetVisualParent(); current is not null; current = current.GetVisualParent())
        {
            IBrush? brush = current switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Button button => button.Background,
                ContentControl content => content.Background,
                _ => null,
            };
            if (brush is ISolidColorBrush { Color.A: > 200 } solid)
            {
                return solid.Color;
            }
        }

        return Color.Parse("#0C0C0F");
    }

    private static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
