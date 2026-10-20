using FluidBar.App.Plugins;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Ui.Tests;

/// <summary>M4 local-channel one-time risk acknowledgement keyed by id@version.</summary>
public sealed class LocalPluginRiskTests
{
    [Fact]
    public void AcknowledgementIsScopedToIdAndVersionAndPersists()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-risk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalPluginRiskStore(root);
            Assert.False(store.IsAcknowledged("sample.plugin", "1.0.0"));

            store.Acknowledge("sample.plugin", "1.0.0");
            Assert.True(store.IsAcknowledged("sample.plugin", "1.0.0"));
            Assert.False(store.IsAcknowledged("sample.plugin", "1.1.0"));
            Assert.False(store.IsAcknowledged("other.plugin", "1.0.0"));

            var reloaded = new LocalPluginRiskStore(root);
            Assert.True(reloaded.IsAcknowledged("sample.plugin", "1.0.0"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingAcknowledgementBlocksTheLocalGate()
    {
        var root = Path.Combine(Path.GetTempPath(), "fluidbar-risk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new LocalPluginRiskStore(root);
            var gate = new LocalPluginRiskGate(store);
            var manifest = new PluginManifest { Id = "sample.plugin", Version = "1.0.0", ApiVersion = "2.0.0" };
            var discovery = new FluidBar.PluginHost.Loader.PluginDiscoveryInfo(
                "sample.plugin", root, Path.Combine(root, "manifest.json"), manifest, true, []);
            var rejected = gate.EvaluateAsync(discovery, CancellationToken.None).GetAwaiter().GetResult();
            Assert.False(rejected.Allowed);
            Assert.Contains("risk", rejected.Error, StringComparison.OrdinalIgnoreCase);

            store.Acknowledge("sample.plugin", manifest.Version);
            var allowed = gate.EvaluateAsync(discovery, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(allowed.Allowed, allowed.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

