using System.Diagnostics;
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
    Task TryTogglePlayPauseAsync();
    Task TrySkipNextAsync();
    Task TrySkipPreviousAsync();
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

            // Read ticks for real-time progress interpolation
            var positionTicks = timeline.Position.Ticks;
            var endTicks = timeline.EndTime.Ticks;

            // Browser site badge detection
            var sourceBadge = (string?)null;
            if (IsBrowserSource(sourceId))
            {
                sourceBadge = FindBrowserSiteBadge(sourceId);
            }

            _currentAumid = sourceId;

            var baseSnapshot = new MediaSnapshot(
                SourceAppUserModelId: sourceId,
                SourceName: sourceName,
                Title: title,
                Artist: artist,
                Album: properties.AlbumTitle ?? "",
                IsPlaying: true,
                ProgressPercent: progress,
                SourceBadge: sourceBadge,
                PositionTicks: positionTicks,
                EndTicks: endTicks,
                LastUpdatedTicks: Environment.TickCount64);

            var lyric = showLyrics
                ? lyricsProvider.TryGetCurrentLine(baseSnapshot, timeline.Position)
                : null;

            return baseSnapshot with { LyricLine = lyric };
        }

        return null;
    }

    public async Task TryTogglePlayPauseAsync()
    {
        await TryMediaCommandAsync(session => session.TryTogglePlayPauseAsync().AsTask());
    }

    public async Task TrySkipNextAsync()
    {
        await TryMediaCommandAsync(session => session.TrySkipNextAsync().AsTask());
    }

    public async Task TrySkipPreviousAsync()
    {
        await TryMediaCommandAsync(session => session.TrySkipPreviousAsync().AsTask());
    }

    private async Task TryMediaCommandAsync(Func<GlobalSystemMediaTransportControlsSession, Task> command)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var aumid = _currentAumid;

            // Find the matching session by AUMID
            GlobalSystemMediaTransportControlsSession? target = null;
            if (!string.IsNullOrEmpty(aumid))
            {
                foreach (var s in manager.GetSessions())
                {
                    if (s.SourceAppUserModelId == aumid)
                    {
                        target = s;
                        break;
                    }
                }
            }
            target ??= manager.GetCurrentSession();

            if (target is not null)
                await command(target);
        }
        catch
        {
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

        return "WEB";
    }

    private static string SiteBadgeFromTitle(string title)
    {
        var lower = title.ToLowerInvariant();
        if (lower.Contains("youtube")) return "YT";
        if (lower.Contains("bilibili") || lower.Contains("b站")) return "B";
        if (lower.Contains("netflix")) return "NF";
        if (lower.Contains("prime video") || lower.Contains("amazon")) return "PV";
        if (lower.Contains("disney")) return "D+";
        if (lower.Contains("spotify")) return "SP";
        if (lower.Contains("soundcloud")) return "SC";
        if (lower.Contains("iqiyi") || lower.Contains("爱奇艺")) return "iQ";
        if (lower.Contains("youku") || lower.Contains("优酷")) return "YK";
        return "WEB";
    }

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
