using System.Security.Cryptography;

namespace FluidBar.Marketplace.Signing;

/// <summary>What a package verification run is allowed/required to trust.</summary>
public sealed record PackageVerificationRequest
{
    /// <summary>Require a valid marketplace countersignature (all marketplace installs).</summary>
    public bool RequireMarketplaceSignature { get; init; } = true;

    /// <summary>Require an author signature from this pinned publisher key (TOFU after first install).</summary>
    public string? ExpectedPublisherKeyId { get; init; }

    /// <summary>The hash the signed index advertises; when set the actual package hash must match.</summary>
    public string? ExpectedSha256 { get; init; }

    /// <summary>Expected package size from the signed index.</summary>
    public long? ExpectedSize { get; init; }
}

/// <summary>Outcome of package hash + detached signature verification.</summary>
public sealed record PackageVerification(
    bool Verified,
    string Sha256,
    long Size,
    IReadOnlyList<SignatureRole> VerifiedRoles,
    IReadOnlyList<string> Errors,
    string? VerifiedPublisherKeyId)
{
    public static PackageVerification Failed(string sha256, long size, IReadOnlyList<string> errors) =>
        new(false, sha256, size, [], errors, null);
}

/// <summary>Creates detached package signatures over the package SHA-256.</summary>
public static class PackageSigner
{
    /// <summary>Signs a raw SHA-256 package digest.</summary>
    public static SignatureEntry SignDigest(
        ECDsa key,
        ReadOnlySpan<byte> sha256Digest,
        string keyId,
        SignatureRole role,
        DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return new SignatureEntry
        {
            KeyId = keyId,
            Role = role,
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            PublicKey = EcdsaKeyMaterial.ExportPublicKeySpkiBase64(key),
            Value = EcdsaKeyMaterial.SignDigest(key, sha256Digest),
            SignedAt = signedAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Creates a signature document for an existing package file (does not write it).</summary>
    public static PackageSignatureDocument CreateDocument(
        string packagePath,
        ReadOnlySpan<byte> sha256Digest,
        long packageSize,
        IReadOnlyList<SignatureEntry> signatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        if (signatures.Count == 0)
        {
            throw new ArgumentException("At least one signature is required.", nameof(signatures));
        }

        return new PackageSignatureDocument
        {
            Package = new PackageHashInfo
            {
                Sha256 = Convert.ToHexString(sha256Digest).ToLowerInvariant(),
                Size = packageSize,
            },
            Signatures = signatures,
        };
    }

    /// <summary>
    /// Signs a package file and writes the detached <c>signature.json</c> next to <paramref name="signaturePath"/>.
    /// </summary>
    public static async Task<PackageSignatureDocument> SignPackageFileAsync(
        string packagePath,
        string signaturePath,
        ECDsa key,
        string keyId,
        SignatureRole role,
        DateTimeOffset? signedAt = null,
        CancellationToken cancellationToken = default)
    {
        var digest = await HashFileAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var size = new FileInfo(packagePath).Length;
        var entry = SignDigest(key, digest, keyId, role, signedAt);
        var document = CreateDocument(packagePath, digest, size, [entry]);
        var directory = Path.GetDirectoryName(Path.GetFullPath(signaturePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(signaturePath, document.ToJson(), cancellationToken).ConfigureAwait(false);
        return document;
    }

    internal static async Task<byte[]> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Verifies detached package signatures. Order matters: actual hash must match the signed document first (and the
/// index-advertised hash when provided), then the marketplace countersignature, then optional author/publisher
/// signatures. Any mismatch fails closed.
/// </summary>
public sealed class PackageSignatureVerifier
{
    private readonly MarketplaceTrustOptions _trust;

    public PackageSignatureVerifier(MarketplaceTrustOptions trust)
    {
        _trust = trust ?? throw new ArgumentNullException(nameof(trust));
    }

    /// <summary>Verifies a package file against a detached signature document.</summary>
    public async Task<PackageVerification> VerifyFileAsync(
        string packagePath,
        PackageSignatureDocument? signatureDocument,
        PackageVerificationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        request ??= new PackageVerificationRequest();
        var digest = await PackageSigner.HashFileAsync(packagePath, cancellationToken).ConfigureAwait(false);
        var size = new FileInfo(packagePath).Length;
        return Verify(Convert.ToHexString(digest).ToLowerInvariant(), size, signatureDocument, request);
    }

    /// <summary>Verifies an already-computed package hash/size against a detached signature document.</summary>
    public PackageVerification Verify(
        string actualSha256,
        long actualSize,
        PackageSignatureDocument? signatureDocument,
        PackageVerificationRequest? request = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actualSha256);
        request ??= new PackageVerificationRequest();
        var errors = new List<string>();

        if (signatureDocument is null)
        {
            if (request.RequireMarketplaceSignature)
            {
                errors.Add("No detached signature document was provided; marketplace installs require a signature.");
            }

            return PackageVerification.Failed(actualSha256, actualSize, errors);
        }

        if (!string.Equals(signatureDocument.Package.Sha256, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Package hash mismatch: signature covers {signatureDocument.Package.Sha256}, actual {actualSha256}.");
        }

        if (signatureDocument.Package.Size != actualSize)
        {
            errors.Add($"Package size mismatch: signature covers {signatureDocument.Package.Size}, actual {actualSize}.");
        }

        if (request.ExpectedSha256 is { } expected &&
            !string.Equals(expected, actualSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Package hash does not match the signed index entry: index {expected}, actual {actualSha256}.");
        }

        if (request.ExpectedSize is { } expectedSize && expectedSize != actualSize)
        {
            errors.Add($"Package size does not match the signed index entry: index {expectedSize}, actual {actualSize}.");
        }

        if (errors.Count > 0)
        {
            return PackageVerification.Failed(actualSha256, actualSize, errors);
        }

        var digest = Convert.FromHexString(actualSha256);
        var verifiedRoles = new List<SignatureRole>();
        string? verifiedPublisherKeyId = null;

        var marketplaceSignatures = signatureDocument.Signatures.Where(static signature => signature.Role == SignatureRole.Marketplace).ToArray();
        var authorSignatures = signatureDocument.Signatures.Where(static signature => signature.Role == SignatureRole.Author).ToArray();

        var marketplaceVerified = false;
        foreach (var signature in marketplaceSignatures)
        {
            var outcome = VerifyEntry(signature, digest);
            if (outcome is not null)
            {
                errors.Add(outcome);
                continue;
            }

            marketplaceVerified = true;
            verifiedRoles.Add(SignatureRole.Marketplace);
            break;
        }

        if (request.RequireMarketplaceSignature && !marketplaceVerified)
        {
            errors.Add("No valid marketplace countersignature was found.");
        }

        if (request.ExpectedPublisherKeyId is { } expectedPublisherKeyId)
        {
            var publisherSignature = authorSignatures.FirstOrDefault(signature =>
                string.Equals(signature.KeyId, expectedPublisherKeyId, StringComparison.Ordinal));
            if (publisherSignature is null)
            {
                errors.Add($"Package is not signed by the pinned publisher key '{expectedPublisherKeyId}'.");
            }
            else
            {
                var outcome = VerifyEntry(publisherSignature, digest);
                if (outcome is not null)
                {
                    errors.Add(outcome);
                }
                else
                {
                    verifiedRoles.Add(SignatureRole.Author);
                    verifiedPublisherKeyId = publisherSignature.KeyId;
                }
            }
        }

        return new PackageVerification(
            errors.Count == 0 && (!request.RequireMarketplaceSignature || marketplaceVerified),
            actualSha256,
            actualSize,
            verifiedRoles,
            errors,
            verifiedPublisherKeyId);
    }

    private string? VerifyEntry(SignatureEntry signature, byte[] digest)
    {
        if (!EcdsaKeyMaterial.IsSupported(signature.Algorithm))
        {
            return $"Signature algorithm '{signature.Algorithm}' for key '{signature.KeyId}' is not supported.";
        }

        var trustedKey = signature.Role == SignatureRole.Marketplace
            ? _trust.FindMarketplaceKey(signature.KeyId)
            : _trust.FindPublisherKey(signature.KeyId);
        if (trustedKey is null)
        {
            return $"Signing key '{signature.KeyId}' ({signature.Role}) is not trusted.";
        }

        var unusableReason = trustedKey.GetUnusableReason(signature.SignedAt, _trust.ClockSkew);
        if (unusableReason is not null)
        {
            return unusableReason;
        }

        if (!string.IsNullOrWhiteSpace(signature.PublicKey) &&
            !string.Equals(signature.PublicKey, trustedKey.PublicKeySpkiBase64, StringComparison.Ordinal))
        {
            return $"Signature for key '{signature.KeyId}' carries a public key that does not match the pinned key.";
        }

        try
        {
            using var publicKey = EcdsaKeyMaterial.ImportPublicKeySpki(trustedKey.PublicKeySpkiBase64);
            if (!EcdsaKeyMaterial.VerifyDigest(publicKey, digest, signature.Value))
            {
                return $"Signature for key '{signature.KeyId}' is invalid.";
            }
        }
        catch (CryptographicException exception)
        {
            return $"Pinned key '{signature.KeyId}' could not be imported: {exception.Message}";
        }
        catch (FormatException exception)
        {
            return $"Signature for key '{signature.KeyId}' is not valid base64: {exception.Message}";
        }

        return null;
    }
}
