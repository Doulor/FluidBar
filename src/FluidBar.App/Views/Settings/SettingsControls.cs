using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using FluidBar.App.Theme;

namespace FluidBar.App.Views.Settings;

/// <summary>Button visual variants of the settings window (one implementation, several tones).</summary>
internal enum SettingsButtonVariant
{
    /// <summary>Filled with <c>--ui-active</c> (primary action: install, submit, update).</summary>
    Solid,

    /// <summary>Raised chip with a hairline (secondary action: open folder, export…).</summary>
    Raised,

    /// <summary>Text-only button on the pane background (rollback, dismiss).</summary>
    Quiet,

    /// <summary>Text/icon button that only shows a surface on hover (icon actions).</summary>
    Ghost,

    /// <summary>Raised chip with the danger tone (reset, reject).</summary>
    Danger,

    /// <summary>Light chip for dark cards (the featured install button).</summary>
    Contrast,

    /// <summary>Inline text link (about page links).</summary>
    Link,
}

/// <summary>Button sizes; each maps to the prototype's component height.</summary>
internal enum SettingsButtonSize
{
    /// <summary>27 px — <c>.mc-install</c>.</summary>
    Xs,

    /// <summary>28 px — <c>.featured-install</c> / community submit.</summary>
    Sm,

    /// <summary>30 px — <c>.small-action</c> / <c>.open-folder</c>.</summary>
    Md,

    /// <summary>32 px — <c>.about-btn</c>.</summary>
    Lg,

    /// <summary>24 px square — <c>.icon-btn</c>.</summary>
    Icon,
}

/// <summary>Chip variants (the prototype's small inline labels).</summary>
internal enum SettingsChipKind
{
    /// <summary>26 px category chip (<c>.cat-chip</c>).</summary>
    Category,

    /// <summary>16 px capability chip (<c>.perm</c>).</summary>
    Permission,

    /// <summary>16 px uppercase badge (<c>.badge</c>).</summary>
    Badge,

    /// <summary>22 px keycap (<c>.keycap</c>).</summary>
    Keycap,

    /// <summary>24 px value tag (<c>.value-tag</c>).</summary>
    Tag,

    /// <summary>24 px contributor chip (<c>.contrib</c>).</summary>
    Contributor,

    /// <summary>16 px counter (<c>.nav-count</c>).</summary>
    Count,
}

/// <summary>Card containers.</summary>
internal enum SettingsCardKind
{
    /// <summary>Plugin row: 10 px radius, 11/10 padding, raised fill.</summary>
    Row,

    /// <summary>Marketplace/community card: 112 px minimum, 12 px padding, 10 px radius.</summary>
    Tile,

    /// <summary>Featured hero card: 122 px minimum, 18 px inset, 14 px radius.</summary>
    Featured,

    /// <summary>Empty-state box.</summary>
    Empty,
}

/// <summary>Text field variants.</summary>
internal enum SettingsFieldKind
{
    /// <summary>Single-line text input.</summary>
    Text,

    /// <summary>Monospaced input (hotkeys, versions, paths).</summary>
    Mono,

    /// <summary>Search field with a leading glyph.</summary>
    Search,

    /// <summary>Read-only mono path with a folder glyph.</summary>
    Path,

    /// <summary>Numeric input.</summary>
    Number,

    /// <summary>Redacted, disabled secret field.</summary>
    Secret,
}

/// <summary>Semantic tone of an inline status line.</summary>
internal enum SettingsTone
{
    /// <summary>Neutral hint.</summary>
    Neutral,

    /// <summary>Informational (accent blue).</summary>
    Info,

    /// <summary>Success.</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Failure.</summary>
    Danger,
}

/// <summary>Field chrome that exposes the input it hosts (focus, selection, live text).</summary>
internal sealed class SettingsFieldView : Border
{
    /// <summary>The hosted text box.</summary>
    public TextBox Box { get; internal set; } = new();
}

/// <summary>Inline status line; the text can be replaced while the pane lives.</summary>
internal sealed class SettingsStatusLine : TextBlock
{
}

