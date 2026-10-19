using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.Marketplace.Catalog;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Settings window (900×620 custom chrome, seven sections) matching the interactive prototype
/// (docs/rewrite/prototype2): 50 DIP title bar, 208 DIP nav with kicker/counts/version, 30/24 pane
/// padding, hairline setting rows and prototype controls. Every change applies live through
/// <see cref="SettingsController"/>; language and theme switches rebuild the window without a restart.
/// </summary>
public sealed class SettingsWindow : Window, ILocalizable
{
    private static readonly (SettingsWindowSection Section, string LabelKey, IconKind Icon)[] SectionOrder =
    [
        (SettingsWindowSection.General, LocKeys.Settings.General.Title, IconKind.Settings),
        (SettingsWindowSection.Appearance, LocKeys.Settings.Appearance.Title, IconKind.Sun),
        (SettingsWindowSection.Island, LocKeys.Settings.Island.Title, IconKind.Grid),
        (SettingsWindowSection.Plugins, LocKeys.Settings.Plugins.Title, IconKind.Agent),
        (SettingsWindowSection.Marketplace, LocKeys.Settings.Marketplace.Title, IconKind.Store),
        (SettingsWindowSection.Advanced, LocKeys.Settings.Advanced.Title, IconKind.Document),
        (SettingsWindowSection.About, LocKeys.Settings.About.Title, IconKind.Bell),
    ];

    private readonly SettingsController _settings;
    private readonly ILocalizer _localizer;
    private readonly IMarketplaceUiService _marketplace;
    private readonly IPluginUiService _plugins;
    private readonly Action<string?> _openPath;
    private readonly StackPanel _nav = new() { Spacing = 2 };
    private readonly ScrollViewer _content = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _subText = new();
    private readonly List<Button> _navButtons = [];
    private readonly Dictionary<SettingsWindowSection, TextBlock> _navCounts = [];
    private readonly Border _shell;
    private SettingsWindowSection _section;
    private IslandPreview? _preview;
    private string? _status;

