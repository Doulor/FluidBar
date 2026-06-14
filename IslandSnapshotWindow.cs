using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace FluidBar;

internal sealed class IslandSnapshotWindow : Window
{
    private const double ShellBleedMargin = 14;
    private const double ShellBleed = ShellBleedMargin * 2;

    private readonly Border _pill;
    private readonly TextBlock _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _content;
    private readonly DropShadowEffect _glow;
    private bool _isClosing;

    private static readonly Dictionary<string, string> IconGlyphs = new()
    {
        ["clipboard"] = "\uE16F",
        ["volume"] = "\uE767",
        ["volume_mute"] = "\uE74F",
        ["battery"] = "\uE850",
        ["battery_charge"] = "\uEBA9",
        ["battery_low"] = "\uEBAF",
        ["inputmethod"] = "\uE765",
        ["lockkey"] = "\uE72E",
        ["network"] = "\uE701",
        ["network_off"] = "\uE8D9",
        ["usb"] = "\uE88E",
        ["brightness"] = "\uE706",
        ["bluetooth"] = "\uE702",
        ["media"] = "\uE768",
        ["notification"] = "\uE7F4",
        ["agent"] = "\uE8F2",
        ["info"] = "\uE946",
    };

    private static readonly Dictionary<string, MediaColor> IconColors = new()
    {
        ["clipboard"] = MediaColor.FromRgb(10, 132, 255),
        ["volume"] = MediaColor.FromRgb(10, 132, 255),
        ["volume_mute"] = MediaColor.FromRgb(142, 142, 147),
        ["battery"] = MediaColor.FromRgb(48, 209, 88),
        ["battery_charge"] = MediaColor.FromRgb(48, 209, 88),
        ["battery_low"] = MediaColor.FromRgb(255, 69, 58),
        ["inputmethod"] = MediaColor.FromRgb(10, 132, 255),
        ["lockkey"] = MediaColor.FromRgb(191, 90, 242),
        ["network"] = MediaColor.FromRgb(48, 209, 88),
        ["network_off"] = MediaColor.FromRgb(255, 69, 58),
        ["usb"] = MediaColor.FromRgb(255, 159, 10),
        ["brightness"] = MediaColor.FromRgb(255, 214, 10),
        ["bluetooth"] = MediaColor.FromRgb(10, 132, 255),
        ["media"] = MediaColor.FromRgb(255, 45, 85),
        ["notification"] = MediaColor.FromRgb(90, 200, 250),
        ["agent"] = MediaColor.FromRgb(191, 90, 242),
        ["info"] = MediaColor.FromRgb(142, 142, 147),
    };

    public IslandSnapshotWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = WpfBrushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Opacity = 0;

        var root = new Grid
        {
            Margin = new Thickness(ShellBleedMargin),
            ClipToBounds = false
        };

        _glow = new DropShadowEffect
        {
            BlurRadius = 28,
            ShadowDepth = 0,
            Opacity = 0.34,
            Color = MediaColor.FromRgb(10, 132, 255)
        };

        _pill = new Border
        {
            Padding = new Thickness(12, 8, 14, 8),
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(MediaColor.FromArgb(238, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(34, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = _glow,
            ClipToBounds = true
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(IconColors["info"]),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _icon = new TextBlock
        {
            FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Foreground = WpfBrushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconBorder.Child = _icon;
        Grid.SetColumn(iconBorder, 0);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _title = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(156, 156, 161)),
            FontFamily = new WpfFontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _content = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(247, 247, 249)),
            FontFamily = new WpfFontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        textPanel.Children.Add(_title);
        textPanel.Children.Add(_content);
        Grid.SetColumn(textPanel, 1);

        grid.Children.Add(iconBorder);
        grid.Children.Add(textPanel);
        _pill.Child = grid;
        root.Children.Add(_pill);
        Content = root;

        Tag = iconBorder;
        Closed += (_, _) => _isClosing = true;
    }

    public void SetView(IslandStackItem item, FluidBarSettings settings)
    {
        var view = item.View;
        var iconKind = view.IconKind ?? "info";
        _icon.Text = IconGlyphs.TryGetValue(iconKind, out var glyph)
            ? glyph
            : IconGlyphs["info"];

        var color = IconColors.TryGetValue(iconKind, out var accent)
            ? accent
            : IconColors["info"];
        if (Tag is Border iconBorder)
            iconBorder.Background = new SolidColorBrush(color);
        _glow.Color = color;

        _title.Text = view.Title;
        _content.Text = view.Kind == IslandViewKind.Progress
            ? $"{view.ProgressPercent}%"
            : view.StatusText.Length > 0
                ? view.StatusText
                : view.Content;

        try
        {
            _pill.Background = new SolidColorBrush(
                (MediaColor)MediaColorConverter.ConvertFromString(settings.BackgroundColor));
        }
        catch
        {
            _pill.Background = new SolidColorBrush(MediaColor.FromArgb(238, 0, 0, 0));
        }

        _pill.Opacity = Math.Clamp(settings.Opacity * 0.94, 0.62, 0.96);
    }

    public void Place(double left, double top, double visualWidth, double visualHeight, bool animated)
    {
        if (_isClosing)
            return;

        var targetWidth = visualWidth + ShellBleed;
        var targetHeight = visualHeight + ShellBleed;

        if (!animated)
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Left = left;
            Top = top;
            Width = targetWidth;
            Height = targetHeight;
            return;
        }

        // .NET 10 throws if DoubleAnimation origin is NaN (first-time placement).
        // Seed position/size directly before animating.
        if (double.IsNaN(Left)) Left = left;
        if (double.IsNaN(Top)) Top = top;
        if (double.IsNaN(Width)) Width = targetWidth;
        if (double.IsNaN(Height)) Height = targetHeight;

        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(LeftProperty, new DoubleAnimation(left, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(top, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        BeginAnimation(WidthProperty, new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
    }

    public void Reveal()
    {
        if (_isClosing)
            return;

        if (!IsVisible)
            Show();

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.88, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    public void Dismiss()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        BeginAnimation(OpacityProperty, null);
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => TryClose();
        BeginAnimation(OpacityProperty, fade);
    }

    private void TryClose()
    {
        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
        }
    }
}
