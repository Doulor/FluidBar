using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Plugins;
using FluidBar.Core.Layout;

namespace FluidBar.App.Views.Settings;

/// <summary>Dependencies the settings sections need (injected so headless tests can swap fakes).</summary>
public sealed record SettingsWindowContext
{
    /// <summary>Settings controller.</summary>
    public required SettingsController Settings { get; init; }

    /// <summary>Localizer.</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>Marketplace service.</summary>
    public required IMarketplaceUiService Marketplace { get; init; }

    /// <summary>Plugin list service.</summary>
    public required IPluginUiService Plugins { get; init; }

    /// <summary>Current settings-window surfaces.</summary>
    public required AppSurfaceColors Surfaces { get; init; }

    /// <summary>Owner window accessor (consent dialog parenting).</summary>
    public required Func<Window?> Owner { get; init; }

    /// <summary>Opens a path/folder with the shell (no-op in tests).</summary>
    public required Action<string?> OpenPath { get; init; }

    /// <summary>Shows a transient status line in the section (exported path, errors).</summary>
    public required Action<string> ShowStatus { get; init; }
}

/// <summary>Builds the seven settings sections from the strongly typed settings model.</summary>
public static class SettingsSectionBuilder
{
    /// <summary>Context of the section currently being built (single-threaded UI composition).</summary>
    [ThreadStatic]
    private static SettingsWindowContext? _ambientContext;

    private static SettingsWindowContext Ambient
        => _ambientContext ?? throw new InvalidOperationException("Settings sections must be built through Build().");

    /// <summary>Builds the section content.</summary>
    public static Control Build(SettingsWindowSection section, SettingsWindowContext context)
    {
        _ambientContext = context;
        try
        {
            return section switch
            {
                SettingsWindowSection.Appearance => BuildAppearance(context),
                SettingsWindowSection.Island => BuildIsland(context),
                SettingsWindowSection.Plugins => BuildPlugins(context),
                SettingsWindowSection.Marketplace => BuildMarketplace(context),
                SettingsWindowSection.Advanced => BuildAdvanced(context),
                SettingsWindowSection.About => BuildAbout(context),
                _ => BuildGeneral(context),
            };
        }
        finally
        {
            _ambientContext = null;
        }
    }

    private static StackPanel Section(string title, string? subtitle, params Control[] rows)
        => Section(Ambient, title, subtitle, rows);

    private static Control Row(string? label, string? description, Control control)
        => Row(Ambient, label, description, control);

    private static Control ToggleRow(string label, string? description, bool value, Action<bool> changed)
        => ToggleRow(Ambient, label, description, value, changed);

    private static Control ComboRow(string label, string? description, (string Label, string Value)[] options,
        string current, Action<string> changed)
        => SegmentedRow(Ambient, label, description, options, current, changed);

    private static Control SliderRow(string label, string? description, double min, double max, double step,
        double value, Func<double, string> format, Action<double> changed)
        => SliderRow(Ambient, label, description, min, max, step, value, format, changed);

    private static Control ButtonRow(string label, string? description, string buttonText, Action clicked)
        => ButtonRow(Ambient, label, description, buttonText, clicked);

    /// <summary>Prototype palette resolved from the current surfaces + accent.</summary>
    private readonly record struct SettingsBrushes(
        IBrush Foreground,
        IBrush Secondary,
        IBrush Tertiary,
        IBrush Line,
        IBrush LineStrong,
        IBrush Raised,
        IBrush RaisedHover,
        IBrush Active,
        IBrush ActiveForeground,
        IBrush Accent)
    {
        public static SettingsBrushes From(SettingsWindowContext context)
        {
            var surfaces = context.Surfaces;
            var accent = Accents.ParseOrDefault(context.Settings.Current.AccentFallback, Accents.Fallback);
            return new SettingsBrushes(
                new SolidColorBrush(surfaces.Foreground),
                new SolidColorBrush(surfaces.ForegroundSecondary),
                new SolidColorBrush(surfaces.ForegroundTertiary),
                new SolidColorBrush(surfaces.Line),
                new SolidColorBrush(surfaces.LineStrong),
                new SolidColorBrush(surfaces.Raised),
                new SolidColorBrush(surfaces.RaisedHover),
                new SolidColorBrush(surfaces.Active),
                new SolidColorBrush(surfaces.ActiveForeground),
                new SolidColorBrush(accent));
        }
    }

