namespace FluidBar;

public sealed record MediaSnapshot(
    string SourceAppUserModelId,
    string SourceName,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    int ProgressPercent,
    string? LyricLine = null,
    string? SecondaryLyricLine = null,
    string? AlbumArtPath = null,
    string? SourceIconPath = null,
    string? SourceBadge = null,
    long PositionTicks = 0,
    long EndTicks = 0,
    long StartTimeTicks = 0,
    long LastUpdatedTicks = 0);

public static class MediaArtworkReadPolicy
{
    public const int InitialReadTimeoutMilliseconds = 180;

    public static async Task<string?> ReadWithInitialTimeoutAsync(Task<string?> readTask)
    {
        if (readTask.IsCompleted)
            return await readTask.ConfigureAwait(false);

        var timeoutTask = Task.Delay(InitialReadTimeoutMilliseconds);
        var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
        if (completedTask == readTask)
            return await readTask.ConfigureAwait(false);

        _ = readTask.ContinueWith(
            static task => _ = task.Exception,
            TaskContinuationOptions.OnlyOnFaulted);
        return null;
    }
}

public static class BrowserMediaSitePolicy
{
    private static readonly Dictionary<string, string> LegacyBadges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["YT"] = "YouTube",
        ["B"] = "BiliBili",
        ["NF"] = "Netflix",
        ["SP"] = "Spotify",
        ["SC"] = "SoundCloud",
        ["PV"] = "Prime Video",
        ["D+"] = "Disney+",
        ["iQ"] = "iQIYI",
        ["YK"] = "Youku",
        ["WEB"] = "Web"
    };

    private static readonly string[] BrowserSourceMarkers =
    [
        "edge", "msedge", "chrome", "firefox", "browser"
    ];

    public static string DisplayNameFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Web";

        var lower = title.ToLowerInvariant();
        if (lower.Contains("youtube")) return "YouTube";
        if (lower.Contains("bilibili") ||
            lower.Contains("哔哩哔哩", StringComparison.Ordinal) ||
            lower.Contains("b站", StringComparison.Ordinal)) return "BiliBili";
        if (lower.Contains("netflix")) return "Netflix";
        if (lower.Contains("prime video") || lower.Contains("amazon prime")) return "Prime Video";
        if (lower.Contains("disney")) return "Disney+";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("soundcloud")) return "SoundCloud";
        if (lower.Contains("iqiyi") || lower.Contains("爱奇艺", StringComparison.Ordinal)) return "iQIYI";
        if (lower.Contains("youku") || lower.Contains("优酷", StringComparison.Ordinal)) return "Youku";
        if (lower.Contains("tencent video") || lower.Contains("腾讯视频", StringComparison.Ordinal)) return "Tencent Video";
        if (lower.Contains("douyin") || lower.Contains("抖音", StringComparison.Ordinal)) return "Douyin";
        if (lower.Contains("xigua") || lower.Contains("西瓜视频", StringComparison.Ordinal)) return "Xigua Video";
        return "Web";
    }

    public static string DisplayNameFromBadge(string? badge)
    {
        if (string.IsNullOrWhiteSpace(badge))
            return "Web";

        var trimmed = badge.Trim();
        return LegacyBadges.TryGetValue(trimmed, out var mapped) ? mapped : trimmed;
    }

    public static bool IsBrowserSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return false;

        var lower = sourceName.ToLowerInvariant();
        return BrowserSourceMarkers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsKnownSiteDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = DisplayNameFromBadge(value);
        return normalized is "YouTube" or "BiliBili" or "Netflix" or "Spotify" or
            "SoundCloud" or "Prime Video" or "Disney+" or "iQIYI" or "Youku" or
            "Tencent Video" or "Douyin" or "Xigua Video" or "Web";
    }
}

public static class MediaIslandEventFactory
{
    public static IslandEvent CreateStopped()
    {
        return new IslandEvent(
            Source: "media",
            Title: "媒体已停止",
            Content: "没有正在播放的媒体",
            IconKind: "media",
            Payload: new IslandEventPayload(
                Kind: IslandEventKind.Media,
                IsActive: false,
                ShowsAudioWave: false));
    }

