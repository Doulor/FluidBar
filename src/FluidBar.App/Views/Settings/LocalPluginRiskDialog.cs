using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Plugins;
using FluidBar.App.Theme;

namespace FluidBar.App.Views.Settings;

/// <summary>One-time warning shown before the first load of a local (unsigned, unreviewed) plugin version.</summary>
public sealed class LocalPluginRiskDialog : Window
{
    public LocalPluginRiskDialog(LocalPluginListItem plugin, SettingsPalette palette)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        Title = "Load local plugin";
        Width = 500;
        Height = 360;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = new SolidColorBrush(palette.Window);
        Content = BuildContent(plugin, palette);
    }

    private Control BuildContent(LocalPluginListItem plugin, SettingsPalette palette)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Load local plugin",
            FontFamily = Fonts.DisplaySemiBold.FontFamily,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.ForegroundBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"{plugin.Id}  ·  {plugin.Version}",
            FontSize = Typography.SettingsCaptionSize,
            Foreground = palette.SecondaryBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "This local plugin was not signed or reviewed by FluidBar. It runs in-process with the same Windows user rights as FluidBar and can access your data, network and UI. Only continue if you trust the source.",
            FontSize = Typography.SettingsBodySize,
            Foreground = palette.ForegroundBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Permissions declared by the plugin",
            FontSize = Typography.SettingsBodySize,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.ForegroundBrush,
            Margin = new Thickness(0, 4, 0, 0),
        });
        var permissions = new StackPanel { Spacing = 4 };
        if (plugin.Permissions.Count == 0)
        {
            permissions.Children.Add(new TextBlock
            {
                Text = "No capabilities declared.",
                Foreground = palette.SecondaryBrush,
                FontSize = Typography.SettingsCaptionSize,
            });
        }
        else
        {
            foreach (var permission in plugin.Permissions)
            {
                permissions.Children.Add(new TextBlock
                {
                    Text = "• " + permission,
                    Foreground = palette.ForegroundBrush,
                    FontSize = Typography.SettingsCaptionSize,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        panel.Children.Add(permissions);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        var load = new Button
        {
            Content = "Load anyway",
            Background = new SolidColorBrush(Accents.Info),
            Foreground = Brushes.White,
        };
        load.Click += (_, _) => Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(load);
        panel.Children.Add(buttons);
        return panel;
    }
}

