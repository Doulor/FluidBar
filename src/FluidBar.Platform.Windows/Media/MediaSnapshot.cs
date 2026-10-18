namespace FluidBar.Platform.Windows.Media;

/// <summary>Playback state mirrored from GSMTC.</summary>
public enum MediaPlaybackState
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

/// <summary>Immutable media snapshot published by the session service.</summary>
/// <param name="SourceAppUserModelId">AUMID of the owning app (stable identity).</param>
/// <param name="SourceDisplayName">Friendly app name when available.</param>
/// <param name="Title">Track title.</param>
/// <param name="Artist">Artist.</param>
/// <param name="Album">Album.</param>
/// <param name="PlaybackState">Playback state.</param>
/// <param name="IsPlaying">Convenience flag (state == Playing).</param>
/// <param name="Position">Last known playback position (App interpolates; the platform layer has no frame timer).</param>
/// <param name="PositionUpdatedAtUtc">When <paramref name="Position"/> was sampled (for App-side interpolation).</param>
/// <param name="Duration">Track duration when known.</param>
/// <param name="CanSeek">Whether the session supports seeking.</param>
/// <param name="CanGoNext">Whether next is available.</param>
/// <param name="CanGoPrevious">Whether previous is available.</param>
/// <param name="TrackKey">Stable key for the current track/artwork (empty when unknown).</param>
public sealed record MediaSnapshot(
    string SourceAppUserModelId,
    string? SourceDisplayName,
    string Title,
    string Artist,
    string Album,
    MediaPlaybackState PlaybackState,
    bool IsPlaying,
    TimeSpan Position,
    DateTimeOffset PositionUpdatedAtUtc,
    TimeSpan Duration,
    bool CanSeek,
    bool CanGoNext,
    bool CanGoPrevious,
    string TrackKey)
{
    /// <summary>An empty/"no session" snapshot.</summary>
    public static MediaSnapshot Empty { get; } = new(
        string.Empty, null, string.Empty, string.Empty, string.Empty, MediaPlaybackState.Unknown, false,
        TimeSpan.Zero, DateTimeOffset.MinValue, TimeSpan.Zero, false, false, false, string.Empty);

    /// <summary>True when there is no usable session at all.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(SourceAppUserModelId);

    /// <summary>Builds the content signature used by activity coalescing (progress/position excluded).</summary>
    public string BuildContentSignature() => string.Join('|', SourceAppUserModelId, Title, Artist, Album,
        IsPlaying ? "1" : "0", TrackKey);
}

/// <summary>Minimal session information used by the pure selection policy.</summary>
/// <param name="SourceAppUserModelId">Owning app AUMID.</param>
/// <param name="State">Playback state.</param>
/// <param name="Title">Track title (used to reject app-name-only sessions).</param>
/// <param name="LastUpdated">When the session last reported an update (tie-break).</param>
public sealed record MediaSessionDescriptor(
    string SourceAppUserModelId,
    MediaPlaybackState State,
    string? Title,
    DateTimeOffset LastUpdated)
{
    public bool IsPlaying => State == MediaPlaybackState.Playing;
}

/// <summary>
/// Pure media source selection (rewritten from v1 semantics, no code copied).
/// </summary>
/// <remarks>
/// Ranking: playing beats paused beats stopped; known music-first players beat browsers which beat
/// everything else; a session whose title is only the app name is demoted (v1 "app name as title" bug);
/// ties are broken by most recent update then stable AUMID ordering.
/// </remarks>
public static class MediaSourceSelectionPolicy
{
    private static readonly string[] PreferredPlayers =
    [
        "kugou", "kgmusic", "cloudmusic", "netease", "qqmusic", "kwmusic", "spotify", "applemusic",
        "zune", "music.ui", "foobar", "aimp", "musicbee",
    ];

    private static readonly string[] Browsers =
    [
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi",
    ];

    private static readonly string[] AppNameTitles =
    [
        "酷狗音乐", "网易云音乐", "qq音乐", "qq 音乐", "酷我音乐", "spotify", "media player", "kugou",
        "cloudmusic", "qqmusic", "kwmusic",
    ];

