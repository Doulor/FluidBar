using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Marketplace section UI matching the prototype: gradient featured card, search field, category chips
/// and a two-column card grid with install/update/rollback/uninstall. A consent dialog with honest
/// capability copy is shown before any install (spec 6.5).
/// </summary>
public sealed class MarketplaceSectionView : StackPanel
{
    private static readonly (string Token, string Key)[] Categories =
    [
        ("all", LocKeys.Settings.Market.Category.All),
        ("media", LocKeys.Settings.Market.Category.Media),
        ("productivity", LocKeys.Settings.Market.Category.Productivity),
        ("system", LocKeys.Settings.Market.Category.System),
        ("developer", LocKeys.Settings.Market.Category.Developer),
        ("other", LocKeys.Settings.Market.Category.Other),
    ];

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
        Spacing = 0;
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
        var tertiary = new SolidColorBrush(_surfaces.ForegroundTertiary);
        var line = new SolidColorBrush(_surfaces.Line);
        var raised = new SolidColorBrush(_surfaces.Raised);

        var featured = string.IsNullOrWhiteSpace(_search) && _category == "all"
            ? plugins.FirstOrDefault(static p => p.Featured)
            : null;
        if (featured is not null)
        {
            Children.Add(BuildFeatured(featured, foreground, secondary));
        }

        Children.Add(BuildToolbar(secondary, tertiary, line, raised));

        if (_marketplace.IsOfflineFallback)
        {
            Children.Add(new Border
            {
                Background = new SolidColorBrush(_surfaces.Raised),
                BorderBrush = new SolidColorBrush(Color.FromArgb(51, 255, 149, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = _marketplace.StatusMessage ?? _localizer.Get(LocKeys.Settings.Market.DemoCatalog),
                    Foreground = new SolidColorBrush(Accents.Warning),
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        if (!string.IsNullOrEmpty(_status))
        {
            Children.Add(new TextBlock
            {
                Text = _status,
                Foreground = new SolidColorBrush(Accents.Warning),
                FontSize = 9.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        if (plugins.Count == 0)
        {
            Children.Add(new TextBlock
            {
                Text = _localizer.Get(LocKeys.Settings.Market.NoResults),
                Foreground = tertiary,
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 30),
            });
        }
        else
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                Margin = new Thickness(0, 10, 0, 0),
            };
            for (var i = 0; i < plugins.Count; i++)
            {
                if (i % 2 == 0)
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }

                var card = BuildCard(plugins[i], foreground, secondary, tertiary, line, raised);
                card.Margin = new Thickness(i % 2 == 0 ? 0 : 4.5, 0, i % 2 == 0 ? 4.5 : 0, 9);
                Grid.SetRow(card, i / 2);
                Grid.SetColumn(card, i % 2);
                grid.Children.Add(card);
            }

            Children.Add(grid);
        }

        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                new SettingsGlyph
                {
                    Kind = IconKind.Check,
                    Brush = tertiary,
                    Width = 10,
                    Height = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = _localizer.Get(LocKeys.Settings.Market.Footnote),
                    FontSize = 8.5,
                    Foreground = tertiary,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 620,
                },
            },
        });
    }