    public static IslandEvent FromSnapshot(MediaSnapshot snapshot)
    {
        var sourceName = string.IsNullOrWhiteSpace(snapshot.SourceName)
            ? FriendlySourceName(snapshot.SourceAppUserModelId)
            : snapshot.SourceName;
        var title = string.IsNullOrWhiteSpace(snapshot.Title)
            ? "正在播放"
            : snapshot.Title;
        var isBrowser = IsBrowserSource(snapshot.SourceAppUserModelId);

        // Browser: TitleText = browser name, Content = window title (video name)
        string content;
        string subtitle;
        string eventTitle;
        if (isBrowser)
        {
            var browserName = FriendlyBrowserName(snapshot.SourceAppUserModelId);
            eventTitle = browserName;
            content = title;
            subtitle = string.Empty;
        }
        else
        {
            eventTitle = sourceName;
            content = title;
            var artist = string.IsNullOrWhiteSpace(snapshot.Artist)
                ? "" : snapshot.Artist;
            subtitle = string.IsNullOrWhiteSpace(snapshot.Album)
                ? artist
                : string.IsNullOrWhiteSpace(artist) ? snapshot.Album : $"{artist} \u2022 {snapshot.Album}";
        }

        var badge = !string.IsNullOrWhiteSpace(snapshot.SourceBadge)
            ? BrowserMediaSitePolicy.DisplayNameFromBadge(snapshot.SourceBadge)
            : isBrowser
                ? BrowserMediaSitePolicy.DisplayNameFromTitle(title)
                : sourceName;

        return new IslandEvent(
            Source: "media",
            Title: eventTitle,
            Content: content,
            IconKind: "media",
            Payload: new IslandEventPayload(
                Kind: IslandEventKind.Media,
                Subtitle: subtitle,
                Badge: badge,
                SourceName: sourceName,
                ProgressPercent: snapshot.ProgressPercent,
                IsActive: snapshot.IsPlaying,
                ShowsAudioWave: snapshot.IsPlaying,
                AlbumArtPath: snapshot.AlbumArtPath,
                AppIconPath: snapshot.SourceIconPath,
                LyricLine: snapshot.LyricLine,
                SecondaryLyricLine: snapshot.SecondaryLyricLine,
                DetailLines: BuildDetailLines(snapshot, sourceName),
                PositionTicks: snapshot.PositionTicks,
                EndTicks: snapshot.EndTicks,
                StartTimeTicks: snapshot.StartTimeTicks,
                LastUpdatedTicks: snapshot.LastUpdatedTicks));
    }

    private static string FriendlyBrowserName(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return "Browser";
        var lower = sourceId.ToLowerInvariant();
        if (lower.Contains("edge") || lower.Contains("msedge")) return "Microsoft Edge";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("firefox")) return "Firefox";
        return "Browser";
    }

    private static bool IsBrowserSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return false;
        var lower = sourceId.ToLowerInvariant();
        return lower.Contains("chrome") || lower.Contains("edge") ||
               lower.Contains("msedge") || lower.Contains("firefox");
    }

    public static string FriendlySourceName(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
            return "Media";

        var lower = sourceAppUserModelId.ToLowerInvariant();
        if (lower.Contains("spotify", StringComparison.Ordinal)) return "Spotify";
        if (lower.Contains("kugou", StringComparison.Ordinal) ||
            lower.Contains("酷狗", StringComparison.Ordinal)) return "酷狗音乐";
        if (lower.Contains("cloudmusic", StringComparison.Ordinal) ||
            lower.Contains("netease", StringComparison.Ordinal) ||
            lower.Contains("网易云", StringComparison.Ordinal)) return "网易云音乐";
        if (lower.Contains("qqmusic", StringComparison.Ordinal) ||
            lower.Contains("qq音乐", StringComparison.Ordinal) ||
            lower.Contains("qq 音乐", StringComparison.Ordinal)) return "QQ 音乐";
        if (lower.Contains("kwmusic", StringComparison.Ordinal) ||
            lower.Contains("酷我", StringComparison.Ordinal)) return "酷我音乐";
        if (lower.Contains("applemusic", StringComparison.Ordinal)) return "Apple Music";
        if (lower.Contains("chrome", StringComparison.Ordinal)) return "Chrome";
        if (lower.Contains("edge", StringComparison.Ordinal)) return "Microsoft Edge";
        if (lower.Contains("zune", StringComparison.Ordinal) ||
            lower.Contains("media", StringComparison.Ordinal)) return "Media Player";

        var bang = sourceAppUserModelId.IndexOf('!', StringComparison.Ordinal);
        var prefix = bang > 0 ? sourceAppUserModelId[..bang] : sourceAppUserModelId;
        var dot = prefix.LastIndexOf('.');
        return dot >= 0 && dot < prefix.Length - 1 ? prefix[(dot + 1)..] : prefix;
    }

    private static IReadOnlyList<string> BuildDetailLines(MediaSnapshot snapshot, string sourceName)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.Album))
            lines.Add(snapshot.Album);
        return lines;
    }
}
