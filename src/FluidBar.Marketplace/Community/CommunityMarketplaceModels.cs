using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;

namespace FluidBar.Marketplace.Community;

/// <summary>Review state of one submitted package.</summary>
public enum CommunitySubmissionState
{
    /// <summary>Waiting for review; never listed in the marketplace.</summary>
    Pending = 0,

    /// <summary>Reviewed and listed in <c>index.json</c>.</summary>
    Approved = 1,

    /// <summary>Rejected (or delisted); the reason is kept in <see cref="CommunitySubmission.ReviewNote"/>.</summary>
    Rejected = 2,
}

/// <summary>Authenticity of a submitted package.</summary>
public enum CommunitySignatureState
{
    /// <summary>No detached signature was supplied; the reviewer accepted it explicitly.</summary>
    Unsigned = 0,

    /// <summary>A detached signature verified against the injected trust keys.</summary>
    Verified = 1,
}

/// <summary>What a reviewer decided about one submission.</summary>
public enum CommunityReviewDecision
{
    /// <summary>Approve (list, or re-list) the submission.</summary>
    Approve = 0,

    /// <summary>Reject the submission or delist it again.</summary>
    Reject = 1,
}

/// <summary>
/// Plugin metadata copied from the verified package manifest at submit time. Nothing here is invented: every
/// field comes from the reviewed <c>manifest.json</c> of the submitted <c>.fbp</c>.
/// </summary>
public sealed record CommunityPluginInfo
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinAppVersion { get; init; } = string.Empty;

    /// <summary>Resolved display name (default locale of the manifest).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Resolved short summary.</summary>
    public string Summary { get; init; } = string.Empty;

    public string PublisherId { get; init; } = string.Empty;

    public string PublisherName { get; init; } = string.Empty;

    public string? AuthorName { get; init; }

    public string? License { get; init; }

    public string EntryAssembly { get; init; } = string.Empty;

    public string EntryType { get; init; } = string.Empty;

    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Declared capability tokens (disclosure only; never an enforcement claim).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Builds the info block from a verified manifest.</summary>
    public static CommunityPluginInfo FromManifest(PluginManifest manifest, string locale = "en-US")
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new CommunityPluginInfo
        {
            Id = manifest.Id,
            Version = manifest.Version,
            ApiVersion = manifest.ApiVersion,
            MinAppVersion = manifest.MinAppVersion,
            Name = Resolve(manifest.Name, locale) is { Length: > 0 } name ? name : manifest.Id,
            Summary = Resolve(manifest.Summary, locale) is { Length: > 0 } summary ? summary : string.Empty,
            PublisherId = manifest.Publisher?.Id ?? string.Empty,
            PublisherName = manifest.Publisher is { DisplayName.Length: > 0 } publisher
                ? publisher.DisplayName
                : manifest.Publisher?.Id ?? string.Empty,
            AuthorName = string.IsNullOrWhiteSpace(manifest.Author?.Name) ? null : manifest.Author.Name,
            License = string.IsNullOrWhiteSpace(manifest.License) ? null : manifest.License,
            EntryAssembly = manifest.Entry?.Assembly ?? string.Empty,
            EntryType = manifest.Entry?.Type ?? string.Empty,
            Categories = [.. (manifest.Categories ?? []).Select(static category => FluidBar.Sdk.Json.WireEnum.GetName(category))],
            Capabilities = [.. manifest.Capabilities.GetDeclaredCapabilityNames()],
        };
    }

    private static string Resolve(LocalizedString? value, string locale) =>
        value?.Resolve(locale, "en-US") ?? string.Empty;
}

/// <summary>One submitted package and its review state.</summary>
public sealed record CommunitySubmission
{
    public int SchemaVersion { get; init; } = 1;

    public string PluginId { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>File name of the stored copy inside the marketplace <c>packages/</c> directory.</summary>
    public string PackageFileName { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;

    public long PackageSize { get; init; }

    /// <summary>File name of the stored detached signature copy, or <see langword="null"/>.</summary>
    public string? SignatureFileName { get; init; }

    public CommunitySignatureState SignatureState { get; init; } = CommunitySignatureState.Unsigned;

    /// <summary>Signing key id reported by the verified signature document.</summary>
    public string? SignatureKeyId { get; init; }

    public CommunityPluginInfo Plugin { get; init; } = new();

    public DateTimeOffset SubmittedAt { get; init; }

    public string Reviewer { get; init; } = CommunityMarketplaceDefaults.Reviewer;

    public CommunitySubmissionState State { get; init; } = CommunitySubmissionState.Pending;

    public DateTimeOffset? ReviewedAt { get; init; }

    public string? ReviewNote { get; init; }
}

/// <summary>Community (device-local) stats written by ratings and installs.</summary>
public sealed record CommunityStats
{
    /// <summary>Successful installs of a listed version on this device.</summary>
    public long Downloads { get; init; }

    /// <summary>Average rating, or <see langword="null"/> until the package has been rated.</summary>
    public double? Rating { get; init; }

    /// <summary>Number of recorded ratings (this local marketplace records one rating per device).</summary>
    public int RatingCount { get; init; }

    /// <summary>The rating this device contributed, if any (drives the filled-star state).</summary>
    public int? UserRating { get; init; }
}

/// <summary>An approved plugin as it appears in the local marketplace.</summary>
public sealed record CommunityListing
{
    public string Id { get; init; } = string.Empty;

    public CommunityPluginInfo Plugin { get; init; } = new();

    /// <summary>Latest approved version.</summary>
    public string Version { get; init; } = string.Empty;

    public string PackageFileName { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;

    public long PackageSize { get; init; }

    public string? SignatureFileName { get; init; }

    public CommunitySignatureState SignatureState { get; init; } = CommunitySignatureState.Unsigned;

    public string? SignatureKeyId { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public string? ApprovedNote { get; init; }

    public CommunityStats Stats { get; init; } = new();

    /// <summary>Approved version strings, newest first.</summary>
    public IReadOnlyList<string> Versions { get; init; } = [];
}

/// <summary>The local marketplace document persisted at <c>%APPDATA%\FluidBar\marketplace\index.json</c>.</summary>
public sealed record LocalMarketplaceIndex
{
    /// <summary>Discriminator of the local index kind (it is not the signed remote index).</summary>
    public const string KindValue = "fluidbar.local-marketplace";

    public int SchemaVersion { get; init; } = 1;

    public string Kind { get; init; } = KindValue;

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<CommunityListing> Plugins { get; init; } = [];

    public IReadOnlyList<CommunitySubmission> Submissions { get; init; } = [];

    /// <summary>An empty document with a stable timestamp (unused when writing).</summary>
    public static LocalMarketplaceIndex Empty { get; } = new();
}

/// <summary>Shared defaults of the local marketplace.</summary>
public static class CommunityMarketplaceDefaults
{
    /// <summary>Reviewer identity recorded for local review actions (there is no server-side account).</summary>
    public const string Reviewer = "local-reviewer";

    /// <summary>The only locale the local marketplace resolves manifest strings against.</summary>
    public const string Locale = "en-US";
}

/// <summary>Raised when the local marketplace store/index cannot be read or written.</summary>
public sealed class CommunityMarketplaceException : Exception
{
    /// <summary>Creates the exception.</summary>
    public CommunityMarketplaceException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public CommunityMarketplaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Serializer configuration for the local marketplace files (forward tolerant, indented for review).</summary>
internal static class CommunityJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    internal static T? TryDeserialize<T>(string json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
