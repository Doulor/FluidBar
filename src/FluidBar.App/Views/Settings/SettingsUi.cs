using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Controls;
using FluidBar.App.Theme;

namespace FluidBar.App.Views.Settings;

/// <summary>Prototype metrics (docs/rewrite/prototype2/styles.css, settings block).</summary>
internal static class SettingsMetrics
{
    public const double TitleBarHeight = 50;
    public const double NavWidth = 208;
    public const double NavItemHeight = 36;
    public const double PanePaddingX = 30;
    public const double PanePaddingY = 24;
    public const double RowMinHeight = 53;
    public const double RowGap = 18;
    public const double HeadSize = 22;
    public const double DescSize = 11;
    public const double RowTitleSize = 12;
    public const double RowDescSize = 10;
    public const double CaptionSize = 8.5;
    public const double SegmentedWidth = 188;
    public const double SegmentedSmallWidth = 148;
    public const double SliderWidth = 200;
    public const double WindowRadius = 22;
}

/// <summary>Vector glyph used by the settings chrome.</summary>
internal sealed class SettingsGlyph : Control
{
    /// <summary>Icon identity.</summary>
    public IconKind Kind { get; set; } = IconKind.Settings;

    /// <summary>Stroke/fill brush.</summary>
    public IBrush Brush { get; set; } = Brushes.White;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
        => Icons.Draw(context, Kind, new Rect(0, 0, Bounds.Width, Bounds.Height), Brush);
}

/// <summary>Two-to-four segment control matching the prototype's <c>.segmented</c>/<c>.seg</c>.</summary>
public sealed class SegmentedControl : Control
{
    private readonly List<(string Label, string Value)> _items = [];
    private readonly List<double> _widths = [];
    private string _selected = string.Empty;

    /// <summary>Total control width (188 by default, 148 for the small variant).</summary>
    public double ControlWidth { get; set; } = SettingsMetrics.SegmentedWidth;

    /// <summary>Container fill (<c>--ui-raised</c>).</summary>
    public IBrush Fill { get; set; } = Brushes.Transparent;

    /// <summary>Container hairline (<c>--ui-line</c>).</summary>
    public IBrush Hairline { get; set; } = Brushes.Transparent;

    /// <summary>Active segment fill (<c>--ui-active</c>).</summary>
    public IBrush ActiveFill { get; set; } = Brushes.White;

    /// <summary>Active segment text (<c>--ui-active-fg</c>).</summary>
    public IBrush ActiveForeground { get; set; } = Brushes.Black;

    /// <summary>Inactive segment text (<c>--ui-fg-2</c>).</summary>
    public IBrush InactiveForeground { get; set; } = Brushes.Gray;

    /// <summary>Raised when the user picks another segment.</summary>
    public event Action<string>? Changed;

    /// <summary>Creates the control.</summary>
    public SegmentedControl()
    {
        Height = 32;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>Current value.</summary>
    public string SelectedValue => _selected;

    /// <summary>Replaces the items and the selection.</summary>
    public void SetItems(IEnumerable<(string Label, string Value)> items, string selected)
    {
        _items.Clear();
        _items.AddRange(items);
        _widths.Clear();
        _selected = selected;
        var total = 6.0;
        foreach (var item in _items)
        {
            var text = new FormattedText(item.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Fonts.SemiBold, 9.5, Brushes.White);
            var width = Math.Max(28, text.Width + 14);
            _widths.Add(width);
            total += width;
        }

        total += 2 * Math.Max(0, _items.Count - 1);
        Width = Math.Clamp(total, ControlWidth, 360);
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(Fill, new Pen(Hairline, 1), bounds, 8, 8);
        if (_items.Count == 0)
        {
            return;
        }

        var inner = new Rect(3, 3, Math.Max(1, bounds.Width - 6), Math.Max(1, bounds.Height - 6));
        var x = inner.X;
        for (var i = 0; i < _items.Count; i++)
        {
            var segWidth = i < _widths.Count ? _widths[i] : 28;
            var rect = new Rect(x, inner.Y, segWidth, inner.Height);
            x += segWidth + 2;
            var active = string.Equals(_items[i].Value, _selected, StringComparison.OrdinalIgnoreCase);
            if (active)
            {
                context.DrawRectangle(ActiveFill, null, rect, 6, 6);
            }

            var text = new FormattedText(_items[i].Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                active ? Fonts.SemiBold : Fonts.Regular, 9.5, active ? ActiveForeground : InactiveForeground);
            context.DrawText(text, new Point(rect.X + ((rect.Width - text.Width) / 2), rect.Y + ((rect.Height - text.Height) / 2)));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_items.Count == 0)
        {
            return;
        }

        var position = e.GetPosition(this).X;
        var x = 3.0;
        var index = _items.Count - 1;
        for (var i = 0; i < _items.Count; i++)
        {
            var segWidth = i < _widths.Count ? _widths[i] : 28;
            if (position >= x && position <= x + segWidth)
            {
                index = i;
                break;
            }

            x += segWidth + 2;
        }
        if (string.Equals(_items[index].Value, _selected, StringComparison.Ordinal))
        {
            return;
        }

        _selected = _items[index].Value;
        InvalidateVisual();
        Changed?.Invoke(_selected);
        e.Handled = true;
    }
}

/// <summary>iOS-style switch matching the prototype's <c>.switch</c> (38×22).</summary>
public sealed class SettingsSwitch : Control
{
    private bool _isOn;

