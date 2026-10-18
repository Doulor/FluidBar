using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

/// <summary>
/// Visual tokens for the spike, copied 1:1 from the v2 spec §3 / App <c>Theme/DesignTokens.cs</c>.
/// Values are DIP at 96 DPI. The spike deliberately does not reference FluidBar.App (no Avalonia),
/// so the token table is duplicated here and guarded by the comparison notes in RESULTS.md.
/// </summary>
internal static class Tokens
{
    // ---- geometry (spec 3.1, Core LayoutTokens) ----
    public const double CompactIdleWidth = 132;
    public const double CompactIdleHeight = 36;
    public const double CompactMediaWidth = 226;
    public const double CompactMediaHeight = 40;
    public const double CompactInset = 12;
    public const double MediaInset = 10;
    public const double MediaArtSize = 28;

    public const double ExpandedWidth = 440;
    public const double ExpandedHeight = 284;
    public const double WindowPadding = 24;
    public const double WindowWidth = 488;
    public const double WindowHeight = 332;

    public const double ContentInsetX = 20;
    public const double ContentInsetY = 16;
    public const double PageWidth = 400;
    public const double PageHeight = 252;
    public const double PageInset = 16;
    public const double UsableWidth = PageWidth - (2 * PageInset);

    public const double ExpandedRadius = 28;
    public const double CardRadius = 12;
    public const double ArtLargeRadius = 12;
    public const double ArtSmallRadius = 8;
    public const double Hairline = 1;

    public const double ArchipelagoGap = 10;
    public const double ArchipelagoBottomMargin = 8;
    public const double ArchipelagoExtraBottom = 52;
    public const double ExpandedLayerHeight = ExpandedHeight + ArchipelagoExtraBottom;
    public const double WindowHeightWithStrip = WindowHeight + ArchipelagoExtraBottom;

    public const double MiniWidth = 48;
    public const double MiniHeight = 26;
    public const double ActiveMiniWidth = 72;
    public const double ActiveMiniHeight = 30;
    public const double MiniGap = 8;
    public const double MiniRadius = 13;
    public const double StripHeight = 34;

    // ---- typography (spec 3.5) ----
    public const float TitleSize = 13.5f;
    public const float CaptionSize = 11f;
    public const float PageTitleSize = 15f;
    public const float DashboardClockSize = 34f;
    public const float DashboardDateSize = 13f;
    public const float HudValueSize = 12.5f;
    public const float BadgeSize = 12f;
    public const float CompactClockSize = 13.5f;
    public const float MiniLabelSize = 10.5f;
    public const double CjkLineHeightFactor = 1.24;

    // ---- colours (spec 3.3, Obsidian) ----
    public static readonly SKColor Background = SKColor.Parse("#08080A");
    public static readonly SKColor BackgroundRaised = SKColor.Parse("#141418");
    public static readonly SKColor BackgroundInset = SKColor.Parse("#0E0E11");
    public static readonly SKColor Stroke = SKColor.Parse("#1E1E22");
    public static readonly SKColor Line = SKColor.Parse("#14FFFFFF");
    public static readonly SKColor LineStrong = SKColor.Parse("#1CFFFFFF");
    public static readonly SKColor Foreground = SKColor.Parse("#FFFFFF");
    public static readonly SKColor ForegroundSecondary = SKColor.Parse("#9B9BA5");
    public static readonly SKColor ForegroundTertiary = SKColor.Parse("#6E6E78");
    public static readonly SKColor Accent = SKColor.Parse("#4CC9F0");
    public static readonly SKColor TrackAccent = SKColor.Parse("#F06AB0");
    public static readonly SKColor Success = SKColor.Parse("#34C759");
    public static readonly SKColor Warning = SKColor.Parse("#FF9500");
    public static readonly SKColor Danger = SKColor.Parse("#FF453A");
    public static readonly SKColor Info = SKColor.Parse("#0A84FF");
    public static readonly SKColor ArtEnd = SKColor.Parse("#1A1A22");
    public static readonly SKColor ArtEndLarge = SKColor.Parse("#181820");
    public static readonly SKColor HudTrack = SKColor.Parse("#23232A");

    public const double FillOpacity = 0.96;
    public const double ShadowOpacity = 0.35;
    public const double ShadowOffsetY = 8;
    public const double ShadowBlur = 24;

