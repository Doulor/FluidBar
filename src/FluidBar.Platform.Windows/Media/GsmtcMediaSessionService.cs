using FluidBar.Core.Configuration;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FluidBar.Platform.Windows.Media;

/// <summary>Event args for a freshly decoded (header-parsed) artwork blob.</summary>
public sealed class MediaArtworkEventArgs : EventArgs
{
    public MediaArtworkEventArgs(string trackKey, ArtworkEntry artwork)
    {
        TrackKey = trackKey;
        Artwork = artwork;
    }

    public string TrackKey { get; }

    public ArtworkEntry Artwork { get; }
}

/// <summary>
/// Event-driven GSMTC media session service.
/// </summary>
/// <remarks>
/// <para>All data comes from <c>GlobalSystemMediaTransportControlsSessionManager</c> events
/// (<c>SessionsChanged</c>, <c>CurrentSessionChanged</c>, <c>MediaPropertiesChanged</c>,
/// <c>PlaybackInfoChanged</c>, <c>TimelinePropertiesChanged</c>) — no polling (v1 scanned every 500 ms).</para>
/// <para>Snapshots are immutable and change-only; artwork is fetched asynchronously, cancelled on track
/// change and stored in a byte/entry-bounded cache. Transport commands target only the active session
/// (no global key injection).</para>
/// </remarks>
public sealed class GsmtcMediaSessionService : PlatformMonitor<MediaSnapshot>, IMediaSessionSource
{
    private static readonly TimeSpan EventRefreshDebounce = TimeSpan.FromMilliseconds(120);

    /// <summary>How long the fallback tolerates an unresolvable player before publishing "no session".</summary>
    public static readonly TimeSpan FallbackMissGrace = TimeSpan.FromSeconds(2.5);

    /// <summary>Empty fallback polls tolerated before "nothing is playing" (a *time* budget: the poll runs
    /// every 250 ms, so a fixed tick count would drop the session after 750 ms of a track switch, which is
    /// exactly when the player is briefly unresolvable).</summary>
    public static int FallbackMissThreshold => Math.Max(1,
        (int)Math.Round(FallbackMissGrace.TotalMilliseconds / FallbackPollInterval.TotalMilliseconds));

    private static readonly TimeSpan ArtworkFetchTimeout = TimeSpan.FromSeconds(10);
    private const int MaxArtworkBytes = 4 * 1024 * 1024;

    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly ArtworkCache _artworkCache;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _sessionGate = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSessionUpdate = new(StringComparer.Ordinal);
    private readonly object _artworkGate = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private BackgroundPoller? _fallbackPoller;

    /// <summary>Window-title fallback poll period (the accessibility read is cheap; 250 ms tracks taps).</summary>
    public static readonly TimeSpan FallbackPollInterval = TimeSpan.FromMilliseconds(250);
    private readonly IMediaKeySender? _mediaKeySender;
    private readonly FallbackPlaybackTracker _fallbackPlayback = new();
    private readonly object _accessibilityGate = new();
    private PlayerAccessibilitySession? _accessibility;
    private int _accessibilityProcessId;
    private long _accessibilityRetryAfterMs;
    private const int AccessibilityRetryBackoffMs = 2500;
    private int _fallbackMissCount;

    /// <summary>Last play/pause state confirmed by the player accessibility glyph (null until read).</summary>
    private bool? _fallbackPlayingHint;

    /// <summary>When the current pause started (0 = playing). Drives the fast resume cadence and the hide delay.</summary>
    private long _fallbackPausedSinceMs;

    /// <summary>True while the paused track has been hidden from the island (5 s after the pause).</summary>
    private bool _fallbackPausedHidden;

    /// <summary>Last published fallback snapshot and the player it belongs to.</summary>
    private MediaSnapshot? _fallbackLastSnapshot;
    private int _fallbackLastPid;
    private long _fallbackLastSeenMs;

    /// <summary>Last published play state, used to log transitions only (one line per flip).</summary>
    private bool? _lastPublishedPlaying;

    /// <summary>Throttles the cross-check log line (arbitration runs on every poll).</summary>
    /// <summary>Lead that cancels the truncation bias of the player's one-second-resolution clock.</summary>
    private static readonly TimeSpan AccessibilityLead = TimeSpan.FromMilliseconds(500);

    private long _lastCrossCheckLogMs;

    /// <summary>Write time of the state file we are currently extrapolating.</summary>
    /// <summary>Master switch for the state-file arbitration. Off while the two clocks fight.</summary>
    private const bool CrossCheckEnabled = false;

    private DateTime? _stateFileWrittenAt;

    /// <summary>Playing time accumulated since that write (wall time would over-count pauses).</summary>
    private double _stateFilePlayingMs;

    /// <summary>Timestamp of the previous poll, used to accumulate the above.</summary>
    private DateTimeOffset? _lastPollAt;

    /// <summary>Consecutive polls where the audio meter reported silence.</summary>
    private int _audioSilentPolls;
    private volatile bool _fallbackActive;
    private CancellationTokenSource? _artworkCts;
    private int _refreshPending;
    private DateTimeOffset _lastRefreshTimestamp = DateTimeOffset.MinValue;

