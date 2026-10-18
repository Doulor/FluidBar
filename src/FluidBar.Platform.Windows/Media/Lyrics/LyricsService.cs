using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media.Lyrics;

/// <summary>
/// Async, single-flight, cancellable lyrics service with a bounded cache. Concurrent requests for the
/// same track share one fetch; a new track cancels the previous track's in-flight request.
/// </summary>
/// <remarks>
/// Providers are tried in order with a per-provider timeout; failures are logged and degradation is
/// silent (no lyrics line is shown), never blocking the UI thread.
/// </remarks>
public sealed class LyricsService : IAsyncDisposable, ILyricsLookup
{
    private static readonly TimeSpan DefaultProviderTimeout = TimeSpan.FromSeconds(8);

    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly LyricsCache _cache;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeSpan _providerTimeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private int _disposed;

    public LyricsService(IEnumerable<ILyricsProvider> providers, LyricsCache cache, IPlatformLogger logger,
        PlatformCounters counters, TimeSpan? providerTimeout = null)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _providerTimeout = providerTimeout ?? DefaultProviderTimeout;
    }

    /// <summary>The bounded cache (exposed for diagnostics and tests).</summary>
    public LyricsCache Cache => _cache;

    /// <summary>Fetches lyrics for a track (cache first, single-flight). Returns null when unavailable.</summary>
    public async Task<LyricsDocument?> GetLyricsAsync(LyricsQuery query, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrEmpty(query.TrackKey) || !query.IsSearchable)
        {
            return null;
        }

        if (_cache.TryGet(query.TrackKey, out var cached))
        {
            return cached;
        }

        PendingRequest request;
        lock (_gate)
        {
            if (!_pending.TryGetValue(query.TrackKey, out request!))
            {
                request = new PendingRequest(query);
                _pending[query.TrackKey] = request;
                _ = RunRequestAsync(request);
            }
        }

        return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels in-flight fetches other than the track that is currently active.</summary>
    public void CancelPending(string? exceptTrackKey = null)
    {
        PendingRequest[] toCancel;
        lock (_gate)
        {
            toCancel = _pending.Where(pair => !string.Equals(pair.Key, exceptTrackKey, StringComparison.Ordinal))
                .Select(pair => pair.Value).ToArray();
        }

        foreach (var request in toCancel)
        {
            request.Cancel();
        }
    }

    private async Task RunRequestAsync(PendingRequest request)
    {
        LyricsDocument? document = null;
        try
        {
            _counters.IncrementLyricsFetches();
            foreach (var provider in _providers)
            {
                if (request.Token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    document = await provider.FetchAsync(request.Query, request.Token)
                        .WaitAsync(_providerTimeout, request.Token)
                        .ConfigureAwait(false);
                    if (document is { IsEmpty: false })
                    {
                        _cache.Add(request.Query.TrackKey, document);
                        _logger.LogInfo(PlatformLogCategory.Media, "lyrics.fetched",
                            $"Lyrics found by provider '{provider.Name}'.",
                            new Dictionary<string, object?>
                            {
                                ["provider"] = provider.Name,
                                ["track"] = request.Query.TrackKey,
                                ["lines"] = document.Lines.Length,
                            });
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    _counters.IncrementLyricsFetchFailures();
                    _logger.LogWarning(PlatformLogCategory.Media, "lyrics.provider-timeout",
                        $"Lyrics provider '{provider.Name}' timed out.");
                }
                catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _counters.IncrementLyricsFetchFailures();
                    _logger.LogWarning(PlatformLogCategory.Media, "lyrics.provider-failed",
                        $"Lyrics provider '{provider.Name}' failed.", ex);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(request.Query.TrackKey);
            }

            request.Complete(document);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PendingRequest[] requests;
        lock (_gate)
        {
            requests = _pending.Values.ToArray();
            _pending.Clear();
        }

        foreach (var request in requests)
        {
            request.Cancel();
            request.Complete(null);
        }

        _cache.Dispose();
        await ValueTask.CompletedTask;
    }

    private sealed class PendingRequest : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<LyricsDocument?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PendingRequest(LyricsQuery query)
        {
            Query = query;
        }

        public LyricsQuery Query { get; }

        public CancellationToken Token => _cts.Token;

        public Task<LyricsDocument?> Task => _completion.Task;

        public void Cancel()
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already completed.
            }
        }

        public void Complete(LyricsDocument? document)
        {
            _completion.TrySetResult(document);
            _cts.Dispose();
        }

        public void Dispose() => _cts.Dispose();
    }
}
