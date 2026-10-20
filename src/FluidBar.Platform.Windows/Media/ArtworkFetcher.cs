namespace FluidBar.Platform.Windows.Media;

/// <summary>Why a cover lookup failed (recorded so failures are attributable instead of silent).</summary>
public enum ArtworkFetchFailure
{
    /// <summary>No failure: a cover was returned.</summary>
    None,

    /// <summary>No source produced a single result for the query.</summary>
    NoResults,

    /// <summary>Results existed but none passed the confidence threshold.</summary>
    BelowThreshold,

    /// <summary>The source did not answer inside the per-source timeout.</summary>
    Timeout,

    /// <summary>Network/HTTP/protocol failure.</summary>
    NetworkError,

    /// <summary>The downloaded bytes were not a usable image.</summary>
    DecodeFailed,
}

/// <summary>Result of one source lookup.</summary>
/// <param name="Candidate">The accepted cover, or null.</param>
/// <param name="Failure">Why it failed (None when a candidate came back).</param>
/// <param name="BestScore">Best confidence seen, even when rejected.</param>
public readonly record struct ArtworkSourceResult(ArtworkCandidate? Candidate, ArtworkFetchFailure Failure,
    double BestScore = 0);

/// <summary>Outcome of a full lookup across the cache and every source.</summary>
/// <param name="Entry">The cover, or null.</param>
/// <param name="Failure">Attributed failure reason (None on success).</param>
/// <param name="Source">Source that answered or was last tried.</param>
/// <param name="Url">URL of the accepted cover.</param>
/// <param name="Score">Confidence of the accepted cover.</param>
public sealed record ArtworkFetchOutcome(ArtworkCacheEntry? Entry, ArtworkFetchFailure Failure, string Source,
    string? Url = null, double Score = 0)
{
    /// <summary>Whether a cover was produced.</summary>
    public bool Succeeded => Entry is not null;
}

/// <summary>A place covers can be fetched from.</summary>
public interface IArtworkSource
{
    /// <summary>Source name (diagnostics).</summary>
    string Name { get; }

    /// <summary>Searches the source for a cover.</summary>
    /// <param name="artist">Artist to look up.</param>
    /// <param name="title">Title to look up.</param>
    /// <param name="album">Album to look up (may be empty).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ArtworkSourceResult> FindAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken);
}

/// <summary>Options for <see cref="ArtworkFetcher"/>.</summary>
public sealed class ArtworkFetcherOptions
{
    /// <summary>Per-source budget (the user must never feel the lookup).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>How many lookups may run at the same time.</summary>
    public int MaxConcurrentFetches { get; init; } = 2;

    /// <summary>Acceptance threshold for the confidence check (0.65: looser than the first cut's 0.72).</summary>
    public double Threshold { get; init; } = ArtworkTitleMatcher.DefaultThreshold;
}

/// <summary>
/// Fetches missing covers in the background, in priority order, and records every accepted cover on disk.
/// </summary>
/// <remarks>
/// <para>The pipeline is: disk cache (previous fetch) → sources in order → the caller keeps its gradient
/// placeholder when everything misses. One lookup per track runs at a time, sources run with a short
/// timeout and are attempted exactly once — a catalogue that is down must not be hammered.</para>
/// <para>Every lookup reports <em>why</em> it failed (<see cref="ArtworkFetchFailure"/>) so the rate of each
/// failure class can be measured instead of guessed.</para>
/// </remarks>
public sealed class ArtworkFetcher : IDisposable
{
    private readonly ArtworkDiskCache _cache;
    private readonly IReadOnlyList<IArtworkSource> _sources;
    private readonly ArtworkFetcherOptions _options;
    private readonly SemaphoreSlim _concurrency;
    private readonly Dictionary<string, Task<ArtworkFetchOutcome>> _inFlight = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _disposed;

    /// <summary>Creates the fetcher.</summary>
    /// <param name="cache">Disk cache (also the first source of bytes).</param>
    /// <param name="sources">Network sources, highest priority first.</param>
    /// <param name="options">Timeout/concurrency/threshold options.</param>
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