    /// <summary>On/off state.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }

            _isOn = value;
            InvalidateVisual();
        }
    }

    /// <summary>Accent used when on (<c>--accent</c>).</summary>
    public IBrush Accent { get; set; } = Brushes.DodgerBlue;

    /// <summary>Off track (<c>--ui-raised-hover</c>).</summary>
    public IBrush OffTrack { get; set; } = Brushes.Gray;

    /// <summary>Off hairline (<c>--ui-line-strong</c>).</summary>
    public IBrush OffHairline { get; set; } = Brushes.Gray;

    /// <summary>Off knob (<c>--ui-fg-2</c>).</summary>
    public IBrush OffKnob { get; set; } = Brushes.LightGray;

    /// <summary>Raised when the user toggles.</summary>
    public event Action<bool>? Toggled;

    /// <summary>Creates the control.</summary>
    public SettingsSwitch()
    {
        Width = 38;
        Height = 22;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var track = new Rect(0, 0, 38, 22);
        context.DrawRectangle(_isOn ? Accent : OffTrack, _isOn ? null : new Pen(OffHairline, 1), track, 11, 11);
        context.DrawEllipse(_isOn ? Brushes.White : OffKnob, null, new Point(_isOn ? 28 : 10, 11), 8, 8);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isOn = !_isOn;
        InvalidateVisual();
        Toggled?.Invoke(_isOn);
        e.Handled = true;
    }
}

/// <summary>Thin slider matching the prototype's <c>.ui-range</c> (3 px track, 10 px thumb).</summary>
public sealed class SettingsSlider : Control
{
    private double _value;
    private bool _dragging;

    /// <summary>Lower bound.</summary>
    public double Minimum { get; set; }

    /// <summary>Upper bound.</summary>
    public double Maximum { get; set; } = 100;

    /// <summary>Snap step (0 = continuous).</summary>
    public double Step { get; set; } = 1;

    /// <summary>Current value.</summary>
    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(clamped - _value) < 1e-9)
            {
                return;
            }

            _value = clamped;
            InvalidateVisual();
        }
    }

    /// <summary>Filled portion (<c>--ui-fg</c>).</summary>
    public IBrush Fill { get; set; } = Brushes.White;

    /// <summary>Remaining track (<c>--ui-raised-hover</c>).</summary>
    public IBrush Track { get; set; } = Brushes.Gray;

    /// <summary>Raised while dragging.</summary>
    public event Action<double>? ValueChanged;

    /// <summary>Creates the control.</summary>
    public SettingsSlider()
    {
        Width = SettingsMetrics.SliderWidth;
        Height = 13;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Math.Max(1, Bounds.Width);
        var centerY = Bounds.Height / 2;
        var fraction = Maximum <= Minimum ? 0 : Math.Clamp((_value - Minimum) / (Maximum - Minimum), 0, 1);
        var thumbX = fraction * width;
        context.DrawRectangle(Track, null, new Rect(0, centerY - 1.5, width, 3), 1.5, 1.5);
        if (thumbX > 0)
        {
            context.DrawRectangle(Fill, null, new Rect(0, centerY - 1.5, thumbX, 3), 1.5, 1.5);
        }

        context.DrawEllipse(Fill, null, new Point(thumbX, centerY), 5, 5);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        UpdateFromPosition(e.GetPosition(this).X);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            UpdateFromPosition(e.GetPosition(this).X);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void UpdateFromPosition(double x)
    {
        var fraction = Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1);
        var raw = Minimum + (fraction * (Maximum - Minimum));
        var snapped = Step > 0 ? Math.Round(raw / Step) * Step : raw;
        snapped = Math.Clamp(snapped, Minimum, Maximum);
        if (Math.Abs(snapped - _value) < 1e-9)
        {
            return;
        }

        _value = snapped;
        InvalidateVisual();
        ValueChanged?.Invoke(_value);
    }
}

