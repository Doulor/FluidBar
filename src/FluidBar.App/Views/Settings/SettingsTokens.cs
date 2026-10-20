using Avalonia.Media;
using FluidBar.App.Settings;
using FluidBar.App.Theme;
using FluidBar.Core.Layout;

namespace FluidBar.App.Views.Settings;

/// <summary>
/// Prototype metrics, transcribed 1:1 from docs/rewrite/prototype2/styles.css (the settings block:
/// <c>.settings-window</c> … <c>.market-footnote</c>). Every number here is a CSS pixel value from the
/// authoritative prototype; nothing is rounded or "tuned by eye".
/// </summary>
internal static class SettingsMetrics
{
    // ---- window chrome (styles.css 2285-2349) ------------------------------------------------
    public const double WindowWidth = 900;
    public const double WindowHeight = 620;
    public const double WindowRadius = 22;
    public const double WindowBorder = 1;

    public const double TitleBarHeight = 50;
    public const double TitleBarPadLeft = 18;
    public const double TitleBarPadRight = 12;
    public const double TitleBarGap = 10;
    public const double MarkSize = 24;
    public const double MarkRadius = 8;
    public const double MarkIcon = 13;
    public const double TitleSize = 13;
    public const double TitleTracking = 0.005;
    public const double SubSize = 9;
    public const double SubTracking = 0.09;
    public const double CloseSize = 32;
    public const double CloseRadius = 8;
    public const double CloseIcon = 14;

    // ---- navigation (2351-2409) --------------------------------------------------------------
    public const double NavWidth = 208;
    public const double NavPadY = 14;
    public const double NavPadX = 10;
    public const double NavGap = 2;
    public const double NavItemHeight = 40;
    public const double NavItemRadius = 8;
    public const double NavItemPadX = 10;
    public const double NavItemGap = 9;
    public const double NavIcon = 18;
    public const double NavLabelSize = 13;
    public const double NavCountHeight = 16;
    public const double NavCountRadius = 5;
    public const double NavCountPadX = 4;
    public const double NavCountSize = 8.5;
    public const double KickerPadTop = 4;
    public const double KickerPadX = 10;
    public const double KickerPadBottom = 7;
    public const double KickerSize = 8.5;
    public const double KickerTracking = 0.13;
    public const double VersionPadTop = 12;
    public const double VersionPadX = 10;
    public const double VersionPadBottom = 4;
    public const double VersionSize = 8.5;
    public const double VersionLineHeight = 1.5;

    // ---- pane & typography (2411-2462) -------------------------------------------------------
    public const double PanePadX = 30;
    public const double PanePadY = 24;
    /// <summary><c>.settings-pane::-webkit-scrollbar</c> width.</summary>
    public const double PaneScrollBarWidth = 6;

    /// <summary>Gutter Chrome reserves for the prototype's overlay scrollbar (measured on shot-11).</summary>
    public const double PaneScrollBarGutter = 9;
    public const double HeadSize = 22;
    public const double HeadLineHeight = 1.2;
    public const double HeadTracking = -0.02;
    public const double HeadMarginBottom = 4;
    public const double DescSize = 11;
    public const double DescLineHeight = 1.55;
    public const double DescMarginBottom = 18;
    public const double DescMaxWidth = 560;
    public const double CaptionSize = 11;
    public const double CaptionTracking = 0.036; // 11 x 0.036 ~= +0.4 DIP letter-spacing
    public const double CaptionMarginTop = 20;
    public const double CaptionMarginBottom = 2;
    public const double RowMinHeight = 53;
    public const double RowPadY = 8;
    public const double RowGap = 18;
    public const double RowTitleSize = 13;

    /// <summary>Row description line ("说明") size.</summary>
    public const double RowDescriptionSize = 11.5;

    /// <summary>Standard settings row height.</summary>
    public const double RowHeight = 48;
    public const double RowTitleLineHeight = 1.35;
    public const double RowDescSize = 10;
    public const double RowDescLineHeight = 1.45;
    public const double RowDescMarginTop = 3;
    public const double ControlGap = 8;

