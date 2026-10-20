using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Install;

/// <summary>
/// On-disk integrity marker retained next to an installed version. It binds the extracted tree to the
/// signed .fbp artifact so a market package can be re-verified at every process start, not only during
/// installation.
/// </summary>
public sealed record PluginIntegrityMarker
{
    public const string FileName = "integrity.json";
    public const string PackageFileName = "package.fbp";
    public const string SignatureFileName = "signature.json";

    public int SchemaVersion { get; init; } = 1;

    public string PluginId { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;

    public long PackageSize { get; init; }

    public string? PublisherKeyId { get; init; }

    public string ExtractedTreeSha256 { get; init; } = string.Empty;

    /// <summary>True only for a package that carried a valid marketplace countersignature at install time.</summary>
    public bool MarketplaceReviewed { get; init; }
}

/// <summary>Result of load-time package/version integrity verification.</summary>
public sealed record PluginIntegrityVerification(bool Verified, string? Sha256, IReadOnlyList<string> Errors)
{
    public static PluginIntegrityVerification Fail(params string[] errors) => new(false, null, errors);
}

/// <summary>Writes and verifies the signed-install marker retained for the market channel.</summary>
public static class PluginIntegrity
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static PluginIntegrityMarker Write(
        string versionDirectory,
        string packagePath,
        PluginManifest manifest,
        PackageSignatureDocument signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(signature);

        Directory.CreateDirectory(versionDirectory);
        File.Copy(packagePath, Path.Combine(versionDirectory, PluginIntegrityMarker.PackageFileName), overwrite: true);
        File.WriteAllText(Path.Combine(versionDirectory, PluginIntegrityMarker.SignatureFileName), signature.ToJson());

        var marker = new PluginIntegrityMarker
        {
            PluginId = manifest.Id,
            Version = manifest.Version,
            PackageSha256 = signature.Package.Sha256,
            PackageSize = signature.Package.Size,
            PublisherKeyId = signature.Signatures
                .FirstOrDefault(entry => entry.Role == SignatureRole.Author)?.KeyId,
            ExtractedTreeSha256 = ComputeTreeHash(versionDirectory),
            MarketplaceReviewed = true,
        };
        File.WriteAllText(Path.Combine(versionDirectory, PluginIntegrityMarker.FileName),
            JsonSerializer.Serialize(marker, JsonOptions));
        return marker;
    }

    public static PluginIntegrityMarker? ReadMarker(string versionDirectory, out string? error)
    {
        error = null;
        var markerPath = Path.Combine(versionDirectory, PluginIntegrityMarker.FileName);
        if (!File.Exists(markerPath))
        {
            error = "No integrity.json is present; market packages must carry a verified marketplace signature.";
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<PluginIntegrityMarker>(File.ReadAllText(markerPath), JsonOptions);
            if (marker is null)
            {
                error = "integrity.json deserialized to null.";
            }

            return marker;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            error = $"integrity.json could not be read: {exception.Message}";
            return null;
        }
    }

    public static async Task<PluginIntegrityVerification> VerifyAsync(
        string versionDirectory,
        MarketplaceTrustOptions trust,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);
        ArgumentNullException.ThrowIfNull(trust);
        var marker = ReadMarker(versionDirectory, out var markerError);
        if (marker is null)
        {
            return PluginIntegrityVerification.Fail(markerError
                ?? "No integrity.json is present; market packages must carry a verified marketplace signature.");
        }

        if (marker.SchemaVersion != 1)
        {
            return PluginIntegrityVerification.Fail($"Unsupported integrity schemaVersion {marker.SchemaVersion}; expected 1.");
        }

        if (!marker.MarketplaceReviewed)
        {
            return PluginIntegrityVerification.Fail("Package is not marked as marketplace-reviewed.");
        }

        if (string.IsNullOrWhiteSpace(marker.PackageSha256) || marker.PackageSize <= 0)
        {
            return PluginIntegrityVerification.Fail("integrity.json has an invalid package hash/size.");
        }

        var packagePath = Path.Combine(versionDirectory, PluginIntegrityMarker.PackageFileName);
        var signaturePath = Path.Combine(versionDirectory, PluginIntegrityMarker.SignatureFileName);
        if (!File.Exists(packagePath) || !File.Exists(signaturePath))
        {
            return PluginIntegrityVerification.Fail("The retained package.fbp/signature.json is missing.");
        }

        PackageSignatureDocument signature;
        try
        {
            signature = PackageSignatureDocument.Parse(
                await File.ReadAllTextAsync(signaturePath, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or PackageSignatureException)
        {
            return PluginIntegrityVerification.Fail($"signature.json could not be read: {exception.Message}");
        }

        var verifier = new PackageSignatureVerifier(trust);
        var verification = await verifier.VerifyFileAsync(
            packagePath,
            signature,
            new PackageVerificationRequest
            {
                RequireMarketplaceSignature = true,
                ExpectedSha256 = marker.PackageSha256,
                ExpectedSize = marker.PackageSize,
                ExpectedPublisherKeyId = string.IsNullOrWhiteSpace(marker.PublisherKeyId) ? null : marker.PublisherKeyId,
            },
            cancellationToken).ConfigureAwait(false);
        if (!verification.Verified)
        {
            return PluginIntegrityVerification.Fail([.. verification.Errors]);
        }

        var treeHash = ComputeTreeHash(versionDirectory);
        if (!string.Equals(treeHash, marker.ExtractedTreeSha256, StringComparison.OrdinalIgnoreCase))
        {
            return PluginIntegrityVerification.Fail(
                "The extracted plugin files do not match the integrity marker.");
        }

        return new PluginIntegrityVerification(true, verification.Sha256, []);
    }

    /// <summary>Deterministic hash over extracted files, excluding the retained package metadata.</summary>
    public static string ComputeTreeHash(string versionDirectory)
    {
        var files = Directory.EnumerateFiles(versionDirectory, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(versionDirectory, path).Replace('\\', '/')))
            .Where(entry => !IsMetadata(entry.Relative))
            .OrderBy(entry => entry.Relative, StringComparer.Ordinal)
            .ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (path, relative) in files)
        {
            var name = Encoding.UTF8.GetBytes(relative);
            hash.AppendData(name);
            hash.AppendData([0]);
            var info = new FileInfo(path);
            hash.AppendData(BitConverter.GetBytes(info.Length));
            using var stream = File.OpenRead(path);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static bool IsMetadata(string relative)
        => string.Equals(relative, PluginIntegrityMarker.FileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, PluginIntegrityMarker.PackageFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relative, PluginIntegrityMarker.SignatureFileName, StringComparison.OrdinalIgnoreCase);
}

