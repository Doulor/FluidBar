using Windows.Media.Control;

namespace FluidBar;

internal static class MediaSessionProviderFactory
{
    public static IMediaSessionProvider Create() => new MediaSessionProvider();
}

internal interface IMediaSessionProvider
{
    Task<MediaSnapshot?> ReadAsync(ILyricsProvider lyricsProvider, bool showLyrics);
}

internal sealed class MediaSessionProvider : IMediaSessionProvider
{
    public async Task<MediaSnapshot?> ReadAsync(ILyricsProvider lyricsProvider, bool showLyrics)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        // Check ALL sessions, not just GetCurrentSession().
        // Priority: music apps > browser > other
        var sessions = manager.GetSessions();
        var orderedSessions = sessions
            .Select(s => new { Session = s, Priority = GetSourcePriority(s.SourceAppUserModelId) })
            .OrderByDescending(x => x.Priority)
            .Select(x => x.Session)
            .ToList();

        // Also check current session in case GetSessions() misses it
        var current = manager.GetCurrentSession();
        if (current is not null && !orderedSessions.Any(s => s.SourceAppUserModelId == current.SourceAppUserModelId))
            orderedSessions.Insert(0, current);

        foreach (var session in orderedSessions)
        {
            var playback = session.GetPlaybackInfo();
            if (playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                continue;

            var properties = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            var title = properties.Title ?? "";
            var artist = properties.Artist ?? "";

            // Skip sessions without meaningful media info
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var progress = CalculateProgressPercent(timeline.Position, timeline.StartTime, timeline.EndTime);
            var sourceId = session.SourceAppUserModelId ?? "";
            var sourceName = MediaIslandEventFactory.FriendlySourceName(sourceId);

            var baseSnapshot = new MediaSnapshot(
                SourceAppUserModelId: sourceId,
                SourceName: sourceName,
                Title: title,
                Artist: artist,
                Album: properties.AlbumTitle ?? "",
                IsPlaying: true,
                ProgressPercent: progress);

            var lyric = showLyrics
                ? lyricsProvider.TryGetCurrentLine(baseSnapshot, timeline.Position)
                : null;

            return baseSnapshot with { LyricLine = lyric };
        }

        return null;
    }

    /// <summary>Priority score for sorting — music apps first, then browsers.</summary>
    private static int GetSourcePriority(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return 0;

        var lower = sourceId.ToLowerInvariant();
        if (lower.Contains("kugou") || lower.Contains("cloudmusic") || lower.Contains("netease") ||
            lower.Contains("qqmusic") || lower.Contains("spotify") || lower.Contains("applemusic") ||
            lower.Contains("kwmusic") || lower.Contains("zune"))
            return 100;
        if (lower.Contains("chrome") || lower.Contains("edge") || lower.Contains("firefox"))
            return 50;
        return 10;
    }

    private static int CalculateProgressPercent(TimeSpan position, TimeSpan start, TimeSpan end)
    {
        var duration = end - start;
        if (duration.TotalMilliseconds <= 0)
            return 0;

        return Math.Clamp(
            (int)Math.Round((position - start).TotalMilliseconds / duration.TotalMilliseconds * 100),
            0,
            100);
    }
}