    // ---- controls (2464-2688) ---------------------------------------------------------------
    public const double SegmentedWidth = 188;
    public const double SegmentedSmallWidth = 148;
    public const double SegmentedPad = 2;
    public const double SegHeight = 26;
    public const double SegRadius = 8;
    public const double SegItemRadius = 6;
    public const double SegGap = 2;
    public const double SegFontSize = 9.5;

    public const double SwitchWidth = 38;
    public const double SwitchHeight = 22;
    public const double SwitchKnob = 16;
    public const double SwitchInset = 2;

    public const double SwatchSize = 24;
    public const double SwatchRingInset = 4;
    public const double AccentGap = 8;

    public const double RangeWidth = 200;
    public const double RangeGap = 10;
    public const double ValueTagMinWidth = 48;
    public const double ValueTagHeight = 24;
    public const double ValueTagPadX = 7;
    public const double ValueTagRadius = 6;
    public const double ValueTagSize = 9.5;
    public const double TrackHeight = 3;
    public const double ThumbSize = 10;

    public const double ShortcutRowHeight = 39;
    public const double ShortcutFontSize = 11;
    public const double KeycapHeight = 22;
    public const double KeycapMinWidth = 22;
    public const double KeycapPadX = 6;
    public const double KeycapRadius = 5;
    public const double KeycapFontSize = 8.5;
    public const double KeycapGap = 3;

    // ---- appearance preview (2510-2615) -----------------------------------------------------
    public const double PreviewMarginTop = 16;
    public const double PreviewPadTop = 12;
    public const double ApHeadMarginBottom = 6;
    public const double ApHeadSize = 8.5;
    public const double ApHeadTracking = 0.11;
    public const double ApMetaSize = 8.5;
    public const double ApCanvasHeight = 84;
    public const double ApCanvasRadius = 12;
    public const double ApShellWidth = 300;
    public const double ApShellHeight = 62;
    public const double ApShellPadX = 11;
    public const double ApShellGap = 10;
    public const double ApArtSize = 34;
    public const double ApArtRadius = 8;
    public const double ApClockSize = 18;
    public const double ApTrackSize = 8.5;
    public const double ApPlaySize = 25;

    // ---- plugin list / marketplace (2689-3023) ----------------------------------------------
    public const double SearchHeight = 34;
    public const double SearchRadius = 8;
    public const double SearchPadX = 10;
    public const double SearchGap = 8;
    public const double SearchIcon = 14;
    public const double SearchFontSize = 11;

    public const double PluginListGap = 6;
    public const double PluginRowPadX = 11;
    public const double PluginRowPadY = 10;
    public const double PluginRowGap = 11;
    public const double PluginRowRadius = 10;
    public const double PluginIcon = 36;
    public const double PluginIconRadius = 10;
    public const double PluginGlyph = 19;
    public const double PluginNameSize = 12;
    public const double PluginVersionSize = 9;
    public const double PluginMetaSize = 9;
    public const double BadgeHeight = 16;
    public const double BadgeRadius = 4;
    public const double BadgePadX = 6;
    public const double BadgeSize = 8.5;
    public const double BadgeTracking = 0.05;
    public const double PermHeight = 16;
    public const double PermPadX = 5;
    public const double PermRadius = 4;
    public const double PermSize = 8.5;
    public const double PermGap = 3;
    public const double IconButton = 24;
    /// <summary><c>.icon-btn</c> corner radius.</summary>
    public const double IconButtonRadius = 6;
    public const double IconButtonGlyph = 12;
    public const double PluginSwitchWidth = 30;
    public const double PluginSwitchHeight = 18;
    public const double PluginSwitchKnob = 12;

    public const double OpenFolderHeight = 30;