    private Control BuildToolbar(IBrush secondary, IBrush tertiary, IBrush line, IBrush raised)
    {
        var field = new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = raised,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 10, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new SettingsGlyph
                    {
                        Kind = IconKind.Search,
                        Brush = tertiary,
                        Width = 14,
                        Height = 14,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            },
        };
        var search = new TextBox
        {
            Watermark = _localizer.Get(LocKeys.Settings.Market.Search),
            Text = _search,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        Grid.SetColumn(search, 1);
        ((Grid)((Border)field).Child!).Children.Add(search);
        search.TextChanged += (_, _) => _search = search.Text ?? string.Empty;
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Refresh();
                e.Handled = true;
            }
        };

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 9, 0, 10) };
        foreach (var (token, key) in Categories)
        {
            var active = string.Equals(token, _category, StringComparison.Ordinal);
            var chip = new Border
            {
                Height = 26,
                Padding = new Thickness(10, 0, 10, 0),
                CornerRadius = new CornerRadius(7),
                Background = active ? new SolidColorBrush(_surfaces.Active) : raised,
                BorderBrush = active ? Brushes.Transparent : line,
                BorderThickness = new Thickness(1),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = _localizer.Get(key),
                    FontSize = 9.5,
                    FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = active ? new SolidColorBrush(_surfaces.ActiveForeground) : secondary,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var categoryToken = token;
            chip.PointerPressed += (_, e) =>
            {
                _category = categoryToken;
                Refresh();
                e.Handled = true;
            };
            chips.Children.Add(chip);
        }

        var toolbar = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        toolbar.Children.Add(field);
        toolbar.Children.Add(chips);
        return toolbar;
    }

    private Control BuildFeatured(MarketPlugin plugin, IBrush foreground, IBrush secondary)
    {
        var copy = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        copy.Children.Add(new TextBlock
        {
            Text = _localizer.Get(LocKeys.Settings.Market.Featured).ToUpperInvariant(),
            FontSize = 8,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#7FD8FF")),
            Margin = new Thickness(0, 0, 0, 5),
        });
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontFamily = Fonts.Display,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
        });
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Summary,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(168, 232, 244, 250)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(0, 11, 0, 0) };
        actions.Children.Add(BuildFeaturedInstall(plugin));
        actions.Children.Add(new TextBlock
        {
            Text = _localizer.Format(LocKeys.Settings.Market.Downloads, ("count", plugin.Installs)),
            FontSize = 8.5,
            Foreground = new SolidColorBrush(Color.FromArgb(138, 232, 244, 250)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        copy.Children.Add(actions);

        var art = new Border
        {
            Width = 80,
            Height = 80,
            CornerRadius = new CornerRadius(19),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(71, 76, 201, 240), 0),
                    new GradientStop(Color.FromArgb(128, 10, 30, 40), 1),
                },
            },
            BorderBrush = new SolidColorBrush(Color.FromArgb(61, 127, 216, 255)),
            BorderThickness = new Thickness(1),
            Child = new SettingsGlyph
            {
                Kind = CategoryIcon(plugin.Category),
                Brush = new SolidColorBrush(Color.Parse("#C8F4FF")),
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16,
        };
        Grid.SetColumn(copy, 0);
        Grid.SetColumn(art, 1);
        body.Children.Add(copy);
        body.Children.Add(art);

        var host = new Grid();
        host.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.9, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#0C1A22"), 0),
                    new GradientStop(Color.Parse("#0A0F16"), 0.58),
                    new GradientStop(Color.Parse("#070A0E"), 1),
                },
            },
        });
        host.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.88, 0.08, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.88, 0.08, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(1.2, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.4, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(71, 76, 201, 240), 0),
                    new GradientStop(Color.FromArgb(0, 76, 201, 240), 0.48),
                },
            },
        });
        host.Children.Add(new Border { Padding = new Thickness(18), Child = body });

        return new Border
        {
            MinHeight = 122,
            CornerRadius = new CornerRadius(14),
            BorderBrush = new SolidColorBrush(Color.FromArgb(23, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = host,
        };
    }

    private Control BuildFeaturedInstall(MarketPlugin plugin)
    {
        var installed = plugin.State is MarketInstallState.Installed or MarketInstallState.Disabled;
        var installing = plugin.State is MarketInstallState.Installing;
        var label = installed
            ? _localizer.Get(LocKeys.Common.Installed)
            : installing
                ? _localizer.Get(LocKeys.Settings.Market.Installing)
                : _localizer.Get(LocKeys.Settings.Market.Install);

        var chip = new Border
        {
            Height = 28,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(7),
            Background = installed || installing
                ? new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))
                : new SolidColorBrush(Color.Parse("#EAFAFF")),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 9.5,
                FontWeight = FontWeight.SemiBold,
                Foreground = installed || installing
                    ? new SolidColorBrush(Color.FromArgb(180, 255, 255, 255))
                    : new SolidColorBrush(Color.Parse("#07151B")),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        if (!installed && !installing)
        {
            chip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            chip.PointerPressed += (_, e) =>
            {
                _ = RunInstallAsync(plugin);
                e.Handled = true;
            };
        }

        return chip;
    }

    private Control BuildCard(MarketPlugin plugin, IBrush foreground, IBrush secondary, IBrush tertiary,
        IBrush line, IBrush raised)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(CategoryColor(plugin.Category)),
            Child = new SettingsGlyph
            {
                Kind = CategoryIcon(plugin.Category),
                Brush = Brushes.White,
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        var titleBlock = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        nameRow.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (plugin.Verified)
        {
            nameRow.Children.Add(new SettingsGlyph
            {
                Kind = IconKind.Check,
                Brush = new SolidColorBrush(Accents.Success),
                Width = 10,
                Height = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        titleBlock.Children.Add(nameRow);
        titleBlock.Children.Add(new TextBlock
        {
            Text = plugin.Publisher + " · v" + plugin.LatestVersion,
            FontSize = 9,
            Foreground = tertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(titleBlock, 1);
        head.Children.Add(titleBlock);

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        foot.Children.Add(new TextBlock
        {
            Text = _localizer.Format(LocKeys.Settings.Market.Downloads, ("count", plugin.Installs)),
            FontSize = 8.5,
            Foreground = tertiary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(BuildActionButton(plugin));
        var rollback = BuildRollbackButton(plugin);
        if (rollback is not null)
        {
            actions.Children.Add(rollback);
        }

        Grid.SetColumn(actions, 1);
        foot.Children.Add(actions);

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = plugin.Summary,
            FontSize = 9.5,
            Foreground = secondary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        panel.Children.Add(foot);

        return new Border
        {
            MinHeight = 112,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = raised,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Child = panel,
        };
    }

    private static IconKind CategoryIcon(string category) => category switch
    {
        "media" => IconKind.Media,
        "productivity" => IconKind.Task,
        "system" => IconKind.Monitor,
        "developer" => IconKind.Agent,
        _ => IconKind.Store,
    };

    private static Color CategoryColor(string category) => category switch
    {
        "media" => Color.Parse("#3E6E86"),
        "productivity" => Color.Parse("#8A6A3A"),
        "system" => Color.Parse("#3F6E52"),
        "developer" => Color.Parse("#4A5686"),
        _ => Color.Parse("#4C5566"),
    };

    private Control BuildActionButton(MarketPlugin plugin)
    {
        var (labelKey, action) = plugin.State switch
        {
            MarketInstallState.Installed => (LocKeys.Settings.Market.Uninstall, "uninstall"),
            MarketInstallState.UpdateAvailable => (LocKeys.Settings.Market.Update, "install"),
            MarketInstallState.Installing => (LocKeys.Settings.Market.Installing, "none"),
            MarketInstallState.Disabled => (LocKeys.Settings.Market.Uninstall, "uninstall"),
            MarketInstallState.Failed => (LocKeys.Common.Retry, "install"),
            _ => (LocKeys.Settings.Market.Install, "install"),
        };

        var installed = plugin.State is MarketInstallState.Installed or MarketInstallState.Disabled;
        var button = new Button
        {
            Content = _localizer.Get(labelKey),
            IsEnabled = !_busy && action != "none" && !plugin.IsDemo && !_marketplace.IsOfflineFallback,
            Height = 27,
            MinWidth = 58,
            Padding = new Thickness(9, 0, 9, 0),
            CornerRadius = new CornerRadius(7),
            FontSize = 9.5,
            FontWeight = installed ? FontWeight.Normal : FontWeight.SemiBold,
            Background = installed ? new SolidColorBrush(_surfaces.Raised) : new SolidColorBrush(_surfaces.Active),
            Foreground = installed ? new SolidColorBrush(_surfaces.ForegroundSecondary) : new SolidColorBrush(_surfaces.ActiveForeground),
            BorderBrush = installed ? new SolidColorBrush(_surfaces.LineStrong) : Brushes.Transparent,
            BorderThickness = new Thickness(installed ? 1 : 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
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

    private async Task RunInstallAsync(MarketPlugin plugin)
    {
        if (_busy || plugin.IsDemo || _marketplace.IsOfflineFallback)
        {
            return;
        }

        _busy = true;
        try
        {
            await InstallWithConsentAsync(plugin);
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
    }

    private Control? BuildRollbackButton(MarketPlugin plugin)
    {
        if (plugin.IsDemo || plugin.InstalledVersion is null || plugin.State is MarketInstallState.Installing)
        {
            return null;
        }

        var target = plugin.VersionHistory.FirstOrDefault(version =>
            !string.Equals(version, plugin.InstalledVersion, StringComparison.Ordinal));
        if (target is null)
        {
            return null;
        }

        var button = new Button
        {
            Content = _localizer.Get(LocKeys.Settings.Market.Rollback),
            IsEnabled = !_busy && !_marketplace.IsOfflineFallback,
            Height = 27,
            Padding = new Thickness(9, 0, 9, 0),
            CornerRadius = new CornerRadius(7),
            FontSize = 9.5,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(_surfaces.ForegroundTertiary),
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, _localizer.Format(LocKeys.Settings.Market.Latest, ("version", target)));
        button.Click += async (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            try
            {
                var progress = new Progress<InstallProgress>(value =>
                {
                    _status = _localizer.Get(value.LocalizationKey);
                });
                await _marketplace.RollbackAsync(plugin.Id, target, progress, CancellationToken.None);
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
