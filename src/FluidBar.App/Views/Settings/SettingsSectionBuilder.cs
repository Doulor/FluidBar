using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluidBar.App.Controls;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.App.Views.Market;
using FluidBar.App.Views.Plugins;
using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Update;

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

    /// <summary>Prototype palette for the current theme.</summary>
    public required SettingsPalette Palette { get; init; }

    /// <summary>Owner window accessor (consent dialog parenting).</summary>
    public required Func<Window?> Owner { get; init; }

    /// <summary>Opens a path/folder with the shell (no-op in tests).</summary>
    public required Action<string?> OpenPath { get; init; }

    /// <summary>Shows a transient status line in the section (exported path, errors).</summary>
    public required Action<string> ShowStatus { get; init; }

    /// <summary>Update service for the About pane (null in previews/headless builds).</summary>
    public IUpdateUiService? Updates { get; init; }

    /// <summary>Frame-timing source for the advanced diagnostics line (null when no island host).</summary>
    public IFrameStatsSource? FrameStats { get; init; }

    /// <summary>Launches the verified installer and exits (About "restart and install").</summary>
    public Action? RestartToInstall { get; init; }
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

    // ---- shared helpers ---------------------------------------------------------------------

    private static StackPanel Section(string title, string? subtitle, params Control[] children)
        => Section(Ambient, title, subtitle, children);

    private static StackPanel Section(SettingsWindowContext context, string title, string? subtitle,
        params Control[] children)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(SettingsWidgets.PaneHeader(title, subtitle, context.Palette));
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static Control Row(string title, string? description, Control control)
        => SettingsWidgets.Row(title, description, control, Ambient.Palette);

    private static Control ToggleRow(SettingsWindowContext context, string label, string? description, bool value,
        Action<bool> changed, bool enabled = true)
    {
        var palette = context.Palette;
        var toggle = new SettingsSwitch
        {
            IsOn = value,
            IsEnabled = enabled,
            Accent = new SolidColorBrush(AccentOf(context)),
            OffTrack = palette.RaisedHoverBrush,
            OffHairline = palette.LineStrongBrush,
            OffKnob = palette.SecondaryBrush,
        };
        if (enabled)
        {
            toggle.Toggled += changed;
        }

        return SettingsWidgets.Row(label, description, toggle, palette);
    }

    private static Control SegmentedRow(SettingsWindowContext context, string label, string? description,
        (string Label, string Value)[] options, string current, Action<string> changed, bool small = false)
    {
        var palette = context.Palette;
        var segmented = new SegmentedControl
        {
            ControlWidth = small ? SettingsMetrics.SegmentedSmallWidth : SettingsMetrics.SegmentedWidth,
            Fill = palette.RaisedBrush,
            Hairline = palette.LineBrush,
            ActiveFill = palette.ActiveBrush,
            ActiveForeground = palette.ActiveForegroundBrush,
            InactiveForeground = palette.SecondaryBrush,
        };
        segmented.SetItems(options, current);
        segmented.Changed += changed;
        return SettingsWidgets.Row(label, description, segmented, palette);
    }

    private static Control SliderRow(SettingsWindowContext context, string label, string? description,
        double min, double max, double step, double value, Func<double, string> format, Action<double> changed)
    {
        var palette = context.Palette;
        var slider = new SettingsSlider
        {
            Minimum = min,
            Maximum = max,
            Step = step,
            Value = Math.Clamp(value, min, max),
            Fill = palette.ForegroundBrush,
            Track = palette.RaisedHoverBrush,
        };
        var tagText = new TextBlock
        {
            Text = format(slider.Value),
            FontSize = SettingsMetrics.ValueTagSize,
            Foreground = palette.SecondaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tag = SettingsWidgets.ValueTag(format(slider.Value), palette);
        if (tag is Border { Child: TextBlock tagBlock })
        {
            tagText = tagBlock;
        }

        slider.ValueChanged += next =>
        {
            tagText.Text = format(next);
            changed(next);
        };
        return SettingsWidgets.Row(label, description, SettingsWidgets.RangeHost(slider, tag), palette);
    }

    private static Color AccentOf(SettingsWindowContext context)
        => Accents.ParseOrDefault(context.Settings.Current.AccentFallback, Accents.Fallback);

    private static string[] SplitHotkey(string value)
        => value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---- general ----------------------------------------------------------------------------

    private static Control BuildGeneral(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var palette = ctx.Palette;
        var languageOptions = new (string, string)[]
        {
            (ctx.Localizer.Get(LocKeys.Settings.General.LanguageOptions.Chinese), "zh-Hans"),
            (ctx.Localizer.Get(LocKeys.Settings.General.LanguageOptions.English), "en-US"),
        };
        var effectiveLanguage = string.Equals(settings.Language, "system", StringComparison.OrdinalIgnoreCase)
            ? (ctx.Localizer.CurrentCulture.TwoLetterISOLanguageName == "zh" ? "zh-Hans" : "en-US")
            : settings.Language;

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Title),
            ctx.Localizer.Get(LocKeys.Settings.General.Subtitle),
            SettingsWidgets.Group(palette, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Language),
                    ctx.Localizer.Get(LocKeys.Settings.General.LanguageHint), languageOptions, effectiveLanguage,
                    value => ctx.Settings.Update(s => s with { Language = value })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindows),
                    ctx.Localizer.Get(LocKeys.Settings.General.StartWithWindowsHint), settings.StartWithWindows,
                    value => ctx.Settings.Update(s => s with { StartWithWindows = value })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdateLabel),
                    ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdateHint),
                    !string.Equals(settings.AutoUpdate, "off", StringComparison.OrdinalIgnoreCase),
                    value => ctx.Settings.Update(s => s with { AutoUpdate = value ? "notify" : "off" })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.TrayIcon),
                    ctx.Localizer.Get(LocKeys.Settings.General.TrayIconHint), settings.TrayIcon,
                    value => ctx.Settings.Update(s => s with { TrayIcon = value }))));

        panel.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.General.Hotkeys), palette));
        panel.Children.Add(SettingsWidgets.Group(palette, topBorder: false,
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Toggle), settings.HotkeyToggle,
                value => ctx.Settings.Update(s => s with { HotkeyToggle = value })),
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Hide), settings.HotkeyHide,
                value => ctx.Settings.Update(s => s with { HotkeyHide = value })),
            HotkeyRow(ctx, ctx.Localizer.Get(LocKeys.Settings.General.Hotkey.Settings), settings.HotkeySettings,
                value => ctx.Settings.Update(s => s with { HotkeySettings = value }), last: true)));
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), palette));
        return panel;
    }

    private static Control HotkeyRow(SettingsWindowContext ctx, string label, string value, Action<string> commit,
        bool last = false)
    {
        var palette = ctx.Palette;
        var caps = SettingsWidgets.Keycaps(SplitHotkey(value), palette);
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
        return SettingsWidgets.ShortcutRow(label, host, palette, last);
    }

    // ---- appearance -------------------------------------------------------------------------

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
        var palette = ctx.Palette;
        var preview = new IslandPreview();
        preview.Update(settings, ctx.Localizer);

        var swatches = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.AccentGap,
        };
        foreach (var (_, hex) in AccentChoices)
        {
            var color = Color.Parse(hex);
            var swatch = new AccentSwatch
            {
                SwatchColor = color,
                Ring = palette.SecondaryBrush,
                // The ring marks the accent actually in use: with mode "artwork" it is the fallback
                // colour the island uses whenever artwork sampling has no answer, so it is always shown.
                IsActive = string.Equals(settings.AccentFallback, hex, StringComparison.OrdinalIgnoreCase),
            };
            swatch.Clicked += () => ctx.Settings.Update(s => s with { AccentFallback = hex, AccentMode = "custom" });
            swatches.Children.Add(swatch);
        }

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Title),
            ctx.Localizer.Get(LocKeys.Settings.Appearance.Subtitle),
            SettingsWidgets.Group(palette, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Desc),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Dark), "dark"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Theme.Light), "light"),
                    ],
                    settings.Theme == AppTheme.Light ? "light" : "dark",
                    value => ctx.Settings.Update(s => s with
                    {
                        Theme = value == "light" ? AppTheme.Light : AppTheme.Dark,
                    })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Desc),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Obsidian), "obsidian"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Material.Acrylic), "acrylic"),
                    ],
                    settings.Material == IslandMaterial.Acrylic ? "acrylic" : "obsidian",
                    value => ctx.Settings.Update(s => s with
                    {
                        Material = value == "acrylic" ? IslandMaterial.Acrylic : IslandMaterial.Obsidian,
                    })),
                SettingsWidgets.Row(ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Desc), swatches, palette),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.PanelRadius),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.PanelRadiusHint), 16, 36, 1, settings.PanelRadius,
                    value => $"{value:0} px", value => ctx.Settings.Update(s => s with { PanelRadius = value })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.FillOpacity),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.FillOpacityHint),
                    LayoutTokens.MinFillOpacity, 1.0, 0.01, settings.FillOpacity,
                    value => $"{value * 100:0}%", value => ctx.Settings.Update(s => s with { FillOpacity = value }))));

        // .appearance-preview { margin-top:16; padding-top:12; border-top }
        var previewHead = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, SettingsMetrics.ApHeadMarginBottom),
        };
        previewHead.Children.Add(SettingsWidgets.Kicker(
            ctx.Localizer.Get(LocKeys.Settings.Appearance.PreviewLabel),
            SettingsMetrics.ApHeadSize,
            SettingsMetrics.ApHeadTracking,
            palette.TertiaryBrush));
        var meta = new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.Appearance.Meta,
                ("radius", $"{settings.PanelRadius:0}"),
                ("opacity", $"{settings.FillOpacity * 100:0}")),
            FontFamily = Fonts.Mono,
            FontSize = SettingsMetrics.ApMetaSize,
            Foreground = palette.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(meta, 1);
        previewHead.Children.Add(meta);

        var canvas = new Border
        {
            Height = SettingsMetrics.ApCanvasHeight,
            CornerRadius = new CornerRadius(SettingsMetrics.ApCanvasRadius),
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = preview,
        };
        var previewBlock = new StackPanel
        {
            Margin = new Thickness(0, SettingsMetrics.PreviewMarginTop, 0, 0),
            Spacing = 0,
        };
        previewBlock.Children.Add(new Border
        {
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, SettingsMetrics.PreviewPadTop, 0, 0),
            Child = new StackPanel { Children = { previewHead, canvas } },
        });
        panel.Children.Add(previewBlock);
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.Appearance.LiveHint), palette));
        return panel;
    }

    // ---- island -----------------------------------------------------------------------------

    private static Control BuildIsland(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var palette = ctx.Palette;
        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Title),
            ctx.Localizer.Get(LocKeys.Settings.Island.Description),
            SettingsWidgets.Group(palette, topBorder: true,
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Desc),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Float), "float"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Flush), "flush"),
                        (ctx.Localizer.Get(LocKeys.Settings.Island.Mode.Dock), "dock"),
                    ],
                    settings.Mode,
                    value => ctx.Settings.Update(s => s with { Mode = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Island.Anchor.Desc),
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
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Island.Stacking.Desc),
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
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.ReducedMotion.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Island.Motion.Desc),
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
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMs),
                    ctx.Localizer.Get(LocKeys.Settings.Island.AutoCollapseMsHint), 0, 8000, 100, settings.AutoCollapseMs,
                    value => $"{value / 1000:0.0} s", value => ctx.Settings.Update(s => s with { AutoCollapseMs = (int)Math.Round(value) })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.MotionStrength),
                    ctx.Localizer.Get(LocKeys.Settings.Island.MotionStrengthHint), 0.5, 1.5, 0.05, settings.MotionStrength,
                    value => $"×{value:0.00}", value => ctx.Settings.Update(s => s with { MotionStrength = value })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMs),
                    ctx.Localizer.Get(LocKeys.Settings.Island.AutoHideMsHint), 0, 30000, 500, settings.AutoHideMs,
                    value => $"{value / 1000:0} s", value => ctx.Settings.Update(s => s with { AutoHideMs = (int)Math.Round(value) })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.PeekOnTrackChange),
                    ctx.Localizer.Get(LocKeys.Settings.Island.PeekOnTrackChangeHint), settings.PeekOnTrackChange,
                    value => ctx.Settings.Update(s => s with { PeekOnTrackChange = value })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Title),
                    ctx.Localizer.Get(LocKeys.Settings.Island.DisplayOn.Desc),
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
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), palette));
        return panel;
    }

    // ---- plugins ----------------------------------------------------------------------------

    private static Control BuildPlugins(SettingsWindowContext ctx)
    {
        var palette = ctx.Palette;
        var list = new StackPanel { Spacing = SettingsMetrics.PluginListGap };
        var contributions = ctx.Plugins as IPluginContributionUiService;
        var search = SettingsWidgets.SearchField(
            ctx.Localizer.Get(LocKeys.Settings.Plugins.Search), string.Empty, _ => { }, palette);
        var rowsHost = list;
        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Plugins.Title),
            ctx.Localizer.Get(LocKeys.Settings.Plugins.Description),
            new Border { Margin = new Thickness(0, 0, 0, 10), Child = search },
            rowsHost);

        var toolbar = BuildPluginToolbar(ctx, palette);
        panel.Children.Add(toolbar);
        var footer = SettingsWidgets.FooterNote(string.Empty, palette);
        panel.Children.Add(footer);
        if (search is Border { Child: Grid searchGrid } && searchGrid.Children.Count > 1
            && searchGrid.Children[1] is TextBox searchBox)
        {
            searchBox.TextChanged += (_, _) => _ = LoadAsync();
        }

        _ = LoadAsync();
        return panel;

        Control BuildPluginToolbar(SettingsWindowContext context, SettingsPalette colors)
        {
            var bar = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, SettingsMetrics.MarketToolbarMarginTop, 0, 0),
            };
            bar.Children.Add(new TextBlock
            {
                Text = context.Localizer.Get(LocKeys.Settings.Plugins.FolderHint),
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = colors.TertiaryBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var open = SettingsWidgets.SmallAction(
                context.Localizer.Get(LocKeys.Settings.Plugins.OpenFolder),
                SettingsIconKind.Folder,
                false,
                () => context.OpenPath(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "plugins")),
                colors);
            open.HorizontalAlignment = HorizontalAlignment.Right;
            open.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(open, 1);
            bar.Children.Add(open);
            return bar;
        }

        async Task LoadAsync()
        {
            var query = (search as Border)?.Child is Grid g && g.Children.Count > 1 && g.Children[1] is TextBox box
                ? box.Text?.Trim() ?? string.Empty
                : string.Empty;
            try
            {
                var plugins = await ctx.Plugins.ListAsync(CancellationToken.None);
                list.Children.Clear();
                var count = 0;
                foreach (var plugin in plugins)
                {
                    if (query.Length > 0
                        && !plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        && !plugin.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    count++;
                    list.Children.Add(BuildPluginRow(ctx, plugin, ShowDetail));
                }

                if (footer is Border { Child: TextBlock footerText })
                {
                    footerText.Text = ctx.Localizer.Format(LocKeys.Settings.Plugins.Count, ("count", count));
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

        void ShowDetail(string pluginId)
        {
            if (contributions is null)
            {
                return;
            }

            panel.Children.Clear();
            var back = SettingsWidgets.SmallAction(
                ctx.Localizer.Get(LocKeys.Common.Back), SettingsIconKind.Refresh, false,
                () =>
                {
                    panel.Children.Clear();
                    panel.Children.Add(SettingsWidgets.PaneHeader(
                        ctx.Localizer.Get(LocKeys.Settings.Plugins.Title),
                        ctx.Localizer.Get(LocKeys.Settings.Plugins.Description), palette));
                    panel.Children.Add(search);
                    panel.Children.Add(list);
                    panel.Children.Add(toolbar);
                    panel.Children.Add(footer);
                    _ = LoadAsync();
                },
                palette);
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, SettingsMetrics.CaptionMarginTop, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = back,
            });
            var name = LocalizedValue.Resolve(contributions.DisplayName(pluginId), ctx.Localizer);
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontFamily = Fonts.Display,
                FontSize = SettingsMetrics.HeadSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = palette.ForegroundBrush,
                Margin = new Thickness(0, 12, 0, 0),
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
                panel.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.Plugins.Commands), palette));
                foreach (var command in commands)
                {
                    var label = LocalizedValue.Resolve(command.Title, ctx.Localizer);
                    var commandId = command.Id;
                    var commandButton = SettingsWidgets.SmallAction(
                        string.IsNullOrEmpty(label) ? command.Id : label,
                        SettingsIconKind.Sliders,
                        false,
                        () =>
                        {
                            _ = contributions.InvokeCommandAsync(pluginId, commandId);
                            ctx.ShowStatus(commandId);
                        },
                        palette);
                    commandButton.HorizontalAlignment = HorizontalAlignment.Left;
                    commandButton.Margin = new Thickness(0, 0, 0, 5);
                    panel.Children.Add(commandButton);
                }
            }
        }
    }

    /// <summary>Resolves a plugin display name: catalog keys are localized, plain names pass through.</summary>
    private static string ResolvePluginName(SettingsWindowContext ctx, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.Contains('.', StringComparison.Ordinal))
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

    /// <summary>One <c>.plugin-row</c>: 36 px icon, name/version/badges, meta, permission chips, actions.</summary>
    private static Control BuildPluginRow(SettingsWindowContext ctx, PluginListItem plugin, Action<string> showDetail)
    {
        var palette = ctx.Palette;
        var enabled = plugin.IsEnabled;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        var icon = new Border
        {
            Width = SettingsMetrics.PluginIcon,
            Height = SettingsMetrics.PluginIcon,
            CornerRadius = new CornerRadius(SettingsMetrics.PluginIconRadius),
            Background = new SolidColorBrush(PluginColor(plugin.Id)),
            Child = new SettingsIcon
            {
                Kind = PluginIcon(plugin.Id),
                Brush = Brushes.White,
                Width = SettingsMetrics.PluginGlyph,
                Height = SettingsMetrics.PluginGlyph,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(icon, 0);
        row.Children.Add(icon);

        var main = new StackPanel { Spacing = 0, Margin = new Thickness(SettingsMetrics.PluginRowGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        nameRow.Children.Add(new TextBlock
        {
            Text = ResolvePluginName(ctx, plugin.Name),
            FontSize = SettingsMetrics.PluginNameSize,
            FontWeight = FontWeight.SemiBold,
            Foreground = palette.ForegroundBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        nameRow.Children.Add(new TextBlock
        {
            Text = "v" + plugin.Version,
            FontSize = SettingsMetrics.PluginVersionSize,
            Foreground = palette.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        nameRow.Children.Add(Badge(
            ctx.Localizer.Get(plugin.Builtin ? LocKeys.Settings.Plugins.Builtin : LocKeys.Settings.Plugins.Community),
            palette));
        main.Children.Add(nameRow);
        main.Children.Add(new TextBlock
        {
            Text = plugin.Publisher,
            FontSize = SettingsMetrics.PluginMetaSize,
            Foreground = palette.TertiaryBrush,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (plugin.CapabilityKeys.Count > 0)
        {
            var perms = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = SettingsMetrics.PermGap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            foreach (var key in plugin.CapabilityKeys)
            {
                perms.Children.Add(Perm(ctx.Localizer.Get(key), palette));
            }

            main.Children.Add(perms);
        }

        if (!plugin.Available)
        {
            main.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.StateUnavailable),
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = new SolidColorBrush(Accents.Warning),
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        else if (!enabled)
        {
            main.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.StateDisabled),
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = palette.TertiaryBrush,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        if (!string.IsNullOrEmpty(plugin.Error))
        {
            main.Children.Add(new TextBlock
            {
                Text = ResolvePluginError(ctx, plugin.Error),
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = new SolidColorBrush(Accents.Warning),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        if (plugin.RestartRequired)
        {
            main.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.Plugins.RestartRequired),
                FontSize = SettingsMetrics.RowDescSize,
                Foreground = new SolidColorBrush(Accents.Warning),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        Grid.SetColumn(main, 1);
        row.Children.Add(main);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(SettingsMetrics.PluginRowGap, 0, 0, 0),
        };
        if (ctx.Plugins is IPluginContributionUiService contributionService
            && (contributionService.SettingsPages(plugin.Id).Count > 0 || contributionService.Commands(plugin.Id).Count > 0))
        {
            actions.Children.Add(SettingsWidgets.IconButton(SettingsIconKind.Settings, () => showDetail(plugin.Id), palette));
        }

        var toggle = new SettingsSwitch
        {
            IsOn = enabled,
            IsEnabled = plugin.Available,
            Compact = true,
            Accent = new SolidColorBrush(AccentOf(ctx)),
            OffTrack = palette.RaisedHoverBrush,
            OffHairline = palette.LineStrongBrush,
            OffKnob = palette.SecondaryBrush,
            Margin = new Thickness(2, 0, 0, 0),
        };
        toggle.Toggled += value => _ = ctx.Plugins.SetEnabledAsync(plugin.Id, value, CancellationToken.None);
        actions.Children.Add(toggle);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);

        return new Border
        {
            Background = palette.RaisedBrush,
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SettingsMetrics.PluginRowRadius),
            Padding = new Thickness(SettingsMetrics.PluginRowPadX, SettingsMetrics.PluginRowPadY,
                SettingsMetrics.PluginRowPadX, SettingsMetrics.PluginRowPadY),
            Child = row,
        };
    }

    private static Control Badge(string text, SettingsPalette palette)
        => new Border
        {
            Height = SettingsMetrics.BadgeHeight,
            Padding = new Thickness(SettingsMetrics.BadgePadX, 0, SettingsMetrics.BadgePadX, 0),
            CornerRadius = new CornerRadius(SettingsMetrics.BadgeRadius),
            Background = palette.RaisedHoverBrush,
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = SettingsMetrics.BadgeSize,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = SettingsMetrics.BadgeSize * SettingsMetrics.BadgeTracking,
                Foreground = palette.SecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private static Control Perm(string text, SettingsPalette palette)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new SettingsIcon
        {
            Kind = SettingsIconKind.Shield,
            Brush = palette.TertiaryBrush,
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = SettingsMetrics.PermSize,
            Foreground = palette.TertiaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new Border
        {
            Height = SettingsMetrics.PermHeight,
            Padding = new Thickness(SettingsMetrics.PermPadX, 0, SettingsMetrics.PermPadX, 0),
            CornerRadius = new CornerRadius(SettingsMetrics.PermRadius),
            Background = palette.RaisedBrush,
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private static SettingsIconKind PluginIcon(string id) => id switch
    {
        var value when value.Contains("media", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Star,
        var value when value.Contains("clipboard", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.File,
        var value when value.Contains("notifications", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Alert,
        var value when value.Contains("agent", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Sliders,
        var value when value.Contains("code", StringComparison.OrdinalIgnoreCase) => SettingsIconKind.Palette,
        _ => SettingsIconKind.Puzzle,
    };

    private static Color PluginColor(string id) => id switch
    {
        var value when value.Contains("media", StringComparison.OrdinalIgnoreCase) => Color.Parse("#3E6E86"),
        var value when value.Contains("clipboard", StringComparison.OrdinalIgnoreCase) => Color.Parse("#8A6A3A"),
        var value when value.Contains("notifications", StringComparison.OrdinalIgnoreCase) => Color.Parse("#4A5686"),
        var value when value.Contains("agent", StringComparison.OrdinalIgnoreCase) => Color.Parse("#3F6E52"),
        var value when value.Contains("code", StringComparison.OrdinalIgnoreCase) => Color.Parse("#4A5686"),
        _ => Color.Parse("#4C5566"),
    };

    // ---- marketplace ------------------------------------------------------------------------

    private static Control BuildMarketplace(SettingsWindowContext ctx)
    {
        var view = new MarketplaceSectionView(
            ctx.Marketplace,
            ctx.Localizer,
            ctx.Palette,
            ctx.Owner,
            ctx.Settings.Current.Plugins.Channel);
        view.Refresh();
        return Section(ctx.Localizer.Get(LocKeys.Settings.Marketplace.Title),
            ctx.Localizer.Get(LocKeys.Settings.Marketplace.Description), view);
    }

    // ---- advanced ---------------------------------------------------------------------------

    private static Control BuildAdvanced(SettingsWindowContext ctx)
    {
        var settings = ctx.Settings.Current;
        var palette = ctx.Palette;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar");
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluidBar");
        var status = new TextBlock
        {
            FontSize = SettingsMetrics.FooterNoteSize,
            Foreground = palette.TertiaryBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        void SetStatus(string text)
        {
            status.Text = text;
            ctx.ShowStatus(text);
        }

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.Title),
            ctx.Localizer.Get(LocKeys.Settings.Advanced.Description));
        panel.Children.Add(SettingsWidgets.Caption(ctx.Localizer.Get(LocKeys.Settings.Advanced.DataFolder), palette));
        panel.Children.Add(SettingsWidgets.PathField(appData, palette));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.AdvActionsGap,
            Margin = new Thickness(0, SettingsMetrics.AdvActionsMarginTop, 0, 0),
        };
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.OpenFolder),
            SettingsIconKind.Folder, false, () => ctx.OpenPath(appData), palette));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.ViewLogs),
            SettingsIconKind.File, false, () => ctx.OpenPath(Path.Combine(localData, "logs")), palette));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Export),
            SettingsIconKind.Download, false, () => _ = ExportAsync(), palette));
        actions.Children.Add(SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Import),
            SettingsIconKind.Refresh, false, () => _ = ImportAsync(), palette));
        panel.Children.Add(actions);

        var confirm = new TextBox
        {
            Watermark = ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetConfirmPlaceholder),
            Width = 180,
            FontFamily = Fonts.Mono,
            FontSize = 11,
        };

        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Child = SettingsWidgets.Group(palette, topBorder: true,
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.FrameStats), null, settings.Advanced.FrameStats,
                    value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { FrameStats = value } })),
                // Live diagnostics line (spec §9.3 frame.p50/p99): the toggle has a visible effect
                // instead of only switching a flag nobody reads.
                settings.Advanced.FrameStats && ctx.FrameStats is not null
                    ? BuildFrameStatsLine(ctx)
                    : new Border { Height = 0 },
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.CrashGuard), null, settings.Advanced.CrashGuard,
                    value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { CrashGuard = value } })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeMode),
                    ctx.Localizer.Get(LocKeys.Settings.Advanced.SafeModeHint), settings.Advanced.SafeMode,
                    value => ctx.Settings.Update(s => s with { Advanced = s.Advanced with { SafeMode = value } })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.ThemeFollowSystem),
                    ctx.Localizer.Get(LocKeys.Settings.Advanced.ThemeFollowSystemHint),
                    settings.Theme == AppTheme.System,
                    value => ctx.Settings.Update(s => s with
                    {
                        Theme = value ? AppTheme.System : (s.Theme == AppTheme.System ? AppTheme.Dark : s.Theme),
                    })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.LanguageFollowSystem),
                    ctx.Localizer.Get(LocKeys.Settings.Advanced.LanguageFollowSystemHint),
                    string.Equals(settings.Language, "system", StringComparison.OrdinalIgnoreCase),
                    value => ctx.Settings.Update(s => s with
                    {
                        Language = value
                            ? "system"
                            : (ctx.Localizer.CurrentCulture.TwoLetterISOLanguageName == "zh" ? "zh-Hans" : "en-US"),
                    })),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Advanced.UpdateMode),
                    ctx.Localizer.Get(LocKeys.Settings.Advanced.UpdateModeHint),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Notify), "notify"),
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Auto), "auto"),
                        (ctx.Localizer.Get(LocKeys.Settings.General.AutoUpdate.Off), "off"),
                    ],
                    settings.AutoUpdate,
                    value => ctx.Settings.Update(s => s with { AutoUpdate = value }), small: true),
                SettingsWidgets.Row(ctx.Localizer.Get(LocKeys.Settings.General.Telemetry),
                    ctx.Localizer.Get(LocKeys.Settings.General.TelemetryHint),
                    new SettingsSwitch
                    {
                        IsOn = false,
                        IsEnabled = false,
                        Accent = new SolidColorBrush(AccentOf(ctx)),
                        OffTrack = palette.RaisedHoverBrush,
                        OffHairline = palette.LineStrongBrush,
                        OffKnob = palette.SecondaryBrush,
                    },
                    palette),
                SegmentedRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.PaletteLabel),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.PaletteLabelHint),
                    [
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Obsidian), "obsidian"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Graphite), "graphite"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Slate), "slate"),
                        (ctx.Localizer.Get(LocKeys.Settings.Appearance.Palette.Nord), "nord"),
                    ],
                    settings.Palette,
                    value => ctx.Settings.Update(s => s with { Palette = value })),
                ToggleRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.Artwork),
                    ctx.Localizer.Get(LocKeys.Settings.Appearance.Accent.ArtworkHint),
                    settings.AccentMode == "artwork",
                    value => ctx.Settings.Update(s => s with { AccentMode = value ? "artwork" : "custom" })),
                SliderRow(ctx, ctx.Localizer.Get(LocKeys.Settings.Appearance.FontScale), null,
                    0.8, 1.6, 0.05, settings.FontScale,
                    value => $"{value:0.00}×", value => ctx.Settings.Update(s => s with { FontScale = value })))});
        // The reset action must be the chip's own callback: SmallAction marks PointerPressed as
        // handled, so a second handler appended after creation would never run (the reset chip used
        // to be a dead button).
        void ApplyReset()
        {
            if (!string.Equals(confirm.Text, "RESET", StringComparison.Ordinal))
            {
                return;
            }

            var defaults = settings with
            {
                Language = AppSettings.Default.Language,
                Theme = AppSettings.Default.Theme,
                StartWithWindows = AppSettings.Default.StartWithWindows,
                TrayIcon = AppSettings.Default.TrayIcon,
                AutoUpdate = AppSettings.Default.AutoUpdate,
                HotkeyToggle = AppSettings.Default.HotkeyToggle,
                HotkeyHide = AppSettings.Default.HotkeyHide,
                HotkeySettings = AppSettings.Default.HotkeySettings,
                Material = AppSettings.Default.Material,
                Palette = AppSettings.Default.Palette,
                AccentMode = AppSettings.Default.AccentMode,
                AccentFallback = AppSettings.Default.AccentFallback,
                FillOpacity = AppSettings.Default.FillOpacity,
                PanelRadius = AppSettings.Default.PanelRadius,
                FontScale = AppSettings.Default.FontScale,
                ReducedMotion = AppSettings.Default.ReducedMotion,
                MotionStrength = AppSettings.Default.MotionStrength,
                HoverPeek = AppSettings.Default.HoverPeek,
                AutoCollapseMs = AppSettings.Default.AutoCollapseMs,
                AutoHideMs = AppSettings.Default.AutoHideMs,
                Stacking = AppSettings.Default.Stacking,
                Mode = AppSettings.Default.Mode,
                Anchor = AppSettings.Default.Anchor,
                DisplayOn = AppSettings.Default.DisplayOn,
                PeekOnTrackChange = AppSettings.Default.PeekOnTrackChange,
                Placement = AppSettings.Default.Placement,
                Dock = AppSettings.Default.Dock,
                Plugins = AppSettings.Default.Plugins,
                Advanced = AppSettings.Default.Advanced,
            };
            ctx.Settings.Replace(defaults);
            SetStatus(ctx.Localizer.Get(LocKeys.Settings.Advanced.ResetDone));
        }

        var resetChip = SettingsWidgets.SmallAction(ctx.Localizer.Get(LocKeys.Settings.Advanced.Reset),
            SettingsIconKind.Trash, true, ApplyReset, palette);
        var resetHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = SettingsMetrics.AdvActionsGap,
            Margin = new Thickness(0, SettingsMetrics.AdvActionsMarginTop, 0, 0),
        };
        resetHost.Children.Add(confirm);
        resetHost.Children.Add(resetChip);
        resetChip.IsHitTestVisible = false;

        // Arm on the property change, not TextChanged: Avalonia's TextBox does not raise TextChanged
        // for every text mutation (e.g. assistive/UIA value sets), which used to leave the chip
        // permanently disabled. PropertyChanged fires for typing and programmatic/UIA edits alike.
        confirm.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBox.TextProperty)
            {
                return;
            }

            var armed = string.Equals(confirm.Text, "RESET", StringComparison.Ordinal);
            resetChip.Opacity = armed ? 1 : 0.45;
            resetChip.IsHitTestVisible = armed;
        };
        resetChip.Opacity = 0.45;
        resetChip.Margin = new Thickness(0, 0, 0, 0);
        panel.Children.Add(new Border
        {
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, SettingsMetrics.AdvActionsMarginTop, 0, SettingsMetrics.AdvActionsMarginTop),
            Margin = new Thickness(0, 12, 0, 0),
            Child = resetHost,
        });
        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Child = status,
        });
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), palette));
        return panel;

        async Task ExportAsync()
        {
            try
            {
                var folder = Path.Combine(appData, "backups");
                var path = Path.Combine(folder, $"fluidbar-settings-{DateTime.Now:yyyyMMdd-HHmm}.json");
                var json = SettingsExport.Create(ctx.Settings.Current, DateTimeOffset.Now);
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
                if (!SettingsExport.TryParse(text, out var parsed, out var error) || parsed is null)
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

    // ---- about ------------------------------------------------------------------------------

    private static Control BuildAbout(SettingsWindowContext ctx)
    {
        var assembly = typeof(AppSettings).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? version;
        var palette = ctx.Palette;
        var settings = ctx.Settings.Current;
        var appearance = SystemAppearanceSnapshot.FromWindows();
        var accent = Accents.ParseOrDefault(
            SystemSettingResolution.ResolveAccent(settings.AccentMode, settings.AccentFallback, appearance.AccentHex),
            Accents.Fallback);
        var updateService = ctx.Updates;

        var panel = Section(ctx, ctx.Localizer.Get(LocKeys.Settings.About.Title),
            ctx.Localizer.Get(LocKeys.Settings.About.Description));
        // .about-pane: a centred column, but .contributors spans the full pane width so its
        // uppercase title stays left-aligned while the chips are centred.
        var pane = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var mark = SettingsWidgets.AboutMark(accent, palette);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        pane.Children.Add(mark);
        pane.Children.Add(new TextBlock
        {
            Text = "FluidBar",
            FontFamily = Fonts.Display,
            FontSize = SettingsMetrics.AboutNameSize,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = -0.46,
            Foreground = palette.ForegroundBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        });
        pane.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Format(LocKeys.Settings.About.Version, ("version", version)),
            FontSize = SettingsMetrics.AboutVersionSize,
            LetterSpacing = 0.3,
            Foreground = palette.TertiaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var busy = updateService?.IsBusy == true;
        var checkChip = SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.CheckForUpdates),
            SettingsIconKind.Refresh,
            () =>
            {
                if (!busy && updateService is not null)
                {
                    _ = updateService.CheckNowAsync();
                }
            }, palette);
        if (busy)
        {
            checkChip.Opacity = 0.55;
        }

        actions.Children.Add(checkChip);
        if (updateService is not null)
        {
            switch (updateService.Current.State)
            {
                case UpdateState.Available:
                    actions.Children.Add(SettingsWidgets.PillAction(
                        ctx.Localizer.Get(LocKeys.Settings.About.Update.Download),
                        SettingsIconKind.Download,
                        () => _ = updateService.DownloadCurrentAsync(), palette));
                    break;
                case UpdateState.ReadyToInstall:
                    actions.Children.Add(SettingsWidgets.PillAction(
                        ctx.Localizer.Get(LocKeys.Settings.About.Update.Install),
                        SettingsIconKind.Refresh,
                        () => ctx.RestartToInstall?.Invoke(), palette));
                    break;
            }
        }
        actions.Children.Add(SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.Website),
            SettingsIconKind.External, () => ctx.OpenPath("https://fluidbar.app"), palette));
        actions.Children.Add(SettingsWidgets.PillAction(ctx.Localizer.Get(LocKeys.Settings.About.SourceCode),
            SettingsIconKind.Github, () => ctx.OpenPath("https://github.com/Doulor/FluidBar"), palette));
        actions.HorizontalAlignment = HorizontalAlignment.Center;
        pane.Children.Add(actions);

        if (updateService is not null)
        {
            var (statusText, statusColor) = DescribeUpdate(updateService, ctx.Localizer);
            pane.Children.Add(new TextBlock
            {
                Text = statusText,
                FontSize = SettingsMetrics.LicenseSize,
                Foreground = new SolidColorBrush(statusColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                Margin = new Thickness(0, 8, 0, 0),
            });
            pane.Children.Add(new TextBlock
            {
                Text = ctx.Localizer.Format(LocKeys.Settings.About.Update.Feed,
                    ("feed", updateService.FeedDescription)),
                FontFamily = Fonts.Mono,
                FontSize = SettingsMetrics.FooterNoteSize,
                Foreground = palette.TertiaryBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        pane.Children.Add(new TextBlock
        {
            Text = ctx.Localizer.Get(LocKeys.Settings.About.License),
            FontSize = SettingsMetrics.LicenseSize,
            Foreground = palette.TertiaryBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        });

        var contributors = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        contributors.Children.Add(new Border
        {
            BorderBrush = palette.LineBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 14, 0, 0),
            Child = new TextBlock
            {
                Text = ctx.Localizer.Get(LocKeys.Settings.About.Contributors).ToUpperInvariant(),
                FontSize = SettingsMetrics.CaptionSize,
                LetterSpacing = SettingsMetrics.CaptionSize * SettingsMetrics.CaptionTracking,
                Foreground = palette.TertiaryBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8),
            },
        });
        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        chips.Children.Add(SettingsWidgets.ContributorChip("Doulor", accent, palette));
        contributors.Children.Add(chips);
        pane.Children.Add(contributors);

        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        };
        links.Children.Add(AboutLink(ctx.Localizer.Get(LocKeys.Settings.About.Docs), SettingsIconKind.File,
            () => ctx.OpenPath("https://fluidbar.app/docs"), palette));
        links.Children.Add(AboutLink(ctx.Localizer.Get(LocKeys.Settings.About.SourceCode), SettingsIconKind.Github,
            () => ctx.OpenPath("https://github.com/Doulor/FluidBar"), palette));
        links.Children.Add(AboutLink(ctx.Localizer.Get(LocKeys.Settings.About.Feedback), SettingsIconKind.Chat,
            () => ctx.OpenPath("https://github.com/Doulor/FluidBar/issues"), palette));
        pane.Children.Add(links);

        panel.Children.Add(pane);
        panel.Children.Add(SettingsWidgets.FooterNote(ctx.Localizer.Get(LocKeys.Settings.FooterNote), palette));
        return panel;
    }

    /// <summary>Builds the live frame-statistics line shown when the advanced setting is on.</summary>
    private static Control BuildFrameStatsLine(SettingsWindowContext ctx)
    {
        var text = new TextBlock
        {
            FontFamily = Fonts.Mono,
            FontSize = SettingsMetrics.FooterNoteSize,
            Foreground = ctx.Palette.SecondaryBrush,
            Margin = new Thickness(0, 0, 0, 10),
        };
        void Refresh() => text.Text = FormatFrameStats(ctx);
        Refresh();

        // Only ticks while the line is on screen; the section rebuild swaps it out on toggle.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Refresh();
        text.AttachedToVisualTree += (_, _) => timer.Start();
        text.DetachedFromVisualTree += (_, _) => timer.Stop();
        return text;
    }

    /// <summary>Formats the current frame-timing snapshot (technical values, localized label).</summary>
    private static string FormatFrameStats(SettingsWindowContext ctx)
    {
        var stats = ctx.FrameStats?.FrameStats ?? FrameStatsSnapshot.Disabled;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{ctx.Localizer.Get(LocKeys.Settings.Advanced.FrameStats)} · p50 {stats.P50Ms:0.00} ms · p99 {stats.P99Ms:0.00} ms · {stats.Frames} frames");
    }

    /// <summary>Maps the live update state onto the localized About status line and its tone.</summary>
    private static (string Text, Color Color) DescribeUpdate(IUpdateUiService service, ILocalizer localizer)
    {
        var snapshot = service.Current;
        var version = snapshot.AvailableRelease?.Version.ToString() ?? service.CurrentVersion.ToString();
        var size = FormatUpdateSize(snapshot.AvailableRelease?.Installer?.SizeBytes ?? 0);
        return snapshot.State switch
        {
            UpdateState.Checking => (localizer.Get(LocKeys.Settings.About.Update.Checking), Accents.Info),
            UpdateState.UpToDate => (localizer.Get(LocKeys.Settings.About.UpToDate), Accents.Success),
            UpdateState.Available => (localizer.Format(LocKeys.Settings.About.Update.Available,
                ("version", version), ("size", size)), Accents.Warning),
            UpdateState.Downloading => (localizer.Format(LocKeys.Settings.About.Update.Downloading,
                ("version", version)), Accents.Info),
            UpdateState.ReadyToInstall => (localizer.Format(LocKeys.Settings.About.Update.Ready,
                ("version", version)), Accents.Success),
            UpdateState.RestartRequired => (localizer.Get(LocKeys.Settings.About.Update.RestartRequired),
                Accents.Success),
            UpdateState.Failed => (localizer.Format(LocKeys.Settings.About.Update.Failed,
                ("reason", snapshot.Message ?? "unknown")), Accents.Danger),
            _ => (string.Empty, Accents.Info),
        };
    }

    /// <summary>Formats an installer size without inventing units (bytes → MB/KB).</summary>
    private static string FormatUpdateSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "?";
        }

        return bytes >= 1024L * 1024L
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB");
    }

    private static Control AboutLink(string label, SettingsIconKind icon, Action clicked, SettingsPalette palette)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        row.Children.Add(new SettingsIcon
        {
            Kind = icon,
            Brush = palette.SecondaryBrush,
            Width = 10,
            Height = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = SettingsMetrics.LicenseSize,
            Foreground = palette.SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.PointerPressed += (_, e) =>
        {
            clicked();
            e.Handled = true;
        };
        return row;
    }
}
