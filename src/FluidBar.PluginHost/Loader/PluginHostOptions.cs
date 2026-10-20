using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// Host configuration. Everything the host needs from the App is injected here so the loader itself is
/// UI-free and testable without a WPF/Avalonia application object.
/// </summary>
public sealed record PluginHostOptions
{
    /// <summary>Directory containing one folder per plugin: <c>&lt;plugins&gt;/&lt;id&gt;/manifest.json</c>.</summary>
    public required string PluginRootDirectory { get; init; }

    /// <summary>The running FluidBar version, used for <c>minAppVersion</c>/<c>maxAppVersion</c> gates.</summary>
    public required SemanticVersion AppVersion { get; init; }

    /// <summary>The SDK ABI version implemented by the host; defaults to the SDK build.</summary>
    public SemanticVersion HostApiVersion { get; init; } = PluginApiVersion.Current;

    /// <summary>Where per-plugin <c>settings.json</c> lives. Defaults to <see cref="PluginRootDirectory"/>.</summary>
    public string? SettingsRootDirectory { get; init; }

    /// <summary>Where per-plugin <c>$PLUGIN_DATA</c> directories live. Defaults to <see cref="PluginRootDirectory"/>.</summary>
    public string? DataRootDirectory { get; init; }

    public TimeSpan InitializeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan DisposeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long unload waits for a best-effort AssemblyLoadContext collection.</summary>
    public TimeSpan UnloadCollectionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Host timeout for one plugin command invocation.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive load/start failures before a plugin is disabled.</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>Maximum plugins discovered; bounded so a hostile plugins folder cannot stall startup.</summary>
    public int MaxDiscoveredPlugins { get; init; } = 512;

    /// <summary>Maximum bytes read for a manifest file.</summary>
    public int MaxManifestBytes { get; init; } = PluginManifestLimits.MaxManifestBytes;

    /// <summary>Structured host log sink.</summary>
    public IPluginHostLogger Logger { get; init; } = NullPluginHostLogger.Instance;

    /// <summary>Injectable clock used by leases and timeouts.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>UI-thread dispatcher supplied by the App; defaults to an inline dispatcher.</summary>
    public IHostDispatcher? Dispatcher { get; init; }

    /// <summary>Creates the island publisher for a plugin. The App wires this to the core event hub.</summary>
    public Func<PluginManifest, IIslandPublisher>? IslandPublisherFactory { get; init; }

    /// <summary>Host-owned cleanup hook fired when a plugin stops or unloads (pages/commands).</summary>
    public Action<string>? PluginStopped { get; init; }

    /// <summary>Creates the notification sink for a plugin. The App wires this to the notification service.</summary>
    public Func<PluginManifest, INotificationSink>? NotificationSinkFactory { get; init; }

    /// <summary>Manifest validation policy (marketplace installs use stricter options).</summary>
    public ManifestValidationOptions ValidationOptions { get; init; } = ManifestValidationOptions.Default;

    /// <summary>
    /// Capabilities the user consented to. When <see langword="null"/> every declared capability is granted
    /// (first-run/dev behavior); production passes the persisted consent snapshot.
    /// </summary>
    public IReadOnlySet<PluginCapability>? GrantedCapabilities { get; init; }

    /// <summary>Host locales used after the plugin's own locales when resolving messages.</summary>
    public IReadOnlyList<string> HostUiLocales { get; init; } = ["en-US"];

    /// <summary>Create plugin/settings/data directories on demand.</summary>
    public bool CreateDirectories { get; init; } = true;

    /// <summary>
    /// Optional load-time package gate. The App sets this only for the market root so signed/reviewed
    /// packages are re-verified on every process start; the local root deliberately leaves it null.
    /// </summary>
    public Func<PluginDiscoveryInfo, CancellationToken, Task<PluginPackageGateResult>>? PackageGate { get; init; }

    /// <summary>Optional assembly names that must be shared from the host (SDK is always shared).</summary>
    public IReadOnlyList<string> AdditionalSharedAssemblies { get; init; } = [];

    /// <summary>Resolved settings root.</summary>
    public string ResolveSettingsRoot() => SettingsRootDirectory ?? PluginRootDirectory;

    /// <summary>Resolved data root.</summary>
    public string ResolveDataRoot() => DataRootDirectory ?? PluginRootDirectory;

    /// <summary>Validates required options and normalizes paths; throws on unusable configuration.</summary>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PluginRootDirectory);
        if (AppVersion.Major <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AppVersion), "AppVersion must be a positive semantic version.");
        }

        if (InitializeTimeout <= TimeSpan.Zero || StartTimeout <= TimeSpan.Zero || StopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InitializeTimeout), "Plugin timeouts must be positive.");
        }

        if (MaxConsecutiveFailures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConsecutiveFailures), "MaxConsecutiveFailures must be at least 1.");
        }
    }
}
