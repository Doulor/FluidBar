using System.Net;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Creates the HTTP clients used for media lookups (artwork catalogues, durations, lyrics).
/// </summary>
/// <remarks>
/// <para>
/// <b>These clients deliberately bypass the system (WinINET) proxy.</b> A typical desktop in mainland China runs a
/// local proxy client (Clash/V2Ray style, e.g. <c>127.0.0.1:7897</c>) for the blocked foreign sites, and that
/// proxy blackholes the domestic hosts instead of failing fast. `HttpClient` honours it by default, so every
/// request to <c>songsearch.kugou.com</c> or <c>music.163.com</c> hung until the client timeout while the same
/// request answered in 0.3 s from curl (which ignores WinINET). That is why covers fell back to the gradient
/// placeholder and lyric providers logged "timed out" (diagnosed 2026-10-26: 12 s timeout through the proxy,
/// 368 ms and three covers at score 1.000 with the proxy bypassed).
/// </para>
/// <para>
/// Set <c>FLUIDBAR_MEDIA_PROXY=system</c> to go through the configured proxy anyway (corporate networks that
/// require it), at the cost of the failure mode above when the proxy cannot reach the domestic hosts.
/// </para>
/// </remarks>
public static class MediaHttpClientFactory
{
    /// <summary>Environment switch that restores the system-proxy behaviour.</summary>
    public const string ProxyModeVariable = "FLUIDBAR_MEDIA_PROXY";

    /// <summary>Default user agent for media lookups.</summary>
    public const string DefaultUserAgent = "FluidBar/2.0 (media)";

    /// <summary>Creates a client for media lookups.</summary>
    /// <param name="timeout">Client timeout (per-request budgets still apply on top).</param>
    /// <param name="userAgent">User agent to advertise.</param>
    public static HttpClient Create(TimeSpan timeout, string userAgent = DefaultUserAgent)
    {
        var client = new HttpClient(CreateHandler(UseSystemProxy()), disposeHandler: true) { Timeout = timeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return client;
    }

    /// <summary>True when <c>FLUIDBAR_MEDIA_PROXY=system</c> asks for the system proxy.</summary>
    public static bool UseSystemProxy()
        => string.Equals(Environment.GetEnvironmentVariable(ProxyModeVariable), "system",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the handler (separate so tests can assert the proxy policy).</summary>
    /// <param name="useSystemProxy">Whether the configured system proxy should be used.</param>
    internal static SocketsHttpHandler CreateHandler(bool useSystemProxy) => new()
    {
        UseProxy = useSystemProxy,
        // A host that is merely slow must not eat the whole per-source budget.
        ConnectTimeout = TimeSpan.FromSeconds(4),
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
}
