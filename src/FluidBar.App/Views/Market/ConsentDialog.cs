using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Theme;

namespace FluidBar.App.Views.Market;

/// <summary>
/// Capability consent dialog (spec 6.5). The copy is deliberately honest: capabilities are a
/// disclosure and the plugin runs with the same rights as the user. Modal; returns true only when the
/// user explicitly accepts.
/// </summary>
public sealed class ConsentDialog : Window
{
    private readonly ILocalizer _localizer;
    private readonly MarketPlugin _plugin;
    private readonly AppSurfaceColors _surfaces;

    /// <summary>Creates the consent dialog for <paramref name="plugin"/>.</summary>
    public ConsentDialog(ILocalizer localizer, MarketPlugin plugin, AppSurfaceColors surfaces)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _surfaces = surfaces;

        Title = localizer.Format(LocKeys.Market.Consent.Title, ("plugin", plugin.Name));
        Width = 480;
        Height = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = new SolidColorBrush(surfaces.Window);
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var foreground = new SolidColorBrush(_surfaces.Foreground);
        var secondary = new SolidColorBrush(_surfaces.ForegroundSecondary);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.Format(LocKeys.Market.Consent.Title, ("plugin", _plugin.Name)),
            FontFamily = Fonts.DisplaySemiBold.FontFamily,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
        });

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.Format(LocKeys.Settings.Plugins.By, ("author", _plugin.Publisher))
                + "  ·  " + _localizer.Format(LocKeys.Settings.Market.Latest, ("version", _plugin.LatestVersion)),
            FontSize = Typography.SettingsCaptionSize,
            Foreground = secondary,
        });

        panel.Children.Add(new TextBlock
        {
            Text = _localizer.Get(LocKeys.Market.Consent.CapabilitiesTitle),
            FontSize = Typography.SettingsBodySize,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            Margin = new Thickness(0, 4, 0, 0),
        });

        var capabilityPanel = new StackPanel { Spacing = 6 };
        foreach (var capability in _plugin.Capabilities)
        {
            var text = _localizer.Get(capability.LocalizationKey);
            if (!string.IsNullOrEmpty(capability.Detail))
            {
                text = text.Replace("{domains}", capability.Detail, StringComparison.Ordinal)
                    .Replace("{detail}", capability.Detail, StringComparison.Ordinal);
            }

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = "•", Foreground = new SolidColorBrush(Accents.Info), FontSize = 14 });
            row.Children.Add(new TextBlock { Text = text, Foreground = foreground, FontSize = Typography.SettingsBodySize, TextWrapping = TextWrapping.Wrap });
            capabilityPanel.Children.Add(row);
        }

        panel.Children.Add(capabilityPanel);

        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(_surfaces.Raised),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = _localizer.Get(LocKeys.Market.Consent.RiskNote),
                Foreground = secondary,
                FontSize = Typography.SettingsCaptionSize,
                TextWrapping = TextWrapping.Wrap,
            },
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var decline = new Button { Content = _localizer.Get(LocKeys.Market.Consent.Decline) };
        decline.Click += (_, _) => Close(false);
        var accept = new Button
        {
            Content = _localizer.Get(LocKeys.Market.Consent.Accept),
            Background = new SolidColorBrush(Accents.Info),
            Foreground = Brushes.White,
        };
        accept.Click += (_, _) => Close(true);
        buttons.Children.Add(decline);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        return panel;
    }
}
