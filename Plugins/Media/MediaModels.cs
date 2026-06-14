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
    string? SourceIconPath = null);

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
        var artist = string.IsNullOrWhiteSpace(snapshot.Artist)
            ? "未知艺术家"
            : snapshot.Artist;
        var subtitle = string.IsNullOrWhiteSpace(snapshot.Album)
            ? artist
            : $"{artist} · {snapshot.Album}";

        return new IslandEvent(
            Source: "media",
            Title: snapshot.IsPlaying ? "正在播放" : "媒体已暂停",
            Content: title,
            IconKind: "media",
            Payload: new IslandEventPayload(
                Kind: IslandEventKind.Media,
                Subtitle: subtitle,
                Badge: sourceName,
                SourceName: sourceName,
                ProgressPercent: snapshot.ProgressPercent,
                IsActive: snapshot.IsPlaying,
                ShowsAudioWave: snapshot.IsPlaying,
                AlbumArtPath: snapshot.AlbumArtPath,
                AppIconPath: snapshot.SourceIconPath,
                LyricLine: snapshot.LyricLine,
                SecondaryLyricLine: snapshot.SecondaryLyricLine,
                DetailLines: BuildDetailLines(snapshot, sourceName)));
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
