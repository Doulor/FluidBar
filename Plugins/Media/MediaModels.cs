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
    long LastUpdatedTicks = 0);

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

        // Browser: show "Platform • Title", skip "未知艺术家"
        string content;
        string subtitle;
        if (isBrowser)
        {
            var siteName = !string.IsNullOrWhiteSpace(snapshot.SourceBadge)
                ? SiteFriendlyName(snapshot.SourceBadge) : sourceName;
            content = $"{siteName} \u2022 {title}";
            subtitle = string.Empty; // No "未知艺术家" for browser
        }
        else
        {
            content = title;
            var artist = string.IsNullOrWhiteSpace(snapshot.Artist)
                ? "" : snapshot.Artist;
            subtitle = string.IsNullOrWhiteSpace(snapshot.Album)
                ? artist
                : string.IsNullOrWhiteSpace(artist) ? snapshot.Album : $"{artist} \u2022 {snapshot.Album}";
        }

        var badge = !string.IsNullOrWhiteSpace(snapshot.SourceBadge)
            ? snapshot.SourceBadge
            : sourceName;

        return new IslandEvent(
            Source: "media",
            Title: snapshot.IsPlaying ? "正在播放" : "媒体已暂停",
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
                LastUpdatedTicks: snapshot.LastUpdatedTicks));
    }

    private static bool IsBrowserSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return false;
        var lower = sourceId.ToLowerInvariant();
        return lower.Contains("chrome") || lower.Contains("edge") ||
               lower.Contains("msedge") || lower.Contains("firefox");
    }

    private static string SiteFriendlyName(string badge)
    {
        return badge switch
        {
            "YT" => "YouTube",
            "B" => "BiliBili",
            "NF" => "Netflix",
            "SP" => "Spotify",
            "SC" => "SoundCloud",
            "PV" => "Prime Video",
            "D+" => "Disney+",
            "iQ" => "爱奇艺",
            "YK" => "优酷",
            "WEB" => "Web",
            _ => badge
        };
    }

    public static string FriendlySourceName(string sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
            return "Media";

        var lower = sourceAppUserModelId.ToLowerInvariant();
        if (lower.Contains("spotify", StringComparison.Ordinal)) return "Spotify";
        if (lower.Contains("kugou", StringComparison.Ordinal)) return "酷狗音乐";
        if (lower.Contains("cloudmusic", StringComparison.Ordinal) ||
            lower.Contains("netease", StringComparison.Ordinal)) return "网易云音乐";
        if (lower.Contains("qqmusic", StringComparison.Ordinal)) return "QQ 音乐";
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
        var lines = new List<string>
        {
            sourceName,
            snapshot.IsPlaying ? "播放中" : "已暂停"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.Album))
            lines.Add(snapshot.Album);
        if (snapshot.ProgressPercent > 0)
            lines.Add($"{Math.Clamp(snapshot.ProgressPercent, 0, 100)}%");
        return lines;
    }
}
