using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Catalog;

/// <summary>Why a catalog fetch failed. Values, not exceptions: the marketplace UI renders a state.</summary>
public enum CatalogFetchStatus
{
    Success = 0,
    NotModified,
    NetworkError,
    HttpError,
    InvalidPayload,
    SignatureInvalid,
    RolledBack,
    Cancelled,
}

/// <summary>Options for the async marketplace catalog client.</summary>
public sealed record MarketplaceCatalogOptions
{
    /// <summary>Primary signed index URL (HTTPS Pages/raw).</summary>
    public required Uri IndexUrl { get; init; }

    /// <summary>Optional fallback index URL (raw.githubusercontent mirror/bootstrap).</summary>
    public Uri? FallbackIndexUrl { get; init; }

    /// <summary>Injected marketplace trust keys; there is no implicit trust.</summary>
    public MarketplaceTrustOptions Trust { get; init; } = MarketplaceTrustOptions.Empty;

    /// <summary>Per-request timeout; applied through a linked CTS, never <see cref="HttpClient.Timeout"/>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Bounded response size; an index over this is rejected instead of buffered.</summary>
    public long MaxResponseBytes { get; init; } = 4L * 1024 * 1024;

    /// <summary>Rollback protection: reject an index older than the last one the client accepted.</summary>
    public DateTimeOffset? MinimumGeneratedAt { get; init; }

    public string UserAgent { get; init; } = "FluidBar/2.0 (+https://fluidbar.app)";

    /// <summary>Optional host/API version for compatibility filtering.</summary>
    public SemanticVersion? AppVersion { get; init; }

    public SemanticVersion? HostApiVersion { get; init; }
}

/// <summary>Result of an index fetch.</summary>
public sealed record CatalogFetchResult
{
    public required CatalogFetchStatus Status { get; init; }

    public MarketplaceIndexDocument? Index { get; init; }

    public string? Error { get; init; }

    public bool FromCache { get; init; }

    public DateTimeOffset? FetchedAt { get; init; }

    public static CatalogFetchResult Fail(CatalogFetchStatus status, string error) => new() { Status = status, Error = error };
}

/// <summary>Result of a per-plugin history shard fetch.</summary>
public sealed record PluginHistoryFetchResult
{
    public required CatalogFetchStatus Status { get; init; }

    public PluginHistoryShard? Shard { get; init; }

    public string? Error { get; init; }

    public static PluginHistoryFetchResult Fail(CatalogFetchStatus status, string error) => new() { Status = status, Error = error };
}

/// <summary>
/// Async marketplace catalog client. All HTTP goes through an injectable <see cref="HttpMessageHandler"/>
/// (tests never touch the network), every call is cancellable with an explicit timeout, responses are size
/// bounded, the index signature is verified before it is trusted, and a single-entry ETag cache avoids
/// unbounded memory. No synchronous HTTP exists on any path.
/// </summary>
public sealed class MarketplaceCatalogClient : IDisposable
{
    private readonly MarketplaceCatalogOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IndexSignatureVerifier _indexVerifier;
    private readonly bool _ownsClient;

    private string? _cachedIndexJson;
    private string? _cachedETag;
    private DateTimeOffset? _cachedGeneratedAt;