    // ---- page content layout (App HomePage/MediaPage, no-rail A1 revision) ----
    public const double HomeClockTop = 22;
    public const double HomeDateTop = 68;
    public const double HomeGreetingTop = 88;
    public const double HomeCardsX = 200;
    public const double HomeCardsTop = 26;
    public const double HomeCardWidth = 184;
    public const double HomeCardHeight = 32;
    public const double HomeCardPitch = 40;
    public const double HomeQuickTop = 140; // production uses 136; +4 avoids the 2 DIP card overlap
    public const double HomeQuickHeight = 82;
    public const double HomeQuickGap = 8;

    public const double MediaArtTop = 24;
    public const double MediaTitleX = 96;
    public const double MediaTitleTop = 22;
    public const double MediaArtistTop = 44;
    public const double MediaAlbumTop = 64;
    public const double MediaTrackTop = 96;
    public const double MediaTimeTop = 102;
    public const double MediaTransportTop = 108;
    public const double MediaLyricTop = 164;

    public static readonly string[] PageTitlesEn =
    [
        "Control center", "Now playing", "Notifications", "Clipboard", "Agent activity", "Widgets",
    ];

    public static readonly string[] PageTitlesZh =
    [
        "控制中心", "正在播放", "通知", "剪贴板", "代理活动", "小组件",
    ];
}

/// <summary>Localized copy used by the spike (subset of en-US.json / zh-Hans.json).</summary>
internal sealed class Copy
{
    public required string IdleClock { get; init; }
    public required string Battery { get; init; }
    public required string MediaTitle { get; init; }
    public required string MediaArtist { get; init; }
    public required string MediaAlbum { get; init; }
    public required string HomeTitle { get; init; }
    public required string HomeDate { get; init; }
    public required string Greeting { get; init; }
    public required string BatteryLabel { get; init; }
    public required string SystemLabel { get; init; }
    public required string FocusLabel { get; init; }
    public required string FocusOff { get; init; }
    public required string Network { get; init; }
    public required string QuickSettings { get; init; }
    public required string QuickMarket { get; init; }
    public required string QuickHide { get; init; }
    public required string MediaPageTitle { get; init; }
    public required string Position { get; init; }
    public required string Duration { get; init; }
    public required string Lyric { get; init; }
    public required string NextLyric { get; init; }
    public required string[] PageTitles { get; init; }

    public static Copy En { get; } = new()
    {
        IdleClock = "19:16",
        Battery = "82%",
        MediaTitle = "Midnight City",
        MediaArtist = "M83",
        MediaAlbum = "Hurry Up, We're Dreaming",
        HomeTitle = "Control center",
        HomeDate = "Sunday, October 18",
        Greeting = "Good evening",
        BatteryLabel = "Battery",
        SystemLabel = "System",
        FocusLabel = "Focus",
        FocusOff = "Focus off",
        Network = "Home Wi-Fi",
        QuickSettings = "Settings",
        QuickMarket = "Plugin market",
        QuickHide = "Hide island",
        MediaPageTitle = "Now playing",
        Position = "1:52",
        Duration = "4:03",
        Lyric = "Waiting for a car to drive me home",
        NextLyric = "The city is my church",
        PageTitles = Tokens.PageTitlesEn,
    };

    public static Copy Zh { get; } = new()
    {
        IdleClock = "19:16",
        Battery = "82%",
        MediaTitle = "午夜城市",
        MediaArtist = "M83",
        MediaAlbum = "快一点，我们正在做梦",
        HomeTitle = "控制中心",
        HomeDate = "星期日, 十月 18",
        Greeting = "晚上好",
        BatteryLabel = "电池",
        SystemLabel = "系统",
        FocusLabel = "专注",
        FocusOff = "专注已关闭",
        Network = "Home Wi-Fi",
        QuickSettings = "设置",
        QuickMarket = "插件市场",
        QuickHide = "隐藏灵动岛",
        MediaPageTitle = "正在播放",
        Position = "1:52",
        Duration = "4:03",
        Lyric = "等一辆车带我回家",
        NextLyric = "这座城市是我的教堂",
        PageTitles = Tokens.PageTitlesZh,
    };
}
