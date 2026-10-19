using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>Shared states and PNG helpers for the island renderer tests.</summary>
internal static class IslandTestFixtures
{
    public const float Scale = 1.5f;

    /// <summary>Every stored golden case (kept in one place so edge/regression tests cannot drift).</summary>
    public static string[] GoldenNames { get; } =
    [
        "01-compact-idle",
        "02-compact-media",
        "03-compact-hud",
        "04-compact-notification",
        "05-compact-clipboard",
        "06-compact-agent",
        "07-compact-timer",
        "08-compact-battery",
        "10-expanded-home",
        "11-expanded-media",
        "12-expanded-page2-strip",
        "13-expanded-light-home",
        "14-expanded-dock-media",
        "15-expanded-home-transition",
        "16-expanded-flush-home",
        "17-expanded-notifications",
        "18-expanded-clipboard",
        "19-expanded-agent",
        "20-expanded-widgets",
        "21-rail-page0-home",
        "22-rail-page1-media",
        "23-rail-page2-notifications",
        "24-rail-page3-clipboard",
        "25-rail-page4-agent",
        "26-rail-page5-widgets",
    ];

    /// <summary>Short rail labels exactly as the shipped zh-Hans catalog renders them.</summary>
    public static string[] ZhShortTitles { get; } = ["主页", "媒体", "通知", "剪贴板", "代理", "小组件"];

    public static IslandTheme Theme => IslandTheme.Dark;

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string ReportDir => Path.Combine(RepoRoot, "build", "reports", "island-mvp");

    public static string GoldenDir => Path.Combine(RepoRoot, "tests", "FluidBar.Island.Tests", "golden");

    public static IslandRenderState Idle() => Compact(new CompactActivity(
        ActivityKind.Idle,
        new CompactModel { ClockText = "19:16", DateText = "Sun, Oct 18" }));

    public static IslandRenderState MediaCompact() => Compact(new CompactActivity(
        ActivityKind.Media,
        new CompactModel
        {
            Title = "Midnight City",
            Subtitle = "M83",
            IsActive = true,
            ProgressPercent = 46,
            AccentHex = "#F06AB0",
        }));

    public static IslandRenderState Hud() => Compact(new CompactActivity(
        ActivityKind.Volume,
        new CompactModel { Title = "Volume", Level = 62, Badge = "62%" }));

    public static IslandRenderState Notification() => Compact(new CompactActivity(
        ActivityKind.Notification,
        new CompactModel { AppName = "Teams", Title = "Design review in 5 minutes", UnreadCount = 3 }));

    public static IslandRenderState Clipboard() => Compact(new CompactActivity(
        ActivityKind.Clipboard,
        new CompactModel { Body = "https://fluidbar.app/docs/rewrite", Badge = "Copied" }));

    public static IslandRenderState Agent() => Compact(new CompactActivity(
        ActivityKind.Agent,
        new CompactModel
        {
            Title = "Refactor island renderer",
            StatusText = "Running",
            DurationText = "2m",
            Status = CompactStatus.Info,
        }));

    public static IslandRenderState Timer() => Compact(new CompactActivity(
        ActivityKind.Timer,
        new CompactModel { TimerText = "24:36", StatusText = "Focus", ProgressPercent = 8 }));

    public static IslandRenderState Battery() => Compact(new CompactActivity(
        ActivityKind.BatteryLow,
        new CompactModel { BatteryPercent = 12, IsCharging = false, StatusText = "Low battery" }));

    public static IslandRenderState Compact(CompactActivity activity, IslandMode mode = IslandMode.Float)
        => IslandRenderState.ForCompact(mode, activity, Theme);