/// <summary>A chip <see cref="Border"/> that exposes its label so callers can retitle it in place.</summary>
internal sealed class SettingsChipView : Border
{
    /// <summary>The chip label.</summary>
    public TextBlock Label { get; internal set; } = new();
}

/// <summary>
/// The single implementation of every settings-window element (task-22). Pages compose these; no page
/// builds its own button/chip/card/field/dropdown/slider/row/status by hand. Every number comes from
/// <see cref="SettingsMetrics"/>, every colour from <see cref="SettingsPalette"/>.
/// </summary>
internal static class SettingsControls
{
    // ---- buttons ------------------------------------------------------------------------------

    /// <summary>Creates a settings button (real <see cref="Button"/>: keyboard, focus and automation work).</summary>
    public static Button Button(
        string label,
        SettingsPalette palette,
        SettingsButtonVariant variant = SettingsButtonVariant.Raised,
        SettingsButtonSize size = SettingsButtonSize.Md,
        SettingsIconKind? icon = null,
        Action? onClick = null,
        bool enabled = true,
        double? minWidth = null)
    {
        var (height, padX, fontSize) = Size(size);
        var (background, border, foreground, weight) = Variant(variant, palette);
        var hover = Hover(variant, palette, background);

        var button = new Button
        {
            Height = height,
            MinWidth = minWidth ?? 0,
            Padding = new Thickness(size == SettingsButtonSize.Icon ? 0 : padX, 0, size == SettingsButtonSize.Icon ? 0 : padX, 0),
            CornerRadius = new CornerRadius(
                size == SettingsButtonSize.Icon ? SettingsMetrics.IconButtonRadius : SettingsMetrics.ButtonRadius),
            FontSize = fontSize,
            FontFamily = Fonts.Ui,
            FontWeight = weight,
            Foreground = foreground,
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(border is null ? 0 : 1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsEnabled = enabled,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        button.Content = icon is null
            ? label
            : IconRow(icon.Value, label, foreground, size == SettingsButtonSize.Icon ? 0 : 5);

        // Fluent paints its own hover/pressed fills; override them for every variant (one code path).
        if (hover is not null)
        {
            AddStateBackground(button, ":pointerover", hover.Value.Background);
            AddStateBackground(button, ":pressed", hover.Value.Pressed);
            AddStateBorder(button, ":pointerover", hover.Value.Border);
        }

        if (onClick is not null && enabled)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    /// <summary>Square icon-only button (<c>.icon-btn</c>).</summary>
    public static Button IconButton(SettingsIconKind kind, SettingsPalette palette, Action onClick, bool enabled = true)
    {
        var button = Button(string.Empty, palette, SettingsButtonVariant.Ghost, SettingsButtonSize.Icon, kind,
            onClick, enabled);
        button.Width = SettingsMetrics.IconButton;
        return button;
    }

    // ---- chips --------------------------------------------------------------------------------

    /// <summary>Creates a chip/badge/tag/keycap (one implementation, per-kind metrics).</summary>
    public static SettingsChipView Chip(
        string text,
        SettingsPalette palette,
        SettingsChipKind kind,
        bool active = false,
        SettingsIconKind? icon = null,
        Color? dot = null)
    {
        var (height, radius, padX, fontSize, uppercase) = kind switch
        {
            SettingsChipKind.Category => (SettingsMetrics.CatChipHeight, SettingsMetrics.CatChipRadius,
                SettingsMetrics.CatChipPadX, SettingsMetrics.CatChipSize, false),
            SettingsChipKind.Permission => (SettingsMetrics.PermHeight, SettingsMetrics.PermRadius,
                SettingsMetrics.PermPadX, SettingsMetrics.PermSize, false),
            SettingsChipKind.Badge => (SettingsMetrics.BadgeHeight, SettingsMetrics.BadgeRadius,
                SettingsMetrics.BadgePadX, SettingsMetrics.BadgeSize, true),
            SettingsChipKind.Keycap => (SettingsMetrics.KeycapHeight, SettingsMetrics.KeycapRadius,
                SettingsMetrics.KeycapPadX, SettingsMetrics.KeycapFontSize, false),
            SettingsChipKind.Tag => (SettingsMetrics.ValueTagHeight, SettingsMetrics.ValueTagRadius,
                SettingsMetrics.ValueTagPadX, SettingsMetrics.ValueTagSize, false),
            SettingsChipKind.Contributor => (SettingsMetrics.ContribHeight, SettingsMetrics.ContribRadius,
                SettingsMetrics.ContribPadX, SettingsMetrics.ContribSize, false),
            _ => (SettingsMetrics.NavCountHeight, SettingsMetrics.NavCountRadius,
                SettingsMetrics.NavCountPadX, SettingsMetrics.NavCountSize, false),
        };

        var foreground = active ? palette.ActiveForegroundBrush
            : kind == SettingsChipKind.Category ? palette.SecondaryBrush
            : kind == SettingsChipKind.Badge ? palette.SecondaryBrush
            : palette.SecondaryBrush;
        var background = active ? palette.ActiveBrush
            : kind == SettingsChipKind.Badge ? palette.RaisedHoverBrush
            : palette.RaisedBrush;
        var border = active ? null : palette.LineBrush;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (dot is not null)
        {
            content.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(dot.Value),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (icon is not null)
        {
            content.Children.Add(new SettingsIcon
            {
                Kind = icon.Value,
                Brush = foreground,
                Width = kind == SettingsChipKind.Permission ? 7 : SettingsMetrics.SmallActionIcon,
                Height = kind == SettingsChipKind.Permission ? 7 : SettingsMetrics.SmallActionIcon,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var label = new TextBlock
        {
            Text = uppercase ? text.ToUpperInvariant() : text,
            FontSize = fontSize,
            FontWeight = active || kind == SettingsChipKind.Badge ? FontWeight.SemiBold : FontWeight.Normal,
            LetterSpacing = uppercase ? fontSize * SettingsMetrics.BadgeTracking : 0,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(label);

        return new SettingsChipView
        {
            Label = label,
            Height = height,
            MinWidth = kind switch
            {
                SettingsChipKind.Keycap => SettingsMetrics.KeycapMinWidth,
                SettingsChipKind.Tag => SettingsMetrics.ValueTagMinWidth,
                SettingsChipKind.Count => SettingsMetrics.NavCountHeight,
                _ => 0,
            },
            Padding = new Thickness(padX, 0, padX, 0),
            CornerRadius = new CornerRadius(radius),
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(border is null ? 0 : 1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = KindAlignment(kind),
            Child = content,
        };
    }

    // ---- cards --------------------------------------------------------------------------------

    /// <summary>Creates a card container.</summary>
    public static Border Card(Control child, SettingsPalette palette, SettingsCardKind kind)
    {
        var (radius, padding, minHeight, border) = kind switch
        {
            SettingsCardKind.Row => (SettingsMetrics.PluginRowRadius,
                new Thickness(SettingsMetrics.PluginRowPadX, SettingsMetrics.PluginRowPadY,
                    SettingsMetrics.PluginRowPadX, SettingsMetrics.PluginRowPadY), 0.0, palette.LineBrush),
            SettingsCardKind.Tile => (SettingsMetrics.MarketCardRadius, new Thickness(SettingsMetrics.MarketCardPad),
                SettingsMetrics.MarketCardMinHeight, palette.LineBrush),
            // The hero card paints its gradient edge to edge; its 18 px inset lives on the content
            // (a padding inside the shell would clip the gradient to a smaller rectangle).
            SettingsCardKind.Featured => (SettingsMetrics.FeaturedRadius,
                new Thickness(0), SettingsMetrics.FeaturedMinHeight,
                new SolidColorBrush(Color.FromArgb(23, 255, 255, 255))),
            _ => (SettingsMetrics.MarketCardRadius, new Thickness(SettingsMetrics.MarketCardPad), 0.0,
                palette.LineBrush),
        };

        return new Border
        {
            CornerRadius = new CornerRadius(radius),
            MinHeight = minHeight,
            Background = palette.RaisedBrush,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            ClipToBounds = kind == SettingsCardKind.Featured,
            Child = new Border { Padding = padding, Child = child },
        };
    }

    // ---- fields -------------------------------------------------------------------------------

    /// <summary>Creates a styled text field (one implementation for every input in the window).</summary>
    public static SettingsFieldView TextField(
        SettingsPalette palette,
        SettingsFieldKind kind = SettingsFieldKind.Text,
        string text = "",
        string? watermark = null,
        double? width = null,
        Action<string>? onCommit = null,
        bool enabled = true,
        Action<string>? onChanged = null,
        Action? onEnter = null)
    {
        var mono = kind is SettingsFieldKind.Mono or SettingsFieldKind.Path or SettingsFieldKind.Secret
            or SettingsFieldKind.Number;
        var readOnly = kind is SettingsFieldKind.Path or SettingsFieldKind.Secret;
        var height = kind == SettingsFieldKind.Number ? SettingsMetrics.PathFieldHeight : SettingsMetrics.SearchHeight;
        var radius = kind == SettingsFieldKind.Search ? SettingsMetrics.SearchRadius : SettingsMetrics.PathFieldRadius;
        var fontSize = kind switch
        {
            SettingsFieldKind.Path => SettingsMetrics.PathFieldSize,
            SettingsFieldKind.Number or SettingsFieldKind.Secret => SettingsMetrics.PathFieldSize,
            _ => SettingsMetrics.SearchFontSize,
        };

        var box = new TextBox
        {
            Text = kind == SettingsFieldKind.Secret ? "••••••••" : text,
            Watermark = watermark,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            FontSize = fontSize,
            FontFamily = mono ? Fonts.Mono : Fonts.Ui,
            Foreground = enabled ? palette.ForegroundBrush : palette.TertiaryBrush,
            IsReadOnly = readOnly,
            IsEnabled = enabled,
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = width is null ? double.NaN : width.Value,
        };
        if (onCommit is not null)
        {
            box.LostFocus += (_, _) => onCommit(box.Text ?? string.Empty);
        }

        if (onChanged is not null)
        {
            box.TextChanged += (_, _) => onChanged(box.Text ?? string.Empty);
        }

        if (onEnter is not null)
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    onEnter();
                    e.Handled = true;
                }
            };
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (kind is SettingsFieldKind.Search or SettingsFieldKind.Path)
        {
            grid.Children.Add(new SettingsIcon
            {
                Kind = kind == SettingsFieldKind.Search ? SettingsIconKind.Search : SettingsIconKind.Folder,
                Brush = palette.TertiaryBrush,
                Width = kind == SettingsFieldKind.Search ? SettingsMetrics.SearchIcon : SettingsMetrics.PathIcon,
                Height = kind == SettingsFieldKind.Search ? SettingsMetrics.SearchIcon : SettingsMetrics.PathIcon,
                VerticalAlignment = VerticalAlignment.Center,
            });
            box.Margin = new Thickness(SettingsMetrics.SearchGap, 0, 0, 0);
            Grid.SetColumn(box, 1);
        }

        grid.Children.Add(box);
        return new SettingsFieldView
        {
            Box = box,
            Height = height,
            Width = width is null ? double.NaN : width.Value + 2,
            Padding = new Thickness(SettingsMetrics.SearchPadX, 0, SettingsMetrics.SearchPadX, 0),
            CornerRadius = new CornerRadius(radius),
            Background = enabled ? palette.FieldBrush : palette.RaisedBrush,
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    /// <summary>Creates a styled drop-down (plugin settings select).</summary>
    public static Control Select(
        IReadOnlyList<(string Label, string Value)> options,
        string selected,
        SettingsPalette palette,
        Action<string> changed,
        double minWidth = SettingsMetrics.SelectMinWidth)
    {
        var combo = new ComboBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            MinWidth = minWidth - (2 * SettingsMetrics.SelectPadX),
            FontSize = SettingsMetrics.SelectFontSize,
            FontFamily = Fonts.Ui,
            Foreground = palette.ForegroundBrush,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var index = 0;
        for (var i = 0; i < options.Count; i++)
        {
            combo.Items.Add(new ComboBoxItem { Content = options[i].Label, Tag = options[i].Value });
            if (string.Equals(options[i].Value, selected, StringComparison.Ordinal))
            {
                index = i;
            }
        }

        combo.SelectedIndex = index;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: string value })
            {
                changed(value);
            }
        };
        return new Border
        {
            Height = SettingsMetrics.SelectHeight,
            MinWidth = minWidth,
            Padding = new Thickness(SettingsMetrics.SelectPadX, 0, SettingsMetrics.SelectPadX, 0),
            CornerRadius = new CornerRadius(SettingsMetrics.SelectRadius),
            Background = palette.FieldBrush,
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            Child = combo,
        };
    }

    /// <summary>Slider + value tag cell (<c>.setting-range</c>).</summary>
    public static Control Range(SettingsSlider slider, Control tag)
    {
        slider.Width = SettingsMetrics.RangeWidth - SettingsMetrics.RangeGap - SettingsMetrics.ValueTagMinWidth;
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.RangeGap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(slider);
        panel.Children.Add(tag);
        return panel;
    }

    /// <summary>Thin progress bar used by installs.</summary>
    public static ProgressBar Progress(SettingsPalette palette, double accentAlpha = 1)
    {
        var bar = new ProgressBar
        {
            Height = SettingsMetrics.ProgressHeight,
            MinHeight = SettingsMetrics.ProgressHeight,
            Minimum = 0,
            Maximum = 100,
            Background = palette.RaisedHoverBrush,
            Foreground = palette.ForegroundBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(SettingsMetrics.ProgressHeight / 2),
        };
        bar.Opacity = accentAlpha;
        return bar;
    }

    // ---- status / empty -----------------------------------------------------------------------

    /// <summary>Inline status line with a semantic tone.</summary>
    public static SettingsStatusLine StatusLine(string text, SettingsPalette palette,
        SettingsTone tone = SettingsTone.Neutral, double fontSize = SettingsMetrics.StatusSize,
        TextAlignment align = TextAlignment.Left)
        => new()
        {
            Text = text,
            FontSize = fontSize,
            Foreground = Tone(tone, palette),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = align,
            HorizontalAlignment = align == TextAlignment.Center ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
        };

    /// <summary>Empty-state box (no plugins, no results, no community listings).</summary>
    public static Control EmptyState(string text, SettingsPalette palette)
        => Card(new TextBlock
        {
            Text = text,
            FontSize = SettingsMetrics.StatusSize,
            Foreground = palette.TertiaryBrush,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        }, palette, SettingsCardKind.Empty);

    /// <summary>Pane footer note (<c>.settings-footer-note</c>) or the market's shield footnote.</summary>
    public static Control FooterNote(string text, SettingsPalette palette, bool market = false)
    {
        if (!market)
        {
            return new Border
            {
                BorderBrush = palette.LineBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, SettingsMetrics.FooterNoteMarginTop, 0, 0),
                Padding = new Thickness(0, SettingsMetrics.FooterNotePadTop, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = SettingsMetrics.FooterNoteSize,
                    Foreground = palette.TertiaryBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Margin = new Thickness(0, SettingsMetrics.MarketFootnoteMarginTop, 0, 0),
        };
        row.Children.Add(new SettingsIcon
        {
            Kind = SettingsIconKind.Shield,
            Brush = palette.TertiaryBrush,
            Width = SettingsMetrics.MarketFootnoteIcon,
            Height = SettingsMetrics.MarketFootnoteIcon,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = SettingsMetrics.MarketFootnoteSize,
            Foreground = palette.TertiaryBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620,
        });
        return row;
    }

    // ---- internals ----------------------------------------------------------------------------

    private static (double Height, double PadX, double FontSize) Size(SettingsButtonSize size) => size switch
    {
        SettingsButtonSize.Xs => (SettingsMetrics.ButtonXs, SettingsMetrics.ButtonPadXs, SettingsMetrics.ButtonSizeXs),
        SettingsButtonSize.Sm => (SettingsMetrics.ButtonSm, SettingsMetrics.ButtonPadSm, SettingsMetrics.ButtonSizeSm),
        SettingsButtonSize.Lg => (SettingsMetrics.ButtonLg, SettingsMetrics.ButtonPadLg, SettingsMetrics.ButtonSizeLg),
        SettingsButtonSize.Icon => (SettingsMetrics.IconButton, 0, SettingsMetrics.ButtonSizeMd),
        _ => (SettingsMetrics.ButtonMd, SettingsMetrics.ButtonPadMd, SettingsMetrics.ButtonSizeMd),
    };

    private static (IBrush? Background, IBrush? Border, IBrush Foreground, FontWeight Weight) Variant(
        SettingsButtonVariant variant, SettingsPalette palette) => variant switch
    {
        SettingsButtonVariant.Solid => (palette.ActiveBrush, null, palette.ActiveForegroundBrush, FontWeight.SemiBold),
        SettingsButtonVariant.Raised => (palette.RaisedBrush, palette.LineBrush, palette.SecondaryBrush, FontWeight.Normal),
        SettingsButtonVariant.Quiet => (Brushes.Transparent, null, palette.TertiaryBrush, FontWeight.Normal),
        SettingsButtonVariant.Ghost => (Brushes.Transparent, null, palette.SecondaryBrush, FontWeight.Normal),
        SettingsButtonVariant.Danger => (new SolidColorBrush(Color.FromArgb(15, 255, 69, 58)),
            new SolidColorBrush(Color.FromArgb(51, 255, 69, 58)), new SolidColorBrush(Color.Parse("#E06A62")),
            FontWeight.Normal),
        SettingsButtonVariant.Contrast => (new SolidColorBrush(Color.Parse("#EAFAFF")), null,
            new SolidColorBrush(Color.Parse("#07151B")), FontWeight.SemiBold),
        _ => (Brushes.Transparent, null, palette.SecondaryBrush, FontWeight.Normal),
    };

    private static (IBrush Background, IBrush Pressed, IBrush Border)? Hover(
        SettingsButtonVariant variant, SettingsPalette palette, IBrush? background) => variant switch
    {
        SettingsButtonVariant.Solid => (palette.ActiveBrush, palette.ActiveBrush, Brushes.Transparent),
        SettingsButtonVariant.Raised => (palette.RaisedHoverBrush, palette.RaisedHoverBrush, palette.LineStrongBrush),
        SettingsButtonVariant.Quiet or SettingsButtonVariant.Ghost or SettingsButtonVariant.Link
            => (palette.RaisedBrush, palette.RaisedHoverBrush, Brushes.Transparent),
        SettingsButtonVariant.Danger => (new SolidColorBrush(Color.FromArgb(31, 255, 69, 58)),
            new SolidColorBrush(Color.FromArgb(45, 255, 69, 58)), new SolidColorBrush(Color.FromArgb(80, 255, 69, 58))),
        SettingsButtonVariant.Contrast => (new SolidColorBrush(Color.Parse("#FFFFFF")),
            new SolidColorBrush(Color.Parse("#D6F2FF")), Brushes.Transparent),
        _ => null,
    };

    private static void AddStateBackground(Button button, string state, IBrush brush)
        => button.Styles.Add(new Style(x => x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(TemplatedControl.BackgroundProperty, brush) },
        });

    private static void AddStateBorder(Button button, string state, IBrush brush)
        => button.Styles.Add(new Style(x => x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(TemplatedControl.BorderBrushProperty, brush) },
        });

    private static Control IconRow(SettingsIconKind icon, string label, IBrush foreground, double gap)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = gap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new SettingsIcon
        {
            Kind = icon,
            Brush = foreground,
            Width = SettingsMetrics.SmallActionIcon,
            Height = SettingsMetrics.SmallActionIcon,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!string.IsNullOrEmpty(label))
        {
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        return row;
    }

    private static HorizontalAlignment KindAlignment(SettingsChipKind kind)
        => kind is SettingsChipKind.Count ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    private static IBrush Tone(SettingsTone tone, SettingsPalette palette) => tone switch
    {
        SettingsTone.Info => new SolidColorBrush(Accents.Info),
        SettingsTone.Success => new SolidColorBrush(Accents.Success),
        SettingsTone.Warning => new SolidColorBrush(Accents.Warning),
        SettingsTone.Danger => new SolidColorBrush(Accents.Danger),
        _ => palette.TertiaryBrush,
    };
}
