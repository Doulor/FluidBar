using System.Security.Cryptography;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;

namespace FluidBar.Marketplace.Index;

/// <summary>Reuses the original signed release bytes for an already-published id/version.</summary>
public static class MarketplaceReleasedArtifact
{
    public static MarketplacePluginArtifact Load(string packagePath, string expectedId, string expectedVersion, MarketplaceTrustOptions trust)
    {
        var signaturePath = packagePath + ".sig.json";
        var signature = PackageSignatureDocument.Parse(File.ReadAllText(signaturePath));
        using var package = File.OpenRead(packagePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        var verification = new PackageSignatureVerifier(trust).Verify(sha256, package.Length, signature);
        if (!verification.Verified)
            throw new MarketplaceIndexException($"Published package verification failed: {string.Join("; ", verification.Errors)}");
        var manifest = FbpPackageReader.Inspect(File.ReadAllBytes(packagePath)).Manifest;
        if (manifest.Id != expectedId || manifest.Version != expectedVersion)
            throw new MarketplaceIndexException($"Published package identity does not match {expectedId} {expectedVersion}.");
        return new MarketplacePluginArtifact
        {
            Manifest = manifest, PackagePath = packagePath, SignaturePath = signaturePath,
            Sha256 = verification.Sha256, Size = verification.Size,
            PublishedAt = signature.Signatures.First(entry => entry.Role == SignatureRole.Marketplace).SignedAt,
        };
    }
}