    public static ExpandedPageContent Content() => new()
    {
        Home = new HomePageModel
        {
            ClockText = "14:32",
            DateText = "Sun, Oct 18",
            WeatherTemp = "18°",
            WeatherCondition = "Partly sunny",
            Toggles =
            [
                new HomeToggleModel(IconKind.Wifi, "Wi-Fi", true),
                new HomeToggleModel(IconKind.Bluetooth, "Bluetooth", false),
                new HomeToggleModel(IconKind.Moon, "Focus", false),
                new HomeToggleModel(IconKind.Moon, "Dark", true),
            ],
            NowPlaying = new MediaSummaryModel
            {
                Title = "Midnight City",
                Subtitle = "M83 · Hurry Up, We're Dreaming",
                ProgressPercent = 46,
                IsPlaying = true,
                AccentHex = "#F06AB0",
            },
            Volume = 62,
            Brightness = 78,
            BatteryPercent = 86,
            IsCharging = true,
            BatteryStatus = "Plugged in",
            BatteryRemaining = "4h left",
        },
        Media = new MediaPageModel
        {
            HasSession = true,
            Eyebrow = "NOW PLAYING",
            Title = "Midnight City",
            Artist = "M83",
            Album = "Hurry Up, We're Dreaming · 2026",
            LyricLine = "Waiting for a car to drive me home",
            NextLyricLine = "The city is my church",
            NextLyricLabel = "NEXT",
            PositionText = "1:52",
            DurationText = "2:11",
            ProgressPercent = 46,
            IsPlaying = true,
            AccentHex = "#F06AB0",
            OutputName = "Speakers",
            QueueCount = 3,
        },
        Notifications = new NotificationsPageModel
        {
            Items =
            [
                new NotificationItemModel
                {
                    AppName = "Teams",
                    Title = "Design review moved to 15:00",
                    Body = "Room A · bring the latest motion walkthrough.",
                    TimeText = "now",
                    AccentHex = "#2F6FED",
                    Icon = IconKind.Chat,
                },
                new NotificationItemModel
                {
                    AppName = "Outlook",
                    Title = "Build finished · prototype2",
                    Body = "All screenshots exported, console is clean.",
                    TimeText = "3 min",
                    AccentHex = "#0A84FF",
                    Icon = IconKind.Bell,
                },
                new NotificationItemModel
                {
                    AppName = "GitHub",
                    Title = "PR #128 awaits review",
                    Body = "pager: stabilise wheel and scroll chaining.",
                    TimeText = "12 min",
                    AccentHex = "#3A3F4A",
                    Icon = IconKind.Document,
                    VerificationCode = "1284",
                },
                new NotificationItemModel
                {
                    AppName = "Calendar",
                    Title = "15:00 design walkthrough",
                    Body = "Starts in 24 minutes, Room A.",
                    TimeText = "25 min",
                    AccentHex = "#34C759",
                    Icon = IconKind.Calendar,
                },
                new NotificationItemModel
                {
                    AppName = "FluidBar Store",
                    Title = "Update available · 2.0.1",
                    Body = "Restart the island to apply.",
                    TimeText = "1 hr",
                    AccentHex = "#4CC9F0",
                    Icon = IconKind.Store,
                },
            ],
            SelectedIndex = 0,
            HoverIndex = 1,
        },
        Clipboard = new ClipboardPageModel
        {
            Items =
            [
                new ClipboardItemModel
                {
                    Preview = "npm run build -- --mode production",
                    Kind = ClipboardContentKind.Code,
                    IsMonospace = true,
                    IsPinned = true,
                    TimeText = "now",
                },
                new ClipboardItemModel
                {
                    Preview = "No overlap in compact: 20px icon slot, single-line ellipsis.",
                    Kind = ClipboardContentKind.Text,
                    TimeText = "2 min",
                },
                new ClipboardItemModel
                {
                    Preview = "https://fluidbar.dev/spec/pager",
                    Kind = ClipboardContentKind.Link,
                    TimeText = "8 min",
                },
                new ClipboardItemModel
                {
                    Preview = "soft-gradient-cover.png",
                    Kind = ClipboardContentKind.Image,
                    TimeText = "14 min",
                },
            ],
            SelectedIndex = 1,
            HoverIndex = 0,
            CopiedIndex = 2,
        },
        Agent = new AgentPageModel
        {
            Items =
            [
                new AgentItemModel
                {
                    Tool = "Claude Code",
                    Project = "fluidbar",
                    Task = "Rebuild the settings motion layer",
                    StatusText = "Running",
                    DurationText = "02:14",
                    Tone = CompactStatus.Info,
                    ProgressPercent = 62,
                },
                new AgentItemModel
                {
                    Tool = "Codex",
                    Project = "prototype2",
                    Task = "Fix compact metrics and ellipsis rules",
                    StatusText = "Done",
                    DurationText = "48s",
                    Tone = CompactStatus.Success,
                    ProgressPercent = 100,
                },
                new AgentItemModel
                {
                    Tool = "Claude Code",
                    Project = "docs",
                    Task = "Generate the design walkthrough shots",
                    StatusText = "Running",
                    DurationText = "01:03",
                    Tone = CompactStatus.Info,
                    ProgressPercent = 34,
                },
                new AgentItemModel
                {
                    Tool = "Codex",
                    Project = "pager",
                    Task = "Scroll-chaining boundary test",
                    StatusText = "Failed",
                    DurationText = "12s",
                    Tone = CompactStatus.Danger,
                    ProgressPercent = 0,
                },
            ],
            SelectedIndex = 2,
            HoverIndex = 0,
        },
        Widgets = new WidgetsPageModel
        {
            CalendarMonthTitle = "October 2026",
            CalendarDays = October2026(),
            CalendarEventText = "Design review · 15:00",
            TodayLabel = "Today",
            WeatherTemp = "18°",
            WeatherCondition = "Partly sunny",
            WeatherHighLow = "18° / 11°",
            WeatherWind = "NW 3",
            Stats =
            [
                new WidgetStatModel("CPU", 38, WidgetStatTone.Accent),
                new WidgetStatModel("RAM", 64, WidgetStatTone.Info),
                new WidgetStatModel("Battery", 86, WidgetStatTone.Success),
            ],
            TasksTitle = "Today",
            TasksProgressText = "1/3 done",
            Tasks =
            [
                new WidgetTaskModel("Motion walkthrough", true),
                new WidgetTaskModel("Sync compact metrics"),
                new WidgetTaskModel("Export walkthrough shots"),
            ],
            SelectedIndex = 1,
        },
        PageTitles =
        [
            "Control center", "Now playing", "Notifications", "Clipboard", "Agent activity", "Widgets",
        ],
        ShortTitles = ["Home", "Media", "Notifs", "Clipboard", "Agent", "Widgets"],
    };

