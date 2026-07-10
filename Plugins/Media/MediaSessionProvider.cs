using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Media.Control;

namespace FluidBar;

internal static class MediaSessionProviderFactory
{
    public static IMediaSessionProvider Create() => new MediaSessionProvider();
}

public interface IMediaSessionProvider
{
    Task<MediaSnapshot?> ReadAsync(ILyricsProvider lyricsProvider, bool showLyrics);
    Task<bool> TryTogglePlayPauseAsync(string? preferredSourceId = null);
    Task<bool> TrySkipNextAsync(string? preferredSourceId = null);
    Task<bool> TrySkipPreviousAsync(string? preferredSourceId = null);
}

internal sealed class MediaSessionProvider : IMediaSessionProvider
{
    // Cache the current session's AUMID for media controls
    private string? _currentAumid;

    public async Task<MediaSnapshot?> ReadAsync(ILyricsProvider lyricsProvider, bool showLyrics)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        // Check ALL sessions, not just GetCurrentSession().
        // Priority: music apps > browser > other
        var sessions = manager.GetSessions();
        var orderedSessions = sessions
            .Select(s => new { Session = s, Priority = MediaSnapshotSelectionPolicy.GetSourcePriority(s.SourceAppUserModelId) })
            .OrderByDescending(x => x.Priority)
            .Select(x => x.Session)
            .ToList();

        // Also check current session in case GetSessions() misses it
        var current = manager.GetCurrentSession();
        if (current is not null && !orderedSessions.Any(s => s.SourceAppUserModelId == current.SourceAppUserModelId))
            orderedSessions.Insert(0, current);

        var candidates = new List<MediaSnapshot>();

        foreach (var session in orderedSessions)
        {
            var playback = session.GetPlaybackInfo();
            var sourceId = session.SourceAppUserModelId ?? "";
            var isBrowser = IsBrowserSource(sourceId);

            // For high-priority sources (music apps), also accept Paused/Opened/Changing status
            // if title is present — some players (e.g. Kugou) don't always report Playing correctly.
            // For browsers, only accept Playing — stale sessions linger with Paused/Opened after navigation.
            var isHighPriority = MediaSnapshotSelectionPolicy.GetSourcePriority(sourceId) >= 100;
            if (playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                if (!isHighPriority || isBrowser)
                    continue;
                var nonStopped = playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped &&
                                 playback.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
                if (!nonStopped)
                    continue;
            }

            var properties = await session.TryGetMediaPropertiesAsync();

            // Skip sessions without meaningful media info
            if (string.IsNullOrWhiteSpace(properties.Title))
                continue;
            var timeline = session.GetTimelineProperties();
            var title = properties.Title ?? "";
            var artist = properties.Artist ?? "";

            // Skip sessions without meaningful media info
            if (string.IsNullOrWhiteSpace(title))
                continue;

            // Browser sessions: reject if timeline shows no real media progress.
            // Edge registers a GSMTC session for any media-capable tab; when the user
            // navigates away the old session lingers as "Playing" with a frozen position.
            if (isBrowser)
            {
                var hasDuration = timeline.EndTime > timeline.StartTime;
                var hasProgress = timeline.Position > timeline.StartTime;
                if (!hasDuration && !hasProgress)
                    continue;
            }

            // Extrapolate stale timeline positions. Browsers (Chromium) only push
            // timeline updates every few seconds; LastUpdatedTime tells us how old
            // the reported Position is. Without this, a stale position is
            // indistinguishable from a user seek, forcing the UI to clamp all
            // backward jumps — which then swallows real backward seeks.
            var position = timeline.Position;
            var isPlayingNow = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (isPlayingNow && timeline.LastUpdatedTime.Year > 2000)
            {
                var age = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (age > TimeSpan.Zero && age < TimeSpan.FromMinutes(30))
                {
                    var rate = playback.PlaybackRate ?? 1.0;
                    position += TimeSpan.FromTicks((long)(age.Ticks * rate));
                    if (timeline.EndTime > timeline.StartTime && position > timeline.EndTime)
                        position = timeline.EndTime;
                }
            }

            var progress = CalculateProgressPercent(position, timeline.StartTime, timeline.EndTime);
            var sourceName = MediaIslandEventFactory.FriendlySourceName(sourceId);

            // Read ticks for real-time progress interpolation
            var positionTicks = position.Ticks;
            var endTicks = timeline.EndTime.Ticks;
            var startTimeTicks = timeline.StartTime.Ticks;

            // Browser site badge detection
            var sourceBadge = (string?)null;
            if (IsBrowserSource(sourceId))
            {
                sourceBadge = FindBrowserSiteBadge(sourceId);
            }

            // Extract app icon from the source process
            var iconPath = MediaSourceVisuals.ExtractAppIconPath(sourceId);

            // Do not block first paint on slow browser thumbnails; later polls can update artwork.
            var albumArtPath = await MediaArtworkReadPolicy.ReadWithInitialTimeoutAsync(
                ExtractAlbumArtAsync(properties));

            candidates.Add(new MediaSnapshot(
                SourceAppUserModelId: sourceId,
                SourceName: sourceName,
                Title: title,
                Artist: artist,
                Album: properties.AlbumTitle ?? "",
                IsPlaying: playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                ProgressPercent: progress,
                SourceBadge: sourceBadge,
                SourceIconPath: iconPath,
                AlbumArtPath: albumArtPath,
                PositionTicks: positionTicks,
                EndTicks: endTicks,
                StartTimeTicks: startTimeTicks,
                LastUpdatedTicks: Environment.TickCount64));
        }

