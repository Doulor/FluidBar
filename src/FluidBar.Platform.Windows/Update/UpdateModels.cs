using System.Collections.Immutable;

namespace FluidBar.Platform.Windows.Update;

/// <summary>Release channel selection.</summary>
public enum UpdateChannel
{
    /// <summary>Stable releases only (default).</summary>
    Stable,

    /// <summary>Include prereleases.</summary>
    Prerelease,
}

/// <summary>Outcome of an update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>No newer release is available.</summary>
    UpToDate,

    /// <summary>A newer release exists; show "update available" but never auto-install.</summary>
    UpdateAvailable,

    /// <summary>HTTP 304: the cached manifest is still current.</summary>
    NotModified,

    /// <summary>The check failed (network/parse/HTTP); the App surfaces a quiet error.</summary>
    Failed,
}

/// <summary>Result of installer verification. Only <see cref="Verified"/> may expose an install action.</summary>
public enum UpdateVerificationStatus
{
    NotVerified,
    Sha256Mismatch,
    SignatureUnsigned,
    SignatureInvalid,
    SignatureUntrusted,
    SignatureCheckFailed,
    Verified,
}

/// <summary>Overall update state for the settings About page and the update activity.</summary>
public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Failed,
}

/// <summary>Immutable description of one release asset.</summary>
public sealed record UpdateAsset(string Name, Uri Url, long SizeBytes, string? Sha256Hex);

/// <summary>Immutable parsed release.</summary>
public sealed record UpdateRelease(
    string Tag,
    SemanticVersion Version,
    string Name,
    string? Notes,
    Uri? ReleasePageUrl,
    bool IsPrerelease,
    DateTimeOffset? PublishedAt,
    ImmutableArray<UpdateAsset> Assets,
    UpdateAsset? Installer,
    string? InstallerSha256Hex)
{
    /// <summary>Whether a verified install action can be offered (hash + URL present).</summary>
    public bool CanOfferInstaller => Installer is not null && !string.IsNullOrWhiteSpace(InstallerSha256Hex);
}

/// <summary>Request for a single check.</summary>
/// <param name="CurrentVersion">Currently installed application version.</param>
/// <param name="Channel">Stable or prerelease.</param>
/// <param name="ETag">Previous ETag for conditional GET (may be null).</param>
public sealed record UpdateCheckRequest(Version CurrentVersion, UpdateChannel Channel = UpdateChannel.Stable,
    string? ETag = null);

/// <summary>Result of a check.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Latest, string? ETag,
    string? FailureReason = null)
{
    public static UpdateCheckResult Failed(string reason, string? etag = null)
        => new(UpdateCheckStatus.Failed, null, etag, reason);
}

/// <summary>Result of an explicit installer download.</summary>
public sealed record UpdateDownloadResult(
    bool Success,
    string? FilePath,
    UpdateVerificationStatus Verification,
    string? Sha256Hex,
    string? FailureReason = null)
{
    /// <summary>Install action may be exposed only for a verified file.</summary>
    public bool CanInstall => Success && Verification == UpdateVerificationStatus.Verified && FilePath is not null;
}

/// <summary>Snapshot consumed by the settings About page and the Core activity mapping.</summary>
public sealed record UpdateStatusSnapshot(
    UpdateState State,
    Version CurrentVersion,
    UpdateRelease? AvailableRelease,
    string? DownloadedFilePath,
    UpdateVerificationStatus Verification,
    DateTimeOffset? LastCheckedAtUtc,
    string? Message)
{
    public static UpdateStatusSnapshot Idle(Version currentVersion)
        => new(UpdateState.Idle, currentVersion, null, null, UpdateVerificationStatus.NotVerified, null, null);
}

/// <summary>Client configuration.</summary>
public sealed class UpdateClientOptions
{
    /// <summary>Release feed URL (GitHub Releases API JSON, e.g. <c>https://api.github.com/repos/org/repo/releases</c>).</summary>
    public required Uri ReleaseFeedUrl { get; init; }

    /// <summary>User-Agent sent with every request (required by the GitHub API).</summary>
    public string UserAgent { get; init; } = "FluidBar-UpdateCheck";

    /// <summary>Per-request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Maximum accepted manifest payload size.</summary>
    public int MaxManifestBytes { get; init; } = 1024 * 1024;

    /// <summary>Maximum accepted installer size.</summary>
    public long MaxInstallerBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>Asset name pattern identifying the Windows installer.</summary>
    public string InstallerAssetPattern { get; init; } = @"FluidBar.*\.exe$";

    /// <summary>Directory used for downloaded installers (created on demand).</summary>
    public required string DownloadDirectory { get; init; }
}
