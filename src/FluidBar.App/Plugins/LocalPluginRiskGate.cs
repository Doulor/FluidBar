using FluidBar.PluginHost.Loader;

namespace FluidBar.App.Plugins;

/// <summary>Prevents an unacknowledged local version from auto-loading; UI acknowledges id@version first.</summary>
public sealed class LocalPluginRiskGate
{
    private readonly LocalPluginRiskStore _store;

    public LocalPluginRiskGate(LocalPluginRiskStore store) => _store = store;

    public Task<PluginPackageGateResult> EvaluateAsync(PluginDiscoveryInfo discovery, CancellationToken cancellationToken)
    {
        if (discovery.Manifest is not { } manifest)
        {
            return Task.FromResult(PluginPackageGateResult.Reject("Local package has no valid manifest.",
                PluginLoadFailureKind.ManifestInvalid));
        }

        return Task.FromResult(_store.IsAcknowledged(manifest.Id, manifest.Version)
            ? PluginPackageGateResult.Allow
            : PluginPackageGateResult.Reject(
                "The first load of this local plugin version requires the one-time risk acknowledgement."));
    }
}