    /// <summary>Selects the session that should be shown, or null when none qualifies.</summary>
    public static MediaSessionDescriptor? SelectActive(IReadOnlyList<MediaSessionDescriptor> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        MediaSessionDescriptor? best = null;
        var bestScore = int.MinValue;
        foreach (var session in sessions)
        {
            if (string.IsNullOrWhiteSpace(session.SourceAppUserModelId))
            {
                continue;
            }

            if (session.State is MediaPlaybackState.Closed or MediaPlaybackState.Unknown)
            {
                continue;
            }

            var score = Score(session);
            if (score > bestScore)
            {
                best = session;
                bestScore = score;
            }
            else if (score == bestScore && best is not null && IsBetterTieBreak(session, best))
            {
                best = session;
            }
        }

        return best;
    }

    /// <summary>Score used by <see cref="SelectActive"/> (exposed for tests/diagnostics).</summary>
    public static int Score(MediaSessionDescriptor session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var score = session.State switch
        {
            MediaPlaybackState.Playing => 1000,
            MediaPlaybackState.Paused => 500,
            MediaPlaybackState.Opened or MediaPlaybackState.Changing => 250,
            MediaPlaybackState.Stopped => 100,
            _ => 0,
        };

        score += GetSourcePriority(session.SourceAppUserModelId);
        if (!string.IsNullOrWhiteSpace(session.Title) && IsAppNameOnlyTitle(session.Title))
        {
            score -= 400;
        }

        return score;
    }

    /// <summary>Known music-first player = 300, browser = 150, everything else = 10.</summary>
    public static int GetSourcePriority(string? sourceAppUserModelId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return 0;
        }

        var normalized = sourceAppUserModelId.ToLowerInvariant();
        if (PreferredPlayers.Any(player => normalized.Contains(player, StringComparison.Ordinal)))
        {
            return 300;
        }

        if (Browsers.Any(browser => normalized.Contains(browser, StringComparison.Ordinal)))
        {
            return 150;
        }

        return 10;
    }

    /// <summary>True when the title is just the app name (v1 showed "Spotify" as the track title).</summary>
    public static bool IsAppNameOnlyTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var trimmed = title.Trim();
        return AppNameTitles.Any(name => trimmed.Equals(name, StringComparison.OrdinalIgnoreCase))
               || IsAumidLookingTitle(trimmed);
    }

    /// <summary>True when a string looks like a raw AUMID ({GUID} form) rather than a track title.</summary>
    public static bool IsAumidLookingTitle(string? text)
        => !string.IsNullOrWhiteSpace(text) && text.Length > 30 && text.StartsWith('{') && text.EndsWith('}')
           && text.Contains('-', StringComparison.Ordinal);

    /// <summary>True when the two AUMIDs belong to the same player family (e.g. packaged vs unpackaged).</summary>
    public static bool IsSamePlayer(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftPriority = GetSourcePriority(left);
        var rightPriority = GetSourcePriority(right);
        if (leftPriority != rightPriority)
        {
            return false;
        }

        foreach (var player in PreferredPlayers.Concat(Browsers))
        {
            if (left.Contains(player, StringComparison.OrdinalIgnoreCase)
                && right.Contains(player, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBetterTieBreak(MediaSessionDescriptor candidate, MediaSessionDescriptor current)
    {
        var updatedComparison = candidate.LastUpdated.CompareTo(current.LastUpdated);
        if (updatedComparison != 0)
        {
            return updatedComparison > 0;
        }

        return string.CompareOrdinal(candidate.SourceAppUserModelId, current.SourceAppUserModelId) < 0;
    }

    /// <summary>Builds a stable track key ("aumid|title|artist|album") for artwork/lyrics caches.</summary>
    public static string BuildTrackKey(string sourceAppUserModelId, string? title, string? artist, string? album)
        => string.Join('|', sourceAppUserModelId ?? string.Empty, title ?? string.Empty, artist ?? string.Empty,
            album ?? string.Empty);
}
