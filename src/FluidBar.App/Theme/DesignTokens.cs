using Avalonia.Media;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.App.Theme;

/// <summary>Island body colors (spec 3.3, "Obsidian"). The island stays dark in every desktop theme.</summary>
/// <param name="Background">Shell fill base color (applied with fill opacity).</param>
/// <param name="BackgroundRaised">Inset card/track surface.</param>
/// <param name="BackgroundInset">Deeper inset surface (HUD track, code blocks).</param>
/// <param name="Stroke">Hairline key line.</param>
/// <param name="Line">List separator.</param>
/// <param name="LineStrong">Group separator.</param>
/// <param name="Foreground">Primary text/icon color.</param>
/// <param name="ForegroundSecondary">Secondary text.</param>
/// <param name="ForegroundTertiary">Disabled/ghost text.</param>
public readonly record struct IslandColors(
    Color Background,
    Color BackgroundRaised,
    Color BackgroundInset,
    Color Stroke,
    Color Line,
    Color LineStrong,
    Color Foreground,
    Color ForegroundSecondary,
    Color ForegroundTertiary)
{
    /// <summary>Default palette: Obsidian <c>#08080A</c>.</summary>
    public static IslandColors Obsidian { get; } = new(
        Color.Parse("#08080A"),
        Color.Parse("#141418"),
        Color.Parse("#0E0E11"),
        Color.Parse("#1E1E22"),
        Color.Parse("#14FFFFFF"),
        Color.Parse("#1CFFFFFF"),
        Colors.White,
        Color.Parse("#9B9BA5"),
        Color.Parse(TextTokens.TertiaryDark));

    /// <summary>Graphite palette <c>#1C1C1E</c>.</summary>
    public static IslandColors Graphite { get; } = Obsidian with
    {
        Background = Color.Parse("#1C1C1E"),
        BackgroundRaised = Color.Parse("#252529"),
        BackgroundInset = Color.Parse("#17171A"),
        Stroke = Color.Parse("#2A2A30"),
    };

    /// <summary>Slate palette <c>#111721</c>.</summary>
    public static IslandColors Slate { get; } = Obsidian with
    {
        Background = Color.Parse("#111721"),
        BackgroundRaised = Color.Parse("#1A2230"),
        BackgroundInset = Color.Parse("#0D121A"),
        Stroke = Color.Parse("#20293A"),
    };

    /// <summary>Nord palette <c>#2E3440</c>.</summary>
    public static IslandColors Nord { get; } = Obsidian with
    {
        Background = Color.Parse("#2E3440"),
        BackgroundRaised = Color.Parse("#3B4252"),
        BackgroundInset = Color.Parse("#272C36"),
        Stroke = Color.Parse("#3F4859"),
    };

    /// <summary>Resolves a palette by settings token.</summary>
    public static IslandColors ForPalette(string? palette) => palette?.ToLowerInvariant() switch
    {
        "graphite" => Graphite,
        "slate" => Slate,
        "nord" => Nord,
        _ => Obsidian,
    };
}

/// <summary>
/// Settings-window / marketplace surfaces. The prototype ships dark and light ("Porcelain") variants;
/// the island itself never changes (spec 3.3).
/// </summary>
/// <param name="Window">Settings window background.</param>
/// <param name="Surface">Panel surface.</param>
/// <param name="Raised">Raised row/hover surface.</param>
/// <param name="RaisedHover">Stronger hover surface.</param>
/// <param name="Field">Input field surface.</param>
/// <param name="Line">Hairline separator.</param>
/// <param name="LineStrong">Stronger separator.</param>
/// <param name="Foreground">Primary text.</param>
/// <param name="ForegroundSecondary">Secondary text.</param>
/// <param name="ForegroundTertiary">Hint text.</param>
/// <param name="Active">Active selection background.</param>
/// <param name="ActiveForeground">Active selection text.</param>
public readonly record struct AppSurfaceColors(
    Color Window,
    Color Surface,
    Color Raised,
    Color RaisedHover,
    Color Field,
    Color Line,
    Color LineStrong,
    Color Foreground,
    Color ForegroundSecondary,
    Color ForegroundTertiary,
    Color Active,
    Color ActiveForeground)
{
    /// <summary>Dark desktop UI ("Graphite").</summary>
    public static AppSurfaceColors Dark { get; } = new(
        Color.Parse("#08080A"),
        Color.Parse("#99141418"),
        Color.Parse("#16FFFFFF"),
        Color.Parse("#22FFFFFF"),
        Color.Parse("#16FFFFFF"),
        Color.Parse("#17FFFFFF"),
        Color.Parse("#1FFFFFFF"),
        Color.Parse("#F4F4F6"),
        Color.Parse("#9B9BA5"),
        Color.Parse(TextTokens.TertiaryDark),
        Color.Parse("#F5F5F7"),
        Color.Parse("#0B0B0D"));

    /// <summary>Light desktop UI ("Porcelain").</summary>
    public static AppSurfaceColors Light { get; } = new(
        Color.Parse("#F9FAFC"),
        Color.Parse("#99FFFFFF"),
        Color.Parse("#0E111827"),
        Color.Parse("#1611181F"),
        Color.Parse("#0E111827"),
        Color.Parse("#17111827"),
        Color.Parse("#22111827"),
        Color.Parse("#151A21"),
        Color.Parse("#59626F"),
        Color.Parse(TextTokens.TertiaryLight),
        Color.Parse("#1B2028"),
        Colors.White);

    /// <summary>Resolves by settings theme token.</summary>
    public static AppSurfaceColors ForTheme(string? theme)
        => string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? Light : Dark;
}