        var snapshot = candidates.Aggregate<MediaSnapshot?, MediaSnapshot?>(
            null,
            (current, next) => MediaSnapshotSelectionPolicy.ChooseBestSnapshot(current, next));
        if (snapshot is null)
            return null;

        _currentAumid = snapshot.SourceAppUserModelId;

        var lyric = showLyrics && snapshot.IsPlaying
            ? lyricsProvider.TryGetCurrentLine(snapshot, TimeSpan.FromTicks(snapshot.PositionTicks))
            : null;

        // When title is AUMID and lyrics are empty, try searching by artist name
        if (string.IsNullOrWhiteSpace(lyric) && showLyrics && snapshot.IsPlaying)
        {
            var isAumidTitle = !string.IsNullOrWhiteSpace(snapshot.Title) &&
                snapshot.Title.Length > 30 && snapshot.Title.StartsWith('{') &&
                snapshot.Title.EndsWith('}') && snapshot.Title.Contains('-');
            if (isAumidTitle && !string.IsNullOrWhiteSpace(snapshot.Artist))
            {
                // Create a modified snapshot with artist as title for Kugou API search
                var searchSnapshot = snapshot with { Title = snapshot.Artist, Artist = "" };
                var searchLyric = lyricsProvider.TryGetCurrentLine(searchSnapshot, TimeSpan.FromTicks(snapshot.PositionTicks));
                if (!string.IsNullOrWhiteSpace(searchLyric))
                {
                    lyric = searchLyric;
                    snapshot = snapshot with { Title = snapshot.Artist, Artist = "", LyricLine = lyric };
                }
            }
        }