    /// <summary>Maps a golden case name to the state it renders (shared by golden and edge tests).</summary>
    public static IslandRenderState GoldenState(string name) => name switch
    {
        "01-compact-idle" => Idle(),
        "02-compact-media" => MediaCompact(),
        "03-compact-hud" => Hud(),
        "04-compact-notification" => Notification(),
        "05-compact-clipboard" => Clipboard(),
        "06-compact-agent" => Agent(),
        "07-compact-timer" => Timer(),
        "08-compact-battery" => Battery(),
        "10-expanded-home" => Expanded(0),
        "11-expanded-media" => Expanded(1),
        "12-expanded-page2-strip" => Expanded(2, strip: true),
        "13-expanded-light-home" => Expanded(0, theme: IslandTheme.Light),
        "14-expanded-dock-media" => Expanded(1, mode: IslandMode.Dock),
        "15-expanded-home-transition" => Expanded(0, pagePosition: 0.4),
        "16-expanded-flush-home" => Expanded(0, mode: IslandMode.Flush),
        "17-expanded-notifications" => Expanded(2),
        "18-expanded-clipboard" => Expanded(3),
        "19-expanded-agent" => Expanded(4),
        "20-expanded-widgets" => Expanded(5),
        "21-rail-page0-home" => RailState(0),
        "22-rail-page1-media" => RailState(1),
        "23-rail-page2-notifications" => RailState(2),
        "24-rail-page3-clipboard" => RailState(3),
        "25-rail-page4-agent" => RailState(4),
        "26-rail-page5-widgets" => RailState(5),
        _ => Expanded(0),
    };

    private static CalendarDayModel[] October2026()
    {
        // 1 October 2026 is a Thursday; the grid is Monday-first and always six rows.
        const int start = 3;
        var days = new CalendarDayModel[35];
        for (var i = 0; i < days.Length; i++)
        {
            var number = i - start + 1;
            if (number < 1)
            {
                days[i] = new CalendarDayModel(30 + number, InMonth: false);
            }
            else if (number > 31)
            {
                days[i] = new CalendarDayModel(number - 31, InMonth: false);
            }
            else
            {
                days[i] = new CalendarDayModel(
                    number,
                    InMonth: true,
                    IsToday: number == 18,
                    HasEvents: number is 18 or 22 or 27);
            }
        }

        return days;
    }