    public const double FeaturedMinHeight = 122;
    public const double FeaturedPad = 18;
    public const double FeaturedGap = 16;
    public const double FeaturedRadius = 14;
    public const double FeaturedKickerSize = 8.5;
    public const double FeaturedKickerTracking = 0.16;
    public const double FeaturedTitleSize = 20;
    public const double FeaturedDescSize = 10;
    public const double FeaturedDescMaxWidth = 400;
    public const double FeaturedActionsMarginTop = 11;
    public const double FeaturedInstallHeight = 28;
    public const double FeaturedInstallPadX = 12;
    public const double FeaturedMetaSize = 9;
    public const double FeaturedArt = 80;
    public const double FeaturedArtRadius = 19;
    public const double FeaturedArtGlyph = 36;

    public const double MarketToolbarMarginTop = 12;
    public const double MarketCatsMarginTop = 9;
    public const double MarketCatsMarginBottom = 10;
    public const double CatChipHeight = 26;
    public const double CatChipPadX = 10;
    public const double CatChipRadius = 7;
    public const double CatChipSize = 9.5;
    public const double CatGap = 4;
    public const double MarketGridGap = 9;
    public const double MarketCardMinHeight = 112;
    public const double MarketCardPad = 12;
    public const double MarketCardRadius = 10;
    public const double MarketCardGap = 6;
    public const double MarketIcon = 34;
    public const double MarketIconRadius = 9;
    public const double MarketGlyph = 18;
    public const double MarketNameSize = 11.5;
    public const double MarketAuthorSize = 9;
    public const double MarketDescSize = 9.5;
    public const double MarketFootGap = 6;
    public const double MarketRatingSize = 9;
    public const double MarketDownloadsSize = 8.5;
    public const double MarketInstallHeight = 27;
    public const double MarketInstallMinWidth = 58;
    public const double MarketInstallPadX = 9;
    public const double MarketInstallRadius = 7;
    public const double MarketInstallSize = 9.5;
    public const double MarketFootnoteMarginTop = 14;
    public const double MarketFootnoteSize = 9.5;
    public const double MarketFootnoteIcon = 10;

    // ---- unified control layer (task-22): one source of truth for every element -----------------
    /// <summary>Shared button radius (all variants).</summary>
    public const double ButtonRadius = 7;

    /// <summary>Button heights by size token.</summary>
    public const double ButtonXs = 27;
    public const double ButtonSm = 28;
    public const double ButtonMd = 30;
    public const double ButtonLg = 32;

    /// <summary>Button horizontal padding by size token.</summary>
    public const double ButtonPadXs = 9;
    public const double ButtonPadSm = 11;
    public const double ButtonPadMd = 10;
    public const double ButtonPadLg = 12;

    /// <summary>Button label sizes by size token.</summary>
    public const double ButtonSizeXs = 9.5;
    public const double ButtonSizeSm = 9.5;
    public const double ButtonSizeMd = 9.5;
    public const double ButtonSizeLg = 10.5;

    /// <summary>Dropdown (select) metrics for plugin settings pages.</summary>
    public const double SelectHeight = 32;
    public const double SelectRadius = 8;
    public const double SelectPadX = 10;
    public const double SelectMinWidth = 160;
    public const double SelectFontSize = 11;

    /// <summary>Install/download progress bar.</summary>
    public const double ProgressHeight = 4;

    /// <summary>Inline status line / empty state type sizes.</summary>
    public const double StatusSize = 9.5;


    // ---- advanced / about / footer (3025-3160) ----------------------------------------------
    public const double PathFieldHeight = 34;
    public const double PathFieldRadius = 7;
    public const double PathFieldSize = 9.5;
    public const double PathIcon = 12;
    public const double AdvActionsGap = 5;
    public const double AdvActionsMarginTop = 10;
    public const double SmallActionHeight = 30;
    public const double SmallActionPadX = 10;
    public const double SmallActionRadius = 7;
    public const double SmallActionSize = 9.5;
    public const double SmallActionIcon = 11;
    public const double AboutMark = 72;
    public const double AboutMarkRadius = 20;
    public const double AboutMarkGlyph = 34;
    public const double AboutNameSize = 23;
    public const double AboutVersionSize = 10;
    public const double AboutBtnHeight = 32;
    public const double AboutBtnPadX = 12;
    public const double AboutBtnSize = 10.5;
    public const double LicenseSize = 9.5;
    public const double ContribHeight = 24;
    public const double ContribPadX = 10;
    public const double ContribRadius = 12;
    public const double ContribSize = 9.5;
    public const double FooterNoteMarginTop = 18;
    public const double FooterNotePadTop = 10;
    public const double FooterNoteSize = 9.5;
}

