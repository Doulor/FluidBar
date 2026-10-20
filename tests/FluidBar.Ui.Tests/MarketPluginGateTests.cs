using FluidBar.App.Plugins;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Ui.Tests;

/// <summary>M4 market-channel load-time trust gate rejects unsigned/flat packages.</summary>
public sealed class MarketPluginGateTests
{
    [Fact]
    public async Task UnsignedVersionedPackageIsRejectedAtLoadTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-market-" + Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "sample-plugin", "1.0.0");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            var manifest = new PluginManifest { Id = "sample-plugin", Version = "1.0.0", ApiVersion = "2.0.0" };
            var discovery = new PluginDiscoveryInfo(
                manifest.Id, pluginDirectory, Path.Combine(pluginDirectory, "manifest.json"), manifest, true, []);
            var gate = new MarketPluginGate(root, MarketplaceTrustOptions.Empty);

            var result = await gate.EvaluateAsync(discovery, CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.Contains("integrity", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FlatMarketPackageIsRejectedBeforeReadingIntegrity()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-market-" + Guid.NewGuid().ToString("N"));
        var pluginDirectory = Path.Combine(root, "sample-plugin");
        Directory.CreateDirectory(pluginDirectory);
        try
        {
            var manifest = new PluginManifest { Id = "sample-plugin", Version = "1.0.0", ApiVersion = "2.0.0" };
            var discovery = new PluginDiscoveryInfo(
                manifest.Id, pluginDirectory, Path.Combine(pluginDirectory, "manifest.json"), manifest, true, []);
            var gate = new MarketPluginGate(root, MarketplaceTrustOptions.Empty);

            var result = await gate.EvaluateAsync(discovery, CancellationToken.None);

            Assert.False(result.Allowed);
            Assert.Contains("versioned", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