    /// <summary>
    /// Expanded prototype rail state: zh short labels and the detached strip revealed at a page. This
    /// is the state the golden rail cases capture (layout contract: 26 / dynamic capsule / 26 trio,
    /// 8 DIP gaps, 3 DIP dots on a 7 DIP pitch).
    /// </summary>
    /// <param name="page">Committed page index.</param>
    /// <param name="position">Continuous pager position (NaN = settled on <paramref name="page"/>).</param>
    public static IslandRenderState RailState(int page, double position = double.NaN)
    {
        var settled = double.IsNaN(position) ? page : position;
        var content = Content() with { ShortTitles = ZhShortTitles, PageTitles = ZhShortTitles };
        return IslandRenderState.Expanded(
            IslandMode.Float,
            PagerRenderState.FromPosition(settled),
            content,
            Theme,
            ArchipelagoState.Shown(settled));
    }

    public static IslandRenderState Expanded(
        int page = 0,
        bool strip = false,
        IslandMode mode = IslandMode.Float,
        IslandTheme? theme = null,
        double pagePosition = double.NaN)
    {
        var resolvedTheme = theme ?? Theme;
        var pager = double.IsNaN(pagePosition)
            ? PagerRenderState.At(page)
            : PagerRenderState.FromPosition(pagePosition);
        return IslandRenderState.Expanded(
            mode,
            pager,
            Content(),
            resolvedTheme,
            strip ? ArchipelagoState.Shown(double.IsNaN(pagePosition) ? page : pagePosition) : ArchipelagoState.HiddenState);
    }

    public static void SaveReportPng(SKBitmap bitmap, string fileName)
    {
        Directory.CreateDirectory(ReportDir);
        SavePng(bitmap, Path.Combine(ReportDir, fileName));
    }

    public static void SaveGoldenPng(SKBitmap bitmap, string fileName)
    {
        Directory.CreateDirectory(GoldenDir);
        SavePng(bitmap, Path.Combine(GoldenDir, fileName));
    }

    public static void SavePng(SKBitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public static SKBitmap? TryLoadGolden(string fileName)
    {
        var path = Path.Combine(GoldenDir, fileName);
        return File.Exists(path) ? SKBitmap.Decode(path) : null;
    }

    /// <summary>
    /// Counts (nearly) opaque pixels outside a host-local DIP rectangle. The threshold stays above the
    /// prototype2 shell shadow (max 199 alpha) so the two-layer CSS shadow does not read as content.
    /// </summary>
    public static int CountOpaqueOutside(SKBitmap bitmap, SKRect dipRect, float scale, byte alphaThreshold = 250)
    {
        var left = (int)Math.Floor(dipRect.Left * scale);
        var top = (int)Math.Floor(dipRect.Top * scale);
        var right = (int)Math.Ceiling(dipRect.Right * scale);
        var bottom = (int)Math.Ceiling(dipRect.Bottom * scale);
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var row = y * bitmap.Width;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (x >= left && x < right && y >= top && y < bottom)
                {
                    continue;
                }

                if (pixels[row + x].Alpha > alphaThreshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Counts pixels above <paramref name="alphaThreshold"/> inside a host-local DIP rectangle.</summary>
    public static int CountOpaqueInside(SKBitmap bitmap, SKRect dipRect, float scale, byte alphaThreshold = 200)
    {
        var left = Math.Max(0, (int)Math.Floor(dipRect.Left * scale));
        var top = Math.Max(0, (int)Math.Floor(dipRect.Top * scale));
        var right = Math.Min(bitmap.Width, (int)Math.Ceiling(dipRect.Right * scale));
        var bottom = Math.Min(bitmap.Height, (int)Math.Ceiling(dipRect.Bottom * scale));
        var pixels = bitmap.Pixels;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            var row = y * bitmap.Width;
            for (var x = left; x < right; x++)
            {
                if (pixels[row + x].Alpha > alphaThreshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static SKColor CenterPixel(SKBitmap bitmap)
        => bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
