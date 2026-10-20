using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.Marketplace.Community;
using FluidBar.Marketplace.Install;
using CommunityCopy = FluidBar.App.Localization.LocKeys.Settings.Marketplace.Community;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Marketplace section UI matching the prototype: gradient featured card, search field, category chips
/// and a two-column card grid with install/update/rollback/uninstall. A consent dialog with honest
/// capability copy is shown before any install (spec 6.5).
/// <para>
/// The section also hosts the device-local community marketplace (submit → review → list → install →
/// download counter → rating). Its state lives in <c>%APPDATA%\FluidBar\marketplace</c> and every action
/// is a real filesystem operation, so a restart shows exactly the same listings, counters and ratings.
/// </para>
/// </summary>
public sealed class MarketplaceSectionView : StackPanel
{
    private static readonly (string Token, string Key)[] Categories =
    [
        ("all", LocKeys.Settings.Market.Category.All),
        ("productivity", LocKeys.Settings.Market.Category.Productivity),
        ("media", LocKeys.Settings.Market.Category.Media),
        ("developer", LocKeys.Settings.Market.Category.Developer),
        ("system", LocKeys.Settings.Market.Category.System),
        ("ai", LocKeys.Settings.Market.Category.Ai),
    ];
    private readonly IMarketplaceUiService _marketplace;
    private readonly ILocalizer _localizer;
    private readonly SettingsPalette? _palette;
    private readonly AppSurfaceColors _surfaces;
    private readonly Func<Window?> _owner;
    private readonly string _channel;
    private readonly CommunityMarketplaceService _community;
    private IReadOnlyList<CommunityListing> _communityListings = [];
    private IReadOnlyList<CommunitySubmission> _pendingSubmissions = [];
    private string _search = string.Empty;
    private string _category = "all";
    private bool _busy;
    private bool _communityBusy;
    private string? _status;
    private string? _communityStatus;

    /// <summary>Creates the marketplace view.</summary>
    /// <param name="marketplace">Signed-index marketplace service (network catalog).</param>
    /// <param name="localizer">Localizer for the signed catalog.</param>
    /// <param name="surfaces">Surface palette.</param>
    /// <param name="owner">Window provider for the consent dialog.</param>
    /// <param name="channel">Update channel.</param>
    /// <param name="community">
    /// Device-local community marketplace; when omitted it is created over
    /// <c>%APPDATA%\FluidBar\marketplace</c> (tests inject a temporary root).
    /// </param>
    public MarketplaceSectionView(
        IMarketplaceUiService marketplace,
        ILocalizer localizer,
        AppSurfaceColors surfaces,
        Func<Window?> owner,
        string channel,
        CommunityMarketplaceService? community = null)
        : this(marketplace, localizer, SettingsPalette.FromSurfaces(surfaces), owner, channel, community)
    {
    }

    /// <summary>Creates the marketplace view with the prototype settings palette.</summary>
    public MarketplaceSectionView(
        IMarketplaceUiService marketplace,
        ILocalizer localizer,
        SettingsPalette palette,
        Func<Window?> owner,
        string channel,
        CommunityMarketplaceService? community = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _palette = palette;
        _surfaces = FromSurfaces(palette);
        _marketplace = marketplace ?? throw new ArgumentNullException(nameof(marketplace));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _owner = owner;
        _channel = channel;
        _community = community ?? CommunityMarketplaceService.CreateDefault();
        Spacing = 0;

        // Subscribe only while the section is on screen. The signed-catalog service is long-lived (one
        // per app), so a ctor subscription without an unsubscribe would root every closed settings
        // window (and keep rebuilding detached UI trees) for the rest of the process lifetime.
        AttachedToVisualTree += OnSectionAttached;
        DetachedFromVisualTree += OnSectionDetached;
    }

    private SettingsPalette Palette => _palette!;

