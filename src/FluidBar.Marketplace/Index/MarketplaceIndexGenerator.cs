using System.Security.Cryptography;
using System.Text.Json;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Index;

/// <summary>One packed + signed plugin artifact that should appear in the marketplace index.</summary>
public sealed record MarketplacePluginArtifact
{
    public required PluginManifest Manifest { get; init; }

    public required string PackagePath { get; init; }

    public required string SignaturePath { get; init; }

    public required string Sha256 { get; init; }

    public required long Size { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public string? Changelog { get; init; }
}

/// <summary>Configuration for deterministic index/shard/keys generation.</summary>
public sealed record MarketplaceIndexGeneratorOptions
{
    public required string OutputDirectory { get; init; }

    /// <summary>Base URL that hosts the <c>.fbp</c> + <c>.sig.json</c> release assets (HTTPS).</summary>
    public required string ArtifactBaseUrl { get; init; }

    /// <summary>Public marketplace base URL (listings, shards, assets).</summary>
    public required string MarketplaceBaseUrl { get; init; }

    public required string KeyId { get; init; }

    /// <summary>Marketplace countersign key (CI reads it from an HSM/KMS or a protected secret).</summary>
    public required ECDsa SigningKey { get; init; }

    public DateTimeOffset? GeneratedAt { get; init; }

    /// <summary>Commit the index was generated from (40 lowercase hex); required by the schema.</summary>
    public string? SourceCommit { get; init; }

    public string Channel { get; init; } = "stable";

    /// <summary>Mark all publishers as verified (single-maintainer curated marketplace default).</summary>
    public bool PublisherVerified { get; init; } = true;

    /// <summary>Optional base URL for listing assets (icons); when null only manifest fields are used.</summary>
    public string? AssetBaseUrl { get; init; }

    /// <summary>Template for package URLs. Tokens: {artifactBaseUrl}, {id}, {version}, {file}.</summary>
    public string ArtifactUrlTemplate { get; init; } = "{artifactBaseUrl}/plugin-{id}-v{version}/{file}";

    /// <summary>Template for detached signature URLs. Tokens: {artifactBaseUrl}, {id}, {version}, {signatureFile}.</summary>
    public string SignatureUrlTemplate { get; init; } = "{artifactBaseUrl}/plugin-{id}-v{version}/{signatureFile}";

    /// <summary>Minimum app version advertised by the index.</summary>
    public SemanticVersion MinimumAppVersion { get; init; } = new(2, 0, 0);

    /// <summary>Supported SDK ABI minor versions advertised by the index.</summary>
    public IReadOnlyList<string> SupportedApiVersions { get; init; } = ["2.0"];

    public string DefaultLocale { get; init; } = "en-US";
}

/// <summary>One generated package reference (used by the CLI summary + tests).</summary>
public sealed record MarketplaceGeneratedArtifact(
    string PluginId,
    string Version,
    string PackagePath,
    string SignaturePath,
    string Sha256,
    long Size,
    string ArtifactUrl,
    string SignatureUrl);

/// <summary>Result of a full index generation.</summary>
public sealed record MarketplaceIndexBuildResult(
    string IndexPath,
    string KeysPath,
    IReadOnlyList<string> ShardPaths,
    MarketplaceIndexDocument Index,
    IReadOnlyList<MarketplaceGeneratedArtifact> Artifacts,
    IReadOnlyList<TrustedKey> MarketplaceKeys);

/// <summary>
/// Generates the signed root index, per-plugin history shards and <c>keys.json</c> from packed + signed
/// artifacts. Deterministic given the same inputs (sorted plugin order, stable JSON writer, explicit timestamps):
/// the marketplace CI runs this instead of hand-editing JSON. Root index holds the latest version per plugin;
/// shards hold the version history used for detail pages/rollback.
/// </summary>
public static class MarketplaceIndexGenerator
{
    public const string IndexFileName = "index.json";
    public const string KeysFileName = "keys.json";
    public const string ShardDirectoryName = "plugins";

    private static readonly JsonSerializerOptions SerializerOptions = PluginJson.CreateStrictOptions();

