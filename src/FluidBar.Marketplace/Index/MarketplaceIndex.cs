using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Json;
using FluidBar.Sdk.Localization;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Index;

/// <summary>Marketplace listing status.</summary>
public enum PluginListingStatus
{
    [WireName("active")] Active = 0,
    [WireName("deprecated")] Deprecated = 1,
    [WireName("blocked")] Blocked = 2,
}

/// <summary>Release channel of an index version.</summary>
public enum PluginChannel
{
    [WireName("stable")] Stable = 0,
    [WireName("beta")] Beta = 1,
}

/// <summary>The signed root marketplace index (<c>index.json</c>).</summary>
public sealed record MarketplaceIndexDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public DateTimeOffset GeneratedAt { get; init; }

    public string SourceCommit { get; init; } = string.Empty;

    public AppCompatibilityInfo? AppCompatibility { get; init; }

    public string DefaultLocale { get; init; } = "en-US";

    public IReadOnlyList<string>? AvailableLocales { get; init; }

    public IReadOnlyList<MarketplaceCategory>? Categories { get; init; }

    public IReadOnlyList<MarketplacePluginEntry>? Plugins { get; init; }

    public IReadOnlyList<MarketplaceAdvisory>? Advisories { get; init; }

    public IReadOnlyList<MarketplaceKeyInfo>? Keys { get; init; }

    public IReadOnlyList<IndexSignatureEntry>? Signatures { get; init; }
}

/// <summary>Application compatibility window advertised by the index.</summary>
public sealed record AppCompatibilityInfo(string MinAppVersion, IReadOnlyList<string> SupportedApiVersions);

/// <summary>Localized category name.</summary>
public sealed record MarketplaceCategory(string Id, LocalizedString Name);

/// <summary>One plugin entry in the root index (latest version only; history lives in the shard).</summary>
public sealed record MarketplacePluginEntry
{
    public string Id { get; init; } = string.Empty;

    public LocalizedString? Name { get; init; }

    public LocalizedString? Summary { get; init; }

    public MarketplacePublisherInfo? Publisher { get; init; }

    public MarketplaceVersionInfo? Latest { get; init; }

    public IReadOnlyList<string>? Categories { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public string? Icon { get; init; }

    public IReadOnlyList<MarketplaceScreenshot>? Screenshots { get; init; }

    public MarketplaceListingInfo? Listing { get; init; }

    public string HistoryUrl { get; init; } = string.Empty;

    public MarketplaceStats? Stats { get; init; }

    public PluginListingStatus Status { get; init; } = PluginListingStatus.Active;

    public bool Featured { get; init; }

    public MarketplaceBlockedInfo? Blocked { get; init; }
}

/// <summary>Marketplace publisher details shown in the UI (separate from the manifest publisher block).</summary>
public sealed record MarketplacePublisherInfo
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool Verified { get; init; }

    public string? KeyId { get; init; }

    public string? Url { get; init; }
}

/// <summary>One published version and its signed artifact.</summary>
public sealed record MarketplaceVersionInfo
{
    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinAppVersion { get; init; } = string.Empty;

    public string? MaxAppVersion { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public PluginChannel Channel { get; init; } = PluginChannel.Stable;

    public bool Yanked { get; init; }

    public string? YankReason { get; init; }

    public MarketplaceArtifactInfo? Artifact { get; init; }

    public IReadOnlyList<string>? Capabilities { get; init; }

    public IReadOnlyList<string>? Locales { get; init; }

    public IReadOnlyDictionary<string, int>? LocaleCoverage { get; init; }

    public string? Changelog { get; init; }

    public string? License { get; init; }
}

/// <summary>The immutable package artifact reference: HTTPS URL + hash + detached signature URL.</summary>
public sealed record MarketplaceArtifactInfo
{
    public string Url { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long Size { get; init; }

    public string SignatureUrl { get; init; } = string.Empty;

    public string KeyId { get; init; } = string.Empty;

    public SignatureAlgorithm Algorithm { get; init; } = SignatureAlgorithm.EcdsaP256Sha256;
}

/// <summary>Download/rating stats; ratings are <see langword="null"/> in v2.0 and clients must tolerate that.</summary>
public sealed record MarketplaceStats
{
    public long Downloads { get; init; }

