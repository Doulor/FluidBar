using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Sdk.Json;

namespace FluidBar.Marketplace.Signing;

/// <summary>Who signed a package or index. The marketplace countersignature is required for marketplace installs.</summary>
public enum SignatureRole
{
    [WireName("author")] Author = 0,
    [WireName("marketplace")] Marketplace = 1,
}

/// <summary>Supported signature algorithms. ECDSA P-256 with SHA-256 is the default; Ed25519 is reserved.</summary>
public enum SignatureAlgorithm
{
    [WireName("ECDSA-P256-SHA256")] EcdsaP256Sha256 = 0,
    [WireName("Ed25519")] Ed25519 = 1,
}

/// <summary>The package hash covered by every signature in a detached <c>signature.json</c>.</summary>
public sealed record PackageHashInfo
{
    public string Sha256 { get; init; } = string.Empty;

    public long Size { get; init; }
}

/// <summary>One detached signature entry (see <c>signature.schema.json</c>).</summary>
public sealed record SignatureEntry
{
    public string KeyId { get; init; } = string.Empty;

    public SignatureRole Role { get; init; }

    public SignatureAlgorithm Algorithm { get; init; } = SignatureAlgorithm.EcdsaP256Sha256;

    /// <summary>SPKI (PKIX) base64 of the signing key; must match the trusted key on verification.</summary>
    public string PublicKey { get; init; } = string.Empty;

    /// <summary>Base64 ECDSA IEEE P1363 signature (r||s, 64 bytes) over the 32-byte SHA-256 package digest.</summary>
    public string Value { get; init; } = string.Empty;

    public DateTimeOffset SignedAt { get; init; }
}

/// <summary>
/// Detached package signature document. It is published next to the <c>.fbp</c>, never inside it (a signature
/// cannot cover the ZIP it is embedded in). It covers <c>package.sha256</c>, not per-file checksums: any
/// re-zip fails verification.
/// </summary>
public sealed record PackageSignatureDocument
{
    private static readonly JsonSerializerOptions SerializerOptions = PluginJson.CreateStrictOptions();

    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public PackageHashInfo Package { get; init; } = new();

    public IReadOnlyList<SignatureEntry> Signatures { get; init; } = [];

    /// <summary>Parses a detached signature document; throws <see cref="PackageSignatureException"/> when malformed.</summary>
    public static PackageSignatureDocument Parse(string json) =>
        TryParse(json, out var document, out var errors) && document is not null
            ? document
            : throw new PackageSignatureException($"Invalid signature.json: {string.Join("; ", errors)}");

    public static bool TryParse(string json, out PackageSignatureDocument? document, out IReadOnlyList<string> errors)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = ["Signature document is empty."];
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<PackageSignatureDocument>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            errors = [$"Signature document is not valid JSON for schema v1: {exception.Message}"];
            return false;
        }

        if (document is null)
        {
            errors = ["Signature document deserialized to null."];
            return false;
        }

        var issues = new List<string>();
        if (document.SchemaVersion != 1)
        {
            issues.Add($"Unsupported signature schemaVersion {document.SchemaVersion}; expected 1.");
        }

        if (string.IsNullOrWhiteSpace(document.Package.Sha256) ||
            document.Package.Sha256.Length != 64 ||
            document.Package.Sha256.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            issues.Add("package.sha256 must be 64 lowercase hex characters.");
        }

        if (document.Package.Size <= 0)
        {
            issues.Add("package.size must be positive.");
        }

        if (document.Signatures.Count == 0)
        {
            issues.Add("signatures must contain at least one entry.");
        }

        foreach (var signature in document.Signatures)
        {
            if (string.IsNullOrWhiteSpace(signature.KeyId))
            {
                issues.Add("signature keyId is required.");
            }

            if (string.IsNullOrWhiteSpace(signature.Value))
            {
                issues.Add($"signature value for key '{signature.KeyId}' is required.");
            }
        }

        errors = issues;
        if (issues.Count > 0)
        {
            document = null;
            return false;
        }

        return true;
    }

    /// <summary>Serializes deterministically (declaration order, camelCase, no whitespace).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}

/// <summary>Raised when a signature document is malformed.</summary>
public sealed class PackageSignatureException : Exception
{
    public PackageSignatureException(string message)
        : base(message)
    {
    }

    public PackageSignatureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