    /// <param name="logger">Platform logger.</param>
    /// <param name="counters">Shared diagnostics counters.</param>
    /// <param name="artworkCache">Byte cache for album art.</param>
    /// <param name="timeProvider">Injected clock (tests/diagnostics).</param>
    /// <param name="mediaKeySender">
    /// Media-key sender used by the window-title fallback (players without SMTC expose no transport
    /// channel through GSMTC). Null uses the Win32 <c>SendInput</c> implementation.
    /// </param>
    /// <param name="artworkFetcher">
    /// Active cover lookup used when the player cached nothing (null uses a default one over
    /// <c>%APPDATA%\FluidBar\artwork</c> with the iTunes catalogue; pass a fetcher with no sources to
    /// disable network artwork in tests).
    /// </param>
    public GsmtcMediaSessionService(IPlatformLogger logger, PlatformCounters counters,
        ArtworkCache? artworkCache = null, TimeProvider? timeProvider = null,
        IMediaKeySender? mediaKeySender = null, ArtworkFetcher? artworkFetcher = null,
        Func<IslandPowerMode>? powerModeProvider = null)
        : base("media", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _artworkCache = artworkCache ?? new ArtworkCache();
        _mediaKeySender = mediaKeySender ?? new SystemMediaKeySender();
        _artworkFetcher = artworkFetcher;
        _powerModeProvider = powerModeProvider ?? (() => IslandPowerMode.Performance);
    }

    private readonly Func<IslandPowerMode> _powerModeProvider;

    private readonly ArtworkFetcher? _artworkFetcher;
    private ArtworkFetcher? _defaultArtworkFetcher;

    /// <summary>Track keys whose cover lookup was already started (bounded; prevents retry storms).</summary>
    private readonly HashSet<string> _artworkLookups = new(StringComparer.Ordinal);

