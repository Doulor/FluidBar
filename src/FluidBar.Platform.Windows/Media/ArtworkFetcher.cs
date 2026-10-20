namespace FluidBar.Platform.Windows.Media;

/// <summary>Options for <see cref="ArtworkFetcher"/>.</summary>
public sealed class ArtworkFetcherOptions
{
    /// <summary>Per-source budget (the user must never feel the lookup).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>How many lookups may run at the same time.</summary>
    public int MaxConcurrentFetches { get; init; } = 2;
}

/// <summary>
/// Fetches missing covers in the background, in priority order, and records every accepted cover on disk.
/// </summary>
/// <remarks>
/// <para>The pipeline is: disk cache (previous fetch) → sources in order → the caller keeps its gradient
/// placeholder when everything misses. One lookup per track runs at a time, sources run with a short
/// timeout and are attempted exactly once — a catalogue that is down must not be hammered.</para>
/// <para>Nothing here decodes pixels; bytes are validated by header only, so the Platform layer stays
/// imaging-free.</para>
/// </remarks>
public sealed class ArtworkFetcher : IDisposable
{
    private readonly ArtworkDiskCache _cache;
    private readonly IReadOnlyList<IArtworkSource> _sources;
    private readonly ArtworkFetcherOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private readonly Dictionary<string, Task<ArtworkCacheEntry?>> _inFlight = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _disposed;

    /// <summary>Creates the fetcher.</summary>
    /// <param name="cache">Disk cache (also the first source of bytes).</param>
    /// <param name="sources">Network sources, highest priority first.</param>
    /// <param name="options">Timeout/concurrency options.</param>
    public ArtworkFetcher(ArtworkDiskCache cache, IReadOnlyList<IArtworkSource> sources,
        ArtworkFetcherOptions? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _sources = sources ?? [];
        _options = options ?? new ArtworkFetcherOptions();
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentFetches));
    }

    /// <summary>Whether any network source is configured.</summary>
    public bool HasSources => _sources.Count > 0;

    /// <summary>Cache used by this fetcher (diagnostics/tests).</summary>
    public ArtworkDiskCache Cache => _cache;

    /// <summary>
    /// Returns a cover for the track: the disk cache when present, otherwise the first source that
    /// produces a confident match. Null when every source misses (the island keeps the placeholder).
    /// </summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="album">Album (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ArtworkCacheEntry?> GetAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var key = ArtworkDiskCache.KeyFor(artist, title, album);
        if (_cache.TryRead(key) is { } cached)
        {
            return cached;
        }

        if (!HasSources)
        {
            return null;
        }

        Task<ArtworkCacheEntry?> task;
        lock (_gate)
        {
            if (!_inFlight.TryGetValue(key, out task!))
            {
                task = FetchAndStoreAsync(key, artist, title, album);
                _inFlight[key] = task;
                _ = task.ContinueWith(
                    _ =>
                    {
                        lock (_gate)
                        {
                            _inFlight.Remove(key);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ArtworkCacheEntry?> FetchAndStoreAsync(string key, string? artist, string? title,
        string? album)
    {
        await _concurrency.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var source in _sources)
            {
                ArtworkCandidate? candidate;
                try
                {
                    using var timeout = new CancellationTokenSource(_options.Timeout);
                    candidate = await source.FindAsync(artist, title, album, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Timeout/cancellation/network failure: try the next source, never retry this one.
                    continue;
                }

                if (candidate is null)
                {
                    continue;
                }

                var metadata = new ArtworkCacheMetadata(
                    key,
                    candidate.SourceUrl.Contains("itunes", StringComparison.OrdinalIgnoreCase)
                        || candidate.SourceUrl.Contains("mzstatic", StringComparison.OrdinalIgnoreCase)
                        ? ArtworkSourceKind.ITunes
                        : ArtworkSourceKind.PlayerCache,
                    candidate.SourceUrl,
                    artist ?? string.Empty,
                    title ?? string.Empty,
                    album ?? string.Empty,
                    candidate.Score,
                    DateTimeOffset.UtcNow);
                var entry = _cache.Write(key, candidate.Bytes, metadata);
                if (entry is not null)
                {
                    return entry;
                }
            }

            return null;
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _concurrency.Dispose();
    }
}
