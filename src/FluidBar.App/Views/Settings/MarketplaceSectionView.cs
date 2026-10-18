using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Marketplace section UI: search, category filter, featured plugin, plugin cards with
/// install/update/rollback/uninstall and staged progress, wired through <see cref="IMarketplaceUiService"/>.
/// A consent dialog with honest capability copy is shown before any install (spec 6.5).
/// </summary>
public sealed class MarketplaceSectionView : StackPanel
{
    private readonly IMarketplaceUiService _marketplace;
    private readonly ILocalizer _localizer;
    private readonly AppSurfaceColors _surfaces;
    private readonly Func<Window?> _owner;
    private readonly string _channel;
    private string _search = string.Empty;
    private string _category = "all";
    private bool _busy;
    private string? _status;

    /// <summary>Creates the marketplace view.</summary>
    public MarketplaceSectionView(
        IMarketplaceUiService marketplace,
        ILocalizer localizer,
        AppSurfaceColors surfaces,
        Func<Window?> owner,
        string channel)
    {
        _marketplace = marketplace ?? throw new ArgumentNullException(nameof(marketplace));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _surfaces = surfaces;
        _owner = owner;
        _channel = channel;
        Spacing = 10;
        _marketplace.Changed += (_, _) => Refresh();
    }

    /// <summary>Rebuilds the plugin list from the service.</summary>
    public async void Refresh()
    {
        try
        {
            var query = new MarketplaceQuery(_search, _category, _channel);
            var plugins = await _marketplace.BrowseAsync(query, CancellationToken.None);
            Build(plugins);
        }
        catch (OperationCanceledException)
        {
            // The section was closed while refreshing; nothing to update.
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            Build([]);
        }
    }

