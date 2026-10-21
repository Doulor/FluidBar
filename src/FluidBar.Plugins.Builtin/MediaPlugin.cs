using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// First-party media plugin: adapts the event-driven platform GSMTC service to SDK island events and
/// exposes transport commands. Artwork bytes stay in the platform cache/temp store; the plugin only
/// publishes the materialized path.
/// </summary>
public sealed class MediaPlugin : BuiltinPluginBase
{
    private readonly IMediaSessionSource _media;
    private readonly ArtworkTempStore? _artworkStore;
    private readonly ILyricsLookup? _lyrics;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _artworkPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _artworkPending = new(StringComparer.Ordinal);
    private string _lastSignature = string.Empty;
    private LyricsDocument? _lyricsDocument;
    private string _lyricsTrackKey = string.Empty;
    private bool _lyricsPending;

    public MediaPlugin(IMediaSessionSource media, ArtworkTempStore? artworkStore = null, ILyricsLookup? lyrics = null)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _artworkStore = artworkStore;
        _lyrics = lyrics;
    }

    public override string PluginId => BuiltinIds.MediaPlugin;

    public static PluginContributions Contributions => BuiltinContributions.ForMedia();

    protected override void OnStarted()
    {
        TrackSubscription(() => _media.SnapshotChanged += OnSnapshotChanged,
            () => _media.SnapshotChanged -= OnSnapshotChanged);
        TrackSubscription(() => _media.ArtworkAvailable += OnArtworkAvailable,
            () => _media.ArtworkAvailable -= OnArtworkAvailable);

        RegisterCommand(BuiltinIds.MediaTogglePlayPause, async (_, cancellationToken) =>
            await _media.TogglePlayPauseAsync(cancellationToken).ConfigureAwait(false));
        RegisterCommand(BuiltinIds.MediaNext, async (_, cancellationToken) =>
            await _media.NextAsync(cancellationToken).ConfigureAwait(false));
        RegisterCommand(BuiltinIds.MediaPrevious, async (_, cancellationToken) =>
            await _media.PreviousAsync(cancellationToken).ConfigureAwait(false));

        PublishSnapshot(_media.Current);
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<MediaSnapshot> args) => PublishSnapshot(args.Snapshot);

    private void OnArtworkAvailable(object? sender, MediaArtworkEventArgs args)
    {
        if (_artworkStore is null || string.IsNullOrEmpty(args.TrackKey))
        {
            return;
        }

        lock (_stateGate)
        {
            if (_artworkPaths.ContainsKey(args.TrackKey) || !_artworkPending.Add(args.TrackKey))
            {
                return;
            }
        }

        RunBackground(async token =>
        {
            var path = await _artworkStore.MaterializeAsync(args.TrackKey, args.Artwork, token)
                .ConfigureAwait(false);
            lock (_stateGate)
            {
                _artworkPending.Remove(args.TrackKey);
                if (path is not null)
                {
                    _artworkPaths[args.TrackKey] = path;
                }
            }

            if (path is not null)
            {
                PublishSnapshot(_media.Current);
            }
        });
    }

    private void PublishSnapshot(MediaSnapshot? snapshot)
    {
        if (!IsStarted)
        {
            return;
        }

        MediaDiagnostics.Log(
            snapshot is null || snapshot.IsEmpty
                ? "plugin snapshot empty -> retract (activity=0)"
                : $"plugin snapshot app={snapshot.SourceAppUserModelId} status={(snapshot.IsPlaying ? "Playing" : "Paused")} " +
                  $"title={snapshot.Title} IsActive={snapshot.IsPlaying} -> activity=1");

        if (snapshot is null || snapshot.IsEmpty)
        {
            lock (_stateGate)
            {
                if (_lastSignature.Length == 0)
                {
                    return;
                }

                _lastSignature = string.Empty;
                _lyricsDocument = null;
                _lyricsTrackKey = string.Empty;
            }

            Retract();
            MediaDiagnostics.Log("plugin retract emitted (activity=0)");
            return;
        }

        var trackKey = snapshot.TrackKey;
        string? artworkPath;
        string signature;
        LyricsLine? currentLyric;
        LyricsLine? nextLyric;
        string? body;
        lock (_stateGate)
        {
            artworkPath = _artworkPaths.TryGetValue(trackKey, out var known) ? known : null;
            var lines = BuildLyricLines(snapshot);
            currentLyric = lines.Current;
            nextLyric = lines.Next;
            body = currentLyric?.Text;
            var isPlayingToken = snapshot.IsPlaying ? "playing" : "paused";
            signature = string.Join('|', snapshot.BuildContentSignature(), artworkPath ?? string.Empty,
                body ?? string.Empty, isPlayingToken);
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastSignature = signature;
        }

        var artKey = trackKey;
        if (artworkPath is null && _artworkStore is not null)
        {
            TryQueueArtwork(artKey);
        }

        EnsureLyricsLoaded(snapshot, trackKey);

        var lyricLines = new List<string>(2);
        if (currentLyric is not null)
        {
            lyricLines.Add(currentLyric.Text);
        }

        if (nextLyric is not null)
        {
            lyricLines.Add(nextLyric.Text);
        }

        var showLyrics = Context.Settings.GetBoolean("media.showLyrics", true);
        var showWave = Context.Settings.GetBoolean("media.showWave", true);
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = "media",
            ["title"] = snapshot.Title,
            ["subtitle"] = snapshot.Artist,
            ["badge"] = snapshot.IsPlaying ? "playing" : "paused",
            ["progress"] = snapshot.Duration > TimeSpan.Zero
                ? Math.Clamp(snapshot.Position.TotalSeconds / snapshot.Duration.TotalSeconds, 0, 1)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                : null,
            ["wave"] = showWave && snapshot.IsPlaying ? "on" : "off",
            ["artwork"] = artworkPath,
            ["body"] = body,
            ["actions"] = BuiltinIds.MediaTogglePlayPause,
        };
        MediaDiagnostics.Log($"plugin publish key=media:gsmtc title={snapshot.Title} IsActive={snapshot.IsPlaying} activity=1");
        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = string.IsNullOrWhiteSpace(snapshot.Title)
                ? snapshot.SourceDisplayName ?? snapshot.SourceAppUserModelId ?? "media"
                : snapshot.Title,
            Content = snapshot.Artist,
            IconKind = "media",
            Kind = IslandEventKind.Media,
            LayoutId = BuiltinIds.MediaCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = snapshot.Artist,
                Badge = snapshot.IsPlaying ? "playing" : "paused",
                SourceName = snapshot.SourceDisplayName ?? snapshot.SourceAppUserModelId,
                Progress = snapshot.Duration > TimeSpan.Zero
                    ? Math.Clamp(snapshot.Position.TotalSeconds / snapshot.Duration.TotalSeconds, 0, 1)
                    : null,
                IsActive = snapshot.IsPlaying,
                AudioWave = null,
                AlbumArtPath = artworkPath,
                LyricLines = showLyrics && lyricLines.Count > 0 ? lyricLines : null,
                PlaybackTicks = snapshot.Position.Ticks,
                DetailLines = null,
                Fields = fields,
            },
        });
    }

    private (LyricsLine? Current, LyricsLine? Next) BuildLyricLines(MediaSnapshot snapshot)
    {
        LyricsDocument? document;
        lock (_stateGate)
        {
            document = _lyricsDocument;
        }

        if (document is null || document.IsEmpty)
        {
            return (null, null);
        }

        // The platform never owns a frame clock: interpolate from the snapshot timestamp for lyrics only.
        var position = snapshot.Position;
        if (snapshot.IsPlaying && snapshot.PositionUpdatedAtUtc > DateTimeOffset.MinValue)
        {
            var elapsed = Context.Time.GetUtcNow() - snapshot.PositionUpdatedAtUtc;
            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(1))
            {
                position += elapsed;
            }
        }

        if (snapshot.Duration > TimeSpan.Zero && position > snapshot.Duration)
        {
            position = snapshot.Duration;
        }

        return (document.LineAt(position), document.NextLine(position));
    }

    private void TryQueueArtwork(string trackKey)
    {
        ArtworkEntry? artwork = null;
        lock (_stateGate)
        {
            if (_artworkStore is null || _artworkPaths.ContainsKey(trackKey) || !_artworkPending.Add(trackKey))
            {
                return;
            }
        }

        if (!_media.TryGetArtwork(trackKey, out artwork) || artwork is null || _artworkStore is null)
        {
            lock (_stateGate)
            {
                _artworkPending.Remove(trackKey);
            }

            return;
        }

        var store = _artworkStore;
        RunBackground(async token =>
        {
            var path = await store.MaterializeAsync(trackKey, artwork, token).ConfigureAwait(false);
            lock (_stateGate)
            {
                _artworkPending.Remove(trackKey);
                if (path is not null)
                {
                    _artworkPaths[trackKey] = path;
                }
            }

            if (path is not null)
            {
                PublishSnapshot(_media.Current);
            }
        });
    }

    private void EnsureLyricsLoaded(MediaSnapshot snapshot, string trackKey)
    {
        if (_lyrics is null || string.IsNullOrWhiteSpace(trackKey))
        {
            return;
        }

        lock (_stateGate)
        {
            if (_lyricsPending || string.Equals(_lyricsTrackKey, trackKey, StringComparison.Ordinal))
            {
                return;
            }

            _lyricsPending = true;
            _lyricsTrackKey = trackKey;
        }

        RunBackground(async token =>
        {
            try
            {
                var document = await _lyrics.GetLyricsAsync(
                    new LyricsQuery(snapshot.Title, snapshot.Artist, snapshot.Duration, trackKey), token)
                    .ConfigureAwait(false);
                lock (_stateGate)
                {
                    _lyricsDocument = document;
                }

                if (document is not null && !document.IsEmpty)
                {
                    PublishSnapshot(_media.Current);
                }
            }
            finally
            {
                lock (_stateGate)
                {
                    _lyricsPending = false;
                }
            }
        });
    }

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _artworkPaths.Clear();
            _artworkPending.Clear();
            _lyricsDocument = null;
            _lyricsTrackKey = string.Empty;
            _lyricsPending = false;
            _lastSignature = string.Empty;
        }
    }
}
