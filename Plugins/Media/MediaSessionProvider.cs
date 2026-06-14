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
        var session = manager.GetCurrentSession();
        if (session is null)
            return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var isPlaying = playback.PlaybackStatus ==
                        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        if (!isPlaying)
            return null;

        var progress = CalculateProgressPercent(timeline.Position, timeline.StartTime, timeline.EndTime);
        var sourceId = session.SourceAppUserModelId ?? "";
        var sourceName = MediaIslandEventFactory.FriendlySourceName(sourceId);

        var baseSnapshot = new MediaSnapshot(
            SourceAppUserModelId: sourceId,
            SourceName: sourceName,
            Title: properties.Title ?? "",
            Artist: properties.Artist ?? "",
            Album: properties.AlbumTitle ?? "",
            IsPlaying: isPlaying,
            ProgressPercent: progress);

        var lyric = showLyrics
            ? lyricsProvider.TryGetCurrentLine(baseSnapshot, timeline.Position)
            : null;

        return baseSnapshot with { LyricLine = lyric };
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
