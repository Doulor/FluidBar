using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Threading;

namespace FluidBar;

public sealed class MediaPlugin : IIslandPlugin
{
    public string Id => "media";
    public string Name => "媒体播放";
    public string Description => "显示当前媒体来源、曲目、播放状态、进度、波形";
    public string Icon => "\uE768";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => _config;
    public event Action<IslandEvent>? EventTriggered;

    private readonly DispatcherTimer _timer;
    private MediaPluginSettings _settings;
    private MediaPluginConfig? _config;
    private ILyricsProvider _lyricsProvider = new NullLyricsProvider();
    private IMediaSessionProvider? _sessionProvider;
    private string _lastSignature = string.Empty;
    private bool _isPolling;
    private bool _disposed;
    private bool _lastFromGsm;
    private bool _suppressFallbackUntilGsmPlaying;
    private readonly KugouLyricsProvider _kugouLyrics = new();

    // Wall-clock position estimation for fallback sources (Kugou etc.)
    private string? _fallbackTrackKey;
    private DateTime _fallbackLastPollTime = DateTime.UtcNow;
    private long _fallbackEstimatedTicks;
    private bool _fallbackIsPaused;

    // Throttle enrichment to avoid repeated HTTP calls
    private string? _lastEnrichmentKey;
    private MediaSnapshot? _lastEnrichedSnapshot;
    private string? _currentTrackKey;
    private bool _bgEnrichmentPending;

    // Track lyric changes separately (lyrics excluded from main signature to avoid island jump)
    private string _lastLyricSignature = string.Empty;

    // Track enrichment failures to avoid re-fetching every poll for instrumental tracks
    private string? _enrichmentFailedKey;
    private DateTime _enrichmentFailedTime = DateTime.MinValue;

    // Cache known media process list to avoid Process.GetProcesses() every poll
    private static List<MediaFallbackProcessInfo> _cachedKnownProcesses = [];
    private static Dictionary<uint, (string ProcName, string FriendlyName)> _cachedKnownProcessIds = new();
    private static DateTime _processCacheTime = DateTime.MinValue;
    private static readonly TimeSpan ProcessCacheTtl = TimeSpan.FromSeconds(5);

    public IMediaSessionProvider? SessionProvider => _sessionProvider;