    public MarketplaceCatalogClient(MarketplaceCatalogOptions options, HttpMessageHandler? handler = null)
        : this(options, handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false), ownsClient: true)
    {
    }

    /// <summary>Test/App constructor with an injected client (caller keeps ownership).</summary>
    public MarketplaceCatalogClient(MarketplaceCatalogOptions options, HttpClient httpClient)
        : this(options, httpClient, ownsClient: false)
    {
    }

    private MarketplaceCatalogClient(MarketplaceCatalogOptions options, HttpClient httpClient, bool ownsClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
        _indexVerifier = new IndexSignatureVerifier(options.Trust);
    }

    /// <summary>Fetches + verifies the signed root index.</summary>
    public async Task<CatalogFetchResult> FetchIndexAsync(CancellationToken cancellationToken = default)
    {
        var primary = await FetchIndexCoreAsync(_options.IndexUrl, cancellationToken).ConfigureAwait(false);
        if (primary.Status == CatalogFetchStatus.Success ||
            primary.Status == CatalogFetchStatus.NotModified ||
            primary.Status == CatalogFetchStatus.Cancelled)
        {
            return primary;
        }

        if (_options.FallbackIndexUrl is { } fallback && fallback != _options.IndexUrl)
        {
            var fallbackResult = await FetchIndexCoreAsync(fallback, cancellationToken).ConfigureAwait(false);
            if (fallbackResult.Status == CatalogFetchStatus.Success)
            {
                return fallbackResult;
            }

            return CatalogFetchResult.Fail(primary.Status, $"{primary.Error} Fallback failed: {fallbackResult.Error}");
        }

        return primary;
    }

    /// <summary>Fetches a per-plugin version history shard (detail page / rollback UI).</summary>
    public async Task<PluginHistoryFetchResult> FetchPluginHistoryAsync(Uri historyUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historyUrl);
        if (historyUrl.Scheme != Uri.UriSchemeHttps)
        {
            return PluginHistoryFetchResult.Fail(CatalogFetchStatus.InvalidPayload, "History URLs must be HTTPS.");
        }

        try
        {
            var json = await GetStringAsync(historyUrl, ifNoneMatch: null, cancellationToken).ConfigureAwait(false);
            if (json.IsNotModified || json.Body is null)
            {
                return PluginHistoryFetchResult.Fail(CatalogFetchStatus.InvalidPayload, "History shard was not modified but no cached copy exists.");
            }

            return new PluginHistoryFetchResult
            {
                Status = CatalogFetchStatus.Success,
                Shard = MarketplaceIndexParser.ParseShard(json.Body),
            };
        }
        catch (MarketplaceIndexException exception)
        {
            return PluginHistoryFetchResult.Fail(CatalogFetchStatus.InvalidPayload, exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return PluginHistoryFetchResult.Fail(CatalogFetchStatus.NetworkError, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PluginHistoryFetchResult.Fail(CatalogFetchStatus.Cancelled, "History fetch was cancelled.");
        }
        catch (TimeoutException)
        {
            return PluginHistoryFetchResult.Fail(CatalogFetchStatus.NetworkError, $"History fetch exceeded the {_options.Timeout} timeout.");
        }
    }

    private async Task<CatalogFetchResult> FetchIndexCoreAsync(Uri url, CancellationToken cancellationToken)
    {
        string? body;
        try
        {
            var response = await GetStringAsync(url, _cachedETag, cancellationToken).ConfigureAwait(false);
            if (response.IsNotModified)
            {
                return _cachedIndexJson is null
                    ? CatalogFetchResult.Fail(CatalogFetchStatus.InvalidPayload, "Server returned 304 but no cached index exists.")
                    : new CatalogFetchResult
                    {
                        Status = CatalogFetchStatus.NotModified,
                        Index = MarketplaceIndexParser.Parse(_cachedIndexJson),
                        FromCache = true,
                        FetchedAt = _cachedGeneratedAt,
                    };
            }

            body = response.Body ?? throw new CatalogFetchStatusException(CatalogFetchStatus.InvalidPayload, "Index response was empty.");
            _cachedETag = response.ETag;
        }
        catch (CatalogFetchStatusException exception)
        {
            return CatalogFetchResult.Fail(exception.Status, exception.Message);
        }
        catch (HttpRequestException exception)
        {
            return CatalogFetchResult.Fail(CatalogFetchStatus.NetworkError, $"Index download failed: {exception.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return CatalogFetchResult.Fail(CatalogFetchStatus.Cancelled, "Index fetch was cancelled.");
        }
        catch (TimeoutException)
        {
            return CatalogFetchResult.Fail(CatalogFetchStatus.NetworkError, $"Index fetch exceeded the {_options.Timeout} timeout.");
        }

        var minimumGeneratedAt = Max(_options.MinimumGeneratedAt, _cachedGeneratedAt);
        var verification = _indexVerifier.Verify(body, minimumGeneratedAt);
        if (!verification.Verified)
        {
            var signatureFailure = verification.Errors.Any(static error => error.Contains("rollback", StringComparison.OrdinalIgnoreCase))
                ? CatalogFetchStatus.RolledBack
                : CatalogFetchStatus.SignatureInvalid;
            return CatalogFetchResult.Fail(signatureFailure, string.Join("; ", verification.Errors));
        }

        try
        {
            var index = MarketplaceIndexParser.Parse(body);
            _cachedIndexJson = body;
            _cachedGeneratedAt = index.GeneratedAt;
            return new CatalogFetchResult
            {
                Status = CatalogFetchStatus.Success,
                Index = index,
                FetchedAt = index.GeneratedAt,
            };
        }
        catch (MarketplaceIndexException exception)
        {
            return CatalogFetchResult.Fail(CatalogFetchStatus.InvalidPayload, exception.Message);
        }
    }

    private async Task<HttpTextResponse> GetStringAsync(Uri url, string? ifNoneMatch, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(ifNoneMatch));
        }

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new HttpTextResponse(true, null, ifNoneMatch);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogFetchStatusException(
                    CatalogFetchStatus.HttpError,
                    $"Index request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var etag = response.Headers.ETag?.Tag;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > _options.MaxResponseBytes)
                {
                    throw new CatalogFetchStatusException(
                        CatalogFetchStatus.InvalidPayload,
                        $"Index response exceeds the {_options.MaxResponseBytes} byte limit.");
                }

                memory.Write(buffer, 0, read);
            }

            return new HttpTextResponse(false, Encoding.UTF8.GetString(memory.ToArray()), etag);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record HttpTextResponse(bool IsNotModified, string? Body, string? ETag);

    private sealed class CatalogFetchStatusException(CatalogFetchStatus status, string message) : Exception(message)
    {
        internal CatalogFetchStatus Status { get; } = status;
    }
}