/// <summary>Semantic accent colors (spec 3.3). One accent per state.</summary>
public static class Accents
{
    /// <summary>Default artwork-accent fallback.</summary>
    public static Color Fallback { get; } = Color.Parse("#4CC9F0");

    /// <summary>Success green.</summary>
    public static Color Success { get; } = Color.Parse("#34C759");

    /// <summary>Warning orange.</summary>
    public static Color Warning { get; } = Color.Parse("#FF9500");

    /// <summary>Danger red.</summary>
    public static Color Danger { get; } = Color.Parse("#FF453A");

    /// <summary>Informational blue.</summary>
    public static Color Info { get; } = Color.Parse("#0A84FF");

    /// <summary>Software-player track accent seen in the prototype.</summary>
    public static Color Track { get; } = Color.Parse("#F06AB0");

    /// <summary>Parses a settings/plugin <c>#RRGGBB</c>/<c>#AARRGGBB</c> accent, falling back safely.</summary>
    public static Color ParseOrDefault(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        return Color.TryParse(hex, out var color) ? color : fallback;
    }
}

/// <summary>UI-side geometry tokens (spec §3.1 plus the lead's uniform-shell decision).</summary>
public static class UiMetrics
{
    /// <summary>Fixed per-monitor host window width in DIP (Core <see cref="LayoutTokens.WindowWidth"/>).</summary>
    public const double WindowWidth = LayoutTokens.WindowWidth;

    /// <summary>Fixed per-monitor host window height in DIP: shell envelope + detached archipelago band (488×384).</summary>
    public static double WindowHeight => HostLayout.WindowHeight;

    /// <summary>Expanded content layer height (440×336: shell + archipelago band).</summary>
    public static double ExpandedLayerHeight => HostLayout.ExpandedLayerHeight;

    /// <summary>Shadow/antialias bleed inside the host window on every side.</summary>
    public const double WindowPadding = LayoutTokens.WindowPadding;

    /// <summary>Expanded shell width (uniform for all six pages).</summary>
    public const double ExpandedWidth = LayoutTokens.ExpandedWidth;

    /// <summary>Expanded shell height (uniform for all six pages; switching pages never resizes).</summary>
    public const double ExpandedHeight = LayoutTokens.ExpandedHeight;

    /// <summary>Legacy rail width token (superseded by the horizontal pager, kept for diagnostics).</summary>
    public const double RailWidth = 48;

    /// <summary>Expanded pager content viewport width (Core constant).</summary>
    public const double PagerViewportWidth = 400;

    /// <summary>Expanded pager viewport horizontal inset.</summary>
    public const double PagerInsetX = 20;

    /// <summary>Expanded pager viewport vertical inset.</summary>
    public const double PagerInsetY = 16;

    /// <summary>Rail item visual size (34; hit area 36).</summary>
    public const double RailItemSize = 34;

    /// <summary>Rail item hit size.</summary>
    public const double RailItemHitSize = 36;

    /// <summary>Rail item gap (8).</summary>
    public const double RailItemGap = 8;

    /// <summary>Rail left padding that centres a 34 DIP item in the 48 DIP rail.</summary>
    public const double RailItemInsetX = (RailWidth - RailItemSize) / 2;

    /// <summary>Icon size inside a rail item.</summary>
    public const double RailIconSize = 18;

    /// <summary>Selected rail item accent bar: 2×16 at x=4.</summary>
    public const double RailIndicatorWidth = 2;

    /// <summary>Selected rail indicator height.</summary>
    public const double RailIndicatorHeight = 16;

    /// <summary>Selected rail indicator left inset.</summary>
    public const double RailIndicatorX = 4;

    /// <summary>Content inset (16) inside the 392 DIP content column.</summary>
    public const double ContentInset = LayoutTokens.ContentInset;

    /// <summary>Vertical page inset (16).</summary>
    public const double PageInsetY = 16;

    /// <summary>Page title row height.</summary>
    public const double PageTitleHeight = 24;