    public double? Rating { get; init; }
}

/// <summary>Localized long-description listing location.</summary>
public sealed record MarketplaceListingInfo
{
    public string DefaultLocale { get; init; } = "en-US";

    public IReadOnlyList<string>? AvailableLocales { get; init; }

    public string BaseUrl { get; init; } = string.Empty;
}

/// <summary>Localized screenshot with alt text.</summary>
public sealed record MarketplaceScreenshot
{
    public string Src { get; init; } = string.Empty;

    public LocalizedString? Alt { get; init; }
}

/// <summary>Block/yank metadata shown as a red banner and used to disable an installed plugin.</summary>
public sealed record MarketplaceBlockedInfo
{
    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; }

    public string? Replacement { get; init; }
}

/// <summary>Security advisory entry.</summary>
public sealed record MarketplaceAdvisory
{
    public string Id { get; init; } = string.Empty;

    public string PluginId { get; init; } = string.Empty;

    public string? AffectedVersions { get; init; }

    public string Severity { get; init; } = "moderate";

    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    public string? Url { get; init; }
}

/// <summary>Key metadata published in the index (informational; trust comes from injected marketplace keys).</summary>
public sealed record MarketplaceKeyInfo
{
    public string KeyId { get; init; } = string.Empty;

    public string Role { get; init; } = "marketplace";

    public SignatureAlgorithm Algorithm { get; init; } = SignatureAlgorithm.EcdsaP256Sha256;

    public string PublicKey { get; init; } = string.Empty;

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}

/// <summary>Index signature entry; the value signs the canonical payload excluding <c>signatures</c>.</summary>
public sealed record IndexSignatureEntry
{
    public string KeyId { get; init; } = string.Empty;

    public SignatureAlgorithm Algorithm { get; init; } = SignatureAlgorithm.EcdsaP256Sha256;

    public string Value { get; init; } = string.Empty;

    public DateTimeOffset SignedAt { get; init; }
}

/// <summary>Per-plugin version history shard (<c>plugins/&lt;id&gt;.json</c>).</summary>
public sealed record PluginHistoryShard
{
    public int SchemaVersion { get; init; } = 1;

    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<MarketplaceVersionInfo>? Versions { get; init; }
}

/// <summary>Strict parser for the marketplace index and per-plugin shards.</summary>
public static class MarketplaceIndexParser
{
    private static readonly JsonSerializerOptions Options = PluginJson.CreateStrictOptions();

    public static MarketplaceIndexDocument Parse(string json) =>
        TryParse(json, out var document, out var errors) && document is not null
            ? document
            : throw new MarketplaceIndexException($"Invalid marketplace index: {string.Join("; ", errors)}");

    public static bool TryParse(string json, out MarketplaceIndexDocument? document, out IReadOnlyList<string> errors)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            errors = ["Index JSON is empty."];
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<MarketplaceIndexDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            errors = [$"Index JSON does not match schema v1: {exception.Message}"];
            return false;
        }

        if (document is null)
        {
            errors = ["Index JSON deserialized to null."];
            return false;
        }

        var validation = MarketplaceIndexValidator.Validate(document);
        errors = validation.Errors;
        return validation.IsValid;
    }

    public static PluginHistoryShard ParseShard(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PluginHistoryShard>(json, Options)
                ?? throw new MarketplaceIndexException("History shard deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new MarketplaceIndexException($"Invalid plugin history shard: {exception.Message}", exception);
        }
    }
}

/// <summary>Semantic validation of a parsed index (rules the JSON Schema cannot express).</summary>
public static class MarketplaceIndexValidator
{
    private static readonly Regex Sha256Regex = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex SourceCommitRegex = new("^[a-f0-9]{40}$", RegexOptions.CultureInvariant);

