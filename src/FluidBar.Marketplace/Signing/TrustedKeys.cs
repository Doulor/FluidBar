using System.Security.Cryptography;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Signing;

/// <summary>
/// A public key trusted for a role. Marketplace keys are compiled-in/injected by the client (never taken from
/// an unsigned document); publisher keys are pinned on first install (TOFU) and changed only with consent.
/// </summary>
public sealed record TrustedKey
{
    public required string KeyId { get; init; }

    public required SignatureRole Role { get; init; }

    public SignatureAlgorithm Algorithm { get; init; } = SignatureAlgorithm.EcdsaP256Sha256;

    /// <summary>Base64 SPKI (PKIX) public key material.</summary>
    public required string PublicKeySpkiBase64 { get; init; }

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>Returns the revocation/validity reason, or <see langword="null"/> when usable at <paramref name="timestamp"/>.</summary>
    public string? GetUnusableReason(DateTimeOffset timestamp, TimeSpan clockSkew)
    {
        if (RevokedAt is { } revokedAt && timestamp + clockSkew >= revokedAt)
        {
            return $"key '{KeyId}' was revoked at {revokedAt:O}.";
        }

        if (NotBefore is { } notBefore && timestamp + clockSkew < notBefore)
        {
            return $"key '{KeyId}' is not valid before {notBefore:O}.";
        }

        if (NotAfter is { } notAfter && timestamp - clockSkew > notAfter)
        {
            return $"key '{KeyId}' expired at {notAfter:O}.";
        }

        return null;
    }

    /// <summary>Creates a trusted key record from a P-256 public key.</summary>
    public static TrustedKey FromPublicKey(ECDsa publicKey, string keyId, SignatureRole role) =>
        new()
        {
            KeyId = keyId,
            Role = role,
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            PublicKeySpkiBase64 = EcdsaKeyMaterial.ExportPublicKeySpkiBase64(publicKey),
        };
}

/// <summary>Trust configuration for package and index verification. Production injects the marketplace public key.</summary>
public sealed class MarketplaceTrustOptions
{
    public static MarketplaceTrustOptions Empty { get; } = new();

    /// <summary>Keys allowed to produce a marketplace (index/package) signature.</summary>
    public IReadOnlyList<TrustedKey> MarketplaceKeys { get; init; } = [];

    /// <summary>Publisher keys pinned on first install (TOFU).</summary>
    public IReadOnlyList<TrustedKey> PublisherKeys { get; init; } = [];

    /// <summary>Allowed clock skew when checking signature validity windows.</summary>
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(5);

    public static MarketplaceTrustOptions ForMarketplaceKeys(params TrustedKey[] keys) =>
        new() { MarketplaceKeys = keys };

    public static MarketplaceTrustOptions ForPublisherKeys(params TrustedKey[] keys) =>
        new() { PublisherKeys = keys };

    public TrustedKey? FindMarketplaceKey(string keyId) =>
        MarketplaceKeys.FirstOrDefault(key =>
            string.Equals(key.KeyId, keyId, StringComparison.Ordinal) &&
            key.Role == SignatureRole.Marketplace);

    public TrustedKey? FindPublisherKey(string keyId) =>
        PublisherKeys.FirstOrDefault(key =>
            string.Equals(key.KeyId, keyId, StringComparison.Ordinal) &&
            key.Role == SignatureRole.Author);
}

/// <summary>ECDSA P-256 key helpers shared by the signer, the verifier, tests and the contributor CLI.</summary>
public static class EcdsaKeyMaterial
{
    /// <summary>Generates a fresh P-256 key (test keys are generated at test time; production keys live in an HSM).</summary>
    public static ECDsa GenerateP256Key() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Exports the public key as base64 SPKI.</summary>
    public static string ExportPublicKeySpkiBase64(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Imports a base64 SPKI P-256 public key; throws when the key is not P-256.</summary>
    public static ECDsa ImportPublicKeySpki(string base64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64);
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64), out var read);
            if (read != Convert.FromBase64String(base64).Length)
            {
                throw new CryptographicException("Unexpected trailing bytes in the public key.");
            }

            EnsureP256(key);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Throws when the key is not the P-256 curve (signature length/format assumptions depend on it).</summary>
    public static void EnsureP256(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.KeySize != 256)
        {
            throw new CryptographicException($"Expected a P-256 key, got a {key.KeySize}-bit key.");
        }

        var parameters = key.ExportParameters(includePrivateParameters: false);
        if (parameters.Curve.IsNamed &&
            !string.Equals(parameters.Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
        {
            throw new CryptographicException($"Expected the nistP256 curve, got '{parameters.Curve.Oid.FriendlyName ?? parameters.Curve.Oid.Value}'.");
        }
    }

    /// <summary>Signs a SHA-256 digest and returns base64 IEEE P1363 (r||s) bytes.</summary>
    public static string SignDigest(ECDsa privateKey, ReadOnlySpan<byte> sha256Digest)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (sha256Digest.Length != 32)
        {
            throw new ArgumentException("Expected a 32-byte SHA-256 digest.", nameof(sha256Digest));
        }

        EnsureP256(privateKey);
        var signature = privateKey.SignHash(
            sha256Digest.ToArray(),
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Convert.ToBase64String(signature);
    }

    /// <summary>Verifies a base64 IEEE P1363 signature over a SHA-256 digest.</summary>
    public static bool VerifyDigest(ECDsa publicKey, ReadOnlySpan<byte> sha256Digest, string signatureBase64)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureBase64);
        if (sha256Digest.Length != 32)
        {
            throw new ArgumentException("Expected a 32-byte SHA-256 digest.", nameof(sha256Digest));
        }

        try
        {
            EnsureP256(publicKey);
            var signature = Convert.FromBase64String(signatureBase64);
            return signature.Length == 64 &&
                   publicKey.VerifyHash(
                       sha256Digest.ToArray(),
                       signature,
                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Validates that an algorithm identifier is supported for verification.</summary>
    public static bool IsSupported(SignatureAlgorithm algorithm) => algorithm == SignatureAlgorithm.EcdsaP256Sha256;

    /// <summary>Converts a manifest signature algorithm to the marketplace signature enum.</summary>
    public static SignatureAlgorithm FromManifest(PluginSignatureAlgorithm algorithm) => algorithm switch
    {
        PluginSignatureAlgorithm.EcdsaP256Sha256 => SignatureAlgorithm.EcdsaP256Sha256,
        PluginSignatureAlgorithm.Ed25519 => SignatureAlgorithm.Ed25519,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown manifest signature algorithm."),
    };
}
