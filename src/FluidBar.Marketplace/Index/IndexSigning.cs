using System.Security.Cryptography;
using System.Text.Json;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Json;

namespace FluidBar.Marketplace.Index;

/// <summary>Raised when the index cannot be signed/verified.</summary>
public sealed class IndexSignatureException : Exception
{
    public IndexSignatureException(string message)
        : base(message)
    {
    }

    public IndexSignatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Signs the canonical root index payload (marketplace key; the private key lives in an HSM in CI).</summary>
public static class IndexSigner
{
    private static readonly JsonSerializerOptions SerializerOptions = PluginJson.CreateStrictOptions();

    /// <summary>The exact bytes that are signed/verified: canonical JSON without the <c>signatures</c> property.</summary>
    public static byte[] CreateSigningPayload(MarketplaceIndexDocument index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var withoutSignatures = index with { Signatures = [] };
        var json = JsonSerializer.Serialize(withoutSignatures, SerializerOptions);
        return CanonicalJson.Canonicalize(json, "signatures");
    }

    /// <summary>Computes the SHA-256 digest of the canonical payload.</summary>
    public static byte[] ComputePayloadDigest(MarketplaceIndexDocument index) =>
        SHA256.HashData(CreateSigningPayload(index));

    /// <summary>Signs the index payload with a marketplace key.</summary>
    public static IndexSignatureEntry Sign(MarketplaceIndexDocument index, ECDsa key, string keyId, DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        var digest = ComputePayloadDigest(index);
        return new IndexSignatureEntry
        {
            KeyId = keyId,
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            Value = EcdsaKeyMaterial.SignDigest(key, digest),
            SignedAt = signedAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Serializes the index with the given signatures (the bytes the client downloads).</summary>
    public static string CreateSignedJson(MarketplaceIndexDocument index, IReadOnlyList<IndexSignatureEntry> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        if (signatures.Count == 0)
        {
            throw new IndexSignatureException("At least one index signature is required.");
        }

        var signed = index with { Signatures = signatures };
        return JsonSerializer.Serialize(signed, SerializerOptions);
    }

    /// <summary>Signs and serializes in one step.</summary>
    public static string SignToJson(MarketplaceIndexDocument index, ECDsa key, string keyId, DateTimeOffset? signedAt = null) =>
        CreateSignedJson(index, [Sign(index, key, keyId, signedAt)]);
}

/// <summary>Result of verifying a signed index.</summary>
public sealed record IndexVerificationResult(
    bool Verified,
    DateTimeOffset? GeneratedAt,
    IReadOnlyList<string> VerifiedKeyIds,
    IReadOnlyList<string> Errors);

/// <summary>
/// Verifies the signed root index: canonical payload → SHA-256 → ECDSA P-256 signature with an injected
/// marketplace key. Also rejects index rollback (older <c>generatedAt</c> than the cache), future timestamps
/// and retired/revoked keys.
/// </summary>
public sealed class IndexSignatureVerifier
{
    private readonly MarketplaceTrustOptions _trust;
    private readonly TimeProvider _timeProvider;

    public IndexSignatureVerifier(MarketplaceTrustOptions trust, TimeProvider? timeProvider = null)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Verifies signed index JSON.</summary>
    public IndexVerificationResult Verify(
        string indexJson,
        DateTimeOffset? minimumGeneratedAt = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexJson);
        var errors = new List<string>();
        MarketplaceIndexDocument index;
        try
        {
            index = MarketplaceIndexParser.Parse(indexJson);
        }
        catch (MarketplaceIndexException exception)
        {
            return new IndexVerificationResult(false, null, [], [exception.Message]);
        }

        var currentTime = now ?? _timeProvider.GetUtcNow();
        if (index.GeneratedAt > currentTime.AddHours(24))
        {
            errors.Add($"generatedAt {index.GeneratedAt:O} is more than 24 hours in the future (check the clock).");
        }

        if (minimumGeneratedAt is { } minimum && index.GeneratedAt < minimum)
        {
            errors.Add($"Index rollback detected: generatedAt {index.GeneratedAt:O} is older than the cached {minimum:O}.");
        }

        var signatures = index.Signatures ?? [];
        if (signatures.Count == 0)
        {
            errors.Add("Index has no signatures.");
        }

        byte[] payload;
        try
        {
            payload = CanonicalJson.Canonicalize(indexJson, "signatures");
        }
        catch (JsonException exception)
        {
            return new IndexVerificationResult(false, index.GeneratedAt, [], [$"Index payload is not valid JSON: {exception.Message}"]);
        }

        var digest = SHA256.HashData(payload);
        var verifiedKeyIds = new List<string>();
        foreach (var signature in signatures)
        {
            if (!EcdsaKeyMaterial.IsSupported(signature.Algorithm))
            {
                errors.Add($"Index signature algorithm '{signature.Algorithm}' is not supported.");
                continue;
            }

            var trustedKey = _trust.FindMarketplaceKey(signature.KeyId);
            if (trustedKey is null)
            {
                errors.Add($"Index signing key '{signature.KeyId}' is not trusted.");
                continue;
            }

            var unusableReason = trustedKey.GetUnusableReason(signature.SignedAt, _trust.ClockSkew);
            if (unusableReason is not null)
            {
                errors.Add(unusableReason);
                continue;
            }

            try
            {
                using var publicKey = EcdsaKeyMaterial.ImportPublicKeySpki(trustedKey.PublicKeySpkiBase64);
                if (!EcdsaKeyMaterial.VerifyDigest(publicKey, digest, signature.Value))
                {
                    errors.Add($"Index signature for key '{signature.KeyId}' is invalid (payload was modified or signed by another key).");
                    continue;
                }

                verifiedKeyIds.Add(signature.KeyId);
            }
            catch (CryptographicException exception)
            {
                errors.Add($"Pinned index key '{signature.KeyId}' could not be imported: {exception.Message}");
            }
            catch (FormatException exception)
            {
                errors.Add($"Index signature for key '{signature.KeyId}' is not valid base64: {exception.Message}");
            }
        }

        if (verifiedKeyIds.Count == 0 && signatures.Count > 0 && errors.Count == 0)
        {
            errors.Add("No valid marketplace index signature was found.");
        }

        return new IndexVerificationResult(errors.Count == 0 && verifiedKeyIds.Count > 0, index.GeneratedAt, verifiedKeyIds, errors);
    }
}