    private void OnSectionAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _marketplace.Changed -= OnMarketplaceChanged;
        _marketplace.Changed += OnMarketplaceChanged;
        _community.Changed -= OnCommunityChanged;
        _community.Changed += OnCommunityChanged;
    }

    private void OnSectionDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _marketplace.Changed -= OnMarketplaceChanged;
        _community.Changed -= OnCommunityChanged;
    }

    private void OnMarketplaceChanged(object? sender, EventArgs e) => RefreshOnUiThread();

    private void OnCommunityChanged(object? sender, EventArgs e)
    {
        if (VisualRoot is null)
        {
            return;
        }

        RefreshOnUiThread();
    }

    /// <summary>
    /// The catalog/community services raise <c>Changed</c> on whatever thread finished the IO (network
    /// poll or thread-pool continuation), while the rebuild mutates Avalonia controls. Marshal to the UI
    /// thread instead of touching the visual tree from a worker thread.
    /// </summary>
    private void RefreshOnUiThread()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Refresh();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>The device-local community marketplace backing this section.</summary>
    public CommunityMarketplaceService Community => _community;

    /// <summary>Projects a prototype palette onto the legacy surface record (community block styling).</summary>
    private static AppSurfaceColors FromSurfaces(SettingsPalette palette)
        => new(
            palette.Window,
            palette.Window,
            palette.Raised,
            palette.RaisedHover,
            palette.Field,
            palette.Line,
            palette.LineStrong,
            palette.Foreground,
            palette.Secondary,
            palette.Tertiary,
            palette.Active,
            palette.ActiveForeground);

    private static AppSurfaceColors FromSurfaces(AppSurfaceColors surfaces) => surfaces;

    /// <summary>Rebuilds the plugin list (signed catalog + device-local community marketplace).</summary>
    /// <remarks>
    /// Kept as the event-handler entry point (async void) for compatibility, but a rebuild must never
    /// surface as an unhandled exception on the UI thread: reload errors are already reflected in the
    /// section status by <see cref="ReloadAsync"/>; a failure while building a detached tree is traced
    /// and dropped (the window that owned this view is already closed).
    /// </remarks>
    public async void Refresh()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("marketplace section refresh failed: " + ex);
        }
    }

    /// <summary>
    /// Reloads both catalogs and rebuilds the section. Awaitable so tests and the harness can render a
    /// settled section without sleeping on a fire-and-forget refresh.
    /// </summary>
    public async Task ReloadAsync()
    {
        IReadOnlyList<MarketPlugin> plugins = [];
        try
        {
            var query = new MarketplaceQuery(_search, _category, _channel);
            plugins = await _marketplace.BrowseAsync(query, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // The section was closed while refreshing; nothing to update.
            return;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }

        await LoadCommunityAsync();
        Build(plugins);
    }

    /// <summary>Reloads the pending review queue and the approved community listings from disk.</summary>
    private async Task LoadCommunityAsync()
    {
        try
        {
            _communityListings = await _community.BrowseAsync(null, CancellationToken.None);
            _pendingSubmissions = await _community.GetPendingAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Nothing to render; keep the last snapshot.
        }
        catch (Exception ex)
        {
            _communityListings = [];
            _pendingSubmissions = [];
            _communityStatus = "The local marketplace is unavailable: " + ex.Message;
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
            // .market-cats already ends with the prototype's 10px bottom margin; .market-grid adds none.
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
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

        Children.Add(BuildCommunitySection(foreground, secondary, tertiary, line, raised));

        // The offline/demo notice is honest state, not prototype chrome: it stays at the bottom of the
        // pane so the grid above matches the prototype's layout exactly.
        Children.Add(BuildMarketNotice());

        // .market-footnote { margin-top: 14px; gap: 5px; 8.5px --ui-fg-3 } with a 10 px shield.
        Children.Add(SettingsControls.FooterNote(_localizer.Get(LocKeys.Settings.Market.Footnote), Palette, market: true));
    }

    /// <summary>Compact catalog-state notice (offline/demo/errors) shown below the grid.</summary>
    private Control BuildMarketNotice()
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 10, 0, 0),
        };
        if (_marketplace.IsOfflineFallback)
        {
            panel.Children.Add(SettingsControls.StatusLine(
                _marketplace.StatusMessage ?? _localizer.Get(LocKeys.Settings.Market.DemoCatalog),
                Palette,
                SettingsTone.Warning,
                SettingsMetrics.MarketFootnoteSize));
        }

        if (!string.IsNullOrEmpty(_status))
        {
            panel.Children.Add(SettingsControls.StatusLine(
                _status!,
                Palette,
                SettingsTone.Warning,
                SettingsMetrics.MarketFootnoteSize));
        }

        return panel;
    }

    private Control BuildToolbar(IBrush secondary, IBrush tertiary, IBrush line, IBrush raised)
    {
        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(0, SettingsMetrics.MarketToolbarMarginTop, 0, 0) };

        // .search-field { height:34; radius:8; background: var(--ui-field); border: 1px var(--ui-line) }
        stack.Children.Add(SettingsControls.TextField(
            Palette,
            SettingsFieldKind.Search,
            _search,
            _localizer.Get(LocKeys.Settings.Market.Search),
            onChanged: text => _search = text,
            onEnter: Refresh));

        // .market-cats { gap:4; margin: 9px 0 10px }
        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.CatGap,
            Margin = new Thickness(0, SettingsMetrics.MarketCatsMarginTop, 0, SettingsMetrics.MarketCatsMarginBottom),
        };
        foreach (var (token, key) in Categories)
        {
            var active = string.Equals(token, _category, StringComparison.Ordinal);
            var chip = SettingsControls.Chip(_localizer.Get(key), Palette, SettingsChipKind.Category, active);
            chip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
            var categoryToken = token;
            chip.PointerPressed += (_, e) =>
            {
                _category = categoryToken;
                Refresh();
                e.Handled = true;
            };
            chips.Children.Add(chip);
        }

        stack.Children.Add(chips);
        return stack;
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
            FontSize = SettingsMetrics.FeaturedKickerSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = SettingsMetrics.FeaturedKickerSize * SettingsMetrics.FeaturedKickerTracking,
            Foreground = new SolidColorBrush(Color.Parse("#7FD8FF")),
            Margin = new Thickness(0, 0, 0, 5),
        });
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontFamily = Fonts.Display,
            FontSize = SettingsMetrics.FeaturedTitleSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = -0.4,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Summary,
            FontSize = SettingsMetrics.FeaturedDescSize,
            LineHeight = SettingsMetrics.FeaturedDescSize * 1.5,
            Foreground = new SolidColorBrush(Color.FromArgb(168, 232, 244, 250)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = SettingsMetrics.FeaturedDescMaxWidth,
            MaxLines = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
        });

        // .featured-actions { display:flex; gap:9px; margin-top:11px }
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Margin = new Thickness(0, SettingsMetrics.FeaturedActionsMarginTop, 0, 0),
        };
        actions.Children.Add(BuildFeaturedInstall(plugin));
        actions.Children.Add(new TextBlock
        {
            Text = plugin.Verified
                ? _localizer.Get(LocKeys.Settings.Market.Verified) + " · " + _localizer.Format(LocKeys.Settings.Market.Downloads, ("count", plugin.Installs))
                : _localizer.Format(LocKeys.Settings.Market.Downloads, ("count", plugin.Installs)),
            FontSize = SettingsMetrics.FeaturedMetaSize,
            Foreground = new SolidColorBrush(Color.FromArgb(138, 232, 244, 250)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        copy.Children.Add(actions);

        var art = new Border
        {
            Width = SettingsMetrics.FeaturedArt,
            Height = SettingsMetrics.FeaturedArt,
            CornerRadius = new CornerRadius(SettingsMetrics.FeaturedArtRadius),
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
            Child = new SettingsIcon
            {
                Kind = CategoryIcon(plugin.Category),
                Brush = new SolidColorBrush(Color.Parse("#C8F4FF")),
                Width = SettingsMetrics.FeaturedArtGlyph,
                Height = SettingsMetrics.FeaturedArtGlyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = SettingsMetrics.FeaturedGap,
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
        // Second prototype glow: radial-gradient(100% 120% at 62% 110%, rgba(217,119,87,.18), transparent 60%)
        // — without it the card's lower half fades into the pane background.
        host.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.62, 1.10, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.62, 1.10, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(1.0, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.2, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(46, 217, 119, 87), 0),
                    new GradientStop(Color.FromArgb(0, 217, 119, 87), 0.6),
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
        host.Children.Add(new Border
        {
            Padding = new Thickness(SettingsMetrics.FeaturedPad),
            Child = body,
        });

        // The shell (radius, hairline, 122 px minimum) is the shared card control; the gradient
        // fills it edge to edge and the 18 px inset rides on the content.
        return SettingsControls.Card(host, Palette, SettingsCardKind.Featured);
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

        return SettingsControls.Button(
            label,
            Palette,
            installed || installing ? SettingsButtonVariant.Raised : SettingsButtonVariant.Contrast,
            SettingsButtonSize.Sm,
            onClick: () => _ = RunInstallAsync(plugin),
            enabled: !installed && !installing);
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
            Child = new SettingsIcon
            {
                Kind = CategoryIcon(plugin.Category),
                Brush = Brushes.White,
                Width = SettingsMetrics.MarketGlyph,
                Height = SettingsMetrics.MarketGlyph,
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
            nameRow.Children.Add(new SettingsIcon
            {
                Kind = SettingsIconKind.Shield,
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

        return SettingsControls.Card(panel, Palette, SettingsCardKind.Tile);
    }

    private static SettingsIconKind CategoryIcon(string category) => category switch
    {
        "media" => SettingsIconKind.Star,
        "productivity" => SettingsIconKind.Sliders,
        "system" => SettingsIconKind.Alert,
        "developer" => SettingsIconKind.Palette,
        "ai" => SettingsIconKind.Shield,
        _ => SettingsIconKind.Storefront,
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
        var button = SettingsControls.Button(
            _localizer.Get(labelKey),
            Palette,
            installed ? SettingsButtonVariant.Raised : SettingsButtonVariant.Solid,
            SettingsButtonSize.Xs,
            enabled: !_busy && action != "none" && !plugin.IsDemo && !_marketplace.IsOfflineFallback,
            minWidth: 58);
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

        var button = SettingsControls.Button(
            _localizer.Get(LocKeys.Settings.Market.Rollback),
            Palette,
            SettingsButtonVariant.Quiet,
            SettingsButtonSize.Xs,
            enabled: !_busy && !_marketplace.IsOfflineFallback);
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

        var progressBar = SettingsControls.Progress(Palette);
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

    // ---------------------------------------------------------------------------------------------------
    // Device-local community marketplace: submit → review → list → install → download counter → rating.
    // ---------------------------------------------------------------------------------------------------

    private Control BuildCommunitySection(IBrush foreground, IBrush secondary, IBrush tertiary, IBrush line, IBrush raised)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titles = new StackPanel { Spacing = 1 };
        titles.Children.Add(new TextBlock
        {
            Text = _localizer.Get(CommunityCopy.Title),
            FontFamily = Fonts.Display,
            FontSize = 13.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
        });
        titles.Children.Add(new TextBlock
        {
            Text = _localizer.Format(CommunityCopy.Subtitle, ("root", _community.Store.RootDirectory)),
            FontSize = 8.5,
            Foreground = tertiary,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
        });
        var submit = SettingsControls.Button(
            _localizer.Get(CommunityCopy.Submit),
            Palette,
            SettingsButtonVariant.Solid,
            SettingsButtonSize.Sm,
            enabled: !_communityBusy);
        submit.VerticalAlignment = VerticalAlignment.Center;
        submit.Click += async (_, _) => await PickAndSubmitAsync();
        Grid.SetColumn(submit, 1);
        header.Children.Add(titles);
        header.Children.Add(submit);
        stack.Children.Add(header);

        if (!string.IsNullOrEmpty(_communityStatus))
        {
            stack.Children.Add(SettingsControls.StatusLine(
                _communityStatus!, Palette, SettingsTone.Warning, SettingsMetrics.MarketFootnoteSize));
        }

        if (_pendingSubmissions.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = _localizer.Format(CommunityCopy.PendingHeader, ("count", _pendingSubmissions.Count)),
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                Foreground = secondary,
                Margin = new Thickness(0, 6, 0, 0),
            });
            foreach (var submission in _pendingSubmissions)
            {
                stack.Children.Add(BuildPendingRow(submission, foreground, secondary, tertiary, line, raised));
            }
        }

        if (_communityListings.Count == 0)
        {
            var empty = SettingsControls.EmptyState(_localizer.Get(CommunityCopy.Empty), Palette);
            empty.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(empty);
            return stack;
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        for (var index = 0; index < _communityListings.Count; index++)
        {
            if (index % 2 == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            var card = BuildCommunityCard(_communityListings[index], foreground, secondary, tertiary, line, raised);
            card.Margin = new Thickness(index % 2 == 0 ? 0 : 4.5, 0, index % 2 == 0 ? 4.5 : 0, 9);
            Grid.SetRow(card, index / 2);
            Grid.SetColumn(card, index % 2);
            grid.Children.Add(card);
        }

        stack.Children.Add(grid);
        return stack;
    }

    private Control BuildPendingRow(
        CommunitySubmission submission,
        IBrush foreground,
        IBrush secondary,
        IBrush tertiary,
        IBrush line,
        IBrush raised)
    {
        var info = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        nameRow.Children.Add(new TextBlock
        {
            Text = submission.Plugin.Name,
            FontSize = 10.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        nameRow.Children.Add(new TextBlock
        {
            Text = "v" + submission.Version,
            FontSize = 9,
            Foreground = tertiary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        nameRow.Children.Add(SettingsControls.Chip(
            SignatureLabel(submission.SignatureState),
            Palette,
            SettingsChipKind.Badge));
        info.Children.Add(nameRow);
        info.Children.Add(new TextBlock
        {
            Text = _localizer.Format(
                CommunityCopy.PendingDetail,
                ("publisher", submission.Plugin.PublisherName),
                ("pluginId", submission.Plugin.Id),
                ("hash", submission.PackageSha256[..Math.Min(12, submission.PackageSha256.Length)])),
            FontSize = 8.5,
            Foreground = tertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        actions.Children.Add(CommunityAction(
            _localizer.Get(CommunityCopy.Approve),
            SettingsButtonVariant.Solid,
            () => ReviewAsync(submission, approve: true)));
        actions.Children.Add(CommunityAction(
            _localizer.Get(CommunityCopy.Reject),
            SettingsButtonVariant.Raised,
            () => ReviewAsync(submission, approve: false)));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10,
        };
        Grid.SetColumn(info, 0);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(info);
        grid.Children.Add(actions);

        return new Border
        {
            Padding = new Thickness(12, 9, 12, 9),
            CornerRadius = new CornerRadius(SettingsMetrics.MarketCardRadius),
            Background = raised,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    private Control BuildCommunityCard(
        CommunityListing listing,
        IBrush foreground,
        IBrush secondary,
        IBrush tertiary,
        IBrush line,
        IBrush raised)
    {
        var panel = new StackPanel { Spacing = 5 };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#4C5566")),
            Child = new SettingsIcon
            {
                Kind = SettingsIconKind.Storefront,
                Brush = Brushes.White,
                Width = 16,
                Height = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        var titles = new StackPanel { Spacing = 1, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = listing.Plugin.Name,
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        titles.Children.Add(new TextBlock
        {
            Text = _localizer.Format(
                CommunityCopy.CardSubtitle,
                ("publisher", listing.Plugin.PublisherName),
                ("version", listing.Version),
                ("signature", SignatureLabel(listing.SignatureState))),
            FontSize = 8.5,
            Foreground = tertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        panel.Children.Add(head);
        panel.Children.Add(new TextBlock
        {
            Text = listing.Plugin.Summary,
            FontSize = 9,
            Foreground = secondary,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var stars = BuildStarRating(listing, tertiary);
        var ratingText = new TextBlock
        {
            Text = listing.Stats.Rating is { } rating
                ? _localizer.Format(
                    CommunityCopy.RatingSummary,
                    ("rating", rating.ToString("0.0", _localizer.CurrentCulture)),
                    ("count", listing.Stats.RatingCount))
                : _localizer.Get(CommunityCopy.NotRated),
            FontSize = 8.5,
            Foreground = tertiary,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0),
        };
        var ratingRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        ratingRow.Children.Add(stars);
        ratingRow.Children.Add(ratingText);

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        foot.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                ratingRow,
                new TextBlock
                {
                    Text = _localizer.Format(CommunityCopy.InstallCount, ("count", listing.Stats.Downloads)),
                    FontSize = 8.5,
                    Foreground = tertiary,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var install = SettingsControls.Button(
            _localizer.Get(CommunityCopy.Install),
            Palette,
            SettingsButtonVariant.Solid,
            SettingsButtonSize.Xs,
            enabled: !_communityBusy,
            minWidth: 58);
        install.Click += async (_, _) => await InstallCommunityAsync(listing);
        actions.Children.Add(install);
        actions.Children.Add(CommunityAction(
            _localizer.Get(CommunityCopy.Delist),
            SettingsButtonVariant.Quiet,
            () => DelistAsync(listing)));
        Grid.SetColumn(actions, 1);
        foot.Children.Add(actions);
        panel.Children.Add(foot);

        return SettingsControls.Card(panel, Palette, SettingsCardKind.Tile);
    }

    private Control BuildStarRating(CommunityListing listing, IBrush tertiary)
    {
        var starBrush = new SolidColorBrush(Color.Parse("#F0B429"));
        var emptyBrush = tertiary;
        var current = listing.Stats.UserRating ?? 0;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (var star = 1; star <= 5; star++)
        {
            var value = star;
            var glyph = new TextBlock
            {
                Text = value <= current ? "\u2605" : "\u2606",
                FontSize = 11.5,
                Foreground = value <= current ? starBrush : emptyBrush,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(glyph, _localizer.Format(CommunityCopy.RateHint, ("stars", value)));
            glyph.PointerPressed += async (_, e) =>
            {
                e.Handled = true;
                await RateCommunityAsync(listing.Id, value);
            };
            row.Children.Add(glyph);
        }

        return row;
    }

    private Button CommunityAction(string label, SettingsButtonVariant variant, Func<Task> action)
    {
        var button = SettingsControls.Button(label, Palette, variant, SettingsButtonSize.Xs,
            enabled: !_communityBusy);
        button.Click += async (_, _) => await action();
        return button;
    }

    private async Task PickAndSubmitAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            _communityStatus = _localizer.Get(CommunityCopy.PickerUnavailable);
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _localizer.Get(CommunityCopy.SubmitDialogTitle),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(_localizer.Get(CommunityCopy.SubmitFileType)) { Patterns = ["*.fbp"] },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var packagePath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            _communityStatus = _localizer.Get(CommunityCopy.PickerUnavailable);
            return;
        }

        // Accept the two detached-signature conventions the repo uses: '<name>.sig.json' and '<name>.fbp.sig.json'.
        string? signaturePath = null;
        foreach (var candidate in new[] { Path.ChangeExtension(packagePath, ".sig.json"), packagePath + ".sig.json" })
        {
            if (File.Exists(candidate))
            {
                signaturePath = candidate;
                break;
            }
        }

        await SubmitAsync(packagePath, signaturePath);
    }

    private async Task SubmitAsync(string packagePath, string? signaturePath)
    {
        _communityBusy = true;
        _communityStatus = _localizer.Format(CommunityCopy.Validating, ("file", Path.GetFileName(packagePath)));
        try
        {
            var result = await _community.SubmitAsync(new CommunitySubmitRequest
            {
                PackagePath = packagePath,
                SignaturePath = signaturePath,
            });
            _communityStatus = result.Success
                ? _localizer.Format(
                    CommunityCopy.Submitted,
                    ("pluginId", result.Submission!.PluginId),
                    ("version", result.Submission.Version))
                    + (result.Submission.SignatureState == CommunitySignatureState.Unsigned
                        ? " " + _localizer.Get(CommunityCopy.UnsignedWarning)
                        : string.Empty)
                : _localizer.Format(CommunityCopy.SubmitFailed, ("errors", JoinErrors(result.Errors)));
        }
        catch (Exception ex)
        {
            _communityStatus = _localizer.Format(CommunityCopy.SubmitFailed, ("errors", ex.Message));
        }
        finally
        {
            _communityBusy = false;
            Refresh();
        }
    }

    private async Task ReviewAsync(CommunitySubmission submission, bool approve)
    {
        if (_communityBusy)
        {
            return;
        }

        _communityBusy = true;
        try
        {
            var result = approve
                ? await _community.ApproveAsync(submission.PluginId, submission.Version, _localizer.Get(CommunityCopy.ApprovedNote))
                : await _community.RejectAsync(submission.PluginId, submission.Version, _localizer.Get(CommunityCopy.RejectedNote));
            _communityStatus = result.Success
                ? approve
                    ? _localizer.Format(CommunityCopy.Approved, ("pluginId", submission.PluginId), ("version", submission.Version))
                    : _localizer.Format(CommunityCopy.Rejected, ("pluginId", submission.PluginId), ("version", submission.Version))
                : _localizer.Format(CommunityCopy.ReviewFailed, ("errors", JoinErrors(result.Errors)));
        }
        catch (Exception ex)
        {
            _communityStatus = _localizer.Format(CommunityCopy.ReviewFailed, ("errors", ex.Message));
        }
        finally
        {
            _communityBusy = false;
            Refresh();
        }
    }

    private async Task DelistAsync(CommunityListing listing)
    {
        if (_communityBusy)
        {
            return;
        }

        _communityBusy = true;
        try
        {
            var result = await _community.RejectAsync(listing.Id, listing.Version, _localizer.Get(CommunityCopy.DelistedNote));
            _communityStatus = result.Success
                ? _localizer.Format(CommunityCopy.Rejected, ("pluginId", listing.Id), ("version", listing.Version))
                : _localizer.Format(CommunityCopy.ReviewFailed, ("errors", JoinErrors(result.Errors)));
        }
        catch (Exception ex)
        {
            _communityStatus = _localizer.Format(CommunityCopy.ReviewFailed, ("errors", ex.Message));
        }
        finally
        {
            _communityBusy = false;
            Refresh();
        }
    }

    private async Task RateCommunityAsync(string pluginId, int stars)
    {
        if (_communityBusy)
        {
            return;
        }

        _communityBusy = true;
        try
        {
            var listing = await _community.RateAsync(pluginId, stars);
            _communityStatus = _localizer.Format(CommunityCopy.Rated, ("pluginId", listing.Id), ("stars", stars));
        }
        catch (ArgumentOutOfRangeException)
        {
            _communityStatus = _localizer.Get(CommunityCopy.RatingOutOfRange);
        }
        catch (Exception ex)
        {
            _communityStatus = _localizer.Format(CommunityCopy.RatingFailed, ("message", ex.Message));
        }
        finally
        {
            _communityBusy = false;
            Refresh();
        }
    }

    private async Task InstallCommunityAsync(CommunityListing listing)
    {
        if (_communityBusy)
        {
            return;
        }

        _communityBusy = true;
        var progress = new Progress<InstallPhase>(phase => _communityStatus = _localizer.Format(
            CommunityCopy.Installing,
            ("pluginId", listing.Id),
            ("stage", InstallStageLabel(phase))));
        try
        {
            var outcome = await _community.InstallAsync(listing.Id, progress);
            _communityStatus = outcome.Success
                ? _localizer.Format(CommunityCopy.Installed, ("pluginId", listing.Id), ("version", listing.Version))
                : _localizer.Format(CommunityCopy.InstallFailed, ("message", outcome.Message));
        }
        catch (Exception ex)
        {
            _communityStatus = _localizer.Format(CommunityCopy.InstallFailed, ("message", ex.Message));
        }
        finally
        {
            _communityBusy = false;
            Refresh();
        }
    }

    /// <summary>Localized signature badge for a community listing/submission.</summary>
    private string SignatureLabel(CommunitySignatureState state) =>
        _localizer.Get(state == CommunitySignatureState.Verified ? CommunityCopy.Signed : CommunityCopy.Unsigned);

    /// <summary>Maps a real install phase onto the localized marketplace stage label.</summary>
    private string InstallStageLabel(InstallPhase phase) => _localizer.Get(phase switch
    {
        InstallPhase.Downloading => LocKeys.Market.Stage.Download,
        InstallPhase.Verifying => LocKeys.Market.Stage.Verify,
        InstallPhase.Extracting => LocKeys.Market.Stage.Extract,
        InstallPhase.Staged => LocKeys.Market.Stage.Prepare,
        _ => LocKeys.Market.Stage.Activate,
    });

    private static string JoinErrors(IReadOnlyList<string> errors) => string.Join("; ", errors);
}
