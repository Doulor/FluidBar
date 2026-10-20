using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Loader;

/// <summary>Why a plugin failed to load or activate. Stable codes for UI/telemetry.</summary>
public enum PluginLoadFailureKind
{
    None = 0,
    ManifestMissing,
    ManifestInvalid,
    Incompatible,
    ForbiddenReference,
    EntryMissing,
    EntryTypeMissing,
    EntryTypeInvalid,
    ConstructorFailed,
    InitializeFailed,
    StartFailed,
    Timeout,
    Unloaded,
    Disabled,
    Unexpected,

    /// <summary>A market package failed its load-time signature/integrity/review gate.</summary>
    Untrusted,
}

/// <summary>Result of loading/activating one plugin. Failures are values, never exceptions thrown at the App.</summary>
public sealed record PluginLoadResult
{
    private PluginLoadResult(PluginHandle? handle, PluginLoadFailureKind failureKind, string message, IReadOnlyList<string> validationErrors)
    {
        Handle = handle;
        FailureKind = failureKind;
        Message = message;
        ValidationErrors = validationErrors;
    }

    public PluginHandle? Handle { get; }

    public bool Success => FailureKind == PluginLoadFailureKind.None && Handle is not null;

    public PluginLoadFailureKind FailureKind { get; }

    public string Message { get; }

    public IReadOnlyList<string> ValidationErrors { get; }

    public string PluginId => Handle?.PluginId ?? string.Empty;

    public static PluginLoadResult Ok(PluginHandle handle) =>
        new(handle, PluginLoadFailureKind.None, string.Empty, []);

    public static PluginLoadResult Fail(PluginHandle? handle, PluginLoadFailureKind kind, string message, IReadOnlyList<string>? validationErrors = null) =>
        new(handle, kind, message, validationErrors ?? []);
}

/// <summary>Result of the best-effort unload sequence.</summary>
public sealed record PluginUnloadResult
{
    public PluginUnloadResult(
        string pluginId,
        bool success,
        bool collected,
        int collectionAttempts,
        TimeSpan duration,
        bool restartRequired,
        string? failureReason)
    {
        PluginId = pluginId;
        Success = success;
        Collected = collected;
        CollectionAttempts = collectionAttempts;
        Duration = duration;
        RestartRequired = restartRequired;
        FailureReason = failureReason;
    }

    public string PluginId { get; }

    public bool Success { get; }

    /// <summary>True when the collectible AssemblyLoadContext was observed collected after the GC loop.</summary>
    public bool Collected { get; }

    public int CollectionAttempts { get; }

    public TimeSpan Duration { get; }

    /// <summary>True when the App must offer "restart FluidBar to finish removing this plugin".</summary>
    public bool RestartRequired { get; }

    public string? FailureReason { get; }
}

/// <summary>Result of a command dispatch; plugin exceptions are contained here.</summary>
public sealed record PluginCommandExecutionResult
{
    private PluginCommandExecutionResult(bool executed, bool succeeded, string? error)
    {
        Executed = executed;
        Succeeded = succeeded;
        Error = error;
    }

    public bool Executed { get; }

    public bool Succeeded { get; }

    public string? Error { get; }

    public static PluginCommandExecutionResult NotAvailable(string reason) => new(executed: false, succeeded: false, reason);

    public static PluginCommandExecutionResult Ok() => new(executed: true, succeeded: true, error: null);

    public static PluginCommandExecutionResult Failed(string error) => new(executed: true, succeeded: false, error);
}

/// <summary>Result of an optional package trust gate evaluated again at load time.</summary>
public sealed record PluginPackageGateResult(bool Allowed, string Error, PluginLoadFailureKind FailureKind = PluginLoadFailureKind.Untrusted)
{
    public static PluginPackageGateResult Allow { get; } = new(true, string.Empty, PluginLoadFailureKind.None);

    public static PluginPackageGateResult Reject(string error, PluginLoadFailureKind kind = PluginLoadFailureKind.Untrusted)
        => new(false, error, kind);
}

/// <summary>Discovery outcome for one plugin directory (valid manifest or a deterministic error).</summary>
public sealed record PluginDiscoveryInfo
{
    public PluginDiscoveryInfo(
        string pluginId,
        string packageDirectory,
        string manifestPath,
        PluginManifest? manifest,
        bool isValid,
        IReadOnlyList<string> errors)
    {
        PluginId = pluginId;
        PackageDirectory = packageDirectory;
        ManifestPath = manifestPath;
        Manifest = manifest;
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary>Id taken from the manifest when it parsed, otherwise the directory name.</summary>
    public string PluginId { get; }

    public string PackageDirectory { get; }

    public string ManifestPath { get; }

    public PluginManifest? Manifest { get; }

    public bool IsValid { get; }

    public IReadOnlyList<string> Errors { get; }
}
