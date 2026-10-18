using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Packaging;

/// <summary>The fixed, normative layout of a <c>.fbp</c> package (deterministic ZIP).</summary>
public static class FbpPackageLayout
{
    public const string ManifestFileName = "manifest.json";
    public const string PluginDirectory = "plugin/";
    public const string LocalesDirectory = "locales/";
    public const string AssetsDirectory = "assets/";
    public const string LicenseFileName = "LICENSE";
    public const string ReadmeFileName = "README.md";
    public const string ChecksumsFileName = "checksums.json";

    /// <summary>Signatures are detached (a signature cannot cover the ZIP it is embedded in).</summary>
    public const string SignatureFileName = "signature.json";
}

/// <summary>Hard package limits shared by the packager, the installer and CI policy checks.</summary>
public static class FbpPackageLimits
{
    public const long MaxCompressedBytes = 50L * 1024 * 1024;
    public const long MaxUncompressedBytes = 150L * 1024 * 1024;
    public const int MaxEntries = 5_000;
    public const int MaxPathLength = 512;
    public const long MaxChecksumFileBytes = 2L * 1024 * 1024;
}

/// <summary>
/// The <c>checksums.json</c> index inside a package: SHA-256 per file, excluding the checksums file itself.
/// Written with sorted keys so two deterministic builds produce identical bytes.
/// </summary>
public sealed record FbpChecksums
{
    public int SchemaVersion { get; init; } = 1;

    public string Algorithm => "sha256";

    public IReadOnlyDictionary<string, string> Files { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Computes checksums for the given package-relative paths under <paramref name="rootDirectory"/>.</summary>
    public static FbpChecksums Compute(string rootDirectory, IEnumerable<string> relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(relativePaths);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in relativePaths)
        {
            if (string.Equals(relativePath, FbpPackageLayout.ChecksumsFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullPath = Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            files[relativePath] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))).ToLowerInvariant();
        }

        return new FbpChecksums { Files = files };
    }

    /// <summary>Serializes deterministically (sorted keys, no whitespace).</summary>
    public string ToCanonicalJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("algorithm", Algorithm);
            writer.WriteStartObject("files");
            foreach (var pair in Files.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parses checksums JSON; throws <see cref="PackageFormatException"/> when malformed.</summary>
    public static FbpChecksums Parse(string json) =>
        TryParse(json, out var checksums, out var errors) && checksums is not null
            ? checksums
            : throw new PackageFormatException($"Invalid checksums.json: {string.Join("; ", errors)}");

    public static bool TryParse(string json, out FbpChecksums? checksums, out IReadOnlyList<string> errors)
    {
        checksums = null;
        var errorList = new List<string>();
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = ["checksums.json is empty."];
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors = ["checksums.json root must be an object."];
                return false;
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.GetInt32() != 1)
            {
                errorList.Add("checksums.json schemaVersion must be 1.");
            }

            if (!root.TryGetProperty("algorithm", out var algorithm) || !string.Equals(algorithm.GetString(), "sha256", StringComparison.OrdinalIgnoreCase))
            {
                errorList.Add("checksums.json algorithm must be sha256.");
            }

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
            {
                errors = ["checksums.json files must be an object."];
                return false;
            }

            var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in files.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    errorList.Add($"checksum for '{property.Name}' must be a string.");
                    continue;
                }

                var hash = property.Value.GetString() ?? string.Empty;
                if (hash.Length != 64 || hash.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
                {
                    errorList.Add($"checksum for '{property.Name}' must be 64 lowercase hex characters.");
                    continue;
                }

                map[property.Name] = hash;
            }

            if (map.Count == 0)
            {
                errorList.Add("checksums.json contains no files.");
            }

            errors = errorList;
            if (errorList.Count > 0)
            {
                return false;
            }

            checksums = new FbpChecksums { Files = map };
            return true;
        }
        catch (JsonException exception)
        {
            errors = [$"checksums.json is not valid JSON: {exception.Message}"];
            return false;
        }
    }
}

/// <summary>One package-relative file mapping used to build a package.</summary>
public sealed record FbpPackageFile(string EntryPath, string SourcePath);

/// <summary>Options controlling deterministic packaging.</summary>
public sealed class FbpPackagerOptions
{
    public static FbpPackagerOptions Default { get; } = new();

    /// <summary>Fixed DOS timestamp for every entry: deterministic builds need identical ZIP bytes.</summary>
    public DateTimeOffset FixedTimestamp { get; init; } = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public long MaxCompressedBytes { get; init; } = FbpPackageLimits.MaxCompressedBytes;

    public long MaxUncompressedBytes { get; init; } = FbpPackageLimits.MaxUncompressedBytes;

    public int MaxEntries { get; init; } = FbpPackageLimits.MaxEntries;

    /// <summary>Require manifest/license/README and validate the manifest (marketplace submissions).</summary>
    public bool ValidateMarketplaceLayout { get; init; } = true;

    /// <summary>Require and validate the checksums index (always generated for marketplace packages).</summary>
    public bool GenerateChecksums { get; init; } = true;
}

/// <summary>Result of a deterministic package build.</summary>
public sealed record FbpPackageBuildResult(
    string PackagePath,
    string Sha256,
    long Size,
    IReadOnlyList<string> Entries,
    string ManifestSha256,
    string EntryAssemblySha256);

/// <summary>Raised when a package cannot be built or read.</summary>
public sealed class PackageFormatException : Exception
{
    public PackageFormatException(string message)
        : base(message)
    {
    }

    public PackageFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
