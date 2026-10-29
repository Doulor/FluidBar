using System.Net;
using System.Net.Http.Headers;

namespace FluidBar.Platform.Windows.Telemetry;

/// <summary>Outcome of one upload attempt sequence.</summary>
public enum TelemetrySendStatus
{
    /// <summary>The Worker accepted the batch (2xx).</summary>
    Sent,

    /// <summary>Every attempt failed; the batch should be retried later.</summary>
    Failed,

    /// <summary>The Worker rejected the batch as invalid; retrying cannot help.</summary>
    Rejected,

    /// <summary>The endpoint answered "not configured"; telemetry is a no-op on this build.</summary>
    Unavailable,
}

/// <summary>Result of <see cref="HttpTelemetrySink.SendAsync"/>.</summary>
/// <param name="Status">Outcome of the attempt sequence.</param>
/// <param name="StatusCode">Last HTTP status, when there was one.</param>
/// <param name="Detail">Short, non-sensitive description used for local logging only.</param>
public sealed record TelemetrySendResult(TelemetrySendStatus Status, int? StatusCode, string? Detail)
{
    /// <summary>True when the batch must be kept for a later attempt.</summary>
    public bool ShouldRetry => Status == TelemetrySendStatus.Failed;
}

/// <summary>
/// Posts telemetry batches to the FluidBar Worker.
/// </summary>
/// <remarks>
/// Deliberately dumb and quiet: system proxy (so corporate/CN environments work), a short timeout, at most three
/// attempts with linear backoff, and never an exception escaping to the caller — an unreachable endpoint must never
/// affect startup or shutdown. No cookies, no redirects, no query string, no user content in headers.
/// </remarks>
public sealed class HttpTelemetrySink : IDisposable
{
    /// <summary>Production endpoint (sub-domain owned by the site Worker).</summary>
    public static readonly Uri DefaultEndpoint = new("https://t.fluidbar.doulor.cn/v1/batch");

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly TimeSpan _retryDelay;
    private readonly int _attempts;

    /// <summary>Creates a sink.</summary>
    /// <param name="endpoint">Batch endpoint.</param>
    /// <param name="httpClient">Optional client (tests); when null a private one is created.</param>
    /// <param name="timeout">Per-attempt timeout.</param>
    /// <param name="attempts">Attempt ceiling (1–5).</param>
    /// <param name="retryDelay">Delay between attempts.</param>
    public HttpTelemetrySink(
        Uri? endpoint = null,
        HttpClient? httpClient = null,
        TimeSpan? timeout = null,
        int attempts = 3,
        TimeSpan? retryDelay = null)
    {
        Endpoint = endpoint ?? DefaultEndpoint;
        _attempts = Math.Clamp(attempts, 1, 5);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        _ownsClient = httpClient is null;
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            UseProxy = true,
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        })
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(8),
        };
    }

    /// <summary>Endpoint this sink posts to.</summary>
    public Uri Endpoint { get; }

    /// <summary>Posts one batch, retrying transient failures.</summary>
    /// <param name="payload">UTF-8 JSON body built by the Core payload writer.</param>
    /// <param name="installId">Anonymous install identifier (sent as a header for routing, not logged).</param>
    /// <param name="appVersion">Version stamp, for the User-Agent.</param>
    /// <param name="cancellationToken">Cancellation for shutdown.</param>
    /// <returns>The outcome; never throws for network or protocol errors.</returns>
    public async Task<TelemetrySendResult> SendAsync(
        ReadOnlyMemory<byte> payload,
        string installId,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty)
        {
            return new TelemetrySendResult(TelemetrySendStatus.Rejected, null, "empty payload");
        }

        Exception? last = null;
        for (var attempt = 1; attempt <= _attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new ByteArrayContent(payload.ToArray()),
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.UserAgent.ParseAdd("FluidBar/" + Sanitize(appVersion));
                request.Headers.Add("X-FluidBar-Install", Sanitize(installId));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    return new TelemetrySendResult(TelemetrySendStatus.Sent, (int)response.StatusCode, null);
                }

                if (response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.Gone ||
                    response.StatusCode == HttpStatusCode.NotImplemented)
                {
                    return new TelemetrySendResult(
                        TelemetrySendStatus.Unavailable, (int)response.StatusCode, "endpoint not configured");
                }

                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge
                    or HttpStatusCode.UnsupportedMediaType)
                {
                    return new TelemetrySendResult(
                        TelemetrySendStatus.Rejected, (int)response.StatusCode, "batch rejected");
                }

                last = null;
                if (attempt < _attempts)
                {
                    await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new TelemetrySendResult(
                    TelemetrySendStatus.Failed, (int)response.StatusCode, "server error");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new TelemetrySendResult(TelemetrySendStatus.Failed, null, "cancelled");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                last = ex;
                if (attempt < _attempts)
                {
                    try
                    {
                        await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return new TelemetrySendResult(TelemetrySendStatus.Failed, null, "cancelled");
                    }

                    continue;
                }
            }
        }

        return new TelemetrySendResult(
            TelemetrySendStatus.Failed, null, last?.GetType().Name ?? "unreachable");
    }

    /// <summary>Replaces anything that is not a safe header character.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        var length = 0;
        foreach (var c in value)
        {
            if (length == buffer.Length)
            {
                break;
            }

            buffer[length++] = char.IsLetterOrDigit(c) || c is '.' or '-' or '+' or '_' ? c : '-';
        }

        return new string(buffer[..length]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
