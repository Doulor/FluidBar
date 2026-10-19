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
    private volatile bool _fallbackActive;
    private CancellationTokenSource? _artworkCts;
    private int _refreshPending;
    private DateTimeOffset _lastRefreshTimestamp = DateTimeOffset.MinValue;

    public GsmtcMediaSessionService(IPlatformLogger logger, PlatformCounters counters,
        ArtworkCache? artworkCache = null, TimeProvider? timeProvider = null)
        : base("media", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _artworkCache = artworkCache ?? new ArtworkCache();
    }

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
        _fallbackPoller = new BackgroundPoller("media-fallback", TimeSpan.FromSeconds(1), PollWindowFallback, _logger);
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

        var candidate = MediaWindowFallback.FindCandidate();
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fb-fallback.log"), "poll candidate=" + (candidate is null ? "null" : candidate.ProcessName + "|" + candidate.Title + "|" + candidate.Artist) + "\n"); } catch { }
        if (candidate is null)
        {
            if (_fallbackActive)
            {
                _fallbackActive = false;
                Publish(MediaSnapshot.Empty);
            }

            return;
        }

        var snapshot = new MediaSnapshot(
            "win:" + candidate.ProcessName,
            candidate.ProcessName,
            candidate.Title,
            candidate.Artist,
            string.Empty,
            MediaPlaybackState.Playing,
            IsPlaying: true,
            TimeSpan.Zero,
            // No timeline on this path: keep the timestamp at MinValue so the App does not interpolate
            // a position that does not exist.
            DateTimeOffset.MinValue,
            TimeSpan.Zero,
            CanSeek: false,
            CanGoNext: false,
            CanGoPrevious: false,
            candidate.TrackKey);

        var changed = !string.Equals(Current?.BuildContentSignature(), snapshot.BuildContentSignature(),
            StringComparison.Ordinal);
        _fallbackActive = true;
        if (changed)
        {
            _counters.IncrementMediaSnapshotChanges();
            Publish(snapshot);
        }
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
        foreach (var session in sessions)
        {
            try
            {
                var playbackStatus = session.GetPlaybackInfo().PlaybackStatus;
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
        var activeSession = selected is null
            ? null
            : sessions.FirstOrDefault(session =>
                string.Equals(session.SourceAppUserModelId, selected.SourceAppUserModelId, StringComparison.Ordinal));

        if (activeSession is null)
        {
            DetachSession();
            // The fallback poller owns the snapshot while it has a live non-SMTC player.
            if (!_fallbackActive)
            {
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

            var handler = ArtworkAvailable;
            if (handler is not null)
            {
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

    /// <summary>Toggles play/pause on the active session.</summary>
    public async Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null && await session.TryTogglePlayPauseAsync().AsTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Starts playback on the active session.</summary>
    public async Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null && await session.TryPlayAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pauses the active session.</summary>
    public async Task<bool> PauseAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null && await session.TryPauseAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Skips to the next track on the active session.</summary>
    public async Task<bool> NextAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null && await session.TrySkipNextAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Skips to the previous track on the active session.</summary>
    public async Task<bool> PreviousAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        return session is not null && await session.TrySkipPreviousAsync().AsTask(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Seeks the active session. Only the active session is targeted (never another app).</summary>
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
