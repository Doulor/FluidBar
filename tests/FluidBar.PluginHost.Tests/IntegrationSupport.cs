using System.Security.Cryptography;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;

namespace FluidBar.PluginHost.Tests;

/// <summary>Generated-at-test-time P-256 keys for package signing in integration tests.</summary>
internal sealed class HostTestKeys : IDisposable
{
    internal HostTestKeys()
    {
        MarketplaceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MarketplaceTrusted = TrustedKey.FromPublicKey(MarketplaceKey, "host-tests-marketplace", SignatureRole.Marketplace);
        Trust = new MarketplaceTrustOptions { MarketplaceKeys = [MarketplaceTrusted] };
    }

    internal ECDsa MarketplaceKey { get; }

    internal TrustedKey MarketplaceTrusted { get; }

    internal MarketplaceTrustOptions Trust { get; }

    internal PackageSignatureDocument Sign(string packagePath)
    {
        var bytes = File.ReadAllBytes(packagePath);
        var digest = SHA256.HashData(bytes);
        var entry = PackageSigner.SignDigest(MarketplaceKey, digest, MarketplaceTrusted.KeyId, SignatureRole.Marketplace);
        return PackageSigner.CreateDocument(packagePath, digest, bytes.Length, [entry]);
    }

    public void Dispose() => MarketplaceKey.Dispose();
}

internal static class SamplePackageBuilder
{
    /// <summary>Builds a signed .fbp from a sample plugin project output directory.</summary>
    internal static (string PackagePath, PackageSignatureDocument Signature) BuildSigned(
        string sampleProjectName,
        string outputDirectory,
        HostTestKeys keys)
    {
        var sampleOutput = TestPaths.SampleOutput(sampleProjectName);
        using var staging = FbpStagingBuilder.FromPluginOutput(sampleOutput);
        Directory.CreateDirectory(outputDirectory);
        var packagePath = Path.Combine(outputDirectory, sampleProjectName + ".fbp");
        staging.Build(packagePath);
        return (packagePath, keys.Sign(packagePath));
    }
}

internal sealed class RecordingActivator : FluidBar.Marketplace.Install.IPluginActivator
{
    private readonly List<string> _activated = [];

    internal IReadOnlyList<string> Activated => _activated;

    public Task<FluidBar.Marketplace.Install.ActivationResult> ActivateAsync(
        FluidBar.Sdk.Manifest.PluginManifest manifest,
        string versionDirectory,
        CancellationToken cancellationToken)
    {
        _activated.Add($"{manifest.Id}@{manifest.Version}");
        return Task.FromResult(FluidBar.Marketplace.Install.ActivationResult.Ok());
    }

    public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
}
