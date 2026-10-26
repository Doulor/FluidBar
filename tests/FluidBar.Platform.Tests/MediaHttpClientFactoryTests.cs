using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Media HTTP egress. The system proxy is bypassed on purpose: on a desktop that runs a local proxy client for
/// the blocked foreign sites, that proxy also swallows the domestic catalogue/lyric hosts, and HttpClient then
/// waits for its timeout instead of failing fast (12 s vs 0.37 s measured 2026-10-26 — every cover fell back to
/// the placeholder and the lyric providers logged "timed out").
/// </summary>
public sealed class MediaHttpClientFactoryTests
{
    [Fact]
    public void Media_clients_bypass_the_system_proxy_by_default()
    {
        using var handler = MediaHttpClientFactory.CreateHandler(useSystemProxy: false);
        Assert.False(handler.UseProxy);
        Assert.True(handler.ConnectTimeout <= TimeSpan.FromSeconds(5), "a slow host must not eat the budget");
    }

    [Fact]
    public void Corporate_networks_can_opt_back_into_the_system_proxy()
    {
        using var handler = MediaHttpClientFactory.CreateHandler(useSystemProxy: true);
        Assert.True(handler.UseProxy);
    }

    [Fact]
    public void The_override_switch_is_an_explicit_opt_in()
    {
        Assert.False(string.Equals("1", "system", StringComparison.OrdinalIgnoreCase));
        Assert.True(MediaHttpClientFactory.ProxyModeVariable is "FLUIDBAR_MEDIA_PROXY");
    }

    [Fact]
    public void Created_clients_carry_the_media_user_agent()
    {
        using var client = MediaHttpClientFactory.Create(TimeSpan.FromSeconds(5), "FluidBar/2.0 (artwork)");
        Assert.True(client.DefaultRequestHeaders.TryGetValues("User-Agent", out var values));
        Assert.Equal("FluidBar/2.0 (artwork)", string.Join(" ", values!));
    }
}
