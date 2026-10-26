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
    /// <summary>Tracks whose cached document came from a local provider (guarded by <c>_gate</c>).</summary>
    private readonly Dictionary<string, bool> _localServed = new(StringComparer.Ordinal);

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

        // An instrumental carries no words. Asking the catalogue anyway is what returned some other
        // song's lyric and scrolled it under this track (user report 2026-10-22). The island then shows
        // the title and artist, which is exactly what an instrumental should show.
        if (InstrumentalDetector.LooksInstrumental(query.Title))
        {
            _logger.LogInfo(PlatformLogCategory.Media, "lyrics.instrumental",
                $"No lyric lookup for instrumental track '{query.Title}'.");
            return null;
        }

        if (_cache.TryGet(query.TrackKey, out var cached))
        {
            if (ServedByLocalCache(query.TrackKey))
            {
                return cached;
            }

            // The player downloads its .krc a while after the track starts, so an online document obtained earlier is
            // only provisional: re-probe the local providers (file index, no network) and prefer their answer.
            if (await TryLocalProvidersAsync(query, cancellationToken).ConfigureAwait(false) is { } local)
            {
                return local;
            }

            return cached;
        }

        PendingRequest request;
        lock (_gate)
        {
            if (!_pending.TryGetValue(query.TrackKey, out request!))
            {
                request = new PendingRequest(query);
                _pending[query.TrackKey] = request;
                // Fire-and-forget by design (the caller awaits request.Task), but never unobserved:
                // an escaping exception here used to surface only as a TaskScheduler.UnobservedTaskException
                // breadcrumb at finalization.
                _ = RunRequestSafelyAsync(request);
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

    /// <summary>Re-runs only the local (file-backed) providers; returns null when they cannot answer.</summary>
    /// <param name="query">Track being looked up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<LyricsDocument?> TryLocalProvidersAsync(LyricsQuery query, CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            if (provider is not { IsLocalCache: true } || cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            try
            {
                var document = await provider.FetchAsync(query, cancellationToken).ConfigureAwait(false);
                if (document is not { IsEmpty: false })
                {
                    continue;
                }

                _cache.Add(query.TrackKey, document);
                MarkLocalCache(query.TrackKey, true);
                _localServed[query.TrackKey] = true;
                _logger.LogDebug(PlatformLogCategory.Media, "lyrics.kugou-local",
                    "The player's lyric file appeared; replacing the provisional online document.");
                return document;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(PlatformLogCategory.Media, "lyrics.local-recheck-failed", ex.Message);
            }
        }

        return null;
    }

    /// <summary>Whether the cached document came from a local (player file) provider.</summary>
    /// <param name="trackKey">Track key.</param>
    private bool ServedByLocalCache(string trackKey)
    {
        lock (_gate)
        {
            return _localServed.TryGetValue(trackKey, out var local) && local;
        }
    }

    private void MarkLocalCache(string trackKey, bool value)
    {
        lock (_gate)
        {
            _localServed[trackKey] = value;
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
                    // WaitAsync abandons the provider task when the timeout or a cancel wins. Abandoned
                    // tasks are not awaited, so a late fault (a broken TLS handshake is the common one)
                    // had nobody to observe it and escalated to TaskScheduler.UnobservedTaskException —
                    // that is the steady "SSL connection could not be established" noise in crash.log.
                    var fetch = provider.FetchAsync(request.Query, request.Token);
                    ObserveLateFault(fetch);
                    document = await fetch
                        .WaitAsync(_providerTimeout, request.Token)
                        .ConfigureAwait(false);
                    if (document is { IsEmpty: false } && InstrumentalDetector.IsPlaceholder(document))
                    {
                        // KuGou ships "纯音乐，请欣赏" as the lyric of an instrumental; that is not a lyric.
                        _logger.LogInfo(PlatformLogCategory.Media, "lyrics.instrumental-placeholder",
                            $"Provider '{provider.Name}' returned an instrumental placeholder for '{request.Query.Title}'.");
                        return;
                    }

                    if (document is { IsEmpty: false })
                    {
                        _cache.Add(request.Query.TrackKey, document);
                        MarkLocalCache(request.Query.TrackKey, provider.IsLocalCache);
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

    /// <summary>
    /// Runs one single-flight request and keeps any escaping exception observed: the pipeline is started
    /// with a discarded task handle, so a throw here would otherwise only appear at finalization.
    /// </summary>
    private async Task RunRequestSafelyAsync(PendingRequest request)
    {
        try
        {
            await RunRequestAsync(request).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // RunRequestAsync already logs per-provider failures and always completes the request in its
            // finally block; this is the "should not happen" net.
            _counters.IncrementLyricsFetchFailures();
            _logger.LogWarning(PlatformLogCategory.Media, "lyrics.request-failed",
                "The lyrics request pipeline failed.", ex);
        }
    }

    /// <summary>
    /// Marks a fault of a task the caller may abandon (timeout, cancellation) as observed, so an expected
    /// network failure never escalates to <see cref="TaskScheduler.UnobservedTaskException"/> at
    /// finalization time.
    /// </summary>
    private static void ObserveLateFault(Task task)
        => _ = task.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