/// <summary>
/// Prototype settings surfaces: <c>:root</c> (dark) and <c>body[data-theme="light"]</c> (light) blocks of
/// docs/rewrite/prototype2/styles.css. The settings window is the only consumer, so it owns the literal
/// prototype tokens instead of re-using the island palette.
/// </summary>
public sealed class SettingsPalette
{
    private SettingsPalette(AppTheme theme)
    {
        IsLight = theme == AppTheme.Light;
        if (IsLight)
        {
            // Material L0 (container) / L1 (card, 60 % overlay, radius 12) / L2 (separator, 9 %).
            Window = Color.Parse("#F9FAFC");
            Raised = Color.FromArgb(153, 255, 255, 255);
            RaisedHover = Color.FromArgb(179, 255, 255, 255);
            Field = Color.FromArgb(9, 17, 24, 39);
            Line = Color.FromArgb(23, 17, 24, 39);
            LineStrong = Color.FromArgb(31, 17, 24, 39);
            Foreground = Color.Parse("#151A21");
            Secondary = Color.Parse("#59626F");
            Tertiary = Color.Parse(TextTokens.TertiaryLight);
            Active = Color.Parse("#1B2028");
            ActiveForeground = Colors.White;
        }
        else
        {
            // Material L0 (near-black container) / L1 (#141418 at 60 %, radius 12) / L2 (white 9 %).
            Window = Color.Parse("#08080A");
            Raised = Color.FromArgb(153, 20, 20, 24);
            RaisedHover = Color.FromArgb(179, 24, 24, 30);
            Field = Color.FromArgb(9, 255, 255, 255);
            Line = Color.FromArgb(23, 255, 255, 255);
            LineStrong = Color.FromArgb(31, 255, 255, 255);
            Foreground = Color.Parse("#F4F4F6");
            Secondary = Color.Parse("#9B9BA5");
            Tertiary = Color.Parse(TextTokens.TertiaryDark);
            Active = Color.Parse("#F5F5F7");
            ActiveForeground = Color.Parse("#0B0B0D");
        }

        // Material L3 top micro-highlight (white 5.1 %, 1.5 DIP fading to transparent) and
        // L4 pressed inset (black 18 %, top inset 8 DIP, sigma 4).
        // Material L3 highlight colour: white 5.1 % (#0DFFFFFF) in both themes, per the shared tokens.
        var highlightTop = Color.FromArgb(13, 255, 255, 255);
        TopHighlightBrush = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new Avalonia.Media.GradientStop(highlightTop, 0),
                new Avalonia.Media.GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
            },
        };
        PressedInsetColor = Color.FromArgb(46, 0, 0, 0);
        WindowBrush = new SolidColorBrush(Window);
        RaisedBrush = new SolidColorBrush(Raised);
        RaisedHoverBrush = new SolidColorBrush(RaisedHover);
        FieldBrush = new SolidColorBrush(Field);
        LineBrush = new SolidColorBrush(Line);
        LineStrongBrush = new SolidColorBrush(LineStrong);
        ForegroundBrush = new SolidColorBrush(Foreground);
        SecondaryBrush = new SolidColorBrush(Secondary);
        TertiaryBrush = new SolidColorBrush(Tertiary);
        ActiveBrush = new SolidColorBrush(Active);
        ActiveForegroundBrush = new SolidColorBrush(ActiveForeground);
    }

    private SettingsPalette(Color window, Color raised, Color raisedHover, Color field, Color line,
        Color lineStrong, Color foreground, Color secondary, Color tertiary, Color active, Color activeForeground)
    {
        Window = window;
        Raised = raised;
        RaisedHover = raisedHover;
        Field = field;
        Line = line;
        LineStrong = lineStrong;
        Foreground = foreground;
        Secondary = secondary;
        Tertiary = tertiary;
        Active = active;
        ActiveForeground = activeForeground;
        WindowBrush = new SolidColorBrush(window);
        RaisedBrush = new SolidColorBrush(raised);
        RaisedHoverBrush = new SolidColorBrush(raisedHover);
        FieldBrush = new SolidColorBrush(field);
        LineBrush = new SolidColorBrush(line);
        LineStrongBrush = new SolidColorBrush(lineStrong);
        ForegroundBrush = new SolidColorBrush(foreground);
        SecondaryBrush = new SolidColorBrush(secondary);
        TertiaryBrush = new SolidColorBrush(tertiary);
        ActiveBrush = new SolidColorBrush(active);
        ActiveForegroundBrush = new SolidColorBrush(activeForeground);
    }

    /// <summary>Resolves the palette for a settings theme.</summary>
    public static SettingsPalette For(AppTheme theme) => new(theme);

    /// <summary>Adapts a legacy <see cref="AppSurfaceColors"/> snapshot (tests / design-time callers).</summary>
    public static SettingsPalette FromSurfaces(AppSurfaceColors surfaces)
        => new(surfaces.Window, surfaces.Raised, surfaces.RaisedHover, surfaces.Field, surfaces.Line,
            surfaces.LineStrong, surfaces.Foreground, surfaces.ForegroundSecondary, surfaces.ForegroundTertiary,
            surfaces.Active, surfaces.ActiveForeground);

    /// <summary>True for the light ("Porcelain") variant.</summary>
    public bool IsLight { get; }

    public Color Window { get; }
    public Color Raised { get; }
    public Color RaisedHover { get; }
    public Color Field { get; }
    public Color Line { get; }
    public Color LineStrong { get; }
    public Color Foreground { get; }
    public Color Secondary { get; }
    public Color Tertiary { get; }
    public Color Active { get; }
    public Color ActiveForeground { get; }

    public IBrush WindowBrush { get; }
    public IBrush RaisedBrush { get; }
    public IBrush RaisedHoverBrush { get; }
    public IBrush FieldBrush { get; }
    public IBrush LineBrush { get; }
    public IBrush LineStrongBrush { get; }
    public IBrush ForegroundBrush { get; }
    public IBrush SecondaryBrush { get; }
    public IBrush TertiaryBrush { get; }
    public IBrush ActiveBrush { get; }
    public IBrush ActiveForegroundBrush { get; }

    /// <summary>Material L3: top micro-highlight gradient (white 5.1 % -> transparent).</summary>
    public IBrush TopHighlightBrush { get; }

    /// <summary>Material L4: pressed inset colour (black 18 %).</summary>
    public Color PressedInsetColor { get; }

    /// <summary>Material L3 highlight height in DIP.</summary>
    public const double TopHighlightHeightDip = 1.5;

    /// <summary>Material L4 pressed inset: top offset in DIP.</summary>
    public const double PressedInsetTopDip = 8;

    /// <summary>Material L4 pressed inset: blur sigma in DIP.</summary>
    public const double PressedInsetSigmaDip = 4;

    /// <summary>Material L1 card corner radius in DIP.</summary>
    public const double CardCornerRadiusDip = 12;

    /// <summary>
    /// Projects this palette onto the material surface record so <c>AppSurfaceMaterial.Validate</c> can
    /// self-check the settings surfaces (L0 container, L1 card, L2 separator) without touching the theme
    /// palette owned by the theme line.
    /// </summary>
    /// <returns>The surfaces consumed by the material self-check.</returns>
    public Theme.AppSurfaceColors ToSurfaces() => new(
        Window, Raised, Raised, RaisedHover, Field, Line, LineStrong,
        Foreground, Secondary, Tertiary, Active, ActiveForeground);

    /// <summary>Prototype <c>--ui-active</c> chip used by the marketplace install button.</summary>
    public IBrush InstallBrush => ActiveBrush;

    /// <summary>Prototype <c>.sw-mark</c> / <c>.about-mark</c> tile.</summary>
    public Color Mark => Color.Parse("#070709");
}