    /// <summary>Builds the index + shards + keys files and writes them under the output directory.</summary>
    public static MarketplaceIndexBuildResult Build(
        IEnumerable<MarketplacePluginArtifact> artifacts,
        MarketplaceIndexGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        var artifactList = artifacts.ToArray();
        if (artifactList.Length == 0)
        {
            throw new MarketplaceIndexException("No plugin artifacts were provided to the index generator.");
        }

        var generatedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow;
        var sourceCommit = string.IsNullOrWhiteSpace(options.SourceCommit) ? new string('0', 40) : options.SourceCommit;
        if (sourceCommit.Length != 40 || sourceCommit.Any(static character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new MarketplaceIndexException("sourceCommit must be a 40 character lowercase hex commit id.");
        }

        var byPlugin = artifactList
            .GroupBy(static artifact => artifact.Manifest.Id, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var generatedArtifacts = new List<MarketplaceGeneratedArtifact>();
        var indexPlugins = new List<MarketplacePluginEntry>();
        var shards = new Dictionary<string, PluginHistoryShard>(StringComparer.Ordinal);
        var categories = new SortedDictionary<string, MarketplaceCategory>(StringComparer.Ordinal);
        var availableLocales = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { options.DefaultLocale };

        foreach (var pluginGroup in byPlugin)
        {
            var pluginId = pluginGroup.Key;
            var versions = pluginGroup
                .OrderByDescending(static artifact => ParseArtifactVersion(artifact))
                .ToArray();
            var latest = versions[0];
            var latestManifest = latest.Manifest;

            if (latestManifest.Publisher?.Id is not { Length: > 0 } publisherId ||
                latestManifest.Locales is not { } manifestLocales)
            {
                throw new MarketplaceIndexException($"Plugin '{pluginId}' is missing publisher/locales required for the index.");
            }

            var versionEntries = new List<MarketplaceVersionInfo>(versions.Length);
            foreach (var artifact in versions)
            {
                var parsedVersion = ParseArtifactVersion(artifact);
                var manifest = artifact.Manifest;
                var packageFileName = Path.GetFileName(artifact.PackagePath);
                var signatureFileName = Path.GetFileName(artifact.SignaturePath);
                var artifactUrl = ExpandTemplate(
                    options.ArtifactUrlTemplate,
                    options.ArtifactBaseUrl,
                    pluginId,
                    parsedVersion.ToString(),
                    packageFileName,
                    signatureFileName);
                var signatureUrl = ExpandTemplate(
                    options.SignatureUrlTemplate,
                    options.ArtifactBaseUrl,
                    pluginId,
                    parsedVersion.ToString(),
                    packageFileName,
                    signatureFileName);

                if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var artifactUri) || artifactUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new MarketplaceIndexException($"Generated artifact URL '{artifactUrl}' is not an absolute HTTPS URL.");
                }

                var locales = manifestLocales.Available?.ToArray() ?? [manifestLocales.Default];
                foreach (var locale in locales)
                {
                    availableLocales.Add(locale);
                }

                versionEntries.Add(new MarketplaceVersionInfo
                {
                    Version = parsedVersion.ToString(),
                    ApiVersion = manifest.ApiVersion,
                    MinAppVersion = manifest.MinAppVersion,
                    MaxAppVersion = string.IsNullOrWhiteSpace(manifest.MaxAppVersion) ? null : manifest.MaxAppVersion,
                    PublishedAt = artifact.PublishedAt ?? generatedAt,
                    Channel = ParseChannel(options.Channel),
                    Yanked = false,
                    Artifact = new MarketplaceArtifactInfo
                    {
                        Url = artifactUrl,
                        Sha256 = artifact.Sha256,
                        Size = artifact.Size,
                        SignatureUrl = signatureUrl,
                        KeyId = options.KeyId,
                        Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
                    },
                    Capabilities = [.. manifest.Capabilities.GetDeclaredCapabilityNames()],
                    Locales = locales,
                    LocaleCoverage = BuildLocaleCoverage(locales, manifestLocales.Default),
                    Changelog = artifact.Changelog,
                    License = manifest.License,
                });

                generatedArtifacts.Add(new MarketplaceGeneratedArtifact(
                    pluginId,
                    parsedVersion.ToString(),
                    Path.GetFullPath(artifact.PackagePath),
                    Path.GetFullPath(artifact.SignaturePath),
                    artifact.Sha256,
                    artifact.Size,
                    artifactUrl,
                    signatureUrl));
            }

            var shard = new PluginHistoryShard { SchemaVersion = 1, Id = pluginId, Versions = versionEntries };
            shards[pluginId] = shard;

            foreach (var category in latestManifest.Categories ?? [])
            {
                var categoryName = WireEnum.GetName(category);
                categories.TryAdd(categoryName, new MarketplaceCategory(categoryName, new LocalizedString(Capitalize(categoryName))));
            }

            indexPlugins.Add(new MarketplacePluginEntry
            {
                Id = pluginId,
                Name = latestManifest.Name,
                Summary = latestManifest.Summary,
                Publisher = new MarketplacePublisherInfo
                {
                    Id = publisherId,
                    DisplayName = latestManifest.Publisher.DisplayName,
                    Verified = options.PublisherVerified,
                    KeyId = latestManifest.Publisher.KeyId,
                    Url = latestManifest.Publisher.Url,
                },
                Latest = versionEntries[0],
                Categories = latestManifest.Categories?.Count > 0
                    ? [.. latestManifest.Categories.Select(WireEnum.GetName)]
                    : ["developer"],
                Tags = latestManifest.Tags,
                Icon = BuildIconUrl(options, pluginId),
                Screenshots = [],
                Listing = new MarketplaceListingInfo
                {
                    DefaultLocale = latestManifest.Locales.Default,
                    AvailableLocales = latestManifest.Locales.Available,
                    BaseUrl = $"{options.MarketplaceBaseUrl.TrimEnd('/')}/listings/{pluginId}/",
                },
                HistoryUrl = $"{options.MarketplaceBaseUrl.TrimEnd('/')}/plugins/{pluginId}.json",
                Stats = new MarketplaceStats { Downloads = 0, Rating = null },
                Status = PluginListingStatus.Active,
                Featured = false,
                Blocked = null,
            });
        }

        var marketplaceKey = new MarketplaceKeyInfo
        {
            KeyId = options.KeyId,
            Role = "marketplace",
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            PublicKey = EcdsaKeyMaterial.ExportPublicKeySpkiBase64(options.SigningKey),
            NotBefore = generatedAt.AddYears(-1),
            NotAfter = generatedAt.AddYears(1),
        };

        var index = new MarketplaceIndexDocument
        {
            Schema = "https://fluidbar.doulor.cn/schemas/marketplace-index.schema.json",
            SchemaVersion = 1,
            GeneratedAt = generatedAt,
            SourceCommit = sourceCommit,
            AppCompatibility = new AppCompatibilityInfo(
                options.MinimumAppVersion.ToString(),
                [.. options.SupportedApiVersions]),
            DefaultLocale = options.DefaultLocale,
            AvailableLocales = [.. availableLocales],
            Categories = [.. categories.Values],
            Plugins = [.. indexPlugins],
            Advisories = [],
            Keys = [marketplaceKey],
            Signatures = [],
        };

        var unsigned = index;
        var signature = IndexSigner.Sign(unsigned, options.SigningKey, options.KeyId, signedAt: generatedAt);
        index = unsigned with { Signatures = [signature] };
        var signedJson = IndexSigner.CreateSignedJson(unsigned, [signature]);

        var validation = MarketplaceIndexValidator.Validate(index);
        if (!validation.IsValid)
        {
            throw new MarketplaceIndexException(
                $"Generated marketplace index is invalid: {string.Join("; ", validation.Errors)}");
        }

        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var shardDirectory = Path.Combine(outputDirectory, ShardDirectoryName);
        Directory.CreateDirectory(shardDirectory);

        var indexPath = Path.Combine(outputDirectory, IndexFileName);
        File.WriteAllText(indexPath, signedJson);

        var keysPath = Path.Combine(outputDirectory, KeysFileName);
        File.WriteAllText(keysPath, JsonSerializer.Serialize(new[] { marketplaceKey }, SerializerOptions));

        var shardPaths = new List<string>(shards.Count);
        foreach (var pair in shards)
        {
            var shardPath = Path.Combine(shardDirectory, pair.Key + ".json");
            File.WriteAllText(shardPath, JsonSerializer.Serialize(pair.Value, SerializerOptions));
            shardPaths.Add(shardPath);
        }

        var trustedMarketplaceKey = new TrustedKey
        {
            KeyId = marketplaceKey.KeyId,
            Role = SignatureRole.Marketplace,
            Algorithm = SignatureAlgorithm.EcdsaP256Sha256,
            PublicKeySpkiBase64 = marketplaceKey.PublicKey,
            NotBefore = marketplaceKey.NotBefore,
            NotAfter = marketplaceKey.NotAfter,
        };

        return new MarketplaceIndexBuildResult(
            indexPath,
            keysPath,
            shardPaths,
            index,
            generatedArtifacts,
            [trustedMarketplaceKey]);
    }