    // Known media player process names → friendly source name
    private static readonly Dictionary<string, string> FallbackPlayers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kugou"]      = "酷狗音乐",
        ["KuGou"]      = "酷狗音乐",
        ["KGMusic"]    = "酷狗音乐",
        ["KuGouMusic"] = "酷狗音乐",
        ["cloudmusic"] = "网易云音乐",
        ["qqmusic"]    = "QQ 音乐",
        ["kwmusic"]    = "酷我音乐",
        ["spotify"]    = "Spotify",
        ["wmplayer"]   = "Media Player",
    };

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public MediaPlugin()
    {
        _settings = MediaPluginSettings.Load();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 400, 5000))
        };
        _timer.Tick += (_, _) => _ = SafePollAsync();
    }

    public void Initialize()
    {
        _config = new MediaPluginConfig(_settings);
        _lyricsProvider = _settings.ShowLyrics
            ? _kugouLyrics
            : new NullLyricsProvider();

        try
        {
            _sessionProvider = MediaSessionProviderFactory.Create();
        }
        catch
        {
            _sessionProvider = null;
        }
    }

    public void Start()
    {
        if (_disposed || _timer.IsEnabled)
            return;
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.PollIntervalMs, 400, 5000));
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _config?.Save();
    }

    private async Task SafePollAsync()
    {
        if (_disposed)
            return;

        try
        {
            await PollAsync();
        }
        catch
        {
        }
    }

    private async Task PollAsync()
    {
        if (_isPolling)
            return;

        _isPolling = true;
        try
        {
            // Run BOTH sources and prefer the higher-priority one.
            // This ensures music apps (Kugou etc.) take precedence over browser sessions.
            MediaSnapshot? gsmSnapshot = null;
            MediaSnapshot? fallbackSnapshot = null;

            if (_sessionProvider is not null)
                gsmSnapshot = await _sessionProvider.ReadAsync(_lyricsProvider, _settings.ShowLyrics);

            if (MediaSnapshotSelectionPolicy.ShouldQueryFallback(gsmSnapshot))
            {
                var (fb, fbAudio) = FindPlayingMediaFallback();
                fallbackSnapshot = fb;

                // Estimate playback position via wall-clock time for fallback sources
                if (fallbackSnapshot is not null)
                    fallbackSnapshot = EstimateFallbackPosition(fallbackSnapshot, fbAudio);
            }

            if (gsmSnapshot?.IsPlaying == true &&
                MediaSnapshotSelectionPolicy.GetSourcePriority(gsmSnapshot.SourceAppUserModelId) >= 100)
            {
                _suppressFallbackUntilGsmPlaying = false;
            }

            var suppressFallback = MediaSnapshotSelectionPolicy.ShouldSuppressFallbackAfterGsmLoss(
                _lastFromGsm,
                _suppressFallbackUntilGsmPlaying,
                gsmSnapshot,
                fallbackSnapshot);

            if (suppressFallback)
                _suppressFallbackUntilGsmPlaying = true;

            var snapshot = suppressFallback
                ? gsmSnapshot
                : MediaSnapshotSelectionPolicy.ChooseBestSnapshot(gsmSnapshot, fallbackSnapshot);

            if (snapshot is null)
            {
                if (!string.IsNullOrEmpty(_lastSignature))
                {
                    _lastSignature = string.Empty;
                    EventTriggered?.Invoke(MediaIslandEventFactory.CreateStopped());
                }
                return;
            }

            // Track active provider
            _lastFromGsm = gsmSnapshot is not null || (snapshot.PositionTicks > 0 && fallbackSnapshot is null);

            if (!snapshot.IsPlaying && !_settings.ShowWhenPaused)
            {
                if (gsmSnapshot is not null)
                    _suppressFallbackUntilGsmPlaying = true;

                // Send stopped event so MainWindow restarts collapse timer
                if (!string.IsNullOrEmpty(_lastSignature))
                {
                    _lastSignature = string.Empty;
                    EventTriggered?.Invoke(MediaIslandEventFactory.CreateStopped());
                }
                return;
            }

            // Enrich music apps with Kugou lyric and artwork metadata.
            // For new tracks: fire event immediately (no lyrics), then enrich async.
            // For same tracks: use cached lyrics (no HTTP).
            if (MediaSnapshotSelectionPolicy.GetSourcePriority(snapshot.SourceAppUserModelId) >= 100)
            {
                var enrichKey = $"{snapshot.Title}|{snapshot.Artist}";
                _currentTrackKey = enrichKey;
                var needsLyric = string.IsNullOrWhiteSpace(snapshot.LyricLine);
                var needsArt = string.IsNullOrWhiteSpace(snapshot.AlbumArtPath);
                var trackChanged = enrichKey != _lastEnrichmentKey;

                if (trackChanged)
                {
                    // New track — clear old enrichment, fire event immediately
                    _lastEnrichmentKey = enrichKey;
                    _lastEnrichedSnapshot = null;
                    // Clear old lyrics to prevent stale lyrics from previous song
                    snapshot = snapshot with { LyricLine = null, SecondaryLyricLine = null };

                    // Publish basic snapshot now (island appears immediately)
                    var signature0 = MediaSnapshotSelectionPolicy.BuildSignature(snapshot);
                    if (signature0 != _lastSignature)
                    {
                        _lastSignature = signature0;
                        _lastLyricSignature = string.Empty;
                        EventTriggered?.Invoke(MediaIslandEventFactory.FromSnapshot(snapshot));
                    }

                    // Enrich in background — lyrics will arrive on next poll
                    if (needsLyric || needsArt)
                    {
                        _bgEnrichmentPending = true;
                        var snap = snapshot;
                        var pos = snapshot.PositionTicks > 0
                            ? TimeSpan.FromTicks(snapshot.PositionTicks)
                            : TimeSpan.Zero;
                        _ = Task.Run(() => EnrichInBackground(snap, pos, enrichKey));
                    }
                }
                else if (_lastEnrichedSnapshot is not null)
                {
                    // Same track with cached enrichment — reuse it (preserves album art, avoids HTTP)
                    snapshot = _lastEnrichedSnapshot with
                    {
                        IsPlaying = snapshot.IsPlaying,
                        ProgressPercent = snapshot.ProgressPercent,
                        PositionTicks = snapshot.PositionTicks,
                        LastUpdatedTicks = snapshot.LastUpdatedTicks,
                    };
                    // Re-select lyrics based on current position (no HTTP, reads from cache)
                    var pos = snapshot.PositionTicks > 0
                        ? TimeSpan.FromTicks(snapshot.PositionTicks)
                        : TimeSpan.Zero;
                    var reselected = _kugouLyrics.ReSelectLyrics(snapshot, pos);
                    if (reselected is not null)
                        snapshot = reselected;
                }
                else if (!_bgEnrichmentPending && (needsLyric || needsArt))
                {
                    // Same track, missing data — throttle for instrumental tracks (skip app-name titles)
                    var isAppTitle = MediaSnapshotSelectionPolicy.IsAppNameString(snapshot.Title);
                    var isThrottled = !isAppTitle &&
                                      enrichKey == _enrichmentFailedKey &&
                                      (DateTime.UtcNow - _enrichmentFailedTime).TotalMinutes < 5;
                    if (!isThrottled)
                    {
                        var pos = snapshot.PositionTicks > 0
                            ? TimeSpan.FromTicks(snapshot.PositionTicks)
                            : TimeSpan.Zero;
                        var enriched = _kugouLyrics.EnrichSnapshot(snapshot, pos);
                        if (string.IsNullOrWhiteSpace(enriched.LyricLine) && needsLyric && !isAppTitle)
                        {
                            _enrichmentFailedKey = enrichKey;
                            _enrichmentFailedTime = DateTime.UtcNow;
                            enriched = enriched with { LyricLine = "纯音乐，请欣赏" };
                        }
                        snapshot = enriched;
                    }
                    else
                    {
                        // Throttled — enrichment failed before, show instrumental message
                        if (needsLyric && !isAppTitle)
                            snapshot = snapshot with { LyricLine = "纯音乐，请欣赏" };
                    }
                }
            }

            var signature = MediaSnapshotSelectionPolicy.BuildSignature(snapshot);
            var lyricSig = $"{snapshot.LyricLine ?? ""}|{snapshot.SecondaryLyricLine ?? ""}";

            if (signature == _lastSignature)
            {
                // Same song — update lyrics if they changed (via ProcessEvent lyric-only fast path)
                if (lyricSig != _lastLyricSignature)
                {
                    _lastLyricSignature = lyricSig;
                    EventTriggered?.Invoke(MediaIslandEventFactory.FromSnapshot(snapshot));
                }
                return;
            }

            _lastSignature = signature;
            _lastLyricSignature = lyricSig;
            EventTriggered?.Invoke(MediaIslandEventFactory.FromSnapshot(snapshot));
        }
        catch
        {
        }
        finally
        {
            _isPolling = false;
        }
    }

    private MediaSnapshot EstimateFallbackPosition(MediaSnapshot snapshot, ProcessAudioPlaybackState audioState)
    {
        var trackKey = $"{snapshot.Title}|{snapshot.Artist}";
        var now = DateTime.UtcNow;
        var isPlaying = audioState != ProcessAudioPlaybackState.NotPlaying;

        if (trackKey != _fallbackTrackKey)
        {
            // Song changed — reset tracking, start with small offset to account for detection delay
            _fallbackTrackKey = trackKey;
            _fallbackEstimatedTicks = TimeSpan.FromSeconds(2).Ticks; // ~2s detection lag
            _fallbackIsPaused = !isPlaying;
            _fallbackLastPollTime = now;
        }
        else if (isPlaying)
        {
            // Same song, playing — advance estimated position
            var delta = (now - _fallbackLastPollTime).TotalSeconds;
            if (delta > 0 && delta < 30) // guard against clock jumps
                _fallbackEstimatedTicks += (long)(delta * TimeSpan.TicksPerSecond);
            _fallbackIsPaused = false;
            _fallbackLastPollTime = now;
        }
        else
        {
            // Same song, paused — freeze position
            _fallbackIsPaused = true;
            _fallbackLastPollTime = now;
        }

        return snapshot with
        {
            PositionTicks = _fallbackEstimatedTicks,
            LastUpdatedTicks = Environment.TickCount64
        };
    }

    private async void EnrichInBackground(MediaSnapshot snapshot, TimeSpan position, string enrichKey)
    {
        try
        {
            var enriched = await Task.Run(() => _kugouLyrics.EnrichSnapshot(snapshot, position));
            // Only store if the song hasn't changed during the async enrichment
            if (enrichKey != _currentTrackKey)
            {
                _bgEnrichmentPending = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(enriched.LyricLine) ||
                !string.IsNullOrWhiteSpace(enriched.AlbumArtPath))
            {
                _lastEnrichmentKey = enrichKey;
                _lastEnrichedSnapshot = enriched;
                _enrichmentFailedKey = null;
                _bgEnrichmentPending = false;
                // Update signature so the next poll doesn't overwrite with basic snapshot
                _lastSignature = MediaSnapshotSelectionPolicy.BuildSignature(enriched);
                _timer.Dispatcher.BeginInvoke(() =>
                    EventTriggered?.Invoke(MediaIslandEventFactory.FromSnapshot(enriched)));
            }
            else
            {
                _bgEnrichmentPending = false;
                _enrichmentFailedKey = enrichKey;
                _enrichmentFailedTime = DateTime.UtcNow;
            }
        }
        catch { _bgEnrichmentPending = false; }
    }

    private static bool HasArtistSongPattern(string title, string processName)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        // Standard pattern: "Artist - Song" (with spaces)
        if (title.Contains(" - ", StringComparison.Ordinal))
            return true;

        // Kugou pattern: "Artist-Song" or "Artist1、Artist2-Song" (no spaces around dash)
        var isKugou = processName.Contains("kugou", StringComparison.OrdinalIgnoreCase);
        if (isKugou)
        {
            var dashIndex = title.IndexOf('-');
            if (dashIndex > 0 && dashIndex < title.Length - 1)
            {
                // Verify both sides look like real text (not UUIDs, not app names)
                var left = title[..dashIndex].Trim();
                var right = title[(dashIndex + 1)..].Trim();
                if (left.Length >= 1 && right.Length >= 1 &&
                    !left.Contains("酷狗") && !right.Contains("酷狗") &&
                    !left.Contains("kugou", StringComparison.OrdinalIgnoreCase) &&
                    !right.Contains("kugou", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [ThreadStatic]
    private static List<(IntPtr Hwnd, int ProcessId, string Title, string ProcessName, string SourceName)>? _fallbackWindows;

    private static (MediaSnapshot? Snapshot, ProcessAudioPlaybackState AudioState) FindPlayingMediaFallback()
    {
        // Refresh known media process cache (Process.GetProcesses is expensive)
        var now = DateTime.UtcNow;
        if (now - _processCacheTime > ProcessCacheTtl)
        {
            _processCacheTime = now;
            var newIds = new Dictionary<uint, (string ProcName, string FriendlyName)>();
            var newProcesses = new List<MediaFallbackProcessInfo>();
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (FallbackPlayers.TryGetValue(process.ProcessName, out var friendlyName))
                    {
                        newIds[(uint)process.Id] = (process.ProcessName, friendlyName);
                        newProcesses.Add(new MediaFallbackProcessInfo(
                            process.Id,
                            process.ProcessName,
                            friendlyName));
                    }
                }
                catch { }
            }
            _cachedKnownProcessIds = newIds;
            _cachedKnownProcesses = newProcesses;
        }

        var knownProcessIds = _cachedKnownProcessIds;
        var knownProcesses = _cachedKnownProcesses;

        if (knownProcessIds.Count == 0)
            return (null, ProcessAudioPlaybackState.Unknown);

        // Enumerate all top-level windows and match by process ID
        _fallbackWindows = new List<(IntPtr, int, string, string, string)>();

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!knownProcessIds.TryGetValue(pid, out var info))
                return true;

            // For known media processes, accept both visible and hidden windows
            // (Kugou minimized to tray still has a hidden main window with the song title)
            if (!IsWindowVisible(hWnd))
            {
                // Hidden window — only accept if it has a title (song info)
                var hiddenTitle = GetWindowTitle(hWnd);
                if (!string.IsNullOrWhiteSpace(hiddenTitle) &&
                    !hiddenTitle.Contains("桌面歌词", StringComparison.Ordinal))
                {
                    _fallbackWindows!.Add((hWnd, (int)pid, hiddenTitle, info.ProcName, info.FriendlyName));
                }
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (!string.IsNullOrWhiteSpace(title))
            {
                // Skip Kugou desktop lyrics window — it's not a media source
                if (title.Contains("桌面歌词", StringComparison.Ordinal))
                    return true;

                _fallbackWindows!.Add((hWnd, (int)pid, title, info.ProcName, info.FriendlyName));

                // Also search child windows recursively for song info
                if (!title.Contains(" - ", StringComparison.Ordinal))
                {
                    SearchChildWindowsRecursive(hWnd, info.ProcName, info.FriendlyName);
                }
            }

            return true;
        }, IntPtr.Zero);

        var candidates = _fallbackWindows;
        _fallbackWindows = null;

        // For Kugou: if no candidate has artist-song pattern, try UI Automation to read song info
        if (!candidates.Any(c => HasArtistSongPattern(c.Title, c.ProcessName)))
        {
            var kugouCandidate = candidates.FirstOrDefault(c =>
                c.ProcessName.Contains("kugou", StringComparison.OrdinalIgnoreCase));
            if (kugouCandidate.Hwnd != IntPtr.Zero)
            {
                var uiaTitle = TryGetKugouSongViaUIA(kugouCandidate.Hwnd);
                if (!string.IsNullOrWhiteSpace(uiaTitle) && uiaTitle.Length > 1)
                {
                    candidates.Add((kugouCandidate.Hwnd, kugouCandidate.ProcessId, uiaTitle, kugouCandidate.ProcessName, kugouCandidate.SourceName));
                }
            }
        }

        // Prefer windows with artist-song pattern, then longest title
        // AUMID-like titles (e.g. {4FF7DEC0-EDE4-46DA-835F-...}) will have short length
        // and no artist-song pattern, so they naturally rank lower
        var best = candidates
            .OrderByDescending(t => HasArtistSongPattern(t.Title, t.ProcessName) ? 100 : t.Title.Length)
            .FirstOrDefault();

        if (best.Title is null)
            return (null, ProcessAudioPlaybackState.Unknown);

        var (artist, song) = ParseMediaTitle(best.Title, best.ProcessName);
        if (string.IsNullOrWhiteSpace(song))
            return (null, ProcessAudioPlaybackState.Unknown);

        // Don't create a snapshot when the "song" is just an app name (e.g. "酷狗音乐")
        // — the app is open but not playing anything identifiable.
        if (MediaSnapshotSelectionPolicy.IsAppNameString(song))
            return (null, ProcessAudioPlaybackState.Unknown);

        var audioGateProcessIds = MediaFallbackProcessPolicy.AudioGateProcessIds(
            knownProcesses,
            best.ProcessName,
            best.SourceName);

        var audioState = ProcessAudioActivity.GetAnyProcessPlaybackState(audioGateProcessIds);
        if (!MediaFallbackProcessPolicy.ShouldAcceptFallbackPlayback(
                best.ProcessName,
                best.SourceName,
                song,
                audioState))
        {
            return (null, audioState);
        }

        var sourceIconPath = MediaSourceVisuals.ExtractAppIconPath(best.ProcessName);

        return (new MediaSnapshot(
            SourceAppUserModelId: best.ProcessName,
            SourceName: best.SourceName,
            Title: song,
            Artist: artist,
            Album: "",
            IsPlaying: true,
            ProgressPercent: -1, // No progress data for fallback sources
            LyricLine: null,
            SourceIconPath: sourceIconPath), audioState);
    }

    private static void SearchChildWindowsRecursive(IntPtr parent, string processName, string sourceName)
    {
        var isKugou = processName.Contains("kugou", StringComparison.OrdinalIgnoreCase)
            || sourceName.Contains("酷狗", StringComparison.Ordinal);
        EnumChildWindows(parent, (childHwnd, _) =>
        {
            var childTitle = GetWindowTitle(childHwnd);
            if (!string.IsNullOrWhiteSpace(childTitle))
            {
                // Standard pattern: "Artist - Song"
                if (childTitle.Contains(" - ", StringComparison.Ordinal))
                {
                    GetWindowThreadProcessId(childHwnd, out var childPid);
                    _fallbackWindows!.Add((childHwnd, (int)childPid, childTitle, processName, sourceName));
                    return false; // Found, stop this branch
                }
                // Kugou: also accept non-generic text that looks like a song name
                if (isKugou && childTitle.Length > 2 && childTitle.Length < 200 &&
                    !childTitle.Contains("酷狗") && !childTitle.Contains("桌面歌词") &&
                    !childTitle.Contains("Lyric", StringComparison.OrdinalIgnoreCase) &&
                    !childTitle.Contains("kugou", StringComparison.OrdinalIgnoreCase))
                {
                    GetWindowThreadProcessId(childHwnd, out var childPid);
                    _fallbackWindows!.Add((childHwnd, (int)childPid, childTitle, processName, sourceName));
                }
            }
            // Recurse into grandchildren
            SearchChildWindowsRecursive(childHwnd, processName, sourceName);
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>Use UI Automation to read song title from Kugou's window accessible tree.</summary>
    private static string? TryGetKugouSongViaUIA(IntPtr hwnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element is null) return null;

            // Search direct children for text elements that could be the song title
            var children = element.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
            {
                try
                {
                    var name = child.Current.Name;
                    if (!string.IsNullOrWhiteSpace(name) &&
                        name.Length > 1 && name.Length < 100 &&
                        !name.Contains("酷狗") && !name.Contains("桌面歌词") &&
                        !name.Contains("kugou", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("Lyric", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("最小化") && !name.Contains("最大化") &&
                        !name.Contains("关闭") && !name.Contains("Menu"))
                    {
                        return name;
                    }
                }
                catch { }
            }

            // Also try grandchildren (one level deeper)
            foreach (AutomationElement child in children)
            {
                try
                {
                    var grandchildren = child.FindAll(TreeScope.Children, Condition.TrueCondition);
                    foreach (AutomationElement gc in grandchildren)
                    {
                        try
                        {
                            var name = gc.Current.Name;
                            if (!string.IsNullOrWhiteSpace(name) &&
                                name.Length > 1 && name.Length < 100 &&
                                !name.Contains("酷狗") && !name.Contains("桌面歌词") &&
                                !name.Contains("kugou", StringComparison.OrdinalIgnoreCase) &&
                                !name.Contains("Lyric", StringComparison.OrdinalIgnoreCase) &&
                                !name.Contains("最小化") && !name.Contains("最大化") &&
                                !name.Contains("关闭") && !name.Contains("Menu"))
                            {
                                return name;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static (string Artist, string Song) ParseMediaTitle(string title, string processName)
    {
        if (processName.Contains("kugou", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("kgmusic", StringComparison.OrdinalIgnoreCase))
        {
            var cleanTitle = title
                .Replace(" - 桌面歌词", "", StringComparison.Ordinal)
                .Replace(" - Kugou Music", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" - 酷狗音乐", "", StringComparison.Ordinal)
                .Replace(" - 酷狗", "", StringComparison.Ordinal)
                .Replace("-酷狗音乐", "", StringComparison.Ordinal)
                .Replace("-酷狗", "", StringComparison.Ordinal)
                .Replace("桌面歌词", "", StringComparison.Ordinal)
                .Trim();

            // Try to split "Artist-Song" or "Artist - Song"
            var dashIdx = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx <= 0)
                dashIdx = cleanTitle.IndexOf('-');
            if (dashIdx > 0 && dashIdx < cleanTitle.Length - 1)
            {
                var artist = cleanTitle[..dashIdx].Trim();
                var song = cleanTitle[(dashIdx + (cleanTitle[dashIdx] == '-' ? 1 : 3))..].Trim();
                if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(song))
                    return (artist, song);
            }

            return ("", cleanTitle);
        }

        var dashIndex = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0 && dashIndex < title.Length - 3)
        {
            var part1 = title[..dashIndex].Trim();
            var part2 = title[(dashIndex + 3)..].Trim();

            var extraDash = part2.LastIndexOf(" - ", StringComparison.Ordinal);
            if (extraDash > 0)
            {
                var suffix = part2[(extraDash + 3)..].Trim();
                if (suffix.Contains("云音乐", StringComparison.Ordinal) ||
                    suffix.Contains("Music", StringComparison.Ordinal) ||
                    suffix.Contains("音乐", StringComparison.Ordinal) ||
                    suffix.Contains("酷狗", StringComparison.Ordinal))
                {
                    part2 = part2[..extraDash].Trim();
                }
            }

            // NetEase uses "Song - Artist" format (opposite of Kugou's "Artist - Song")
            if (processName.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase) ||
                processName.Contains("netease", StringComparison.OrdinalIgnoreCase))
                return (part2, part1);  // Song first, artist second

            return (part1, part2);  // Default: artist first, song second
        }

        return ("", title.Trim());
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

}