/// <summary>Circular accent swatch with the prototype's active ring.</summary>
public sealed class AccentSwatch : Control
{
    /// <summary>Swatch color.</summary>
    public Color SwatchColor { get; set; }

    /// <summary>Whether the ring is drawn.</summary>
    public bool IsActive { get; set; }

    /// <summary>Ring brush (<c>--ui-fg-2</c>).</summary>
    public IBrush Ring { get; set; } = Brushes.Gray;

    /// <summary>Raised on click.</summary>
    public event Action? Clicked;

    /// <summary>Creates the swatch.</summary>
    public AccentSwatch()
    {
        Width = 32;
        Height = 32;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var center = new Point(16, 16);
        if (IsActive)
        {
            context.DrawEllipse(null, new Pen(Ring, 1), center, 15.5, 15.5);
        }

        context.DrawEllipse(new SolidColorBrush(SwatchColor), new Pen(new SolidColorBrush(Color.FromArgb(41, 255, 255, 255)), 1), center, 12, 12);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Clicked?.Invoke();
        e.Handled = true;
    }
}

/// <summary>Prototype-faithful text/row widgets shared by the settings sections.</summary>
internal static class SettingsWidgets
{
    /// <summary>22 px pane title + 11 px description (prototype <c>.pane-head-lg</c>/<c>.pane-desc</c>).</summary>
    public static Control PaneHeader(string title, string description, IBrush foreground, IBrush secondary)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Fonts.Display,
            FontSize = SettingsMetrics.HeadSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = SettingsMetrics.DescSize,
                Foreground = secondary,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 5, 0, 18),
            });
        }

        return panel;
    }

    /// <summary>8.5 px uppercase section caption (prototype <c>.section-caption</c>).</summary>
    public static Control Caption(string text, IBrush tertiary)
        => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = SettingsMetrics.CaptionSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = tertiary,
            Margin = new Thickness(0, 20, 0, 2),
        };

    /// <summary>Group of rows; the first row gets the prototype's group hairline when requested.</summary>
    public static Control Group(IBrush hairline, bool topBorder, params Control[] rows)
    {
        var panel = new StackPanel();
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return new Border
        {
            BorderBrush = hairline,
            BorderThickness = new Thickness(0, topBorder ? 1 : 0, 0, 0),
            Child = panel,
        };
    }

    /// <summary>One setting row: 12 px title, 10 px description, right-aligned control, hairline below.</summary>
    public static Control Row(string title, string? description, Control control, IBrush foreground,
        IBrush tertiary, IBrush hairline)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            MinHeight = SettingsMetrics.RowMinHeight,
        };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
            Margin = new Thickness(0, 8, SettingsMetrics.RowGap, 8),
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = SettingsMetrics.RowTitleSize,
            FontWeight = FontWeight.Medium,
            Foreground = foreground,
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = tertiary,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
            });
        }

        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border
        {
            BorderBrush = hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    /// <summary>Shortcut row with right-aligned keycaps (prototype <c>.shortcut-row</c>).</summary>
    public static Control ShortcutRow(string label, string[] keys, IBrush foreground, IBrush secondary,
        IBrush hairline, IBrush raised, IBrush lineStrong)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 39 };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var keycaps = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (var key in keys)
        {
            keycaps.Children.Add(Keycap(key, secondary, raised, lineStrong));
        }

        Grid.SetColumn(keycaps, 1);
        grid.Children.Add(keycaps);
        return new Border
        {
            BorderBrush = hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    /// <summary>Shortcut row with a custom right-hand control (editable hotkeys).</summary>
    public static Control ShortcutRow(string label, Control right, IBrush foreground, IBrush hairline)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 39 };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeight.Medium,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });
        right.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            BorderBrush = hairline,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
        };
    }

    /// <summary>Single keycap chip.</summary>
    public static Control Keycap(string text, IBrush secondary, IBrush raised, IBrush lineStrong)
        => new Border
        {
            MinWidth = 22,
            Height = 22,
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(5),
            Background = raised,
            BorderBrush = lineStrong,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 8.5,
                Foreground = secondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    /// <summary>Muted footer note with the prototype's top hairline.</summary>
    public static Control FooterNote(string text, IBrush tertiary, IBrush hairline)
        => new Border
        {
            BorderBrush = hairline,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(0, 10, 0, 0),
            Child = new TextBlock
            {
                Text = text,
                FontSize = SettingsMetrics.CaptionSize,
                Foreground = tertiary,
                TextWrapping = TextWrapping.Wrap,
            },
        };
}
