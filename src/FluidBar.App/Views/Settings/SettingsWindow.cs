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
/// Settings window (900×620 custom chrome, seven sections). Every change is applied live through
/// <see cref="SettingsController"/>; the island host listens to the same controller. Language and theme
/// switches repaint/re-build the window without restarting the app (spec 8, S8).
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
    private readonly List<Button> _navButtons = [];
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
        Background = new SolidColorBrush(settings.Current.Surfaces.Window);
        Content = BuildLayout();

        _settings.Changed += OnSettingsChanged;
        _localizer.Changed += OnLocalizerChanged;
        RebuildContent();
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
    protected override void OnClosed(EventArgs e)
    {
        _settings.Changed -= OnSettingsChanged;
        _localizer.Changed -= OnLocalizerChanged;
        base.OnClosed(e);
    }

    private Control BuildLayout()
    {
        var surfaces = _settings.Current.Surfaces;
        var root = new Grid { RowDefinitions = new RowDefinitions("48,*") };

        // Title bar: app name, section title, drag surface, close button.
        var titleBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = new SolidColorBrush(surfaces.Surface) };
        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        _titleText.FontFamily = Fonts.DisplaySemiBold.FontFamily;
        _titleText.FontSize = 13;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.VerticalAlignment = VerticalAlignment.Center;
        _titleText.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(_titleText, 0);
        titleBar.Children.Add(_titleText);

        var dragArea = new Border { Background = Brushes.Transparent };
        dragArea.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        Grid.SetColumn(dragArea, 1);
        titleBar.Children.Add(dragArea);

        var close = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 46,
            Height = 34,
            Margin = new Thickness(0, 7, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        titleBar.Children.Add(close);
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
        var navHost = new Border
        {
            Background = new SolidColorBrush(surfaces.Surface),
            Padding = new Thickness(12, 10, 12, 10),
            Child = _nav,
        };
        Grid.SetColumn(navHost, 0);
        body.Children.Add(navHost);

        _content.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _content.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _content.Padding = new Thickness(24, 12, 20, 12);
        Grid.SetColumn(_content, 1);
        body.Children.Add(_content);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        RebuildNav();
        return root;
    }

    private void RebuildNav()
    {
        _nav.Children.Clear();
        _navButtons.Clear();
        var foreground = new SolidColorBrush(_settings.Current.Surfaces.Foreground);
        foreach (var (section, labelKey, icon) in SectionOrder)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new ItemIcon { Kind = icon, Width = 18, Height = 18, Brush = foreground });
            row.Children.Add(new TextBlock { Text = _localizer.Get(labelKey), VerticalAlignment = VerticalAlignment.Center, FontSize = Typography.SettingsBodySize });
            var button = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = foreground,
                Tag = section,
            };
            button.Click += (_, _) => ShowSection(section);
            _navButtons.Add(button);
            _nav.Children.Add(button);
        }

        _titleText.Text = _localizer.Get(LocKeys.Settings.Title);
        UpdateNavSelection();
    }

    private void UpdateNavSelection()
    {
        var surfaces = _settings.Current.Surfaces;
        for (var i = 0; i < _navButtons.Count; i++)
        {
            var selected = (SettingsWindowSection)_navButtons[i].Tag! == _section;
            _navButtons[i].Background = selected
                ? new SolidColorBrush(surfaces.Raised)
                : Brushes.Transparent;
            _navButtons[i].FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
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

        _content.Content = new Border
        {
            Padding = new Thickness(0, 4, 0, 0),
            Child = content,
        };
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
        Background = new SolidColorBrush(current.Surfaces.Window);
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

    /// <summary>Draws a small vector glyph for a nav item (no icon font/emoji).</summary>
    private sealed class ItemIcon : Control
    {
        public IconKind Kind { get; set; } = IconKind.Settings;

        public IBrush Brush { get; set; } = Brushes.White;

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            Icons.Draw(context, Kind, bounds, Brush);
        }
    }
}