    /// <summary>Creates the settings window.</summary>
    public SettingsWindow(
        SettingsController settings,
        ILocalizer localizer,
        SettingsWindowSection section,
        IMarketplaceUiService? marketplace = null,
        IPluginUiService? plugins = null,
        Action<string?>? openPath = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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

        Width = 900;
        Height = 620;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.None;
        Title = localizer.Get(LocKeys.Settings.Title);
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _shell = new Border
        {
            CornerRadius = new CornerRadius(SettingsMetrics.WindowRadius),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = BuildLayout(),
        };
        ApplyChrome();
        Content = _shell;

        _settings.Changed += OnSettingsChanged;
        _localizer.Changed += OnLocalizerChanged;
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
            Background = new SolidColorBrush(WindowBackground());
        }
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        _localizer.Changed -= OnLocalizerChanged;
        base.OnClosed(e);
    }

    private void ApplyChrome()
    {
        _shell.Background = new SolidColorBrush(WindowBackground());
        _shell.BorderBrush = new SolidColorBrush(_settings.Current.Surfaces.LineStrong);
    }

    private Color WindowBackground()
        => _settings.Current.Surfaces.Window;

    private Control BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("50,*") };
        root.Children.Add(BuildTitleBar());

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions($"{SettingsMetrics.NavWidth},*") };
        body.Children.Add(BuildNav());
        body.Children.Add(BuildPane());
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private Control BuildTitleBar()
    {
        var surfaces = _settings.Current.Surfaces;
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
            Height = SettingsMetrics.TitleBarHeight,
            Background = new SolidColorBrush(WindowBackground()),
        };
        bar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };

        var mark = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#070709")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new IslandMark(),
        };
        Grid.SetColumn(mark, 0);
        bar.Children.Add(mark);

        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        _titleText.FontFamily = Fonts.Ui;
        _titleText.FontSize = 13;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.Foreground = new SolidColorBrush(surfaces.Foreground);
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_titleText, 1);
        bar.Children.Add(_titleText);

        _subText.Text = "FLUIDBAR 2.0";
        _subText.FontSize = 9;
        _subText.Foreground = new SolidColorBrush(surfaces.ForegroundTertiary);
        _subText.VerticalAlignment = VerticalAlignment.Center;
        _subText.Margin = new Thickness(9, 0, 0, 0);
        Grid.SetColumn(_subText, 2);
        bar.Children.Add(_subText);

        var close = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = "\u2715",
                FontSize = 13,
                Foreground = new SolidColorBrush(surfaces.ForegroundSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        close.PointerEntered += (_, _) => close.Background = new SolidColorBrush(Color.FromArgb(209, 255, 69, 58));
        close.PointerExited += (_, _) => close.Background = Brushes.Transparent;
        close.PointerPressed += (_, _) => Close();
        Grid.SetColumn(close, 4);
        bar.Children.Add(close);

        return new Border
        {
            BorderBrush = new SolidColorBrush(surfaces.Line),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private Control BuildNav()
    {
        var surfaces = _settings.Current.Surfaces;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        var kicker = new TextBlock
        {
            Text = _localizer.Get(LocKeys.Settings.NavKicker),
            FontSize = SettingsMetrics.CaptionSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(surfaces.ForegroundTertiary),
            Margin = new Thickness(10, 4, 10, 7),
        };
        Grid.SetRow(kicker, 0);
        grid.Children.Add(kicker);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _nav,
        };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        var version = new StackPanel
        {
            Margin = new Thickness(10, 12, 10, 4),
        };
        version.Children.Add(new TextBlock
        {
            Text = "FluidBar 2.0.0",
            FontSize = SettingsMetrics.CaptionSize,
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(surfaces.ForegroundSecondary),
        });
        version.Children.Add(new TextBlock
        {
            Text = _localizer.Get(LocKeys.Settings.VersionLine),
            FontSize = SettingsMetrics.CaptionSize,
            Foreground = new SolidColorBrush(surfaces.ForegroundTertiary),
            Margin = new Thickness(0, 1, 0, 0),
        });
        var footer = new Border
        {
            BorderBrush = new SolidColorBrush(surfaces.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = version,
        };
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        RebuildNav();
        return new Border
        {
            BorderBrush = new SolidColorBrush(surfaces.Line),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(10, 14, 10, 14),
            Child = grid,
        };
    }

    private Control BuildPane()
    {
        _content.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _content.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _content.Padding = new Thickness(SettingsMetrics.PanePaddingX, SettingsMetrics.PanePaddingY,
            SettingsMetrics.PanePaddingX, SettingsMetrics.PanePaddingY);
        Grid.SetColumn(_content, 1);
        return _content;
    }

    private void RebuildNav()
    {
        _nav.Children.Clear();
        _navButtons.Clear();
        _navCounts.Clear();
        var surfaces = _settings.Current.Surfaces;
        foreach (var (section, labelKey, icon) in SectionOrder)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            var glyph = new SettingsGlyph
            {
                Kind = icon,
                Brush = new SolidColorBrush(surfaces.ForegroundTertiary),
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(glyph, 0);
            row.Children.Add(glyph);

            var label = new TextBlock
            {
                Text = _localizer.Get(labelKey),
                FontSize = 12,
                Foreground = new SolidColorBrush(surfaces.ForegroundSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 0, 0),
            };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            var count = new Border
            {
                MinWidth = 16,
                Height = 16,
                Padding = new Thickness(4, 0, 4, 0),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(surfaces.RaisedHover),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsVisible = false,
                Child = new TextBlock
                {
                    FontSize = SettingsMetrics.CaptionSize,
                    Foreground = new SolidColorBrush(surfaces.ForegroundSecondary),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(count, 2);
            row.Children.Add(count);
            _navCounts[section] = (TextBlock)count.Child!;

            var button = new Button
            {
                Content = row,
                Height = SettingsMetrics.NavItemHeight,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 0, 10, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = section,
            };
            button.Click += (_, _) => ShowSection(section);
            button.PointerEntered += (_, _) =>
            {
                if ((SettingsWindowSection)button.Tag! != _section)
                {
                    button.Background = new SolidColorBrush(surfaces.Raised);
                }
            };
            button.PointerExited += (_, _) =>
            {
                if ((SettingsWindowSection)button.Tag! != _section)
                {
                    button.Background = Brushes.Transparent;
                }
            };
            _navButtons.Add(button);
            _nav.Children.Add(button);
        }

        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        UpdateNavSelection();
    }

    private void UpdateNavSelection()
    {
        var surfaces = _settings.Current.Surfaces;
        foreach (var button in _navButtons)
        {
            var selected = (SettingsWindowSection)button.Tag! == _section;
            button.Background = selected ? new SolidColorBrush(surfaces.Raised) : Brushes.Transparent;
            if (button.Content is Grid row)
            {
                foreach (var child in row.Children)
                {
                    if (child is TextBlock label)
                    {
                        label.Foreground = new SolidColorBrush(selected ? surfaces.Foreground : surfaces.ForegroundSecondary);
                        label.FontWeight = selected ? FontWeight.Medium : FontWeight.Normal;
                    }
                    else if (child is SettingsGlyph glyph)
                    {
                        glyph.Brush = new SolidColorBrush(selected ? surfaces.Foreground : surfaces.ForegroundTertiary);
                        glyph.InvalidateVisual();
                    }
                }
            }
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
        if (!_navCounts.TryGetValue(section, out var text) || count <= 0)
        {
            return;
        }

        text.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (text.Parent is Border badge)
        {
            badge.IsVisible = true;
        }
    }

    private void RebuildContent()
    {
        var context = new SettingsWindowContext
        {
            Settings = _settings,
            Localizer = _localizer,
            Marketplace = _marketplace,
            Plugins = _plugins,
            Surfaces = _settings.Current.Surfaces,
            Owner = () => this,
            OpenPath = _openPath,
            ShowStatus = ShowStatus,
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
        var themeChanged = previous.Theme != current.Theme;
        ApplyChrome();
        if (themeChanged)
        {
            RebuildNav();
            RebuildContent();
        }
        else
        {
            _preview?.Update(current, _localizer);
        }
    }

    /// <summary>Draws the prototype's island mark (a pill outline with a status dot).</summary>
    private sealed class IslandMark : Control
    {
        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            context.DrawRectangle(null, new Pen(Brushes.White, 1.1),
                new Rect(width * 0.20, height * 0.36, width * 0.52, height * 0.28), 3, 3);
            context.DrawEllipse(Brushes.White, null, new Point(width * 0.80, height * 0.5), 1.5, 1.5);
        }
    }
}