    /// <summary>Verifies a signed index file against a trusted marketplace key (post-publish CI check).</summary>
    public static IndexVerificationResult VerifySignedIndex(string indexPath, IEnumerable<TrustedKey> marketplaceKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexPath);
        ArgumentNullException.ThrowIfNull(marketplaceKeys);
        var json = File.ReadAllText(indexPath);
        var trust = new MarketplaceTrustOptions { MarketplaceKeys = [.. marketplaceKeys] };
        return new IndexSignatureVerifier(trust).Verify(json);
    }

    /// <summary>Reads a <c>keys.json</c> file into trusted marketplace keys (informational/rotation checks).</summary>
    public static IReadOnlyList<TrustedKey> ReadKeysFile(string keysPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keysPath);
        var keys = JsonSerializer.Deserialize<MarketplaceKeyInfo[]>(File.ReadAllText(keysPath), SerializerOptions)
            ?? throw new MarketplaceIndexException($"'{keysPath}' could not be parsed.");
        return
        [
            .. keys.Select(static key => new TrustedKey
            {
                KeyId = key.KeyId,
                Role = key.Role == "publisher" ? SignatureRole.Author : SignatureRole.Marketplace,
                Algorithm = key.Algorithm,
                PublicKeySpkiBase64 = key.PublicKey,
                NotBefore = key.NotBefore,
                NotAfter = key.NotAfter,
                RevokedAt = key.RevokedAt,
            }),
        ];
    }

    private static void ValidateOptions(MarketplaceIndexGeneratorOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.KeyId);
        ArgumentNullException.ThrowIfNull(options.SigningKey);
        if (!Uri.TryCreate(options.ArtifactBaseUrl, UriKind.Absolute, out var artifactBase) || artifactBase.Scheme != Uri.UriSchemeHttps)
        {
            throw new MarketplaceIndexException("ArtifactBaseUrl must be an absolute HTTPS URL.");
        }

        if (!Uri.TryCreate(options.MarketplaceBaseUrl, UriKind.Absolute, out var marketplaceBase) || marketplaceBase.Scheme != Uri.UriSchemeHttps)
        {
            throw new MarketplaceIndexException("MarketplaceBaseUrl must be an absolute HTTPS URL.");
        }
    }

    private static SemanticVersion ParseArtifactVersion(MarketplacePluginArtifact artifact) =>
        artifact.Manifest.TryGetVersion(out var version)
            ? version
            : throw new MarketplaceIndexException($"Plugin '{artifact.Manifest.Id}' has an invalid version '{artifact.Manifest.Version}'.");

    private static PluginChannel ParseChannel(string channel) =>
        string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase) ? PluginChannel.Beta : PluginChannel.Stable;

    private static IReadOnlyDictionary<string, int> BuildLocaleCoverage(IReadOnlyList<string> locales, string defaultLocale)
    {
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var locale in locales)
        {
            coverage[locale] = string.Equals(locale, defaultLocale, StringComparison.OrdinalIgnoreCase) ? 100 : 0;
        }

        return coverage;
    }

    private static string? BuildIconUrl(MarketplaceIndexGeneratorOptions options, string pluginId) =>
        options.AssetBaseUrl is { Length: > 0 } assetBase
            ? $"{assetBase.TrimEnd('/')}/{pluginId}/icon.png"
            : null;

    private static string ExpandTemplate(
        string template,
        string artifactBaseUrl,
        string pluginId,
        string version,
        string fileName,
        string signatureFileName) =>
        template
            .Replace("{artifactBaseUrl}", artifactBaseUrl.TrimEnd('/'), StringComparison.Ordinal)
            .Replace("{id}", pluginId, StringComparison.Ordinal)
            .Replace("{version}", version, StringComparison.Ordinal)
            .Replace("{file}", fileName, StringComparison.Ordinal)
            .Replace("{signatureFile}", signatureFileName, StringComparison.Ordinal);

    private static string Capitalize(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
