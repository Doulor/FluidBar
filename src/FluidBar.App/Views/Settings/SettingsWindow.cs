using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Market;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Settings window: 900×620 custom chrome, seven sections, transcribed from the interactive prototype
/// (docs/rewrite/prototype2): <c>.settings-window</c> (22 px radius, 1 px <c>--ui-line-strong</c> rim),
/// <c>.settings-titlebar</c> (50 px, 18/12 padding, 24 px mark, 13 px title, 9 px uppercase sub, 32 px close),
/// <c>.settings-nav</c> (208 px, 14/10 padding, 36 px items, kicker, counts, version block) and
/// <c>.settings-pane</c> (24/30 padding). Every change applies live through <see cref="SettingsController"/>.
/// </summary>
public sealed class SettingsWindow : Window, ILocalizable
{
    private static readonly (SettingsWindowSection Section, string LabelKey, SettingsIconKind Icon)[] SectionOrder =
    [
        (SettingsWindowSection.General, LocKeys.Settings.General.Title, SettingsIconKind.Settings),
        (SettingsWindowSection.Appearance, LocKeys.Settings.Appearance.Title, SettingsIconKind.Palette),
        (SettingsWindowSection.Island, LocKeys.Settings.Island.Title, SettingsIconKind.Island),
        (SettingsWindowSection.Plugins, LocKeys.Settings.Plugins.Title, SettingsIconKind.Puzzle),
        (SettingsWindowSection.Marketplace, LocKeys.Settings.Marketplace.Title, SettingsIconKind.Storefront),
        (SettingsWindowSection.Advanced, LocKeys.Settings.Advanced.Title, SettingsIconKind.Sliders),
        (SettingsWindowSection.About, LocKeys.Settings.About.NavLabel, SettingsIconKind.Info),
    ];

    private readonly SettingsController _settings;
    private readonly ILocalizer _localizer;
    private readonly IMarketplaceUiService _marketplace;
    private readonly IPluginUiService _plugins;
    private readonly Action<string?> _openPath;
    private readonly IUpdateUiService? _updates;
    private readonly IFrameStatsSource? _frameStats;
    private readonly Action? _restartToInstall;
    private readonly StackPanel _nav = new() { Spacing = SettingsMetrics.NavGap };
    private readonly ScrollViewer _content = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _subText = new();
    private readonly TextBlock _versionText = new();
    private readonly TextBlock _versionLine = new();
    private readonly List<Border> _navRows = [];
    private readonly List<SettingsIcon> _navIcons = [];
    private readonly List<TextBlock> _navLabels = [];
    private readonly Dictionary<SettingsWindowSection, Border> _navCounts = [];
    private readonly Border _shell;
    private SettingsWindowSection _section;
    private IslandPreview? _preview;
    private string? _status;
    private SettingsPalette _palette;

    /// <summary>Creates the settings window.</summary>
    public SettingsWindow(
        SettingsController settings,
        ILocalizer localizer,
        SettingsWindowSection section,
        IMarketplaceUiService? marketplace = null,
        IPluginUiService? plugins = null,
        Action<string?>? openPath = null,
        IUpdateUiService? updates = null,
        IFrameStatsSource? frameStats = null,
        Action? restartToInstall = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _updates = updates;
        _frameStats = frameStats;
        _restartToInstall = restartToInstall;
        _marketplace = marketplace ?? CreateDefaultMarketplace(localizer);
        _plugins = plugins ?? new FluidBar.App.Views.Plugins.BuiltinPluginContributionUiService(settings, localizer);
        _openPath = openPath ?? (static path =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
        });
        _section = section;
        _palette = SettingsPalette.For(ResolvePaletteTheme(settings.Current.Theme));

        // .settings-window { width: min(900px, 100vw - 48px); height: min(620px, 100vh - 48px) }
        Width = SettingsMetrics.WindowWidth;
        Height = SettingsMetrics.WindowHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        Title = localizer.Get(LocKeys.Settings.Title);
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _shell = new Border
        {
            CornerRadius = new CornerRadius(SettingsMetrics.WindowRadius),
            BorderThickness = new Thickness(SettingsMetrics.WindowBorder),
            ClipToBounds = true,
            Child = BuildLayout(),
        };
        ApplyChrome();
        Content = _shell;

