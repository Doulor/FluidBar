using System.Globalization;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Playback state a player persisted to its own settings file (no SMTC required).
/// </summary>
/// <param name="RawTitle">Track text exactly as the player wrote it ("artist - title" for KuGou).</param>
/// <param name="Position">Playback position at the moment the file was written.</param>
/// <param name="SampledAtUtc">File write time in UTC (the App interpolates forward from here).</param>
/// <param name="IsMuted">Player mute flag, or false when the file exposes none.</param>
public sealed record PlayerPlaybackState(
    string RawTitle,
    TimeSpan Position,
    DateTimeOffset SampledAtUtc,
    bool IsMuted);

/// <summary>
/// Reads the playback state KuGou persists in <c>%APPDATA%\KuGou8\KuGou.ini</c>.
/// </summary>
/// <remarks>
/// <para>KuGou registers no SMTC session, so Windows exposes neither its timeline nor a media-key
/// transport target. The player still rewrites its settings file roughly every 30 s and stores the
/// current <c>LastPlayingTitleName</c> plus <c>LastPlayingSongPos</c> (milliseconds) there — real
/// position data that lets the island show progress and scroll line-by-line lyrics instead of
/// pretending the track has no timeline.</para>
/// <para>Everything here is best-effort and read-only: a missing, locked or truncated file simply
/// yields <c>null</c> and the window-title fallback keeps running without a timeline.</para>
/// </remarks>
internal static class PlayerPlaybackStateReader
{
    /// <summary>KuGou settings file name inside its roaming data directory.</summary>
    internal const string KugouSettingsFileName = "KuGou.ini";

    internal const string PlaybackSection = "PlaybackState";
    internal const string PlayerSection = "Player";
    internal const string TitleKey = "LastPlayingTitleName";
    internal const string PositionKey = "LastPlayingSongPos";
    internal const string MuteKey = "Mute";
    internal const string LyricSection = "LyricConfigSection";
    internal const string LyricPathKey = "LyricPath";

    /// <summary>Reads the current KuGou playback state, or null when it cannot be read.</summary>
    public static PlayerPlaybackState? TryReadKugou(DateTimeOffset sampledAtUtc)
    {
        foreach (var directory in KugouDataDirectories())
        {
            if (directory is null)
            {
                continue;
            }

            string path;
            try
            {
                path = Path.Combine(directory, KugouSettingsFileName);
                if (!File.Exists(path))
                {
                    continue;
                }
            }
            catch (Exception)
            {
                continue;
            }

            try
            {
                var content = File.ReadAllText(path);
                var writtenAt = File.GetLastWriteTimeUtc(path);
                var sampled = writtenAt == DateTime.MinValue
                    ? sampledAtUtc
                    : new DateTimeOffset(DateTime.SpecifyKind(writtenAt, DateTimeKind.Utc));
                var state = Parse(content, sampled);
                if (state is not null)
                {
                    return state;
                }
            }
            catch (IOException)
            {
                // The player rewrites the file while we read it; the next poll retries.
            }
            catch (UnauthorizedAccessException)
            {
                // Locked down for this process: no state, no timeline.
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the lyric cache directory from the player's settings (<c>[LyricConfigSection] LyricPath</c>),
    /// or null when the file does not name one.
    /// </summary>
    public static string? TryReadKugouLyricDirectory()
    {
        foreach (var directory in KugouDataDirectories())
        {
            if (directory is null)
            {
                continue;
            }

            try
            {
                var path = Path.Combine(directory, KugouSettingsFileName);
                if (!File.Exists(path))
                {
                    continue;
                }

                var configured = ParseLyricDirectory(File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured;
                }
            }
            catch (IOException)
            {
                // Raced with the player's rewrite: fall through to the default location.
            }
            catch (UnauthorizedAccessException)
            {
                // Not readable for this process.
            }
        }

        return null;
    }

    /// <summary>Extracts <c>LyricPath</c> from KuGou.ini content (pure; exposed for tests).</summary>
    internal static string? ParseLyricDirectory(string? ini)
    {
        if (string.IsNullOrWhiteSpace(ini))
        {
            return null;
        }

        var inLyricSection = false;
        foreach (var line in ini.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');
                inLyricSection = close > 1
                                 && string.Equals(trimmed[1..close], LyricSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inLyricSection)
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && string.Equals(trimmed[..separator], LyricPathKey, StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[(separator + 1)..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static IEnumerable<string?> KugouDataDirectories()
    {
        string? roaming = null;
        try
        {
            roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        catch (Exception)
        {
            // Sandboxed environments can fail to resolve the folder.
        }

        if (!string.IsNullOrEmpty(roaming))
        {
            yield return Path.Combine(roaming, "KuGou8");
        }
    }

    /// <summary>Parses KuGou.ini content (pure; exposed for tests).</summary>
    internal static PlayerPlaybackState? Parse(string? ini, DateTimeOffset sampledAtUtc)
    {
        if (string.IsNullOrWhiteSpace(ini))
        {
            return null;
        }

        var section = string.Empty;
        var rawTitle = string.Empty;
        long positionMs = -1;
        var isMuted = false;

        foreach (var line in ini.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');
                section = close > 1 ? trimmed[1..close] : string.Empty;
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator];
            var value = trimmed[(separator + 1)..];
            if (string.Equals(section, PlaybackSection, StringComparison.Ordinal))
            {
                if (string.Equals(key, TitleKey, StringComparison.Ordinal))
                {
                    rawTitle = value.Trim();
                }
                else if (string.Equals(key, PositionKey, StringComparison.Ordinal)
                         && long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                             out var parsed))
                {
                    positionMs = parsed;
                }
            }
            else if (string.Equals(section, PlayerSection, StringComparison.Ordinal)
                     && string.Equals(key, MuteKey, StringComparison.Ordinal))
            {
                isMuted = string.Equals(value.Trim(), "1", StringComparison.Ordinal);
            }
        }

        if (string.IsNullOrWhiteSpace(rawTitle) || positionMs < 0)
        {
            return null;
        }

        return new PlayerPlaybackState(rawTitle, TimeSpan.FromMilliseconds(positionMs), sampledAtUtc, isMuted);
    }
}