using FluidBar.Marketplace.Consent;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Install;

/// <summary>Failure classes of the install/update/rollback/uninstall state machine.</summary>
public enum InstallFailureKind
{
    None = 0,
    DownloadFailed,
    HashMismatch,
    SignatureInvalid,
    Incompatible,
    Blocked,
    Yanked,
    InvalidPackage,
    ExtractionFailed,
    ActivationFailed,
    AlreadyInstalled,
    VersionAlreadyExists,
    RollbackFailed,
    NotInstalled,
    Cancelled,
    IoError,
}

/// <summary>Phases of the install state machine (mirrors the design doc).</summary>
public enum InstallPhase
{
    Available = 0,
    Downloading,
    Verifying,
    Extracting,
    Staged,
    Activating,
    Active,
}

/// <summary>A package source: local file, injected stream (tests) or HTTPS download.</summary>
public interface IPackageSource
{
    string Description { get; }

    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
}

/// <summary>Offline install from a local <c>.fbp</c>.</summary>
public sealed class FilePackageSource : IPackageSource
{
    private readonly string _path;

    public FilePackageSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public string Description => _path;

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException("Package file was not found.", _path);
        }

        return Task.FromResult<Stream>(new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true));
    }
}

/// <summary>HTTPS package source used by marketplace installs (bounded by the caller while copying).</summary>
public sealed class HttpPackageSource : IPackageSource
{
    private readonly HttpClient _httpClient;
    private readonly Uri _url;

    public HttpPackageSource(HttpClient httpClient, Uri url)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _url = url ?? throw new ArgumentNullException(nameof(url));
        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Package URLs must be HTTPS.", nameof(url));
        }
    }

    public string Description => _url.ToString();

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _url);
        var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"Package download failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Activates a staged version directory in the plugin host (and deactivates the previous one).</summary>
public interface IPluginActivator
{
    Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken);

    Task DeactivateAsync(string pluginId, CancellationToken cancellationToken);
}

/// <summary>Activation outcome supplied by the host adapter.</summary>
public sealed record ActivationResult(bool Success, string? ErrorCode, string? Message)
{
    public static ActivationResult Ok() => new(true, null, null);

    public static ActivationResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>Install request. Signature verification happens before extraction/activation.</summary>
public sealed record InstallRequest
{
    public required IPackageSource Source { get; init; }

    /// <summary>Detached signature document; required for marketplace installs.</summary>
    public PackageSignatureDocument? Signature { get; init; }

    /// <summary>Signed index entry for the version being installed (hash/size/capabilities expected).</summary>
    public MarketplaceVersionInfo? ExpectedVersion { get; init; }

    /// <summary>Pinned publisher key id (TOFU); when set the package must carry a matching author signature.</summary>
    public string? ExpectedPublisherKeyId { get; init; }

    /// <summary>Allow an unsigned offline local package (UI must show the unverified warning).</summary>
    public bool AllowUnsigned { get; init; }

    /// <summary>When true the version must be newer than the active one and the old activation is replaced.</summary>
    public bool IsUpdate { get; init; }

    /// <summary>Expected plugin id (protects updates/rollbacks against a swapped package).</summary>
    public string? ExpectedPluginId { get; init; }

    /// <summary>
    /// Optional stage observer for the marketplace UI. Reports the real phases of the state machine
    /// (<see cref="InstallPhase.Downloading"/> → Verifying → Extracting → Staged → Activating → Active).
    /// </summary>
    public IProgress<InstallPhase>? Progress { get; init; }
}

/// <summary>Install outcome.</summary>
public sealed record InstallResult
{
    public required bool Success { get; init; }

    public required InstallFailureKind FailureKind { get; init; }

    public string PluginId { get; init; } = string.Empty;