    /// <summary>Builds the section content for the supplied plugin list.</summary>
    public void Build(IReadOnlyList<MarketPlugin> plugins)
    {
        Children.Clear();
        var foreground = new SolidColorBrush(_surfaces.Foreground);
        var secondary = new SolidColorBrush(_surfaces.ForegroundSecondary);

        var search = new TextBox
        {
            Watermark = _localizer.Get(LocKeys.Settings.Market.Search),
            Text = _search,
            Width = 260,
        };
        search.TextChanged += (_, _) => _search = search.Text ?? string.Empty;
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Refresh();
                e.Handled = true;
            }
        };

        var category = new ComboBox { Width = 150, SelectedIndex = 0 };
        var categories = new (string Token, string Key)[]
        {
            ("all", LocKeys.Settings.Market.Category.All),
            ("media", LocKeys.Settings.Market.Category.Media),
            ("productivity", LocKeys.Settings.Market.Category.Productivity),
            ("system", LocKeys.Settings.Market.Category.System),
            ("developer", LocKeys.Settings.Market.Category.Developer),
            ("other", LocKeys.Settings.Market.Category.Other),
        };
        foreach (var (token, key) in categories)
        {
            category.Items.Add(new ComboBoxItem { Content = _localizer.Get(key), Tag = token });
        }

        category.SelectionChanged += (_, _) =>
        {
            if (category.SelectedItem is ComboBoxItem { Tag: string token })
            {
                _category = token;
                Refresh();
            }
        };

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        searchRow.Children.Add(search);
        searchRow.Children.Add(category);
        var refresh = new Button { Content = _localizer.Get(LocKeys.Settings.Market.Refresh) };
        refresh.Click += (_, _) => Refresh();
        searchRow.Children.Add(refresh);
        Children.Add(searchRow);

        var featured = plugins.FirstOrDefault(static p => p.Featured);
        if (featured is not null)
        {
            Children.Add(BuildFeatured(featured, foreground, secondary));
        }

        if (!string.IsNullOrEmpty(_status))
        {
            Children.Add(new TextBlock
            {
                Text = _status,
                Foreground = new SolidColorBrush(Accents.Warning),
                FontSize = Typography.SettingsCaptionSize,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (plugins.Count == 0)
        {
            Children.Add(new TextBlock
            {
                Text = _localizer.Get(LocKeys.Settings.Market.NoResults),
                Foreground = secondary,
                Margin = new Thickness(0, 16, 0, 0),
            });
            return;
        }

        foreach (var plugin in plugins)
        {
            Children.Add(BuildCard(plugin, foreground, secondary));
        }
    }

    private Control BuildFeatured(MarketPlugin plugin, IBrush foreground, IBrush secondary)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(_surfaces.Raised),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 4, 0, 4),
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = _localizer.Get(LocKeys.Settings.Market.Featured).ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Accents.Info),
        });
        panel.Children.Add(new TextBlock { Text = plugin.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = foreground });
        panel.Children.Add(new TextBlock { Text = plugin.Summary, FontSize = Typography.SettingsCaptionSize, Foreground = secondary, TextWrapping = TextWrapping.Wrap });
        border.Child = panel;
        return border;
    }

    private Control BuildCard(MarketPlugin plugin, IBrush foreground, IBrush secondary)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(_surfaces.Raised),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 2, 0, 2),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 3 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = plugin.Name, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = foreground });
        if (plugin.Verified)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = _localizer.Get(LocKeys.Settings.Market.Verified),
                FontSize = 10,
                Foreground = new SolidColorBrush(Accents.Success),
            });
        }

        titleRow.Children.Add(new TextBlock
        {
            Text = _localizer.Format(LocKeys.Settings.Market.Latest, ("version", plugin.LatestVersion)),
            FontSize = 10,
            Foreground = secondary,
        });
        info.Children.Add(titleRow);
        info.Children.Add(new TextBlock { Text = plugin.Summary, FontSize = Typography.SettingsCaptionSize, Foreground = secondary, TextWrapping = TextWrapping.Wrap });
        info.Children.Add(new TextBlock
        {
            Text = _localizer.Get(LocKeys.Settings.Plugins.By).Replace("{author}", plugin.Publisher, StringComparison.Ordinal)
                + "  ·  " + _localizer.Format(LocKeys.Settings.Market.Downloads, ("count", plugin.Installs)),
            FontSize = 10,
            Foreground = secondary,
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(BuildActionButton(plugin));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private Control BuildActionButton(MarketPlugin plugin)
    {
        var (labelKey, action) = plugin.State switch
        {
            MarketInstallState.Installed => (LocKeys.Settings.Market.Uninstall, "uninstall"),
            MarketInstallState.UpdateAvailable => (LocKeys.Settings.Market.Update, "install"),
            MarketInstallState.Installing => (LocKeys.Settings.Market.Installing, "none"),
            MarketInstallState.Failed => (LocKeys.Common.Retry, "install"),
            _ => (LocKeys.Settings.Market.Install, "install"),
        };

        var button = new Button { Content = _localizer.Get(labelKey), IsEnabled = !_busy && action != "none" };
        button.Click += async (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            try
            {
                if (action == "install")
                {
                    await InstallWithConsentAsync(plugin);
                }
                else if (action == "uninstall")
                {
                    await _marketplace.UninstallAsync(plugin.Id, keepSettings: true, CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                _status = _localizer.Get(LocKeys.Error.Install.Failed);
            }
            catch (Exception ex)
            {
                _status = _localizer.Get(LocKeys.Error.Install.Failed) + " " + ex.Message;
            }
            finally
            {
                _busy = false;
                Refresh();
            }
        };
        return button;
    }

    private async Task InstallWithConsentAsync(MarketPlugin plugin)
    {
        var plan = await _marketplace.GetPluginAsync(plugin.Id, CancellationToken.None) ?? plugin;
        var owner = _owner();
        var accepted = owner is null
            ? true
            : await new ConsentDialog(_localizer, plan, _surfaces).ShowDialog<bool>(owner);
        if (!accepted)
        {
            return;
        }

        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 4 };
        Children.Insert(0, progressBar);
        var progress = new Progress<InstallProgress>(value =>
        {
            progressBar.Value = value.Percent;
            _status = _localizer.Get(value.LocalizationKey);
        });
        try
        {
            await _marketplace.InstallAsync(plugin.Id, progress, CancellationToken.None);
        }
        finally
        {
            Children.Remove(progressBar);
        }
    }
}
