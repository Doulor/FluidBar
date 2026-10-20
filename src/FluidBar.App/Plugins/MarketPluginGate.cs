using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk.Versioning;

namespace FluidBar.App.Plugins;

/// <summary>
/// Load-time trust gate for the market channel. It re-verifies the retained signed artifact and the
/// extracted tree on every discovery/activation; unsigned or unreviewed packages never reach PluginHost.
/// The local channel intentionally does not use this gate.
/// </summary>
public sealed class MarketPluginGate
{
    private readonly string _root;
    private readonly MarketplaceTrustOptions _trust;

    public MarketPluginGate(string root, MarketplaceTrustOptions trust)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
    }

    public async Task<PluginPackageGateResult> EvaluateAsync(
        PluginDiscoveryInfo discovery,
        CancellationToken cancellationToken)
    {
        if (discovery.Manifest is not { } manifest)
        {
            return PluginPackageGateResult.Reject("Market package has no valid manifest.",
                PluginLoadFailureKind.ManifestInvalid);
        }

        var packageDirectory = Path.GetFullPath(discovery.PackageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pluginDirectory = Path.GetDirectoryName(packageDirectory);
        var parentDirectory = pluginDirectory is null ? null : Path.GetDirectoryName(pluginDirectory);
        if (parentDirectory is null
            || !string.Equals(parentDirectory, _root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(pluginDirectory), manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            return PluginPackageGateResult.Reject(
                "Market packages must use the versioned layout <plugin-id>/<version>/manifest.json.");
        }

        var versionName = Path.GetFileName(packageDirectory);
        if (!SemanticVersion.TryParse(versionName, out var folderVersion)
            || !manifest.TryGetVersion(out var manifestVersion)
            || folderVersion != manifestVersion)
        {
            return PluginPackageGateResult.Reject(
                "The market package version directory does not match the manifest version.");
        }

        var marker = PluginIntegrity.ReadMarker(packageDirectory, out var markerError);
        if (marker is null)
        {
            return PluginPackageGateResult.Reject(markerError ?? "Package is not marketplace-reviewed.");
        }

        if (!string.Equals(marker.PluginId, manifest.Id, StringComparison.Ordinal)
            || !string.Equals(marker.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            return PluginPackageGateResult.Reject("The integrity marker does not match the plugin manifest.");
        }

        var verification = await PluginIntegrity.VerifyAsync(packageDirectory, _trust, cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Verified)
        {
            return PluginPackageGateResult.Reject(
                "Market package integrity verification failed: " + string.Join("; ", verification.Errors));
        }

        return PluginPackageGateResult.Allow;
    }
}

