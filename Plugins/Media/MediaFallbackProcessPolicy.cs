namespace FluidBar;

public sealed record MediaFallbackProcessInfo(int ProcessId, string ProcessName, string SourceName);

public static class MediaFallbackProcessPolicy
{
    public static IReadOnlyList<int> AudioGateProcessIds(
        IEnumerable<MediaFallbackProcessInfo> processes,
        string bestProcessName,
        string bestSourceName)
    {
        var result = processes
            .Where(process =>
                MediaSnapshotSelectionPolicy.IsSamePlayerApp(process.ProcessName, bestProcessName) ||
                MediaSnapshotSelectionPolicy.IsSamePlayerApp(process.SourceName, bestSourceName) ||
                string.Equals(process.ProcessName, bestProcessName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(process.SourceName, bestSourceName, StringComparison.OrdinalIgnoreCase))
            .Select(process => process.ProcessId)
            .Where(processId => processId > 0)
            .Distinct()
            .ToList();

        return result;
    }

    public static bool ShouldAcceptFallbackPlayback(
        string processName,
        string sourceName,
        string songTitle,
        ProcessAudioPlaybackState audioState)
    {
        if (audioState == ProcessAudioPlaybackState.Playing)
            return true;

        if (audioState == ProcessAudioPlaybackState.NotPlaying)
            return false;

        return CanTrustWindowTitleForUnknownAudio(processName, sourceName, songTitle);
    }

    private static bool CanTrustWindowTitleForUnknownAudio(
        string processName,
        string sourceName,
        string songTitle)
    {
        // Check known sources FIRST — even with empty titles, these are trusted
        if (IsKugouSource(processName, sourceName))
        {
            if (!string.IsNullOrWhiteSpace(songTitle))
            {
                if (songTitle.Contains("桌面歌词", StringComparison.Ordinal) ||
                    songTitle.Contains("酷狗音乐", StringComparison.Ordinal) ||
                    songTitle.Contains("KuGou", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        if (IsNeteaseSource(processName, sourceName))
        {
            if (!string.IsNullOrWhiteSpace(songTitle))
            {
                if (songTitle.Contains("网易云音乐", StringComparison.Ordinal) ||
                    songTitle.Equals("DesktopLyric", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        // Unknown sources: require non-empty, non-app-name title
        if (string.IsNullOrWhiteSpace(songTitle) ||
            MediaSnapshotSelectionPolicy.IsAppNameString(songTitle))
        {
            return false;
        }

        return true;
    }

    private static bool IsNeteaseSource(string processName, string sourceName)
    {
        return processName.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("netease", StringComparison.OrdinalIgnoreCase) ||
               sourceName.Contains("网易云", StringComparison.Ordinal);
    }

    private static bool IsKugouSource(string processName, string sourceName)
    {
        return MediaSnapshotSelectionPolicy.IsSamePlayerApp(processName, "kugou") ||
               MediaSnapshotSelectionPolicy.IsSamePlayerApp(sourceName, "酷狗音乐") ||
               processName.Contains("kugou", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("kgmusic", StringComparison.OrdinalIgnoreCase) ||
               sourceName.Contains("酷狗", StringComparison.Ordinal);
    }
}