    private static readonly IReadOnlySet<string> AllowedArtifactHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "objects.githubusercontent.com",
        "marketplace.fluidbar.app",
        "raw.githubusercontent.com",
    };

    public static IndexValidationResult Validate(MarketplaceIndexDocument index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (index.SchemaVersion != 1)
        {
            errors.Add($"Unsupported index schemaVersion {index.SchemaVersion}; expected 1.");
        }

        if (index.GeneratedAt == default)
        {
            errors.Add("generatedAt is required.");
        }

        if (!SourceCommitRegex.IsMatch(index.SourceCommit))
        {
            errors.Add("sourceCommit must be a 40 character lowercase hex commit id.");
        }

        if (index.AppCompatibility is null)
        {
            errors.Add("appCompatibility is required.");
        }
        else
        {
            if (!SemanticVersion.TryParse(index.AppCompatibility.MinAppVersion, out _))
            {
                errors.Add("appCompatibility.minAppVersion must be a semantic version.");
            }

            if (index.AppCompatibility.SupportedApiVersions.Count == 0)
            {
                errors.Add("appCompatibility.supportedApiVersions must not be empty.");
            }
        }

        if (!LocaleTags.IsValid(index.DefaultLocale))
        {
            errors.Add($"defaultLocale '{index.DefaultLocale}' is not a canonical BCP-47 tag.");
        }

        var plugins = index.Plugins ?? [];
        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in plugins)
        {
            ValidatePlugin(plugin, index, pluginIds, errors, warnings);
        }

        if (index.Signatures is not { Count: > 0 })
        {
            errors.Add("signatures must contain at least one entry.");
        }

        if (index.Keys is not { Count: > 0 })
        {
            errors.Add("keys must contain at least one entry.");
        }

        return new IndexValidationResult(errors.Count == 0, errors, warnings);
    }

    private static void ValidatePlugin(
        MarketplacePluginEntry plugin,
        MarketplaceIndexDocument index,
        HashSet<string> pluginIds,
        List<string> errors,
        List<string> warnings)
    {
        var path = $"plugins[{plugin.Id}]";
        if (string.IsNullOrWhiteSpace(plugin.Id) ||
            plugin.Id.Length > PluginManifestLimits.MaxIdLength ||
            !System.Text.RegularExpressions.Regex.IsMatch(plugin.Id, PluginIdentifiers.IdPattern, RegexOptions.CultureInvariant))
        {
            errors.Add($"{path}.id is invalid.");
        }
        else if (!pluginIds.Add(plugin.Id))
        {
            errors.Add($"{path}.id is duplicated.");
        }

        if (plugin.Name is null)
        {
            errors.Add($"{path}.name is required.");
        }

        if (plugin.Summary is null)
        {
            errors.Add($"{path}.summary is required.");
        }

        if (plugin.Publisher is null || string.IsNullOrWhiteSpace(plugin.Publisher.Id))
        {
            errors.Add($"{path}.publisher is required.");
        }

        if (plugin.Latest is not { } latest)
        {
            errors.Add($"{path}.latest is required.");
            return;
        }

        ValidateVersion(plugin, latest, path, errors);

        if (plugin.Status == PluginListingStatus.Blocked)
        {
            if (plugin.Blocked is null || string.IsNullOrWhiteSpace(plugin.Blocked.Reason))
            {
                errors.Add($"{path}.blocked requires a reason when status is blocked.");
            }
        }
        else if (plugin.Blocked is not null)
        {
            warnings.Add($"{path}.blocked is set but status is not blocked.");
        }

        if (plugin.Categories is not { Count: > 0 })
        {
            errors.Add($"{path}.categories must not be empty.");
        }

        if (!IsHttps(plugin.HistoryUrl))
        {
            errors.Add($"{path}.historyUrl must be an absolute HTTPS URL.");
        }

        if (plugin.Listing is null)
        {
            errors.Add($"{path}.listing is required.");
        }

        if (plugin.Publisher?.KeyId is { } publisherKeyId)
        {
            var key = index.Keys?.FirstOrDefault(candidate => string.Equals(candidate.KeyId, publisherKeyId, StringComparison.Ordinal));
            if (key is null)
            {
                warnings.Add($"{path}.publisher.keyId '{publisherKeyId}' is not published in keys[].");
            }
            else if (key.RevokedAt is not null)
            {
                errors.Add($"{path}.publisher.keyId '{publisherKeyId}' is revoked.");
            }
        }
    }

    private static void ValidateVersion(
        MarketplacePluginEntry plugin,
        MarketplaceVersionInfo latest,
        string path,
        List<string> errors)
    {
        if (!SemanticVersion.TryParse(latest.Version, out _))
        {
            errors.Add($"{path}.latest.version is not a semantic version.");
        }

        if (!SemanticVersion.TryParse(latest.ApiVersion, out _))
        {
            errors.Add($"{path}.latest.apiVersion is not a semantic version.");
        }

        if (!SemanticVersion.TryParse(latest.MinAppVersion, out _))
        {
            errors.Add($"{path}.latest.minAppVersion is not a semantic version.");
        }

        if (latest.Yanked && string.IsNullOrWhiteSpace(latest.YankReason))
        {
            errors.Add($"{path}.latest.yankReason is required when the version is yanked.");
        }

        if (latest.Artifact is not { } artifact)
        {
            errors.Add($"{path}.latest.artifact is required.");
            return;
        }

        if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var artifactUri) || artifactUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{path}.latest.artifact.url must be an absolute HTTPS URL.");
        }
        else if (!AllowedArtifactHosts.Contains(artifactUri.Host))
        {
            errors.Add($"{path}.latest.artifact.url host '{artifactUri.Host}' is not allowlisted.");
        }

        if (!Sha256Regex.IsMatch(artifact.Sha256))
        {
            errors.Add($"{path}.latest.artifact.sha256 must be 64 lowercase hex characters.");
        }

        if (artifact.Size <= 0 || artifact.Size > FbpPackageLimits.MaxUncompressedBytes)
        {
            errors.Add($"{path}.latest.artifact.size is outside the supported package size range.");
        }

        if (!IsHttps(artifact.SignatureUrl))
        {
            errors.Add($"{path}.latest.artifact.signatureUrl must be an absolute HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(artifact.KeyId))
        {
            errors.Add($"{path}.latest.artifact.keyId is required.");
        }

        if (artifact.Algorithm != SignatureAlgorithm.EcdsaP256Sha256)
        {
            errors.Add($"{path}.latest.artifact.algorithm '{artifact.Algorithm}' is not supported in v2.0.");
        }

        if (latest.LocaleCoverage is { } coverage && coverage.Values.Any(static value => value is < 0 or > 100))
        {
            errors.Add($"{path}.latest.localeCoverage values must be between 0 and 100.");
        }
    }

    private static bool IsHttps(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Compatibility filter used by the client: is this plugin's latest version installable here?</summary>
    public static bool IsCompatible(MarketplacePluginEntry plugin, SemanticVersion appVersion, SemanticVersion apiVersion)
    {
        if (plugin.Latest is not { } latest || plugin.Status == PluginListingStatus.Blocked || latest.Yanked)
        {
            return false;
        }

        if (!SemanticVersion.TryParse(latest.ApiVersion, out var pluginApi) ||
            !SemanticVersion.TryParse(latest.MinAppVersion, out var minAppVersion))
        {
            return false;
        }

        SemanticVersion? maxAppVersion = null;
        if (!string.IsNullOrWhiteSpace(latest.MaxAppVersion))
        {
            if (!SemanticVersion.TryParse(latest.MaxAppVersion, out var parsedMax))
            {
                return false;
            }

            maxAppVersion = parsedMax;
        }

        return PluginApiVersion.CheckCompatibility(pluginApi, minAppVersion, maxAppVersion, apiVersion, appVersion).IsCompatible;
    }
}

/// <summary>Outcome of index semantic validation.</summary>
public sealed record IndexValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Raised when marketplace index JSON is malformed or invalid.</summary>
public sealed class MarketplaceIndexException : Exception
{
    public MarketplaceIndexException(string message)
        : base(message)
    {
    }

    public MarketplaceIndexException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