    /// <summary>Active cover lookup (created on first use when the host did not inject one).</summary>
    private ArtworkFetcher? ActiveArtworkFetcher
    {
        get
        {
            if (_artworkFetcher is not null)
            {
                return _artworkFetcher;
            }

            // Two independent public catalogues: iTunes answers most Latin releases, Deezer often has the
            // CJK/remix editions iTunes lacks. Each source is tried once (no retries) inside a 4 s budget.
            var http = CreateArtworkHttpClient();
            return _defaultArtworkFetcher ??= new ArtworkFetcher(
                new ArtworkDiskCache(new ArtworkDiskCacheOptions
                {
                    DirectoryPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "FluidBar", "artwork"),
                }),
                [new ItunesArtworkSource(http), new DeezerArtworkSource(http)]);
        }
    }

    /// <summary>HTTP client for catalogue lookups (4 s timeout, no keep-alive thrash).</summary>
    private static HttpClient CreateArtworkHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(6),
        DefaultRequestHeaders = { { "User-Agent", "FluidBar/2.0 (artwork)" } },
    };

    /// <summary>
    /// Channel the next transport command would use. The window-title fallback has no SMTC session, so
    /// its commands travel as system media keys; tests/diagnostics read this without touching input.
    /// </summary>
    public TransportRouteDecision CurrentTransportRoute
        => FallbackTransportPolicy.Decide(_session is not null, _fallbackActive, _mediaKeySender is not null);

    /// <summary>The bounded artwork cache (App fetches bytes through this and decodes once).</summary>
    public ArtworkCache ArtworkCache => _artworkCache;

    /// <summary>Raised on a thread-pool thread when artwork for a track becomes available.</summary>
    public event EventHandler<MediaArtworkEventArgs>? ArtworkAvailable;

    /// <summary>Looks up artwork for a track key without a second fetch.</summary>
    public bool TryGetArtwork(string trackKey, out ArtworkEntry? artwork) => _artworkCache.TryGet(trackKey, out artwork);

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
            .AsTask(cancellationToken).ConfigureAwait(false);
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _logger.LogInfo(PlatformLogCategory.Media, "media.started", "GSMTC media service started.");
        await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);

        // Players without SMTC support (KuGou, some Chinese players) never appear in GSMTC. A 1 s
        // WASAPI session probe + window-title parser covers them; it only runs while GSMTC has no
        // active session, so the normal path stays fully event-driven.
        // 250 ms: the MSAA read is a few microseconds and the play/pause glyph is the only fast signal a
        // fallback player (KuGou) gives us — at 1 s the island's icon lagged visibly behind the tap.
        _fallbackPoller = new BackgroundPoller("media-fallback", FallbackPollInterval, PollWindowFallback, _logger);
        _fallbackPoller.Start();
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var manager = _manager;
        _manager = null;
        if (manager is not null)
        {
            manager.SessionsChanged -= OnSessionsChanged;
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        _fallbackPoller?.Dispose();
        _fallbackPoller = null;
        _fallbackActive = false;
        ReleaseAccessibility();
        DetachSession();
        CancelArtworkFetch();
        Publish(MediaSnapshot.Empty);
        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fallback tick: when GSMTC has no active session, find the process that is actually producing
    /// sound and publish its window-title metadata. No artwork/timeline exists on this path.
    /// </summary>
    private void PollWindowFallback()
    {
        if (State != PlatformMonitorState.Running || _manager is null)
        {
            return;
        }

        lock (_sessionGate)
        {
            if (_session is not null)
            {
                return;
            }
        }

        var audioOutput = MediaWindowFallback.TryGetAudioOutput(out var audioPeak);
        MediaDiagnostics.Log($"fallback tick audio={audioOutput} peak={audioPeak:0.000} misses={Volatile.Read(ref _fallbackMissCount)}");
        var candidate = MediaWindowFallback.FindCandidate();
        if (candidate is null)
        {
            // A paused player vanishes from the render-session list. Using the DEVICE-wide meter here (as
            // this branch used to) only proved that some other app was making noise, so a paused KuGou
            // looked "playing" until the browser/game went quiet — the 5-10 s latency the user reported
            // (2026-10-22). Judge the last known player's own sessions instead.
            if (_fallbackLastSnapshot is { } retained)
            {
                var retainedNowMs = _timeProviderNow().ToUnixTimeMilliseconds();
                var retainedPeak = MediaWindowFallback.GetProcessPeak(_fallbackLastPid);
                _audioSilentPolls = retainedPeak > 0.0005 ? 0 : _audioSilentPolls + 1;
                if (retainedPeak > 0.0005)
                {
                    _fallbackPausedSinceMs = 0;
                    _fallbackPausedHidden = false;
                    _fallbackLastSeenMs = retainedNowMs;
                    Publish(retained);
                    _fallbackPoller?.SetInterval(FallbackPollInterval);
                    return;
                }

                if (_audioSilentPolls >= 2)
                {
                    if (_fallbackPausedSinceMs == 0)
                    {
                        _fallbackPausedSinceMs = retainedNowMs;
                    }

                    // Keep the paused track on screen. Hiding it after 5 s made the island disappear
                    // whenever the level probe mis-read a playing player as silent (user report
                    // 2026-10-22: "音乐灵动岛不显示了").
                    Publish(retained with { IsPlaying = false });

                    _fallbackPoller?.SetInterval(FallbackPollInterval);
                    return;
                }
            }

            if (audioOutput)
            {
                // The render session proves sound is playing; a missing title is transient during
                // track switches. Keep the previous snapshot and retry at the fast cadence instead
                // of publishing an empty media snapshot.
                MediaDiagnostics.Log("fallback no-candidate audio-active -> keep snapshot, retry fast");
                _fallbackPoller?.SetInterval(FallbackPollInterval);
                return;
            }

            // A track change briefly makes the player unresolvable (its audio session and window title
            // are rewritten). Only a run of empty polls really means "nothing is playing", otherwise the
            // island flickers to the empty state and transport commands are dropped mid-switch: the last
            // snapshot stays published for FallbackMissGrace so the island keeps showing the track.
            var misses = Interlocked.Increment(ref _fallbackMissCount);
            MediaDiagnostics.Log($"fallback miss={misses}/{FallbackMissThreshold} audio={audioOutput}");
            if (misses < FallbackMissThreshold)
            {
                return;
            }

            MediaDiagnostics.Log("fallback retract: no candidate and no audio output");
            _fallbackPoller?.SetInterval(_powerModeProvider() == IslandPowerMode.Saver
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(1));
            ResetFallbackPlayback();
            if (_fallbackActive)
            {
                _fallbackActive = false;
                Publish(MediaSnapshot.Empty);
            }

            return;
        }

        Interlocked.Exchange(ref _fallbackMissCount, 0);
        MediaDiagnostics.Log($"fallback candidate app={candidate.ProcessName} title={candidate.Title} artist={candidate.Artist} audio={audioOutput} peak={audioPeak:0.000}");

        candidate = EnrichWithAccessibility(candidate);
        // Cross-check the two independent clocks. The MSAA time text has no timestamp, so a frozen text
        // is re-stamped as "now" and every later sample agrees with our own extrapolation — an offset of
        // ten seconds then never looks like a seek and is never corrected (user report 2026-10-22).
        // The state file, on the other hand, carries its own write time: when it is fresh it is the more
        // trustworthy absolute value, so a large disagreement is resolved in its favour.
        candidate = CrossCheckTimeline(candidate);
        // Experimental: KuGou keeps a millisecond position counter in its own memory; see
        // KugouMemoryPosition. No-op unless FLUIDBAR_KUGOU_MEM_POSITION=1 and always falls back.
        candidate = KugouMemoryPosition.Apply(candidate, _timeProviderNow());
        // The MSAA glyph read is the only live truth a fallback player gives us. When it is ambiguous
        // (both/neither glyph visible during a repaint) the peak heuristic used to decide — and it
        // flips the published state, so the island showed "paused" while the track was playing.
        // Hold the last confirmed glyph instead; the heuristic only seeds the very first reading.
        var playing = candidate.IsPlayingHint ?? _fallbackPlayingHint ?? DetermineFallbackPlaying(candidate);
        if (candidate.IsPlayingHint is { } confirmed)
        {
            _fallbackPlayingHint = confirmed;
        }

        // The audio meter is the only signal the player cannot lie about: a silent session is paused,
        // no matter what the transport glyph says. Three consecutive silent polls are required so a
        // quiet passage or a fade-out does not flip the icon (user report: the island kept showing
        // "playing" after the song was paused).
        // Only THIS player's output counts: the device-wide meter also carries the browser, system
        // sounds and every other app, which is why a paused KuGou still looked like it was playing.
        var playerPeak = MediaWindowFallback.GetProcessPeak(candidate.ProcessId);
        _audioSilentPolls = playerPeak > 0.0005 ? 0 : _audioSilentPolls + 1;
        if (playerPeak > 0.002)
        {
            playing = true;
            _fallbackPlayingHint = true;
        }
        else if (playing && _audioSilentPolls >= 2)
        {
            playing = false;
            _fallbackPlayingHint = false;
        }
        // 500 ms while playing: the user sees their own pause/play in KuGou within half a second instead
        // of waiting for the next one-second tick (user report 2026-10-21).
        var nowMs = _timeProviderNow().ToUnixTimeMilliseconds();
        if (playing)
        {
            _fallbackPausedSinceMs = 0;
            _fallbackPausedHidden = false;
        }
        else if (_fallbackPausedSinceMs == 0)
        {
            _fallbackPausedSinceMs = nowMs;
        }

        var pausedForMs = _fallbackPausedSinceMs == 0 ? 0 : nowMs - _fallbackPausedSinceMs;
        // While a pause is fresh keep the 250 ms cadence: that is what makes the icon flip back within half
        // a second and what makes resume instant. Only a long pause may slow down.
        var fastPause = pausedForMs < 5000;
        _fallbackPoller?.SetInterval(_powerModeProvider() == IslandPowerMode.Saver
            ? (playing ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5))
            : (playing || fastPause ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(1)));
        var snapshot = new MediaSnapshot(
            "win:" + candidate.ProcessName,
            candidate.ProcessName,
            candidate.Title,
            candidate.Artist,
            string.Empty,
            playing ? MediaPlaybackState.Playing : MediaPlaybackState.Paused,
            playing,
            // KuGou persists its position in KuGou.ini; without it the fallback publishes no timeline
            // (kept at MinValue so the App never interpolates a position that does not exist).
            candidate.HasTimeline ? candidate.Position : TimeSpan.Zero,
            candidate.HasTimeline ? candidate.PositionSampledAtUtc : DateTimeOffset.MinValue,
            candidate.HasTimeline ? candidate.Duration : TimeSpan.Zero,
            CanSeek: false,
            // Media keys still reach a player that registered no SMTC session, so next/previous are
            // real capabilities of this path (SMTC simply does not advertise them).
            CanGoNext: _mediaKeySender is not null,
            CanGoPrevious: _mediaKeySender is not null,
            candidate.TrackKey);

        var previous = Current;
        var changed = !string.Equals(previous?.BuildContentSignature(), snapshot.BuildContentSignature(),
            StringComparison.Ordinal);
        // The settings file is rewritten every ~30 s: republish whenever the position anchor moved so
        // the App can interpolate between writes (progress bar + lyric highlight follow it).
        var positionMoved = candidate.HasTimeline
                            && previous is { PositionUpdatedAtUtc: var sampled } && sampled > DateTimeOffset.MinValue
                            && (previous.Position - candidate.Position).Duration() >= TimeSpan.FromMilliseconds(500);
        _fallbackActive = true;
        if (changed)
        {
            _counters.IncrementMediaSnapshotChanges();
        }

        // Attach the cover *before* publishing: the App builds its page models from this snapshot, and
        // the models are only rebuilt when their content signature changes — a cover that arrived one
        // event later would keep the placeholder until the next track.
        if (changed)
        {
            TryAttachFallbackArtwork(candidate);
        }

        if (changed || positionMoved)
        {
            MediaDiagnostics.Log($"publish source=fallback app={snapshot.SourceAppUserModelId} title={snapshot.Title} playing={snapshot.IsPlaying}");
            _fallbackLastSnapshot = snapshot;
        _fallbackLastPid = candidate.ProcessId;
        _fallbackLastSeenMs = _timeProviderNow().ToUnixTimeMilliseconds();
        Publish(snapshot);
        }
    }

    /// <summary>
    /// Overlays the timeline the player exposes through MSAA. KuGou does not publish SMTC data, but its
    /// accessibility tree carries the live "mm:ss/mm:ss" text, which is both finer-grained (updated
    /// every second) and more accurate (real duration) than the position it persists every 30 s.
    /// </summary>
    /// <summary>
    /// Resolves a disagreement between the accessibility clock and the player's state file in favour of the
    /// one that can prove when it was sampled.
    /// </summary>
    private WindowMediaCandidate CrossCheckTimeline(WindowMediaCandidate candidate)
    {
        // DISABLED: the state file is rewritten every ~30 s and the accessibility text is repainted on the
        // same cadence, so the two disagree by up to half a minute and the arbitration kept jumping the
        // published position forward — which made the media pill vanish (user report 2026-10-22).
        if (!CrossCheckEnabled)
        {
            return candidate;
        }

        if (!string.Equals(candidate.ProcessName, "KuGou", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (PlayerPlaybackStateReader.TryReadKugou(_timeProviderNow()) is not { } state)
        {
            return candidate;
        }

        var statePosition = state.Position;

        if (candidate.Position <= TimeSpan.Zero)
        {
            return candidate; // no accessibility reading at all (player minimised): cross-check is mute
        }

        var age = _timeProviderNow() - state.SampledAtUtc;
        var nowForAccumulation = _timeProviderNow();
        if (_stateFileWrittenAt != state.SampledAtUtc.UtcDateTime)
        {
            _stateFileWrittenAt = state.SampledAtUtc.UtcDateTime;
            _stateFilePlayingMs = 0;
        }
        else if (candidate.IsPlayingHint != false && _lastPollAt is { } previous)
        {
            _stateFilePlayingMs += (nowForAccumulation - previous).TotalMilliseconds;
        }

        _lastPollAt = nowForAccumulation;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(45))
        {
            return candidate; // too old to arbitrate
        }

        // The state file is written while playing, so extrapolate it to now.
        // Extrapolate by the time the track was actually PLAYING since the file was written, not by wall
        // time: the file is only rewritten every ~30 s, so a pause inside that window made the estimate
        // up to twenty seconds too large (user report 2026-10-22: "jumped 10 s, still 10 s off").
        var expected = statePosition + TimeSpan.FromMilliseconds(Math.Clamp(_stateFilePlayingMs, 0, 60_000));
        var difference = (candidate.Position - expected).Duration();
        if (difference <= TimeSpan.FromSeconds(3))
        {
            return candidate;
        }

        var nowTicks = _timeProviderNow().ToUnixTimeMilliseconds();
        if (nowTicks - _lastCrossCheckLogMs > 10_000)
        {
            _lastCrossCheckLogMs = nowTicks;
            MediaDiagnostics.LogAlways($"timeline.crosscheck resolved=state-file delta={difference.TotalSeconds:0.0}s " +
                $"msaa={candidate.Position.TotalSeconds:0.0}s state={expected.TotalSeconds:0.0}s age={age.TotalSeconds:0.0}s");
        }
        return candidate with
        {
            Position = expected,
            PositionSampledAtUtc = _timeProviderNow(),
            TimelineSource = PlayerTimelineSource.StateFile,
        };
    }

    private WindowMediaCandidate EnrichWithAccessibility(WindowMediaCandidate candidate)
    {
        if (candidate.ProcessId <= 0 || !MediaWindowFallback.IsKugouProcessName(candidate.ProcessName))
        {
            ReleaseAccessibility();
            return candidate;
        }

        var audible = MediaWindowFallback.TryGetAudioOutput(out var audioPeak);
        var now = Environment.TickCount64;
        PlayerAccessibilitySession? session;
        lock (_accessibilityGate)
        {
            if (_accessibility is null || _accessibilityProcessId != candidate.ProcessId)
            {
                if (now < _accessibilityRetryAfterMs && !audible)
                {
                    MediaDiagnostics.Log($"fallback accessibility backoff remaining={_accessibilityRetryAfterMs - now}ms audio={audible} peak={audioPeak:0.000}");
                    return candidate;
                }

                ReleaseAccessibilityLocked();
                _accessibility = PlayerAccessibilitySession.TryCreate(candidate.ProcessId);
                _accessibilityProcessId = _accessibility is null ? 0 : candidate.ProcessId;
                if (_accessibility is null)
                {
                    _accessibilityRetryAfterMs = now + AccessibilityRetryBackoffMs;
                    MediaDiagnostics.Log($"fallback accessibility create-failed retryIn={AccessibilityRetryBackoffMs}ms audio={audible} peak={audioPeak:0.000}");
                    return candidate;
                }

                MediaDiagnostics.Log($"fallback accessibility attached app={candidate.ProcessName} pid={candidate.ProcessId}");
            }

            session = _accessibility;
        }

        if (session is not null && audible)
        {
            // A meter level is positive proof that the player is producing audio; do not let a
            // transient MSAA failure hide it for the whole backoff window.
            session.ResetReadBackoff();
        }

        if (session is { IsReadBackoffActive: true })
        {
            MediaDiagnostics.Log($"fallback accessibility read-backoff app={candidate.ProcessName} audio={audible} peak={audioPeak:0.000}");
        }

        if (session is null)
        {
            return candidate;
        }

        // The transport glyph tells play/pause even in player modes that hide the time text; prefer it
        // over the position heuristic, which needs a full settings write (~30 s) to see a pause.
        var enriched = candidate;
        if (session.TryReadPlaybackState(out var isPlaying))
        {
            enriched = enriched with { IsPlayingHint = isPlaying };
        }

        if (!session.TryReadTimeline(out var timeline))
        {
            return enriched;
        }

        // The text is the FLOOR of the true position (mm:ss truncates) and the paint lags a little on top,
        // which made the island sit a steady second behind the song. Half a second of lead cancels the
        // truncation bias; the residual is then transport latency, well under a tenth (user report
        // 2026-10-22: "还是比真实歌曲慢大概 1s").
        return enriched with
        {
            Position = timeline.Position + AccessibilityLead,
            Duration = timeline.Duration,
            // Back-dated by the reading's age: a one-second-resolution clock is stale by up to half a
            // poll window the moment it is seen (see PlayerTimeline.Age).
            PositionSampledAtUtc = _timeProviderNow() - timeline.Age,
            HasTimeline = true,
            TimelineSource = PlayerTimelineSource.Accessibility,
        };
    }

    private void ReleaseAccessibility()
    {
        lock (_accessibilityGate)
        {
            ReleaseAccessibilityLocked();
        }
    }

    private void ReleaseAccessibilityLocked()
    {
        _accessibility?.Dispose();
        _accessibility = null;
        _accessibilityProcessId = 0;
    }

    /// <summary>
    /// Decides whether the fallback player is playing. KuGou keeps its audio session "active" while
    /// muted or paused, so the only honest signal is the position it persists (see
    /// <see cref="FallbackPlaybackTracker"/>).
    /// </summary>
    private bool DetermineFallbackPlaying(WindowMediaCandidate candidate)
    {
        // A frozen position only means "paused" after the source has refreshed without moving; the
        // accessibility timeline refreshes every second, the settings file every ~30 s.
        var staleAfter = candidate.TimelineSource == PlayerTimelineSource.Accessibility
            ? FallbackPlaybackTracker.AccessibilityStaleAfter
            : FallbackPlaybackTracker.StateFileStaleAfter;
        return _fallbackPlayback.Observe(candidate.TrackKey, candidate.HasTimeline, candidate.Position,
            candidate.PositionSampledAtUtc, staleAfter);
    }

    private void ResetFallbackPlayback()
    {
        _fallbackPlayback.Reset();
        _fallbackPlayingHint = null;
    }

    /// <summary>Requests a coalesced refresh (called by GSMTC events; never blocks the WinRT callback thread).</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _refreshPending, 1);
        return RefreshCoalescedAsync(cancellationToken);
    }

    private async Task RefreshCoalescedAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshPending, 0);
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            while (Interlocked.CompareExchange(ref _refreshPending, 0, 0) != 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _counters.IncrementMediaSessionErrors();
            _logger.LogError(PlatformLogCategory.Media, "media.refresh-failed", "Media refresh failed.", ex);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var manager = _manager;
        if (manager is null)
        {
            return;
        }

        var descriptors = new List<MediaSessionDescriptor>();
        var sessions = manager.GetSessions();
        var now = _timeProviderNow();
        MediaDiagnostics.Log($"poll sessions={sessions.Count}");
        foreach (var session in sessions)
        {
            try
            {
                var playbackStatus = session.GetPlaybackInfo().PlaybackStatus;
                if (MediaDiagnostics.Enabled)
                {
                    var props = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).ConfigureAwait(false);
                    MediaDiagnostics.Log(
                        $"session app={session.SourceAppUserModelId} status={playbackStatus} title={props?.Title ?? "null"}");
                }

                _lastSessionUpdate.TryGetValue(session.SourceAppUserModelId, out var updated);
                descriptors.Add(new MediaSessionDescriptor(session.SourceAppUserModelId, MapPlaybackState(playbackStatus),
                    null, updated == default ? now : updated));
            }
            catch (Exception ex)
            {
                _counters.IncrementMediaSessionErrors();
                _logger.LogWarning(PlatformLogCategory.Media, "media.session-descriptor-failed",
                    "Could not read a media session descriptor.", ex);
            }
        }

        var selected = MediaSourceSelectionPolicy.SelectActive(descriptors);
        MediaDiagnostics.Log($"selected={(selected?.SourceAppUserModelId ?? "none")}");
        var activeSession = selected is null
            ? null
            : sessions.FirstOrDefault(session =>
                string.Equals(session.SourceAppUserModelId, selected.SourceAppUserModelId, StringComparison.Ordinal));

        if (activeSession is null)
        {
            DetachSession();
            var audioOutput = MediaWindowFallback.TryGetAudioOutput(out var audioPeak);
            if (audioOutput)
            {
                // GSMTC has no session (KuGou is a window/MSAA fallback player), but WASAPI proves
                // audio is playing. Prefer the fallback path and do not retract the media island.
                MediaDiagnostics.Log($"gsmtc empty audio-active peak={audioPeak:0.000} -> fallback");
                _fallbackPoller?.TriggerNow();
                return;
            }

            // The fallback poller owns the snapshot while it has a live non-SMTC player.
            if (!_fallbackActive)
            {
                MediaDiagnostics.Log("gsmtc empty no-audio -> publish empty");
                Publish(MediaSnapshot.Empty);
            }

            return;
        }

        await AttachAndSnapshotAsync(activeSession, cancellationToken).ConfigureAwait(false);
    }

    private async Task AttachAndSnapshotAsync(GlobalSystemMediaTransportControlsSession session,
        CancellationToken cancellationToken)
    {
        _fallbackActive = false;
        ResetFallbackPlayback();
        lock (_sessionGate)
        {
            if (!ReferenceEquals(_session, session))
            {
                DetachSessionCore();
                _session = session;
                session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken)
                .ConfigureAwait(false);
            var playbackInfo = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var aumid = session.SourceAppUserModelId ?? string.Empty;
            var trackKey = MediaSourceSelectionPolicy.BuildTrackKey(aumid, properties.Title, properties.Artist,
                properties.AlbumTitle);

            var duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero;
            var position = timeline.Position;
            if (duration > TimeSpan.Zero && position > duration)
            {
                position = duration;
            }

            var controls = playbackInfo.Controls;
            var snapshot = new MediaSnapshot(
                aumid,
                null,
                properties.Title ?? string.Empty,
                properties.Artist ?? string.Empty,
                properties.AlbumTitle ?? string.Empty,
                MapPlaybackState(playbackInfo.PlaybackStatus),
                playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                position < TimeSpan.Zero ? TimeSpan.Zero : position,
                timeline.LastUpdatedTime,
                duration,
                controls.IsPlaybackPositionEnabled,
                controls.IsNextEnabled,
                controls.IsPreviousEnabled,
                trackKey);

            _lastSessionUpdate[aumid] = _timeProviderNow();
            var changed = !string.Equals(Current?.BuildContentSignature(), snapshot.BuildContentSignature(),
                StringComparison.Ordinal);
            if (changed)
            {
                _counters.IncrementMediaSnapshotChanges();
                _lastRefreshTimestamp = _timeProviderNow();
            }

            MediaDiagnostics.Log($"publish source=gsmtc app={aumid} title={properties.Title} playing={snapshot.IsPlaying}");
            Publish(snapshot);

            if (changed || !_artworkCache.TryGet(trackKey, out _))
            {
                StartArtworkFetch(session, trackKey, properties);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _counters.IncrementMediaSessionErrors();
            _logger.LogWarning(PlatformLogCategory.Media, "media.snapshot-failed",
                "Could not read the active media session snapshot.", ex);
        }
    }

    private void DetachSession()
    {
        lock (_sessionGate)
        {
            DetachSessionCore();
        }
    }

    private void DetachSessionCore()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        TryDetach(() => session.MediaPropertiesChanged -= OnMediaPropertiesChanged);
        TryDetach(() => session.PlaybackInfoChanged -= OnPlaybackInfoChanged);
        TryDetach(() => session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged);
    }

    private void TryDetach(Action detach)
    {
        try
        {
            detach();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(PlatformLogCategory.Media, "media.detach-failed",
                $"Could not detach a media session handler: {ex.GetType().Name}.");
        }
    }

    private void StartArtworkFetch(GlobalSystemMediaTransportControlsSession session, string trackKey,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (string.IsNullOrEmpty(trackKey))
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_artworkGate)
        {
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = cts = new CancellationTokenSource();
        }

        _ = FetchArtworkSafeAsync(session, trackKey, properties, cts.Token);
    }

    private void CancelArtworkFetch()
    {
        lock (_artworkGate)
        {
            _artworkCts?.Cancel();
            _artworkCts?.Dispose();
            _artworkCts = null;
        }
    }

    private async Task FetchArtworkSafeAsync(GlobalSystemMediaTransportControlsSession session, string trackKey,
        GlobalSystemMediaTransportControlsSessionMediaProperties properties, CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = properties.Thumbnail;
            if (thumbnail is null)
            {
                return;
            }

            var bytes = await ReadThumbnailAsync(thumbnail, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0 || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var entry = _artworkCache.Add(trackKey, bytes);
            if (entry is null)
            {
                _logger.LogDebug(PlatformLogCategory.Media, "media.artwork-rejected",
                    "Artwork rejected by the cache budget (dimensions or size).");
                return;
            }

            PublishArtwork(trackKey, entry);

            _logger.LogDebug(PlatformLogCategory.Media, "media.artwork-ready",
                "Artwork cached.", new Dictionary<string, object?>
                {
                    ["track"] = trackKey,
                    ["bytes"] = entry.Data.Length,
                    ["pixels"] = entry.Dimensions.PixelCount,
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Track changed; the new track's fetch takes over.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Media, "media.artwork-fetch-failed",
                "Fetching album artwork failed.", ex);
        }
    }

    /// <summary>Raises <see cref="ArtworkAvailable"/>; a throwing subscriber never breaks the fetch.</summary>
    private void PublishArtwork(string trackKey, ArtworkEntry entry)
    {
        var handler = ArtworkAvailable;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new MediaArtworkEventArgs(trackKey, entry));
        }
        catch (Exception ex)
        {
            _logger.LogError(PlatformLogCategory.Media, "media.artwork-subscriber-failed",
                "An artwork subscriber threw.", ex);
        }
    }

    /// <summary>
    /// Attaches the cover the fallback player cached for this song. Runs only when the track changes,
    /// so the folder scan never happens on the 1 Hz poll.
    /// </summary>
    private void TryAttachFallbackArtwork(WindowMediaCandidate candidate)
    {
        if (string.IsNullOrEmpty(candidate.TrackKey) || _artworkCache.TryGet(candidate.TrackKey, out _))
        {
            return;
        }

        if (!MediaWindowFallback.IsKugouProcessName(candidate.ProcessName))
        {
            return;
        }

        var bytes = KugouArtworkCache.TryRead(candidate.Title);
        if (bytes is not null)
        {
            var entry = _artworkCache.Add(candidate.TrackKey, bytes);
            if (entry is not null)
            {
                PublishArtwork(candidate.TrackKey, entry);
            }

            return;
        }

        // The player cached no cover for this song (it only caches what it displayed before), so fetch it
        // from the catalogue in the background: the island shows the gradient placeholder until then.
        StartArtworkLookup(candidate);
    }

    /// <summary>
    /// Background cover lookup for the fallback player: cache first, then the catalogue sources. Runs at
    /// most once per track and never blocks the poll.
    /// </summary>
    private void StartArtworkLookup(WindowMediaCandidate candidate)
    {
        // One lookup per track, not one globally: the old global latch silently skipped every song that
        // changed while a previous lookup was still in flight (fast skipping through a playlist lost most
        // covers). The fetcher already single-flights per key and caps concurrency.
        lock (_artworkLookups)
        {
            if (!_artworkLookups.Add(candidate.TrackKey))
            {
                return;
            }

            if (_artworkLookups.Count > 256)
            {
                _artworkLookups.Clear();
                _artworkLookups.Add(candidate.TrackKey);
            }
        }

        var fetcher = ActiveArtworkFetcher;
        if (fetcher is null || !fetcher.HasSources)
        {
            return;
        }

        // Window-title scrapes carry the player's own chrome ("正在播放: "), and artist/title are often
        // glued into one string; searching raw text is the main reason covers were missed.
        var (searchTitle, searchArtist) = ArtworkSearchTerms.Split(candidate.Title, candidate.Artist);

        _ = Task.Run(async () =>
        {
            try
            {
                // Three attempts: the title settles a beat after the track starts and a provider hiccup
                // must not cost the whole track its cover (user report 2026-10-21).
                ArtworkFetchOutcome outcome = default;
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt * 3)).ConfigureAwait(false);
                        if (!string.Equals(Current?.TrackKey, candidate.TrackKey, StringComparison.Ordinal))
                        {
                            return;
                        }
                    }

                    outcome = await fetcher.FetchAsync(searchArtist, searchTitle, album: null).ConfigureAwait(false);
                    if (outcome.Succeeded)
                    {
                        break;
                    }
                }

                if (!outcome.Succeeded || outcome.Entry is not { } entry || string.IsNullOrEmpty(candidate.TrackKey))
                {
                    // Attributable failure: which level missed and how close the best candidate was.
                    _logger.LogInfo(PlatformLogCategory.Media, "media.artwork-miss",
                        "No cover for '" + candidate.Title + "': " + outcome.Failure + " (source=" + outcome.Source
                        + ", bestScore=" + outcome.Score.ToString("0.000") + ").");
                    return;
                }

                var artwork = _artworkCache.Add(candidate.TrackKey, entry.Bytes);
                if (artwork is null)
                {
                    return;
                }

                _logger.LogInfo(PlatformLogCategory.Media, "media.artwork-fetched",
                    "Cover fetched from the catalogue.",
                    new Dictionary<string, object?>
                    {
                        ["source"] = entry.Metadata.SourceKind.ToString(),
                        ["url"] = entry.Metadata.SourceUrl,
                        ["score"] = entry.Metadata.Score,
                    });
                PublishArtwork(candidate.TrackKey, artwork);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(PlatformLogCategory.Media, "media.artwork-fetch-failed",
                    "Cover lookup failed: " + ex.Message);
            }
        });
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference thumbnail,
        CancellationToken cancellationToken)
    {
        using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var size = stream.Size;
        if (size == 0 || size > MaxArtworkBytes)
        {
            return null;
        }

        using var reader = new DataReader(stream.GetInputStreamAt(0));
        var buffer = new byte[size];
        var loaded = await reader.LoadAsync((uint)size).AsTask(cancellationToken).ConfigureAwait(false);
        reader.ReadBytes(buffer);
        return loaded == size ? buffer : null;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
        => QueueRefresh("sessions-changed");

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
        => QueueRefresh("current-session-changed");

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
        => QueueRefresh("properties-changed");

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
        => QueueRefresh("playback-changed");

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
        => QueueRefresh("timeline-changed");

    private void QueueRefresh(string reason)
    {
        _counters.IncrementMediaSessionEvents();
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        _logger.LogTrace(PlatformLogCategory.Media, "media.event", $"GSMTC event: {reason}.");
        Interlocked.Exchange(ref _refreshPending, 1);
        _ = RefreshCoalescedAsync(CancellationToken.None);
    }

    private DateTimeOffset _timeProviderNow() => TimeProvider.GetUtcNow();

    /// <summary>Last time a snapshot's content actually changed (diagnostics).</summary>
    public DateTimeOffset LastRefreshTimestamp => _lastRefreshTimestamp;

    /// <summary>
    /// Toggles play/pause. With a GSMTC session the session is asked directly; on the window-title
    /// fallback the command travels as the system media key, which is the only channel a player that
    /// registers no SMTC session still answers.
    /// </summary>
    public async Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null
            ? await session.TryTogglePlayPauseAsync().AsTask(cancellationToken).ConfigureAwait(false)
            : TryFallbackTransport(MediaKeyCommand.TogglePlayPause);
    }

    /// <summary>Starts playback on the active session (media key on the fallback path).</summary>
    public async Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null
            ? await session.TryPlayAsync().AsTask(cancellationToken).ConfigureAwait(false)
            : TryFallbackTransport(MediaKeyCommand.TogglePlayPause);
    }

    /// <summary>Pauses the active session (media key on the fallback path).</summary>
    public async Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null
            ? await session.TryPauseAsync().AsTask(cancellationToken).ConfigureAwait(false)
            : TryFallbackTransport(MediaKeyCommand.TogglePlayPause);
    }

    /// <summary>Skips to the next track on the active session (media key on the fallback path).</summary>
    public async Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null
            ? await session.TrySkipNextAsync().AsTask(cancellationToken).ConfigureAwait(false)
            : TryFallbackTransport(MediaKeyCommand.NextTrack);
    }

    /// <summary>Skips to the previous track on the active session (media key on the fallback path).</summary>
    public async Task<bool> PreviousAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null
            ? await session.TrySkipPreviousAsync().AsTask(cancellationToken).ConfigureAwait(false)
            : TryFallbackTransport(MediaKeyCommand.PreviousTrack);
    }

    /// <summary>
    /// Routes a transport command to the system media keys. Returns false (and logs why) when no
    /// fallback snapshot is live or the input queue refuses the key — never a silent fake success.
    /// </summary>
    private bool TryFallbackTransport(MediaKeyCommand command)
    {
        PlayerAccessibilitySession? session;
        lock (_accessibilityGate)
        {
            session = _accessibility;
        }

        // A live accessibility session is itself proof that a fallback player is there, even if the last
        // poll raced with a track switch.
        var fallbackLive = _fallbackActive || session is not null;
        var decision = FallbackTransportPolicy.Decide(hasSession: false, fallbackActive: fallbackLive,
            accessibilityAvailable: session is not null, senderAvailable: _mediaKeySender is not null);
        if (!decision.IsExecutable)
        {
            _logger.LogDebug(PlatformLogCategory.Media, "media.transport-unavailable",
                "Transport command ignored: " + decision.Reason);
            return false;
        }

        // Preferred channel first (accessibility), then the generic media key as a last resort. A press
        // that the player ignores must never be reported as success, so both channels must fail before
        // the command returns false.
        if (session is not null && session.TryInvoke(command, _fallbackPlayback.IsPlaying))
        {
            _logger.LogInfo(PlatformLogCategory.Media, "media.transport-accessibility",
                $"Pressed '{command}' through the player accessibility tree.");
            _fallbackPoller?.TriggerNow();
            return true;
        }

        if (_mediaKeySender is not null)
        {
            if (_mediaKeySender.TrySend(command))
            {
                _logger.LogInfo(PlatformLogCategory.Media, "media.transport-media-key",
                    $"Sent media key '{command}' to the fallback player.");
                _fallbackPoller?.TriggerNow();
                return true;
            }

            _logger.LogWarning(PlatformLogCategory.Media, "media.transport-failed",
                $"Media key '{command}' could not be injected: {_mediaKeySender.LastFailureReason}");
        }

        return false;
    }

    /// <summary>
    /// Seeks the active session. Only the active session is targeted (never another app); the
    /// window-title fallback exposes no seek API, so it reports false instead of pretending.
    /// </summary>
    public async Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        var ticks = Math.Max(0, position.Ticks);
        return await session.TryChangePlaybackPositionAsync(ticks).AsTask(cancellationToken).ConfigureAwait(false);
    }

    private static MediaPlaybackState MapPlaybackState(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlaybackState.Closed,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackState.Opened,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
        _ => MediaPlaybackState.Unknown,
    };

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _artworkCache.Dispose();
        _refreshGate.Dispose();
    }
}