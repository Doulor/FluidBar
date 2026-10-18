using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>Async lyrics lookup consumed by first-party plugins. Implemented by <see cref="LyricsService"/>.</summary>
public interface ILyricsLookup
{
    /// <summary>Fetches lyrics for a track (bounded cache + single-flight inside the implementation).</summary>
    Task<LyricsDocument?> GetLyricsAsync(LyricsQuery query, CancellationToken cancellationToken = default);
}