        _settings.Changed += OnSettingsChanged;
        _localizer.Changed += OnLocalizerChanged;
        if (_updates is not null)
        {
            _updates.Changed += OnUpdateChanged;
        }

        RebuildContent();
        _ = RefreshNavCountsAsync();
    }

    private static IMarketplaceUiService CreateDefaultMarketplace(ILocalizer localizer)
        => new MarketplaceFallbackUiService(null, new DesignMarketplaceUiService(), localizer);

    /// <summary>Raised when a status line should be surfaced (tests/accessibility).</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Currently selected section.</summary>
    public SettingsWindowSection CurrentSection => _section;

    /// <summary>Status text last shown by a section.</summary>
    public string? Status => _status;

    /// <summary>Switches section (used by the marketplace shortcut).</summary>
    public void ShowSection(SettingsWindowSection section)
    {
        _section = section;
        UpdateNavSelection();
        RebuildContent();
    }

    /// <inheritdoc />
    public void ApplyLocalization(ILocalizer localizer)
    {
        RebuildNav();
        RebuildContent();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Rounded corners need a transparent window; when the platform refuses it, fall back to a
        // square opaque window instead of leaving black corner triangles.
        if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
        {
            _shell.CornerRadius = new CornerRadius(0);
            Background = new SolidColorBrush(_palette.Window);
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        _localizer.Changed -= OnLocalizerChanged;
        if (_updates is not null)
        {
            _updates.Changed -= OnUpdateChanged;
        }

        base.OnClosed(e);
    }

    private static AppTheme ResolvePaletteTheme(AppTheme theme)
        => SystemSettingResolution.ResolveTheme(theme, SystemAppearanceSnapshot.FromWindows().LightTheme);

    private void OnUpdateChanged(object? sender, EventArgs e)
        => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_section == SettingsWindowSection.About)
            {
                RebuildContent();
            }
        });

    private void ApplyChrome()
    {
        _shell.Background = new SolidColorBrush(_palette.Window);
        _shell.BorderBrush = _palette.LineStrongBrush;
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("50,*") };
        root.Children.Add(BuildTitleBar());

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions(SettingsMetrics.NavWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",*") };
        body.Children.Add(BuildNav());
        body.Children.Add(BuildPane());
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private Control BuildTitleBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
            Height = SettingsMetrics.TitleBarHeight,
            Margin = new Thickness(SettingsMetrics.TitleBarPadLeft, 0, SettingsMetrics.TitleBarPadRight, 0),
        };
        bar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        // .sw-mark: 24×24, 8 px radius, #070709 with a 14% white rim.
        var mark = new Border
        {
            Width = SettingsMetrics.MarkSize,
            Height = SettingsMetrics.MarkSize,
            CornerRadius = new CornerRadius(SettingsMetrics.MarkRadius),
            Background = new SolidColorBrush(_palette.Mark),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SettingsIcon
            {
                Kind = SettingsIconKind.Island,
                Brush = Brushes.White,
                Width = SettingsMetrics.MarkIcon,
                Height = SettingsMetrics.MarkIcon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(mark, 0);
        bar.Children.Add(mark);

        // .sw-title: 13 px / 600 / 0.005em.
        _titleText.FontFamily = FluidBar.App.Theme.Fonts.Ui;
        _titleText.FontSize = SettingsMetrics.TitleSize;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.LetterSpacing = SettingsMetrics.TitleSize * SettingsMetrics.TitleTracking;
        _titleText.Foreground = _palette.ForegroundBrush;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(SettingsMetrics.TitleBarGap, 0, 0, 0);
        Grid.SetColumn(_titleText, 1);
        bar.Children.Add(_titleText);

        // .sw-sub: 9 px uppercase, 0.09em tracking, --ui-fg-3.
        _subText.FontSize = SettingsMetrics.SubSize;
        _subText.LetterSpacing = SettingsMetrics.SubSize * SettingsMetrics.SubTracking;
        _subText.Foreground = _palette.TertiaryBrush;
        _subText.VerticalAlignment = VerticalAlignment.Center;
        _subText.Margin = new Thickness(SettingsMetrics.TitleBarGap, 0, 0, 0);
        Grid.SetColumn(_subText, 2);
        bar.Children.Add(_subText);

        // .sw-close: 32×32, margin-left auto, danger-red hover.
        var closeIcon = new SettingsIcon
        {
            Kind = SettingsIconKind.Close,
            Brush = _palette.SecondaryBrush,
            Width = SettingsMetrics.CloseIcon,
            Height = SettingsMetrics.CloseIcon,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = new Border
        {
            Width = SettingsMetrics.CloseSize,
            Height = SettingsMetrics.CloseSize,
            CornerRadius = new CornerRadius(SettingsMetrics.CloseRadius),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = closeIcon,
        };
        close.PointerEntered += (_, _) =>
        {
            close.Background = new SolidColorBrush(Color.FromArgb(209, 255, 69, 58));
            closeIcon.Brush = Brushes.White;
            closeIcon.InvalidateVisual();
        };
        close.PointerExited += (_, _) =>
        {
            close.Background = Brushes.Transparent;
            closeIcon.Brush = _palette.SecondaryBrush;
            closeIcon.InvalidateVisual();
        };
        close.PointerPressed += (_, _) => Close();
        Grid.SetColumn(close, 4);
        bar.Children.Add(close);

        return new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Control BuildNav()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        // .nav-kicker { padding: 4px 10px 7px; 8.5px/600; 0.13em; uppercase }
        var kicker = new TextBlock
        {
            FontSize = SettingsMetrics.KickerSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = SettingsMetrics.KickerSize * SettingsMetrics.KickerTracking,
            Foreground = _palette.TertiaryBrush,
            Margin = new Thickness(SettingsMetrics.KickerPadX, SettingsMetrics.KickerPadTop,
                SettingsMetrics.KickerPadX, SettingsMetrics.KickerPadBottom),
        };
        Grid.SetRow(kicker, 0);
        grid.Children.Add(kicker);
        _kicker = kicker;

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _nav,
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        // .nav-version { margin-top: auto; padding: 12px 10px 4px; 8.5px/1.5; border-top }
        var version = new StackPanel
        {
            Margin = new Thickness(SettingsMetrics.VersionPadX, SettingsMetrics.VersionPadTop,
                SettingsMetrics.VersionPadX, SettingsMetrics.VersionPadBottom),
        };
        _versionText.Text = "FluidBar 2.0.0";
        _versionText.FontSize = SettingsMetrics.VersionSize;
        _versionText.FontWeight = FontWeight.Medium;
        _versionText.LineHeight = SettingsMetrics.VersionSize * SettingsMetrics.VersionLineHeight;
        _versionText.Foreground = _palette.SecondaryBrush;
        version.Children.Add(_versionText);
        _versionLine.FontSize = SettingsMetrics.VersionSize;
        _versionLine.LineHeight = SettingsMetrics.VersionSize * SettingsMetrics.VersionLineHeight;
        _versionLine.Foreground = _palette.TertiaryBrush;
        _versionLine.TextWrapping = TextWrapping.Wrap;
        version.Children.Add(_versionLine);
        var footer = new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = version,
        };
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        RebuildNav();
        return new Border
        {
            BorderBrush = _palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(SettingsMetrics.NavPadX, SettingsMetrics.NavPadY,
                SettingsMetrics.NavPadX, SettingsMetrics.NavPadY),
            Child = grid,
        };
    }

    private TextBlock? _kicker;

    private Control BuildPane()
    {
        _content.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _content.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _content.Padding = new Thickness(SettingsMetrics.PanePadX, SettingsMetrics.PanePadY,
            SettingsMetrics.PanePadX, SettingsMetrics.PanePadY);
        ApplyPaneScrollBarStyle();
        Grid.SetColumn(_content, 1);
        return _content;
    }

    /// <summary>
    /// .settings-pane scrollbar: 6 px wide, 4 px radius thumb at rgba(255,255,255,.16), transparent track.
    /// The Fluent default is wider and brighter, which is visible in the prototype comparison.
    /// </summary>
    private void ApplyPaneScrollBarStyle()
    {
        var thumb = new SolidColorBrush(Color.FromArgb(41, 255, 255, 255));
        var thumbHover = new SolidColorBrush(Color.FromArgb(66, 255, 255, 255));
        _content.AllowAutoHide = false;
        // Fluent's scrollbar paints its thumb from theme resources; provide the prototype values so the
        // pane scrollbar matches .settings-pane (thumb rgba(255,255,255,.16), transparent track).
        foreach (var key in new[]
                 {
                     "ScrollBarThumbFill", "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed",
                     "ScrollBarTrackFill", "ScrollBarBackground",
                 })
        {
            _content.Resources[key] = thumb;
        }

        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>())
        {
            Setters =
            {
                new Setter(WidthProperty, SettingsMetrics.PaneScrollBarGutter),
                new Setter(MinWidthProperty, SettingsMetrics.PaneScrollBarGutter),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.ForegroundProperty, thumb),
            },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>())
        {
            Setters =
            {
                new Setter(WidthProperty, SettingsMetrics.PaneScrollBarWidth),
                // The prototype's thumb is 6 px inside a wider overlay gutter.
                new Setter(MarginProperty, new Thickness((SettingsMetrics.PaneScrollBarGutter - SettingsMetrics.PaneScrollBarWidth) / 2, 0)),
                new Setter(CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.BackgroundProperty, thumb),
                // Windows renders the prototype's overlay scrollbar at half strength while idle
                // (measured in shot-11: thumb ≈ rgba(255,255,255,.08) over the pane fill).
                new Setter(OpacityProperty, 0.23),
            },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"))
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, thumbHover) },
        });
        _content.Styles.Add(new Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Template().OfType<Border>())
        {
            Setters = { new Setter(Border.BackgroundProperty, thumb) },
        });
    }

    private void RebuildNav()
    {
        _nav.Children.Clear();
        _navRows.Clear();
        _navIcons.Clear();
        _navLabels.Clear();
        _navCounts.Clear();
        foreach (var (section, labelKey, icon) in SectionOrder)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var glyph = new SettingsIcon
            {
                Kind = icon,
                Brush = _palette.TertiaryBrush,
                Width = SettingsMetrics.NavIcon,
                Height = SettingsMetrics.NavIcon,
            };
            Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);
            _navIcons.Add(glyph);

            var label = new TextBlock
            {
                Text = _localizer.Get(labelKey),
                FontSize = SettingsMetrics.NavLabelSize,
                Foreground = _palette.SecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(SettingsMetrics.NavItemGap, 0, 0, 0),
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            _navLabels.Add(label);

            // .nav-count { margin-left: auto; min-width: 16; height: 16; padding: 0 4; radius 5; }
            var countHost = new Border
            {
                MinWidth = SettingsMetrics.NavCountHeight,
                Height = SettingsMetrics.NavCountHeight,
                Padding = new Thickness(SettingsMetrics.NavCountPadX, 0, SettingsMetrics.NavCountPadX, 0),
                CornerRadius = new CornerRadius(SettingsMetrics.NavCountRadius),
                Background = _palette.RaisedHoverBrush,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsVisible = false,
                Child = new TextBlock
                {
                    FontSize = SettingsMetrics.NavCountSize,
                    Foreground = _palette.SecondaryBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(countHost, 2);
            row.Children.Add(countHost);
            _navCounts[section] = countHost;

            var item = new Border
            {
                Height = SettingsMetrics.NavItemHeight,
                CornerRadius = new CornerRadius(SettingsMetrics.NavItemRadius),
                Padding = new Thickness(SettingsMetrics.NavItemPadX, 0, SettingsMetrics.NavItemPadX, 0),
                Background = Brushes.Transparent,
                Child = row,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var captured = section;
            item.PointerEntered += (_, _) =>
            {
                if (captured != _section)
                {
                    item.Background = _palette.RaisedBrush;
                }
            };
            item.PointerExited += (_, _) =>
            {
                if (captured != _section)
                {
                    item.Background = Brushes.Transparent;
                }
            };
            item.PointerPressed += (_, e) =>
            {
                ShowSection(captured);
                e.Handled = true;
            };
            _navRows.Add(item);
            _nav.Children.Add(item);
        }

        if (_kicker is not null)
        {
            _kicker.Text = _localizer.Get(LocKeys.Settings.NavKicker).ToUpperInvariant();
        }

        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        _subText.Text = "FLUIDBAR 2.0";
        _versionLine.Text = _localizer.Get(LocKeys.Settings.VersionLine);
        UpdateNavSelection();
    }

    private void UpdateNavSelection()
    {
        for (var i = 0; i < _navRows.Count; i++)
        {
            var section = SectionOrder[i].Section;
            var selected = section == _section;
            _navRows[i].Background = selected ? _palette.RaisedBrush : Brushes.Transparent;
            _navLabels[i].Foreground = selected ? _palette.ForegroundBrush : _palette.SecondaryBrush;
            _navLabels[i].FontWeight = selected ? FontWeight.Medium : FontWeight.Normal;
            _navIcons[i].Brush = selected ? _palette.ForegroundBrush : _palette.TertiaryBrush;
            _navIcons[i].InvalidateVisual();
        }
    }

    private async Task RefreshNavCountsAsync()
    {
        try
        {
            var plugins = await _plugins.ListAsync(CancellationToken.None).ConfigureAwait(true);
            SetNavCount(SettingsWindowSection.Plugins, plugins.Count);
        }
        catch (Exception)
        {
            // A count badge is decorative; never let it break the window.
        }

        try
        {
            var entries = await _marketplace.BrowseAsync(new MarketplaceQuery(null, "all", "stable"), CancellationToken.None)
                .ConfigureAwait(true);
            SetNavCount(SettingsWindowSection.Marketplace, entries.Count);
        }
        catch (Exception)
        {
            // Same as above.
        }
    }

    private void SetNavCount(SettingsWindowSection section, int count)
    {
        if (!_navCounts.TryGetValue(section, out var host) || count <= 0)
        {
            return;
        }

        if (host.Child is TextBlock text)
        {
            text.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        host.IsVisible = true;
    }

    private void RebuildContent()
    {
        var context = new SettingsWindowContext
        {
            Settings = _settings,
            Localizer = _localizer,
            Marketplace = _marketplace,
            Plugins = _plugins,
            Palette = _palette,
            Owner = () => this,
            OpenPath = _openPath,
            ShowStatus = ShowStatus,
            Updates = _updates,
            FrameStats = _frameStats,
            RestartToInstall = _restartToInstall,
        };
        var content = SettingsSectionBuilder.Build(_section, context);
        if (_section == SettingsWindowSection.Appearance)
        {
            _preview = FindPreview(content);
        }

        _content.Content = content;
        UpdateNavSelection();
    }

    private static IslandPreview? FindPreview(Control root)
    {
        if (root is IslandPreview preview)
        {
            return preview;
        }

        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control control)
                {
                    var found = FindPreview(control);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        if (root is Decorator decorator && decorator.Child is Control decorated)
        {
            return FindPreview(decorated);
        }

        if (root is ContentControl contentControl && contentControl.Content is Control content)
        {
            return FindPreview(content);
        }

        return null;
    }

    private void ShowStatus(string text)
    {
        _status = text;
        StatusChanged?.Invoke(text);
    }

    private void OnLocalizerChanged(object? sender, EventArgs e)
    {
        RebuildNav();
        RebuildContent();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        var themeChanged = ResolvePaletteTheme(previous.Theme) != ResolvePaletteTheme(current.Theme);
        if (themeChanged)
        {
            _palette = SettingsPalette.For(ResolvePaletteTheme(current.Theme));
            ApplyChrome();
            RebuildTitleBarBrushes();
            RebuildNav();
            RebuildContent();
            return;
        }

        if (_preview is not null && _section == SettingsWindowSection.Appearance)
        {
            _preview.Update(current, _localizer);
        }

        // Diagnostics/update panes show live values: rebuild so the toggle has an immediate effect.
        if (_section == SettingsWindowSection.Advanced && previous.Advanced.FrameStats != current.Advanced.FrameStats)
        {
            RebuildContent();
        }
    }

    private void RebuildTitleBarBrushes()
    {
        _titleText.Foreground = _palette.ForegroundBrush;
        _subText.Foreground = _palette.TertiaryBrush;
        _versionText.Foreground = _palette.SecondaryBrush;
        _versionLine.Foreground = _palette.TertiaryBrush;
        if (_kicker is not null)
        {
            _kicker.Foreground = _palette.TertiaryBrush;
        }
    }
}