    /// <summary>Row height in list pages.</summary>
    public const double ListRowHeight = 40;

    /// <summary>Dock strip height.</summary>
    public const double DockStripHeight = LayoutTokens.DockStripHeight;

    /// <summary>Dock app tile hit target.</summary>
    public const double DockTileHit = 40;

    /// <summary>Dock app tile visual size.</summary>
    public const double DockTileSize = 36;

    /// <summary>Compact content horizontal inset.</summary>
    public const double CompactInset = LayoutTokens.CompactInset;

    /// <summary>HUD row height.</summary>
    public const double HudHeight = 40;

    /// <summary>Compact media artwork size.</summary>
    public const double MediaArtSize = 28;

    /// <summary>Expanded media artwork size.</summary>
    public const double MediaArtLarge = 64;

    /// <summary>Rail item count.</summary>
    public const int RailItemCount = 6;

    /// <summary>Preferred gap between the dock strip and the panel above it (spec 2.2).</summary>
    public const double DockExpandedGap = LayoutTokens.DockExpandedGap;

    /// <summary>Total stable text slots in expanded pages (no dynamic height).</summary>
    public const double PageRowGap = 8;
}

/// <summary>Typography tokens (spec 3.5).</summary>
public static class Typography
{
    /// <summary>Island title / track title size.</summary>
    public const double TitleSize = 13.5;

    /// <summary>Secondary caption size (the only Normal weight in the island).</summary>
    public const double CaptionSize = 11;

    /// <summary>Page title size.</summary>
    public const double PageTitleSize = 15;

    /// <summary>Dashboard clock size.</summary>
    public const double DashboardClockSize = 34;

    /// <summary>Dashboard date size.</summary>
    public const double DashboardDateSize = 13;

    /// <summary>HUD value size (tabular).</summary>
    public const double HudValueSize = 12.5;

    /// <summary>Badge/tag size.</summary>
    public const double BadgeSize = 12;

    /// <summary>Section caption size.</summary>
    public const double SectionCaptionSize = 11;

    /// <summary>Compact clock size.</summary>
    public const double CompactClockSize = 13.5;

    /// <summary>Root settings-window body size.</summary>
    public const double SettingsBodySize = 13;

    /// <summary>Settings-window section title size.</summary>
    public const double SettingsSectionSize = 14;

    /// <summary>Settings-window caption size.</summary>
    public const double SettingsCaptionSize = 12;

    /// <summary>CJK-safe explicit line height factor (spec 3.5: +4 %).</summary>
    public const double LineHeightFactor = 1.24;

    /// <summary>Letter spacing for uppercase section captions (0.04 em).</summary>
    public const double CaptionLetterSpacing = 0.04;
}

/// <summary>Non-spring durations exposed to UI code (spec 4.6).</summary>
public static class UiDurations
{
    /// <summary>Content fade-in (ms).</summary>
    public const int FadeIn = 180;

    /// <summary>Content fade-out (ms).</summary>
    public const int FadeOut = 120;

    /// <summary>Cross-fade between replaced content / pages (ms).</summary>
    public const int CrossFade = 220;

    /// <summary>Hover intent delay (ms).</summary>
    public const int HoverIntent = 90;

    /// <summary>Leave debounce (ms).</summary>
    public const int LeaveDebounce = 120;

    /// <summary>Material swap cross-fade (ms).</summary>
    public const int MaterialSwap = 180;

    /// <summary>Hard animation ceiling (ms).</summary>
    public static int MaxAnimation => MotionDurations.Default.MaxAnimationMs;
}

/// <summary>Brushes shared by custom-drawn island content (immutable, cached).</summary>
public static class IslandBrushes
{
    /// <summary>Foreground brush for the Obsidian island.</summary>
    public static readonly IBrush Foreground = new SolidColorBrush(Colors.White);

    /// <summary>Secondary text brush.</summary>
    public static readonly IBrush Secondary = new SolidColorBrush(Color.Parse("#9B9BA5"));

    /// <summary>Tertiary text brush.</summary>
    public static readonly IBrush Tertiary = new SolidColorBrush(Color.Parse(TextTokens.TertiaryDark));

    /// <summary>Default accent brush (artwork fallback).</summary>
    public static readonly IBrush Accent = new SolidColorBrush(Accents.Fallback);

    /// <summary>Danger brush.</summary>
    public static readonly IBrush Danger = new SolidColorBrush(Accents.Danger);

    /// <summary>Success brush.</summary>
    public static readonly IBrush Success = new SolidColorBrush(Accents.Success);

    /// <summary>Info brush.</summary>
    public static readonly IBrush Info = new SolidColorBrush(Accents.Info);

    /// <summary>Warning brush.</summary>
    public static readonly IBrush Warning = new SolidColorBrush(Accents.Warning);
}