    public string? Version { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Consent data for the install UI; populated from the reviewed manifest on success.</summary>
    public CapabilityConsentData? Consent { get; init; }

    public string? InstalledDirectory { get; init; }

    public bool RestartRequired { get; init; }

    public static InstallResult Fail(InstallFailureKind kind, string message, string pluginId = "", IReadOnlyList<string>? warnings = null) =>
        new() { Success = false, FailureKind = kind, Message = message, PluginId = pluginId, Warnings = warnings ?? [] };
}

/// <summary>Rollback outcome.</summary>
public sealed record RollbackResult(bool Success, string PluginId, string? ActiveVersion, string? PreviousVersion, string Message)
{
    public static RollbackResult Fail(string pluginId, string message) => new(false, pluginId, null, null, message);
}

/// <summary>Uninstall outcome.</summary>
public sealed record UninstallResult(bool Success, string PluginId, IReadOnlyList<string> RemovedVersions, bool SettingsRetained, string Message)
{
    public bool RestartRequired { get; init; }
}

/// <summary>Persisted per-plugin install state (<c>&lt;id&gt;/state.json</c>).</summary>
public sealed record PluginInstallState
{
    public int SchemaVersion { get; init; } = 1;

    public string PluginId { get; init; } = string.Empty;

    public string? ActiveVersion { get; init; }

    public string? PreviousVersion { get; init; }

    public string? LastGoodVersion { get; init; }

    /// <summary>Publisher key pinned on first install (TOFU).</summary>
    public string? PublisherKeyId { get; init; }

    public DateTimeOffset InstalledAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<string>? RetainedVersions { get; init; }

    /// <summary>Version → package SHA-256, so re-installing the same artifact is idempotent and a different artifact under the same version is rejected.</summary>
    public IReadOnlyDictionary<string, string>? PackageHashes { get; init; }

    public string? BlockedReason { get; init; }

    public bool Disabled { get; init; }
}

/// <summary>Policy decision for an installed plugin against the current signed index.</summary>
public sealed record PluginPolicyDecision(
    PluginListingStatus Status,
    bool IsBlocked,
    bool IsDeprecated,
    bool HasYankedLatest,
    string? Reason,
    string? Replacement,
    bool LatestCompatible)
{
    public bool MustDisable => IsBlocked;
}

/// <summary>Install logging abstraction (the App routes this to the JSON-lines log sink).</summary>
public interface IPluginInstallLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>Null logger for headless use/tests that only assert results.</summary>
public sealed class NullPluginInstallLogger : IPluginInstallLogger
{
    public static NullPluginInstallLogger Instance { get; } = new();

    private NullPluginInstallLogger()
    {
    }

    public void Info(string message)
    {
    }

    public void Warn(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
    }
}

/// <summary>Install service configuration.</summary>
public sealed class PluginInstallOptions
{
    /// <summary>Plugins root, normally <c>%APPDATA%\FluidBar\plugins</c>.</summary>
    public required string PluginRootDirectory { get; init; }

    /// <summary>Host adapter that loads/unloads the activated version.</summary>
    public required IPluginActivator Activator { get; init; }

    /// <summary>Trusted marketplace + pinned publisher keys.</summary>
    public MarketplaceTrustOptions Trust { get; init; } = MarketplaceTrustOptions.Empty;

    /// <summary>Require the marketplace countersignature for marketplace installs.</summary>
    public bool RequireMarketplaceSignature { get; init; } = true;

    /// <summary>Allow unsigned offline local installs (UI shows the unverified warning).</summary>
    public bool AllowUnsignedLocalInstall { get; init; } = true;

    /// <summary>App version used for min/max compatibility gates.</summary>
    public SemanticVersion AppVersion { get; init; } = new(2, 0, 0);

    /// <summary>SDK ABI version implemented by the host.</summary>
    public SemanticVersion HostApiVersion { get; init; } = PluginApiVersion.Current;

    /// <summary>Keep at most this many installed versions per plugin (active + previous by default).</summary>
    public int RetainedVersions { get; init; } = 2;

    /// <summary>Staging/activation timeout.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public FbpPackageReaderOptions ReaderOptions { get; init; } = FbpPackageReaderOptions.Default;

    public IPluginInstallLogger Logger { get; init; } = NullPluginInstallLogger.Instance;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Root folder name for per-plugin data/settings (sibling of version directories).</summary>
    public string DataDirectoryName { get; init; } = "data";

    public string SettingsFileName { get; init; } = "settings.json";
}