    /// <summary>Returns a cover, or null when every source missed.</summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="album">Album (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ArtworkCacheEntry?> GetAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken = default)
        => (await FetchAsync(artist, title, album, cancellationToken).ConfigureAwait(false)).Entry;

    /// <summary>Returns the cover plus the attribution of a failure.</summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="album">Album (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ArtworkFetchOutcome> FetchAsync(string? artist, string? title, string? album,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ArtworkFetchOutcome(null, ArtworkFetchFailure.NoResults, "none");
        }

        var key = ArtworkDiskCache.KeyFor(artist, title, album);
        if (_cache.TryRead(key) is { } cached)
        {
            return new ArtworkFetchOutcome(cached, ArtworkFetchFailure.None, "cache", cached.Metadata.SourceUrl,
                cached.Metadata.Score);
        }

        if (!HasSources)
        {
            return new ArtworkFetchOutcome(null, ArtworkFetchFailure.NoResults, "none");
        }

        Task<ArtworkFetchOutcome> task;
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

    private async Task<ArtworkFetchOutcome> FetchAndStoreAsync(string key, string? artist, string? title,
        string? album)
    {
        await _concurrency.WaitAsync().ConfigureAwait(false);
        try
        {
            // The most specific failure wins: a network outage explains more than "no results".
            var failure = ArtworkFetchFailure.NoResults;
            var failureSource = _sources.Count > 0 ? _sources[0].Name : "none";
            var bestScore = 0.0;
            foreach (var source in _sources)
            {
                ArtworkSourceResult result;
                try
                {
                    using var timeout = new CancellationTokenSource(_options.Timeout);
                    result = await source.FindAsync(artist, title, album, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    failure = Worse(failure, ArtworkFetchFailure.Timeout);
                    failureSource = source.Name;
                    continue;
                }
                catch (Exception)
                {
                    failure = Worse(failure, ArtworkFetchFailure.NetworkError);
                    failureSource = source.Name;
                    continue;
                }

                bestScore = Math.Max(bestScore, result.BestScore);
                if (result.Candidate is null)
                {
                    failure = Worse(failure, result.Failure);
                    failureSource = source.Name;
                    continue;
                }

                var kind = result.Candidate.SourceUrl.Contains("itunes", StringComparison.OrdinalIgnoreCase)
                    || result.Candidate.SourceUrl.Contains("mzstatic", StringComparison.OrdinalIgnoreCase)
                        ? ArtworkSourceKind.ITunes
                        : ArtworkSourceKind.PlayerCache;
                var metadata = new ArtworkCacheMetadata(key, kind, result.Candidate.SourceUrl,
                    artist ?? string.Empty, title ?? string.Empty, album ?? string.Empty, result.Candidate.Score,
                    DateTimeOffset.UtcNow);
                var entry = _cache.Write(key, result.Candidate.Bytes, metadata);
                if (entry is not null)
                {
                    return new ArtworkFetchOutcome(entry, ArtworkFetchFailure.None, source.Name,
                        result.Candidate.SourceUrl, result.Candidate.Score);
                }

                failure = Worse(failure, ArtworkFetchFailure.DecodeFailed);
                failureSource = source.Name;
            }

            return new ArtworkFetchOutcome(null, failure, failureSource, null, bestScore);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    /// <summary>Ranks failure classes by how much they explain (higher wins).</summary>
    private static ArtworkFetchFailure Worse(ArtworkFetchFailure current, ArtworkFetchFailure candidate)
        => Rank(candidate) > Rank(current) ? candidate : current;

    private static int Rank(ArtworkFetchFailure failure) => failure switch
    {
        ArtworkFetchFailure.NetworkError => 5,
        ArtworkFetchFailure.Timeout => 4,
        ArtworkFetchFailure.DecodeFailed => 3,
        ArtworkFetchFailure.BelowThreshold => 2,
        ArtworkFetchFailure.NoResults => 1,
        _ => 0,
    };

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
