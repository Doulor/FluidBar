using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluidBar.App.Theme;

namespace FluidBar.App.Views.Settings;

/// <summary>Two-to-four segment control matching the prototype's <c>.segmented</c>/<c>.seg</c>.</summary>
public sealed class SegmentedControl : Control
{
    private static readonly Typeface SemiBold = new(Fonts.Ui, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Regular = new(Fonts.Ui, FontStyle.Normal, FontWeight.Normal);
    private readonly List<(string Label, string Value)> _items = [];
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
        // .segmented: 1px border + 2px padding + 26px segments.
        Height = SettingsMetrics.SegHeight + (2 * SettingsMetrics.SegmentedPad) + 2;
        Cursor = new Cursor(StandardCursorType.Hand);
        Width = ControlWidth;
    }

    /// <summary>Current value.</summary>
    public string SelectedValue => _selected;

    /// <summary>Items currently offered (tests/diagnostics).</summary>
    internal IReadOnlyList<(string Label, string Value)> Items => _items;

    /// <summary>Replaces the items and the selection.</summary>
    public void SetItems(IEnumerable<(string Label, string Value)> items, string selected)
    {
        _items.Clear();
        _items.AddRange(items);
        _selected = selected;
        Width = ControlWidth;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawRectangle(Fill, new Pen(Hairline, 1), bounds, SettingsMetrics.SegRadius, SettingsMetrics.SegRadius);
        if (_items.Count == 0)
        {
            return;
        }

        // .segmented { padding: 2px; gap: 2px } → equal flex segments.
        var pad = SettingsMetrics.SegmentedPad + 1;
        var gap = SettingsMetrics.SegGap;
        var available = bounds.Width - (2 * pad) - (gap * (_items.Count - 1));
        var segWidth = available / _items.Count;
        var x = pad;
        for (var i = 0; i < _items.Count; i++)
        {
            var rect = new Rect(x, pad, segWidth, bounds.Height - (2 * pad));
            x += segWidth + gap;
            var active = string.Equals(_items[i].Value, _selected, StringComparison.OrdinalIgnoreCase);
            if (active)
            {
                context.DrawRectangle(ActiveFill, null, rect, SettingsMetrics.SegItemRadius, SettingsMetrics.SegItemRadius);
            }

            var text = new FormattedText(
                _items[i].Label,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                active ? SemiBold : Regular,
                SettingsMetrics.SegFontSize,
                active ? ActiveForeground : InactiveForeground);
            context.DrawText(text, new Point(
                rect.X + ((rect.Width - text.Width) / 2),
                rect.Y + ((rect.Height - text.Height) / 2)));
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

        var pad = SettingsMetrics.SegmentedPad + 1;
        var gap = SettingsMetrics.SegGap;
        var available = Bounds.Width - (2 * pad) - (gap * (_items.Count - 1));
        var segWidth = available / _items.Count;
        var position = e.GetPosition(this).X;
        var index = _items.Count - 1;
        for (var i = 0; i < _items.Count; i++)
        {
            var left = pad + (i * (segWidth + gap));
            if (position >= left && position <= left + segWidth)
            {
                index = i;
                break;
            }
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

    /// <summary>Creates the control.</summary>
    public SettingsSwitch()
    {
        Width = SettingsMetrics.SwitchWidth;
        Height = SettingsMetrics.SwitchHeight;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

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

    /// <summary>True for the compact plugin-row variant (<c>.plugin-actions .switch</c>, 30×18).</summary>
    public bool Compact
    {
        get => Width < SettingsMetrics.SwitchWidth;
        set
        {
            Width = value ? SettingsMetrics.PluginSwitchWidth : SettingsMetrics.SwitchWidth;
            Height = value ? SettingsMetrics.PluginSwitchHeight : SettingsMetrics.SwitchHeight;
            _knob = value ? SettingsMetrics.PluginSwitchKnob : SettingsMetrics.SwitchKnob;
            InvalidateVisual();
        }
    }

    private double _knob = SettingsMetrics.SwitchKnob;

    /// <summary>Raised when the user toggles.</summary>
    public event Action<bool>? Toggled;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var radius = height / 2;
        var track = new Rect(0, 0, width, height);
        context.DrawRectangle(
            _isOn ? Accent : OffTrack,
            _isOn ? null : new Pen(OffHairline, 1),
            track,
            radius,
            radius);

        // .switch::after { left: 2px; width: 16px } → translateX(16px) when on.
        var travel = width - _knob - (2 * SettingsMetrics.SwitchInset) - (width - _knob - 4);
        var inset = SettingsMetrics.SwitchInset;
        var centerX = _isOn
            ? width - inset - (_knob / 2)
            : inset + (_knob / 2);
        _ = travel;
        context.DrawEllipse(_isOn ? Brushes.White : OffKnob, null, new Point(centerX, height / 2), _knob / 2, _knob / 2);
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

    /// <summary>Creates the control.</summary>
    public SettingsSlider()
    {
        Height = 13;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

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

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var width = Math.Max(1, Bounds.Width);
        var centerY = Bounds.Height / 2;
        var fraction = Maximum <= Minimum ? 0 : Math.Clamp((_value - Minimum) / (Maximum - Minimum), 0, 1);
        var thumbX = SettingsMetrics.ThumbSize / 2 + (fraction * (width - SettingsMetrics.ThumbSize));
        var half = SettingsMetrics.TrackHeight / 2;
        context.DrawRectangle(Track, null, new Rect(0, centerY - half, width, SettingsMetrics.TrackHeight), 2, 2);
        if (thumbX > 0)
        {
            context.DrawRectangle(Fill, null, new Rect(0, centerY - half, thumbX, SettingsMetrics.TrackHeight), 2, 2);
        }

        context.DrawEllipse(Fill, null, new Point(thumbX, centerY), SettingsMetrics.ThumbSize / 2, SettingsMetrics.ThumbSize / 2);
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
        var usable = Math.Max(1, Bounds.Width - SettingsMetrics.ThumbSize);
        var fraction = Math.Clamp((x - (SettingsMetrics.ThumbSize / 2)) / usable, 0, 1);
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

/// <summary>
/// Circular accent swatch. It is a <see cref="ToggleButton"/> so the selection is exposed to UI Automation
/// (TogglePattern.ToggleState) and can be changed from the keyboard (Space/Enter) without a custom peer.
/// The visual state is drawn directly in <see cref="Render"/>:
/// a 2 px ring in the foreground colour plus a 2 px gap marks the selected swatch, a softer ring marks
/// hover, and a dashed ring marks keyboard focus so the three states are unmistakable in both themes.
/// </summary>
public sealed class AccentSwatch : ToggleButton
{
    /// <summary>Creates the swatch (24 px circle + 4 px ring gutter).</summary>
    public AccentSwatch()
    {
        Width = SettingsMetrics.SwatchSize + (2 * SettingsMetrics.SwatchRingInset);
        Height = SettingsMetrics.SwatchSize + (2 * SettingsMetrics.SwatchRingInset);
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Swatch color.</summary>
    public Color SwatchColor { get; set; }

    /// <summary>Whether the swatch is the active accent (mirrors <see cref="ToggleButton.IsChecked"/>).</summary>
    public bool IsActive
    {
        get => IsChecked == true;
        set => IsChecked = value;
    }

    /// <summary>Ring brush used for the selected state (<c>--ui-fg</c>).</summary>
    public IBrush Ring { get; set; } = Brushes.White;

    /// <summary>Brush used for the hover ring (<c>--ui-fg-2</c>).</summary>
    public IBrush HoverRing { get; set; } = Brushes.Gray;

    /// <summary>Raised when the user activates the swatch.</summary>
    public event Action? Clicked;

    /// <summary>Activates the swatch (same path as a click or Space/Enter).</summary>
    public void Activate() => Clicked?.Invoke();

    /// <inheritdoc />
    protected override void OnClick()
    {
        base.OnClick();
        Activate();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        var radius = SettingsMetrics.SwatchSize / 2;
        var selected = IsActive;
        var hover = IsPointerOver;
        var focused = IsFocused || IsKeyboardFocusWithin;

        // Selected: a 2 px foreground ring with a 2 px gap from the colour disc (the prototype's
        // inset: -4px ring, thickened so it reads at a glance at 150 % DPI).
        if (selected)
        {
            var ringRadius = radius + SettingsMetrics.SwatchRingInset;
            context.DrawEllipse(null, new Pen(Ring, 2), center, ringRadius, ringRadius);
        }
        else if (hover)
        {
            var ringRadius = radius + (SettingsMetrics.SwatchRingInset / 2);
            context.DrawEllipse(null, new Pen(HoverRing, 1), center, ringRadius, ringRadius);
        }

        // Keyboard focus: dashed outer ring, independent of the selected state.
        if (focused)
        {
            var focusRadius = radius + SettingsMetrics.SwatchRingInset + 2;
            var pen = new Pen(Ring, 1, new DashStyle([2, 2], 0));
            context.DrawEllipse(null, pen, center, focusRadius, focusRadius);
        }

        context.DrawEllipse(
            new SolidColorBrush(SwatchColor),
            new Pen(new SolidColorBrush(Color.FromArgb(selected ? (byte)140 : (byte)41, 255, 255, 255)),
                selected ? 1.5 : 1),
            center,
            radius,
            radius);

        // Selected also carries a check mark: colour alone is not enough for colour-vision deficiencies.
        if (selected)
        {
            var pen = new Pen(Brushes.Black, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawLine(pen, new Point(center.X - 4.5, center.Y + 0.5), new Point(center.X - 1.2, center.Y + 3.8));
            context.DrawLine(pen, new Point(center.X - 1.2, center.Y + 3.8), new Point(center.X + 4.8, center.Y - 3.6));
        }
    }
}

/// <summary>Prototype-faithful text/row widgets shared by the settings sections.</summary>
internal static class SettingsWidgets
{
    /// <summary><c>.pane-head-lg</c> + <c>.pane-desc</c>.</summary>
    public static Control PaneHeader(string title, string? description, SettingsPalette palette)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Fonts.Display,
            FontSize = SettingsMetrics.HeadSize,
            FontWeight = FontWeight.SemiBold,
            LineHeight = SettingsMetrics.HeadSize * SettingsMetrics.HeadLineHeight,
            LetterSpacing = SettingsMetrics.HeadSize * SettingsMetrics.HeadTracking,
            Foreground = palette.ForegroundBrush,
            Margin = new Thickness(0, 0, 0, SettingsMetrics.HeadMarginBottom),
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = SettingsMetrics.DescSize,
                LineHeight = SettingsMetrics.DescSize * SettingsMetrics.DescLineHeight,
                Foreground = palette.SecondaryBrush,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = SettingsMetrics.DescMaxWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 0, 0, SettingsMetrics.DescMarginBottom),
            });
        }

        return panel;
    }

    /// <summary><c>.section-caption</c>.</summary>
    public static Control Caption(string text, SettingsPalette palette)
        => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = SettingsMetrics.CaptionSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = SettingsMetrics.CaptionSize * SettingsMetrics.CaptionTracking,
            Foreground = palette.TertiaryBrush,
            Margin = new Thickness(0, SettingsMetrics.CaptionMarginTop, 0, SettingsMetrics.CaptionMarginBottom),
        };

    /// <summary><c>.setting-group</c>: hairline above the first row unless a caption precedes it.</summary>
    public static Control Group(SettingsPalette palette, bool topBorder, params Control[] rows)
    {
        var panel = new StackPanel();
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return new Border
        {
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, topBorder ? 1 : 0, 0, 0),
            Child = panel,
        };
    }

    /// <summary><c>.setting-row</c>: 53 px minimum, 8 px vertical padding, 18 px gap, hairline below.</summary>
    public static Control Row(string title, string? description, Control control, SettingsPalette palette)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 0,
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = SettingsMetrics.RowTitleSize,
            FontWeight = FontWeight.Medium,
            LineHeight = SettingsMetrics.RowTitleSize * SettingsMetrics.RowTitleLineHeight,
            Foreground = palette.ForegroundBrush,
        });
        if (!string.IsNullOrWhiteSpace(description))
        {
            text.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = SettingsMetrics.RowDescSize,
                LineHeight = SettingsMetrics.RowDescSize * SettingsMetrics.RowDescLineHeight,
                Foreground = palette.TertiaryBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, SettingsMetrics.RowDescMarginTop, 0, 0),
            });
        }

        Grid.SetColumn(text, 0);
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.Margin = new Thickness(SettingsMetrics.RowGap, 0, 0, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return new Border
        {
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = SettingsMetrics.RowMinHeight,
            Padding = new Thickness(0, SettingsMetrics.RowPadY, 0, SettingsMetrics.RowPadY),
            Child = grid,
        };
    }

    /// <summary><c>.setting-control</c>: horizontal group with the prototype's 8 px gap.</summary>
    public static Control ControlHost(params Control[] controls)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.ControlGap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    /// <summary><c>.shortcut-row</c>: 39 px tall, right-aligned keycaps.</summary>
    public static Control ShortcutRow(string label, IReadOnlyList<string> keys, SettingsPalette palette, bool last = false)
        => ShortcutRow(label, Keycaps(keys, palette), palette, last);

    /// <summary><c>.shortcut-row</c> with a custom right-hand control.</summary>
    public static Control ShortcutRow(string label, Control right, SettingsPalette palette, bool last = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = SettingsMetrics.ShortcutRowHeight };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = SettingsMetrics.ShortcutFontSize,
            FontWeight = FontWeight.Medium,
            Foreground = palette.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        right.VerticalAlignment = VerticalAlignment.Center;
        right.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border
        {
            // .shortcut-row:last-child { border-bottom: 0 }
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
            Child = grid,
        };
    }

    /// <summary><c>.keycaps</c>: 3 px gap container.</summary>
    public static Control Keycaps(IReadOnlyList<string> keys, SettingsPalette palette)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.KeycapGap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var key in keys)
        {
            panel.Children.Add(SettingsControls.Chip(key, palette, SettingsChipKind.Keycap));
        }

        return panel;
    }

    /// <summary><c>.about-mark</c>: 72 px rounded tile with an accent bloom behind the island glyph.</summary>
    public static Control AboutMark(Color accent, SettingsPalette palette)
    {
        var host = new Grid { Width = SettingsMetrics.AboutMark, Height = SettingsMetrics.AboutMark };
        var tile = new Border
        {
            Width = SettingsMetrics.AboutMark,
            Height = SettingsMetrics.AboutMark,
            CornerRadius = new CornerRadius(SettingsMetrics.AboutMarkRadius),
            Background = new SolidColorBrush(palette.Mark),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        var bloom = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(accent, 0.4),
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, -10, -10),
        };
        tile.Child = bloom;
        host.Children.Add(tile);
        host.Children.Add(new SettingsIcon
        {
            Kind = SettingsIconKind.Island,
            Brush = Brushes.White,
            Width = SettingsMetrics.AboutMarkGlyph,
            Height = SettingsMetrics.AboutMarkGlyph,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return host;
    }

    /// <summary>Uppercase kicker text (<c>.nav-kicker</c>, <c>.ap-head</c>, <c>.featured-kicker</c>).</summary>
    public static TextBlock Kicker(string text, double size, double tracking, IBrush brush, FontWeight weight = FontWeight.SemiBold)
        => new()
        {
            Text = text.ToUpperInvariant(),
            FontSize = size,
            FontWeight = weight,
            LetterSpacing = size * tracking,
            Foreground = brush,
        };
}