    private static SettingsBrushes Palette(SettingsWindowContext context) => SettingsBrushes.From(context);

    private static StackPanel Section(SettingsWindowContext context, string title, string? subtitle, params Control[] rows)
    {
        var brushes = Palette(context);
        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(SettingsWidgets.PaneHeader(title, subtitle ?? string.Empty, brushes.Foreground, brushes.Secondary));
        foreach (var row in rows)
        {
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Row(SettingsWindowContext context, string? label, string? description, Control control)
    {
        var brushes = Palette(context);
        return SettingsWidgets.Row(label ?? string.Empty, description, control, brushes.Foreground, brushes.Tertiary, brushes.Line);
    }

    private static Control ToggleRow(SettingsWindowContext context, string label, string? description, bool value, Action<bool> changed)
    {
        var brushes = Palette(context);
        var toggle = new SettingsSwitch
        {
            IsOn = value,
            Accent = brushes.Accent,
            OffTrack = brushes.RaisedHover,
            OffHairline = brushes.LineStrong,
            OffKnob = brushes.Secondary,
        };
        toggle.Toggled += changed;
        return SettingsWidgets.Row(label, description, toggle, brushes.Foreground, brushes.Tertiary, brushes.Line);
    }

    private static Control SegmentedRow(SettingsWindowContext context, string label, string? description,
        (string Label, string Value)[] options, string current, Action<string> changed, bool small = false)
    {
        var brushes = Palette(context);
        var segmented = new SegmentedControl
        {
            ControlWidth = small ? SettingsMetrics.SegmentedSmallWidth : SettingsMetrics.SegmentedWidth,
            Fill = brushes.Raised,
            Hairline = brushes.Line,
            ActiveFill = brushes.Active,
            ActiveForeground = brushes.ActiveForeground,
            InactiveForeground = brushes.Secondary,
        };
        segmented.SetItems(options, current);
        segmented.Changed += changed;
        return SettingsWidgets.Row(label, description, segmented, brushes.Foreground, brushes.Tertiary, brushes.Line);
    }

    private static Control SliderRow(SettingsWindowContext context, string label, string? description,
        double min, double max, double step, double value, Func<double, string> format, Action<double> changed)
    {
        var brushes = Palette(context);
        var slider = new SettingsSlider
        {
            Minimum = min,
            Maximum = max,
            Step = step,
            Value = Math.Clamp(value, min, max),
            Width = 142,
            Fill = brushes.Foreground,
            Track = brushes.RaisedHover,
        };
        var tagText = new TextBlock
        {
            Text = format(slider.Value),
            FontSize = SettingsMetrics.CaptionSize,
            Foreground = brushes.Secondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tag = new Border
        {
            MinWidth = 48,
            Height = 24,
            Padding = new Thickness(7, 0, 7, 0),
            CornerRadius = new CornerRadius(6),
            Background = brushes.Raised,
            BorderBrush = brushes.Line,
            BorderThickness = new Thickness(1),
            Child = tagText,
        };
        slider.ValueChanged += value2 =>
        {
            tagText.Text = format(value2);
            changed(value2);
        };
        var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(slider);
        host.Children.Add(tag);
        return SettingsWidgets.Row(label, description, host, brushes.Foreground, brushes.Tertiary, brushes.Line);
    }

    private static Control ButtonRow(SettingsWindowContext context, string label, string? description, string buttonText, Action clicked)
    {
        var button = new Button { Content = buttonText, Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(8) };
        button.Click += (_, _) => clicked();
        return Row(context, label, description, button);
    }

    private static string[] SplitHotkey(string value)
        => value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Control BuildGeneral(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var brushes = Palette(ctx);
        var languageOptions = new (string, string)[]
        {
            (ctx.Localizer.Get(LocKeys.Locale.System), "system"),
            (ctx.Localizer.Get(LocKeys.Locale.Chinese), "zh-Hans"),
            (ctx.Localizer.Get(LocKeys.Locale.English), "en-US"),
        };

        var telemetry = new SettingsSwitch
        {
            IsOn = false,
            IsEnabled = false,
            Accent = brushes.Accent,
            OffTrack = brushes.RaisedHover,
            OffHairline = brushes.LineStrong,
            OffKnob = brushes.Secondary,
        };

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Title),
            ctx.Localizer.Get(LocKeys.Settings.General.LanguageHint),
            SettingsWidgets.Group(brushes.Line, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Language),
                    ctx.Localizer.Get(LocKeys.Settings.General.LanguageHint), languageOptions, settings.Language,
                    value => ctx.Settings.Update(s => s with { Language = value })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindows),
                    ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindowsHint), settings.StartWithWindows,
                    value => ctx.Settings.Update(s => s with { StartWithWindows = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdateLabel), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Notify), "notify"),
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Auto), "auto"),
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Off), "off"),
                    ],
                    settings.AutoUpdate,
                    value => ctx.Settings.Update(s => s with { AutoUpdate = value }), small: true),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.TrayIcon),
                    ctx.Localizer.Get(LocKeys.Settings.General.TrayIconHint), settings.TrayIcon,
                    value => ctx.Settings.Update(s => s with { TrayIcon = value })),
                SettingsWidgets.Row(ctx.Localizer.Get(LocKeys.Settings.General.Telemetry),
                    ctx.Localizer.Get(LocKeys.Settings.General.TelemetryHint), telemetry,
                    brushes.Foreground, brushes.Tertiary, brushes.Line)));

        panel.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.General.Hotkeys), brushes.Tertiary));
        panel.Children.Add(SettingsWidgets.Group(brushes.Line, topBorder: false,
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Toggle), settings.HotkeyToggle,
                value => ctx.Settings.Update(s => s with { HotkeyToggle = value })),
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Hide), settings.HotkeyHide,
                value => ctx.Settings.Update(s => s with { HotkeyHide = value })),
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Settings), settings.HotkeySettings,
                value => ctx.Settings.Update(s => s with { HotkeySettings = value })),
            StaticKeycapRow(ctx, "LeftAlt", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.HoldToHide)),
            StaticKeycapRow(ctx, "Esc", ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Toggle))));
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), brushes.Tertiary, brushes.Line));
        return panel;
    }

    private static Control StaticKeycapRow(SettingsWindowContext ctx, string keys, string label)
    {
        var brushes = Palette(ctx);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var key in SplitHotkey(keys))
        {
            caps.Children.Add(SettingsWidgets.Keycap(key, brushes.Secondary, brushes.Raised, brushes.LineStrong));
        }

        return SettingsWidgets.ShortcutRow(label, caps, brushes.Foreground, brushes.Line);
    }

    private static Control HotkeyRow(SettingsWindowContext ctx, string label, string value, Action<string> commit)
    {
        var brushes = Palette(ctx);
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var key in SplitHotkey(value))
        {
            caps.Children.Add(SettingsWidgets.Keycap(key, brushes.Secondary, brushes.Raised, brushes.LineStrong));
        }

        var box = new TextBox
        {
            Text = value,
            Width = 150,
            FontFamily = Fonts.Mono,
            FontSize = 11,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var host = new Grid();
        host.Children.Add(caps);
        host.Children.Add(box);

        void EndEdit()
        {
            var text = (box.Text ?? string.Empty).Trim();
            box.IsVisible = false;
            caps.IsVisible = true;
            if (!string.IsNullOrEmpty(text) && !string.Equals(text, value, StringComparison.Ordinal))
            {
                commit(text);
            }
        }

        caps.PointerPressed += (_, e) =>
        {
            caps.IsVisible = false;
            box.IsVisible = true;
            box.Focus();
            box.SelectAll();
            e.Handled = true;
        };
        box.LostFocus += (_, _) => EndEdit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                EndEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = value;
                EndEdit();
                e.Handled = true;
            }
        };
        return SettingsWidgets.ShortcutRow(label, host, brushes.Foreground, brushes.Line);
    }

    /// <summary>Accent swatch colors offered by the appearance section (prototype ACCENTS).</summary>
    private static readonly (string Name, string Hex)[] AccentChoices =
    [
        ("#4CC9F0", "#4CC9F0"),
        ("#34C759", "#34C759"),
        ("#FF9500", "#FF9500"),
        ("#F06AB0", "#F06AB0"),
        ("#0A84FF", "#0A84FF"),
    ];

    private static Control BuildAppearance(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var brushes = Palette(ctx);
        var preview = new IslandPreview();
        preview.Update(settings, ctx.Localizer);

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (_, hex) in AccentChoices)
        {
            var color = Color.Parse(hex);
            var swatch = new AccentSwatch
            {
                SwatchColor = color,
                Ring = brushes.Secondary,
                IsActive = settings.AccentMode == "custom"
                    && string.Equals(settings.AccentFallback, hex, StringComparison.OrdinalIgnoreCase),
            };
            swatch.Clicked += () => ctx.Settings.Update(s => s with { AccentFallback = hex, AccentMode = "custom" });
            swatches.Children.Add(swatch);
        }

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Title),
            ctx.Localizer.Get(LocKeys.Settings.Appearance.PreviewHint),
            SettingsWidgets.Group(brushes.Line, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.System), "system"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Dark), "dark"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Light), "light"),
                    ],
                    settings.Theme switch { AppTheme.Dark => "dark", AppTheme.Light => "light", _ => "system" },
                    value => ctx.Settings.Update(s => s with
                    {
                        Theme = value switch { "dark" => AppTheme.Dark, "light" => AppTheme.Light, _ => AppTheme.System },
                    })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.AcrylicUnavailable),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Obsidian), "obsidian"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Acrylic), "acrylic"),
                    ],
                    settings.Material == IslandMaterial.Acrylic ? "acrylic" : "obsidian",
                    value => ctx.Settings.Update(s => s with
                    {
                        Material = value == "acrylic" ? IslandMaterial.Acrylic : IslandMaterial.Obsidian,
                    })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.PaletteLabel), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Obsidian), "obsidian"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Graphite), "graphite"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Slate), "slate"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Nord), "nord"),
                    ],
                    settings.Palette,
                    value => ctx.Settings.Update(s => s with { Palette = value })),
                SettingsWidgets.Row(ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Title), null, swatches,
                    brushes.Foreground, brushes.Tertiary, brushes.Line),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.PanelRadius),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.PanelRadiusHint), 16, 36, 1, settings.PanelRadius,
                    value => $"{value:0} px", value => ctx.Settings.Update(s => s with { PanelRadius = value })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Artwork), null,
                    settings.AccentMode == "artwork",
                    value => ctx.Settings.Update(s => s with { AccentMode = value ? "artwork" : "custom" })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.FillOpacity), null,
                    LayoutTokens.MinFillOpacity, 1.0, 0.01, settings.FillOpacity,
                    value => $"{value * 100:0}%", value => ctx.Settings.Update(s => s with { FillOpacity = value })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.FontScale), null,
                    0.8, 1.6, 0.05, settings.FontScale,
                    value => $"{value:0.00}×", value => ctx.Settings.Update(s => s with { FontScale = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.ReducedMotion.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.ReducedMotion.System), "system"),
                        (ctx.Localizer.Get(LocKeys.Common.On), "on"),
                        (ctx.Localizer.Get(LocKeys.Common.Off), "off"),
                    ],
                    settings.ReducedMotion switch
                    {
                        ReducedMotionPreference.On => "on",
                        ReducedMotionPreference.Off => "off",
                        _ => "system",
                    },
                    value => ctx.Settings.Update(s => s with
                    {
                        ReducedMotion = value switch
                        {
                            "on" => ReducedMotionPreference.On,
                            "off" => ReducedMotionPreference.Off,
                            _ => ReducedMotionPreference.System,
                        },
                    })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.MotionStrength), null,
                    0.5, 1.5, 0.05, settings.MotionStrength,
                    value => $"{value:0.00}×", value => ctx.Settings.Update(s => s with { MotionStrength = value }))));

        panel.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Get(LocKeys.Settings.Appearance.PreviewLabel),
            FontSize = SettingsMetrics.CaptionSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = brushes.Tertiary,
            Margin = new Thickness(0, 20, 0, 8),
        });
        panel.Children.Add(new Border
        {
            Height = 104,
            CornerRadius = new CornerRadius(12),
            BorderBrush = brushes.Line,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = preview,
        });
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.Appearance.LiveHint), brushes.Tertiary, brushes.Line));
        return panel;
    }

    private static Control BuildIsland(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var brushes = Palette(ctx);
        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Title),
            ctx.Localizer.Get(LocKeys.Settings.Island.Description),
            SettingsWidgets.Group(brushes.Line, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Float), "float"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Flush), "flush"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Dock), "dock"),
                    ],
                    settings.Mode,
                    value => ctx.Settings.Update(s => s with { Mode = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopLeft), "top-left"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopCenter), "top-center"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.TopRight), "top-right"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomLeft), "bottom-left"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomCenter), "bottom-center"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.BottomRight), "bottom-right"),
                    ],
                    settings.Anchor,
                    value => ctx.Settings.Update(s => s with { Anchor = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Stacked), "stacked"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Latest), "latest"),
                    ],
                    settings.Stacking == StackingStrategySetting.Latest ? "latest" : "stacked",
                    value => ctx.Settings.Update(s => s with
                    {
                        Stacking = value == "latest" ? StackingStrategySetting.Latest : StackingStrategySetting.Stacked,
                    })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.HoverPeek),
                    ctx.Localizer.Get(LocKeys.Settings.Island.HoverPeekHint), settings.HoverPeek,
                    value => ctx.Settings.Update(s => s with { HoverPeek = value })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMs),
                    ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMsHint), 0, 8000, 100, settings.AutoCollapseMs,
                    value => $"{value:0} ms", value => ctx.Settings.Update(s => s with { AutoCollapseMs = (int)Math.Round(value) })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMs),
                    ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMsHint), 0, 30000, 500, settings.AutoHideMs,
                    value => $"{value:0} ms", value => ctx.Settings.Update(s => s with { AutoHideMs = (int)Math.Round(value) })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.PeekOnTrackChange), null, settings.PeekOnTrackChange,
                    value => ctx.Settings.Update(s => s with { PeekOnTrackChange = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Title), null,
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Primary), "primary"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.All), "all"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.FollowMouse), "follow-mouse"),
                    ],
                    settings.DisplayOn switch
                    {
                        DisplayTarget.All => "all",
                        DisplayTarget.FollowMouse => "follow-mouse",
                        _ => "primary",
                    },
                    value => ctx.Settings.Update(s => s with
                    {
                        DisplayOn = value switch
                        {
                            "all" => DisplayTarget.All,
                            "follow-mouse" => DisplayTarget.FollowMouse,
                            _ => DisplayTarget.Primary,
                        },
                    }))));
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), brushes.Tertiary, brushes.Line));
        return panel;
    }

    private static Control BuildPlugins(SettingsWindowContext ctx)
    {
        var list = new StackPanel { Spacing = 6 };
        var search = new TextBox { Watermark = ctx.Localizer.Get(LocKeys.Settings.Plugins.Search), Width = 260 };
        var contributions = ctx.Plugins as FluidBar.App.Views.Plugins.IPluginContributionUiService;
        string? selectedPlugin = null;
        var panel = Section(
            ctx.Localizer.Get(LocKeys.Settings.Plugins.Title),
            null,
            search,
            list);
        search.TextChanged += (_, _) => _ = LoadAsync();
        _ = LoadAsync();
        return panel;

        void ShowDetail(string pluginId)
        {
            if (contributions is null)
            {
                return;
            }

            selectedPlugin = pluginId;
            panel.Children.Clear();
            var back = new Button { Content = ctx.Localizer.Get(LocKeys.Common.Back) };
            back.Click += (_, _) =>
            {
                selectedPlugin = null;
                panel.Children.Clear();
                panel.Children.Add(search);
                panel.Children.Add(list);
                _ = LoadAsync();
            };
            panel.Children.Add(back);
            var name = FluidBar.App.Views.Plugins.LocalizedValue.Resolve(contributions.DisplayName(pluginId), ctx.Localizer);
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = Typography.SettingsSectionSize,
                FontWeight = FontWeight.SemiBold,
            });
            foreach (var page in contributions.SettingsPages(pluginId))
            {
                panel.Children.Add(PluginSettingsPageRenderer.BuildPage(
                    page,
                    pluginId,
                    ctx.Localizer,
                    contributions.Values,
                    (id, commandId) =>
                    {
                        _ = contributions.InvokeCommandAsync(id, commandId);
                        ctx.ShowStatus(commandId);
                    },
                    ctx.OpenPath));
            }

            var commands = contributions.Commands(pluginId);
            if (commands.Count > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.Commands),
                    FontSize = Typography.SettingsBodySize,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 10, 0, 0),
                });
                foreach (var command in commands)
                {
                    var label = FluidBar.App.Views.Plugins.LocalizedValue.Resolve(command.Title, ctx.Localizer);
                    var commandButton = new Button
                    {
                        Content = string.IsNullOrEmpty(label) ? command.Id : label,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    var commandId = command.Id;
                    commandButton.Click += (_, _) =>
                    {
                        _ = contributions.InvokeCommandAsync(pluginId, commandId);
                        ctx.ShowStatus(commandId);
                    };
                    panel.Children.Add(commandButton);
                }
            }
        }

        async Task LoadAsync()
        {
            try
            {
                var plugins = await ctx.Plugins.ListAsync(CancellationToken.None);
                list.Children.Clear();
                var query = search.Text?.Trim() ?? string.Empty;
                foreach (var plugin in plugins.Where(p => query.Length == 0
                    || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Id.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Children.Add(BuildPluginRow(ctx, plugin, ShowDetail));
                }
            }
            catch (OperationCanceledException)
            {
                // Section closed while loading; nothing to attach.
            }
            catch (Exception ex)
            {
                ctx.ShowStatus(ex.Message);
            }
        }
    }

    /// <summary>Resolves a plugin display name: catalog keys are localized, plain names pass through.</summary>
    private static string ResolvePluginName(SettingsWindowContext ctx, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (!name.Contains('.', StringComparison.Ordinal))
        {
            return name;
        }

        var localized = ctx.Localizer.Get(name);
        return string.Equals(localized, name, StringComparison.Ordinal) ? name : localized;
    }

    /// <summary>Resolves host error keys (plugin runtime) and passes raw diagnostics through.</summary>
    private static string ResolvePluginError(SettingsWindowContext ctx, string error)
    {
        if (!error.StartsWith("Settings.Plugins.", StringComparison.Ordinal))
        {
            return error;
        }

        var localized = ctx.Localizer.Get(error);
        return string.Equals(localized, error, StringComparison.Ordinal) ? error : localized;
    }

    private static Control BuildPluginRow(SettingsWindowContext ctx, PluginListItem plugin, Action<string> showDetail)
    {
        var brushes = Palette(ctx);
        var enabled = plugin.IsEnabled;
        var border = new Border
        {
            BorderBrush = brushes.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 14, 0, 14),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var info = new StackPanel { Spacing = 4 };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock
        {
            Text = ResolvePluginName(ctx, plugin.Name),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = brushes.Foreground,
        });
        title.Children.Add(new TextBlock
        {
            Text = "v" + plugin.Version,
            FontSize = SettingsMetrics.CaptionSize,
            Foreground = brushes.Tertiary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(new Border
        {
            Padding = new Thickness(5, 1, 5, 1),
            CornerRadius = new CornerRadius(5),
            Background = brushes.Raised,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = ctx.Localizer.Get(plugin.Builtin ? LocKeys.Settings.Plugins.Builtin : LocKeys.Settings.Plugins.Community),
                FontSize = 8.5,
                Foreground = brushes.Secondary,
            },
        });
        info.Children.Add(title);

        info.Children.Add(new TextBlock
        {
            Text = plugin.Publisher,
            FontSize = SettingsMetrics.CaptionSize,
            Foreground = brushes.Tertiary,
        });

        if (plugin.CapabilityKeys.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", plugin.CapabilityKeys.Select(ctx.Localizer.Get)),
                FontSize = 9.5,
                Foreground = brushes.Tertiary,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
            });
        }

        if (!plugin.Available)
        {
            info.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.StateUnavailable),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Accents.Warning),
            });
        }
        else if (!enabled)
        {
            info.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.StateDisabled),
                FontSize = 9.5,
                Foreground = brushes.Tertiary,
            });
        }

        if (!string.IsNullOrEmpty(plugin.Error))
        {
            info.Children.Add(new TextBlock
            {
                Text = ResolvePluginError(ctx, plugin.Error),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Accents.Warning),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (plugin.RestartRequired)
        {
            info.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.RestartRequired),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Accents.Warning),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        if (ctx.Plugins is FluidBar.App.Views.Plugins.IPluginContributionUiService contributionService
            && (contributionService.SettingsPages(plugin.Id).Count > 0 || contributionService.Commands(plugin.Id).Count > 0))
        {
            var settingsButton = new Button
            {
                Content = ctx.Localizer.Get(LocKeys.Settings.Plugins.SettingsButton),
                Tag = plugin.Id,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(8),
            };
            settingsButton.Click += (_, _) => showDetail(plugin.Id);
            actions.Children.Add(settingsButton);
        }

        var toggle = new SettingsSwitch
        {
            IsOn = enabled,
            IsEnabled = plugin.Available,
            Accent = brushes.Accent,
            OffTrack = brushes.RaisedHover,
            OffHairline = brushes.LineStrong,
            OffKnob = brushes.Secondary,
        };
        toggle.Toggled += value => _ = ctx.Plugins.SetEnabledAsync(plugin.Id, value, CancellationToken.None);
        actions.Children.Add(toggle);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        border.Child = grid;
        return border;
    }

    private static Control BuildMarketplace(SettingsWindowContext ctx)
    {
        var view = new MarketplaceSectionView(ctx.Marketplace, ctx.Localizer, ctx.Surfaces, ctx.Owner, ctx.Settings.Current.Plugins.Channel);
        view.Refresh();
        return Section(ctx.Localizer.Get(LocKeys.Settings.Marketplace.Title), null, view);
    }

    private static Control BuildAdvanced(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar");
        var status = new TextBlock { FontSize = Typography.SettingsCaptionSize, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        void SetStatus(string text)
        {
            status.Text = text;
            ctx.ShowStatus(text);
        }

        var brushes = Palette(ctx);
        var scope = new SegmentedControl
        {
            ControlWidth = 240,
            Fill = brushes.Raised,
            Hairline = brushes.Line,
            ActiveFill = brushes.Active,
            ActiveForeground = brushes.ActiveForeground,
            InactiveForeground = brushes.Secondary,
        };
        scope.SetItems(
        [
            (ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetAppearance), "appearance"),
            (ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetPluginsAndCaches), "plugins"),
            (ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetEverything), "all"),
        ], "appearance");
        var confirm = new TextBox
        {
            Watermark = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmPlaceholder),
            Width = 180,
            FontFamily = Fonts.Mono,
            FontSize = 11,
        };
        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.Title), null);
        panel.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.Advanced.DataFolder), brushes.Tertiary));
        panel.Children.Add(SettingsWidgets.PathField(appData, brushes.Raised, brushes.Line, brushes.Secondary, brushes.Tertiary));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.OpenFolder),
            IconKind.Folder, false, () => ctx.OpenPath(appData), brushes.Raised, brushes.RaisedHover, brushes.Line,
            brushes.Secondary, brushes.Foreground));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.ViewLogs),
            IconKind.Document, false, () => ctx.OpenPath(Path.Combine(localData, "logs")), brushes.Raised,
            brushes.RaisedHover, brushes.Line, brushes.Secondary, brushes.Foreground));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Export),
            IconKind.Download, false, () => _ = ExportAsync(), brushes.Raised, brushes.RaisedHover, brushes.Line,
            brushes.Secondary, brushes.Foreground));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Import),
            IconKind.Rollback, false, () => _ = ImportAsync(), brushes.Raised, brushes.RaisedHover, brushes.Line,
            brushes.Secondary, brushes.Foreground));
        panel.Children.Add(actions);

        panel.Children.Add(SettingsWidgets.Group(brushes.Line, topBorder: true,
            ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.FrameStats), null, settings.Advanced.FrameStats,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { FrameStats = value } })),
            ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.CrashGuard), null, settings.Advanced.CrashGuard,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { CrashGuard = value } })),
            ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeMode),
                ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeModeHint), settings.Advanced.SafeMode,
                value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { SafeMode = value } }))));

        var resetChip = SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Reset), IconKind.Warning,
            true, () =>
            {
                if (!string.Equals(confirm.Text, "RESET", StringComparison.Ordinal))
                {
                    return;
                }

                var defaults = scope.SelectedValue switch
                {
                    "plugins" => settings with
                    {
                        Plugins = AppSettings.Default.Plugins with { Channel = settings.Plugins.Channel },
                    },
                    "all" => AppSettings.Default with { Language = settings.Language, Theme = settings.Theme },
                    _ => settings with
                    {
                        Theme = AppSettings.Default.Theme,
                        Material = AppSettings.Default.Material,
                        Palette = AppSettings.Default.Palette,
                        AccentMode = AppSettings.Default.AccentMode,
                        AccentFallback = AppSettings.Default.AccentFallback,
                        FillOpacity = AppSettings.Default.FillOpacity,
                        FontScale = AppSettings.Default.FontScale,
                        ReducedMotion = AppSettings.Default.ReducedMotion,
                        MotionStrength = AppSettings.Default.MotionStrength,
                    },
                };
                ctx.Settings.Replace(defaults);
                SetStatus(ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetDone));
            }, brushes.Raised, brushes.RaisedHover, brushes.Line, brushes.Secondary, brushes.Foreground);
        resetChip.Opacity = 0.45;
        resetChip.IsHitTestVisible = false;
        confirm.TextChanged += (_, _) =>
        {
            var armed = string.Equals(confirm.Text, "RESET", StringComparison.Ordinal);
            resetChip.Opacity = armed ? 1 : 0.45;
            resetChip.IsHitTestVisible = armed;
        };

        var resetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        resetRow.Children.Add(confirm);
        resetRow.Children.Add(resetChip);
        panel.Children.Add(SettingsWidgets.Row(ctx.Localizer.Get(LocKeys.Settings.Advanced.Reset),
            ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmBody), scope, brushes.Foreground, brushes.Tertiary, brushes.Line));
        panel.Children.Add(new Border
        {
            BorderBrush = brushes.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 12),
            Child = resetRow,
        });
        panel.Children.Add(status);
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), brushes.Tertiary, brushes.Line));
        return panel;

        async Task ExportAsync()
        {
            try
            {
                var folder = Path.Combine(appData, "backups");
                var path = Path.Combine(folder, $"fluidbar-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
                var json = ctx.Settings.Current.ToJson();
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(path, json);
                }).ConfigureAwait(true);
                SetStatus(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus(ex.Message);
            }
        }

        async Task ImportAsync()
        {
            try
            {
                var storage = ctx.Owner()?.StorageProvider;
                if (storage is null)
                {
                    SetStatus(ctx.Localizer.Get(LocKeys.Error.Generic));
                    return;
                }

                var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = ctx.Localizer.Get(LocKeys.Settings.Advanced.Import),
                    AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
                });
                var file = files.FirstOrDefault();
                if (file is null)
                {
                    return;
                }

                var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                var parsed = AppSettings.TryParse(text, out var error);
                if (parsed is null)
                {
                    SetStatus(error ?? ctx.Localizer.Get(LocKeys.Error.Generic));
                    return;
                }

                ctx.Settings.Replace(parsed);
                SetStatus(ctx.Localizer.Get(LocKeys.Settings.Advanced.Import));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
        }
    }

    private static Control BuildAbout(SettingsWindowContext ctx)
    {
        var assembly = typeof(AppSettings).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
        var brushes = Palette(ctx);
        var settings = ctx.Settings.Current;
        var accent = Accents.ParseOrDefault(settings.AccentFallback, Accents.Fallback);
        var updates = new TextBlock
        {
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Accents.Success),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.About.Title), null);
        var pane = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        pane.Children.Add(SettingsWidgets.AboutMark(accent, Colors.White));
        pane.Children.Add(new TextBlock
        {
            Text = "FluidBar",
            FontFamily = Fonts.Display,
            FontSize = 23,
            FontWeight = FontWeight.SemiBold,
            Foreground = brushes.Foreground,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        });
        pane.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.About.Version, ("version", version)),
            FontSize = 10,
            Foreground = brushes.Tertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });
        pane.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.About.Build, ("build", informational)),
            FontSize = 9,
            Foreground = brushes.Tertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        actions.Children.Add(SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.CheckForUpdates),
            IconKind.Download, () => updates.Text = ctx.Localizer.Get(LocKeys.Settings.About.UpToDate),
            brushes.Raised, brushes.RaisedHover, brushes.Line, brushes.Secondary, brushes.Foreground));
        actions.Children.Add(SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.Website),
            IconKind.ExternalLink, () => ctx.OpenPath("https://fluidbar.app"),
            brushes.Raised, brushes.RaisedHover, brushes.Line, brushes.Secondary, brushes.Foreground));
        actions.Children.Add(SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.SourceCode),
            IconKind.ExternalLink, () => ctx.OpenPath("https://github.com/Doulor/FluidBar"),
            brushes.Raised, brushes.RaisedHover, brushes.Line, brushes.Secondary, brushes.Foreground));
        pane.Children.Add(actions);
        pane.Children.Add(updates);

        pane.Children.Add(new TextBlock
        {
            Text = "MIT · 2026",
            FontSize = 9.5,
            Foreground = brushes.Tertiary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        });

        var contributors = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        contributors.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.About.Contributors), brushes.Tertiary));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        chips.Children.Add(SettingsWidgets.ContributorChip("Doulor", accent, brushes.Raised, brushes.Line, brushes.Secondary));
        contributors.Children.Add(chips);
        pane.Children.Add(contributors);

        panel.Children.Add(pane);
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), brushes.Tertiary, brushes.Line));
        return panel;
    }

    /// <summary>Draws the app mark glyph (vector, no emoji/bitmap).</summary>
    private sealed class MarkGlyph : Control
    {
        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height).Deflate(8);
            Icons.Draw(context, IconKind.Agent, bounds, new SolidColorBrush(Color.Parse("#4CC9F0")));
        }
    }
}
