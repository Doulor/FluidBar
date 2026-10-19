using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Community;

/// <summary>Install request for one approved community package.</summary>
public sealed record CommunityPackageInstallRequest
{
    /// <summary>Absolute path of the reviewed package copy.</summary>
    public required string PackagePath { get; init; }

    /// <summary>Absolute path of the detached signature copy, or <see langword="null"/>.</summary>
    public string? SignaturePath { get; init; }

    /// <summary>Expected plugin id (protects against a swapped package).</summary>
    public required string PluginId { get; init; }

    /// <summary>Expected version (recorded by the install state machine).</summary>
    public required string Version { get; init; }

    /// <summary>Allow installing an unsigned package (the listing must be flagged unverified).</summary>
    public bool AllowUnsigned { get; init; } = true;

    /// <summary>Optional real install-phase observer for the UI.</summary>
    public IProgress<InstallPhase>? Progress { get; init; }
}

/// <summary>Installs an approved community package. Injected so tests never touch the host activator.</summary>
public interface ICommunityPackageInstaller
{
    /// <summary>Installs/updates one package through the real install state machine.</summary>
    Task<InstallResult> InstallAsync(CommunityPackageInstallRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ICommunityPackageInstaller"/>: delegates to the production <see cref="PluginInstallService"/>
/// (verify hash/signature → extract → atomic version directory → active pointer). The host activator is injected;
/// the desktop app supplies a deferred one, so the running process stages the plugin and loads it from
/// <c>%APPDATA%\FluidBar\plugins</c> on the next start (Windows cannot reliably reload an in-place plugin DLL).
/// </summary>
public sealed class PluginInstallServiceCommunityInstaller : ICommunityPackageInstaller, IDisposable
{
    private readonly Lazy<PluginInstallService> _service;
    private bool _disposed;

    /// <summary>Wraps a configured install service.</summary>
    public PluginInstallServiceCommunityInstaller(PluginInstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _service = new Lazy<PluginInstallService>(() => new PluginInstallService(options), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Creates the default local installer over <c>%APPDATA%\FluidBar\plugins</c> (env overridable).</summary>
    public static PluginInstallServiceCommunityInstaller CreateDefault(MarketplaceTrustOptions? trust = null) =>
        new(new PluginInstallOptions
        {
            PluginRootDirectory = CommunityPluginPaths.ResolvePluginsRoot(),
            Activator = new DeferredActivationPluginActivator(),
            Trust = trust ?? MarketplaceTrustOptions.Empty,
            RequireMarketplaceSignature = false,
            AllowUnsignedLocalInstall = true,
            AppVersion = new SemanticVersion(2, 0, 0),
            HostApiVersion = PluginApiVersion.Current,
        });

    /// <inheritdoc />
    public async Task<InstallResult> InstallAsync(CommunityPackageInstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!File.Exists(request.PackagePath))
        {
            return InstallResult.Fail(InstallFailureKind.IoError, $"Package '{request.PackagePath}' does not exist.", request.PluginId);
        }

        PackageSignatureDocument? signature = null;
        if (!string.IsNullOrWhiteSpace(request.SignaturePath) && File.Exists(request.SignaturePath))
        {
            signature = PackageSignatureDocument.Parse(await File.ReadAllTextAsync(request.SignaturePath, cancellationToken).ConfigureAwait(false));
        }

        return await _service.Value.InstallAsync(
            new InstallRequest
            {
                Source = new FilePackageSource(request.PackagePath),
                Signature = signature,
                AllowUnsigned = request.AllowUnsigned,
                ExpectedPluginId = request.PluginId,
                Progress = request.Progress,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_service.IsValueCreated)
        {
            _service.Value.Dispose();
        }
    }
}

/// <summary>
/// Host activator used when no live plugin host is available (the local marketplace view runs inside the app
/// process but does not own the plugin host handle). The filesystem install is complete; activation happens on
/// the next app start.
/// </summary>
public sealed class DeferredActivationPluginActivator : IPluginActivator
{
    /// <summary>Version directories that were staged for the next start.</summary>
    public List<string> StagedDirectories { get; } = [];

    /// <inheritdoc />
    public Task<ActivationResult> ActivateAsync(PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        StagedDirectories.Add(versionDirectory);
        return Task.FromResult(ActivationResult.Ok());
    }

    /// <inheritdoc />
    public Task DeactivateAsync(string pluginId, CancellationToken cancellationToken) => Task.CompletedTask;
}
