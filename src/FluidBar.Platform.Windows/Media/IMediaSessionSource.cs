using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Minimal event-driven media contract consumed by first-party plugins (and App adapters).
/// Implemented by <see cref="GsmtcMediaSessionService"/>; fakes are used in tests.
/// </summary>
public interface IMediaSessionSource
{
    /// <summary>Latest immutable media snapshot, or null before the first observation.</summary>
    MediaSnapshot? Current { get; }

    /// <summary>Change-only coalesced snapshots (raised on a thread-pool thread).</summary>
    event EventHandler<SnapshotEventArgs<MediaSnapshot>>? SnapshotChanged;

    /// <summary>Raised when artwork bytes become available for a track key.</summary>
    event EventHandler<MediaArtworkEventArgs>? ArtworkAvailable;

    /// <summary>Looks up cached artwork bytes for a track key.</summary>
    bool TryGetArtwork(string trackKey, out ArtworkEntry? artwork);

    /// <summary>Bounded artwork byte cache (plugins materialize paths from it).</summary>
    ArtworkCache ArtworkCache { get; }

    Task<bool> TogglePlayPauseAsync(CancellationToken cancellationToken = default);

    Task<bool> NextAsync(CancellationToken cancellationToken = default);

    Task<bool> PreviousAsync(CancellationToken cancellationToken = default);

    Task<bool> SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
}