        return snapshot with { LyricLine = lyric };
    }

    public async Task<bool> TryTogglePlayPauseAsync(string? preferredSourceId = null)
    {
        return await TryMediaCommandAsync(
            session => session.TryTogglePlayPauseAsync().AsTask(),
            preferredSourceId,
            session => session.GetPlaybackInfo().Controls.IsPlayPauseToggleEnabled ||
                       session.GetPlaybackInfo().Controls.IsPlayEnabled ||
                       session.GetPlaybackInfo().Controls.IsPauseEnabled);
    }

    public async Task<bool> TrySkipNextAsync(string? preferredSourceId = null)
    {
        return await TryMediaCommandAsync(
            session => session.TrySkipNextAsync().AsTask(),
            preferredSourceId,
            session => session.GetPlaybackInfo().Controls.IsNextEnabled);
    }

    public async Task<bool> TrySkipPreviousAsync(string? preferredSourceId = null)
    {
        return await TryMediaCommandAsync(
            session => session.TrySkipPreviousAsync().AsTask(),
            preferredSourceId,
            session => session.GetPlaybackInfo().Controls.IsPreviousEnabled);
    }

    private static async Task<bool> WithTimeout(Task<bool> task, int milliseconds = 3000)
    {
        var delay = Task.Delay(milliseconds);
        return await Task.WhenAny(task, delay) == task && task.IsCompletedSuccessfully && task.Result;
    }

    private async Task<bool> TryMediaCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command,
        string? preferredSourceId,
        Func<GlobalSystemMediaTransportControlsSession, bool> isCommandEnabled)
    {
        try
        {
            // Run entire GSMTC operation on thread pool to avoid blocking UI thread.
            // RequestAsync + session enumeration can be slow and freeze the UI.
            return await Task.Run(async () =>
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var current = manager.GetCurrentSession();
                var sessions = manager.GetSessions().ToList();
                if (current is not null && !sessions.Contains(current))
                    sessions.Insert(0, current);

                var candidates = MediaSessionCommandPolicy.OrderCandidates(
                    sessions,
                    preferredSourceId,
                    _currentAumid,
                    session => session.SourceAppUserModelId,
                    session => ReferenceEquals(session, current) ||
                               string.Equals(
                                   session.SourceAppUserModelId,
                                   current?.SourceAppUserModelId,
                                   StringComparison.OrdinalIgnoreCase),
                    session => SafeIsCommandEnabled(session, isCommandEnabled));

                foreach (var session in candidates)
                {
                    try
                    {
                        if (await command(session))
                            return true;
                    }
                    catch
                    {
                    }
                }

                return false;
            });
        }
        catch
        {
        }

        return false;
    }

    private static bool SafeIsCommandEnabled(
        GlobalSystemMediaTransportControlsSession session,
        Func<GlobalSystemMediaTransportControlsSession, bool> isCommandEnabled)
    {
        try
        {
            return isCommandEnabled(session);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Check if the source is a browser.</summary>
    private static bool IsBrowserSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;
        var lower = sourceId.ToLowerInvariant();
        return lower.Contains("chrome") || lower.Contains("edge") ||
               lower.Contains("msedge") || lower.Contains("firefox");
    }

    /// <summary>Find the browser's window title and extract the site badge.</summary>
    private static string FindBrowserSiteBadge(string sourceAppUserModelId)
    {
        try
        {
            var sourceLower = sourceAppUserModelId.ToLowerInvariant();
            string? foundTitle = null;

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    var procName = proc.ProcessName.ToLowerInvariant();

                    // Match browser process to GSMTC source
                    if ((sourceLower.Contains("chrome") && procName.Contains("chrome")) ||
                        (sourceLower.Contains("edge") && procName.Contains("msedge")) ||
                        (sourceLower.Contains("msedge") && procName.Contains("msedge")) ||
                        (sourceLower.Contains("firefox") && procName.Contains("firefox")))
                    {
                        var title = GetWindowTitle(hWnd);
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            foundTitle = title;
                            return false; // Stop
                        }
                    }
                }
                catch { }

                return true;
            }, IntPtr.Zero);

            if (!string.IsNullOrWhiteSpace(foundTitle))
                return SiteBadgeFromTitle(foundTitle);
        }
        catch { }

        return "Web";
    }

    private static string SiteBadgeFromTitle(string title) =>
        BrowserMediaSitePolicy.DisplayNameFromTitle(title);

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;
        var builder = new StringBuilder(length + 1);
        GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

    private static async Task<string?> ExtractAlbumArtAsync(
        GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        try
        {
            var thumbnail = properties.Thumbnail;
            if (thumbnail is null) return null;

            using var stream = await thumbnail.OpenReadAsync();
            var bytes = new byte[stream.Size];
            await stream.AsStreamForRead().ReadExactlyAsync(bytes);

            var tempDir = Path.Combine(Path.GetTempPath(), "FluidBar", "art");
            Directory.CreateDirectory(tempDir);
            var fileName = $"art_{Math.Abs(HashCode.Combine(
                properties.Title ?? "", properties.Artist ?? ""))}.jpg";
            var outPath = Path.Combine(tempDir, fileName);
            if (File.Exists(outPath)) return outPath;

            await File.WriteAllBytesAsync(outPath, bytes);
            return outPath;
        }
        catch { return null; }
    }
}
