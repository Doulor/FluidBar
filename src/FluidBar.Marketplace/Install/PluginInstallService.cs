using System.Diagnostics;
using System.Security.Cryptography;
using FluidBar.Marketplace.Consent;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Packaging;
using FluidBar.Marketplace.Signing;
using FluidBar.Sdk.Capabilities;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Versioning;

namespace FluidBar.Marketplace.Install;

/// <summary>
/// Install / update / rollback / uninstall state machine over the versioned plugin directory layout:
/// download → verify hash + signature → extract safely → atomic move to <c>&lt;id&gt;/&lt;version&gt;</c> →
/// atomically switch <c>active.json</c> → activate through the injected host adapter. A failed activation
/// restores the previous version; a rollback is a pointer switch, not a re-download (Windows locks loaded
/// DLLs, so versions are never overwritten in place).
/// </summary>
public sealed class PluginInstallService : IDisposable
{
    private const string StagingDirectoryName = ".staging";
    private static readonly TimeSpan StaleStagingAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly PluginInstallOptions _options;
    private readonly PackageSignatureVerifier _signatureVerifier;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _stagingDirectory;
    private bool _disposed;

    public PluginInstallService(PluginInstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PluginRootDirectory);
        ArgumentNullException.ThrowIfNull(options.Activator);
        _options = options;
        _signatureVerifier = new PackageSignatureVerifier(options.Trust);
        _stagingDirectory = Path.Combine(Path.GetFullPath(options.PluginRootDirectory), StagingDirectoryName);
        Directory.CreateDirectory(options.PluginRootDirectory);
        Directory.CreateDirectory(_stagingDirectory);
        CleanupStaleStaging();
    }

    /// <summary>Installs from a local <c>.fbp</c> file (offline install; unsigned allowed when configured).</summary>
    public Task<InstallResult> InstallFromFileAsync(
        string packagePath,
        PackageSignatureDocument? signature = null,
        bool? allowUnsigned = null,
        CancellationToken cancellationToken = default) =>
        InstallAsync(
            new InstallRequest
            {
                Source = new FilePackageSource(packagePath),
                Signature = signature,
                AllowUnsigned = allowUnsigned ?? _options.AllowUnsignedLocalInstall,
                IsUpdate = false,
            },
            cancellationToken);

    /// <summary>Installs a package (first install or update in place of the active version).</summary>
    public async Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await InstallCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _options.Logger.Info($"Install of '{request.Source.Description}' finished in {stopwatch.ElapsedMilliseconds} ms.");
        }
    }

    /// <summary>Updates a plugin, requiring the target version to be newer than the active version.</summary>
    public Task<InstallResult> UpdateAsync(InstallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedVersion is null)
        {
            return Task.FromResult(InstallResult.Fail(InstallFailureKind.Incompatible, "An update requires the target index version."));
        }

        return InstallAsync(request with { IsUpdate = true }, cancellationToken);
    }

    /// <summary>Rolls back to a retained previous version (pointer switch + host activation).</summary>
    public async Task<RollbackResult> RollbackAsync(string pluginId, string targetVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = new PluginStateStore(_options.PluginRootDirectory, pluginId, _options.Logger);
            var state = store.ReadState();
            if (state is null)
            {
                return RollbackResult.Fail(pluginId, $"Plugin '{pluginId}' is not installed.");
            }

            var activeVersion = store.ReadActiveVersion() ?? state.ActiveVersion;
            if (string.Equals(activeVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            {
                return new RollbackResult(true, pluginId, activeVersion, state.PreviousVersion, "Requested version is already active.");
            }

            var targetDirectory = store.GetVersionDirectory(targetVersion);
            var targetManifestPath = Path.Combine(targetDirectory, "manifest.json");
            if (!File.Exists(targetManifestPath))
            {
                return RollbackResult.Fail(pluginId, $"Version '{targetVersion}' is not installed; cannot roll back.");
            }

            PluginManifest manifest;
            try
            {
                manifest = await PluginManifestParser.ParseFileAsync(targetManifestPath, cancellationToken).ConfigureAwait(false);
            }
            catch (PluginManifestException exception)
            {
                return RollbackResult.Fail(pluginId, $"Rollback target manifest is invalid: {exception.Message}");
            }

            try
            {
                await _options.Activator.DeactivateAsync(pluginId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _options.Logger.Warn($"Could not deactivate '{pluginId}' before rollback: {exception.GetType().FullName}: {exception.Message}");
            }

            store.WriteActiveVersion(targetVersion);
            var activation = await ActivateAsync(pluginId, manifest, targetDirectory, cancellationToken).ConfigureAwait(false);
            if (!activation.Success)
            {
                if (!string.IsNullOrWhiteSpace(activeVersion))
                {
                    store.WriteActiveVersion(activeVersion);
                }
                else
                {
                    store.DeleteActivePointer();
                }

                return RollbackResult.Fail(pluginId, $"Rollback activation failed: {activation.Message ?? activation.ErrorCode}");
            }

            var updated = state with
            {
                ActiveVersion = targetVersion,
                PreviousVersion = activeVersion,
                LastGoodVersion = targetVersion,
                UpdatedAt = _options.TimeProvider.GetUtcNow(),
            };
            store.WriteState(updated);
            _options.Logger.Info($"Plugin '{pluginId}' rolled back from {activeVersion} to {targetVersion}.");
            return new RollbackResult(true, pluginId, targetVersion, activeVersion, "Rollback completed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Uninstalls a plugin. Settings/data are retained when <paramref name="keepSettings"/> is true.</summary>
    public async Task<UninstallResult> UninstallAsync(string pluginId, bool keepSettings = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = new PluginStateStore(_options.PluginRootDirectory, pluginId, _options.Logger);
            var state = store.ReadState();
            if (state is null && !Directory.Exists(store.PluginDirectory))
            {
                return new UninstallResult(false, pluginId, [], keepSettings, $"Plugin '{pluginId}' is not installed.");
            }

            try
            {
                await _options.Activator.DeactivateAsync(pluginId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _options.Logger.Warn($"Could not deactivate '{pluginId}' during uninstall: {exception.GetType().FullName}: {exception.Message}");
            }

            var removed = new List<string>();
            var locked = false;
            foreach (var version in store.EnumerateInstalledVersions())
            {
                try
                {
                    store.DeleteVersionDirectory(version);
                    removed.Add(version);
                }
                catch (IOException exception)
                {
                    locked = true;
                    _options.Logger.Warn($"Version directory '{version}' is still locked; restart FluidBar to finish uninstall: {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    locked = true;
                    _options.Logger.Warn($"Version directory '{version}' could not be deleted: {exception.Message}");
                }
            }

            if (!locked)
            {
                if (!keepSettings)
                {
                    DeleteFileIfExists(Path.Combine(store.PluginDirectory, _options.SettingsFileName));
                    var dataDirectory = Path.Combine(store.PluginDirectory, _options.DataDirectoryName);
                    if (Directory.Exists(dataDirectory))
                    {
                        Directory.Delete(dataDirectory, recursive: true);
                    }
                }

                DeleteFileIfExists(store.ActivePointerPath);
                DeleteFileIfExists(store.StatePath);
                if (Directory.Exists(store.PluginDirectory) && !Directory.EnumerateFileSystemEntries(store.PluginDirectory).Any())
                {
                    Directory.Delete(store.PluginDirectory);
                }
            }

            _options.Logger.Info($"Plugin '{pluginId}' uninstalled; removed {removed.Count} version(s), settings retained: {keepSettings}.");
            return new UninstallResult(
                true,
                pluginId,
                removed,
                keepSettings,
                locked
                    ? "Plugin uninstalled but some files are locked by the running process; restart FluidBar to finish cleanup."
                    : "Plugin uninstalled.")
            {
                RestartRequired = locked,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads persisted install state (null when not installed).</summary>
    public PluginInstallState? GetState(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new PluginStateStore(_options.PluginRootDirectory, pluginId, _options.Logger).ReadState();
    }

    /// <summary>Lists installed version directories for a plugin, newest first.</summary>
    public IReadOnlyList<string> GetInstalledVersions(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new PluginStateStore(_options.PluginRootDirectory, pluginId, _options.Logger).EnumerateInstalledVersions();
    }

    /// <summary>Evaluates blocked/yanked/deprecated/compatibility policy for an installed plugin.</summary>
    public PluginPolicyDecision EvaluatePolicy(
        MarketplacePluginEntry entry,
        SemanticVersion appVersion,
        SemanticVersion hostApiVersion)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var latest = entry.Latest;
        var compatible = MarketplaceIndexValidator.IsCompatible(entry, appVersion, hostApiVersion);
        return new PluginPolicyDecision(
            entry.Status,
            entry.Status == PluginListingStatus.Blocked,
            entry.Status == PluginListingStatus.Deprecated,
            latest?.Yanked == true,
            entry.Blocked?.Reason ?? latest?.YankReason,
            entry.Blocked?.Replacement,
            compatible);
    }

    // ---- install pipeline ----------------------------------------------------------------------------------

    private async Task<InstallResult> InstallCoreAsync(InstallRequest request, CancellationToken cancellationToken)
    {
        var stagingId = Guid.NewGuid().ToString("N");
        var stagedPackagePath = Path.Combine(_stagingDirectory, stagingId + ".fbp");
        var extractDirectory = Path.Combine(_stagingDirectory, stagingId + ".extract");
        string pluginId = request.ExpectedPluginId ?? string.Empty;
        var warnings = new List<string>();

        try
        {
            // 1. Download/copy to staging while hashing, bounded.
            var download = await StagePackageAsync(request.Source, stagedPackagePath, cancellationToken).ConfigureAwait(false);
            if (!download.Success)
            {
                return InstallResult.Fail(download.FailureKind, download.Message, pluginId);
            }

            // 2. Verify hash + detached signature BEFORE extraction.
            var verification = await VerifyPackageAsync(request, download.Sha256, download.Size, warnings, cancellationToken).ConfigureAwait(false);
            if (!verification.Verified)
            {
                return InstallResult.Fail(
                    verification.FailureKind,
                    string.Join("; ", verification.Errors),
                    pluginId,
                    warnings);
            }

            // 3. Extract safely (zip-slip/limits/checksums) and parse/validate the manifest.
            FbpExtractionResult extraction;
            try
            {
                extraction = await FbpPackageReader
                    .ExtractFileAsync(stagedPackagePath, extractDirectory, _options.ReaderOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PackageFormatException exception)
            {
                return InstallResult.Fail(InstallFailureKind.ExtractionFailed, exception.Message, pluginId, warnings);
            }

            var manifest = extraction.Manifest;
            pluginId = manifest.Id;
            if (!string.IsNullOrWhiteSpace(request.ExpectedPluginId) &&
                !string.Equals(request.ExpectedPluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                return InstallResult.Fail(
                    InstallFailureKind.InvalidPackage,
                    $"Package is for plugin '{pluginId}' but '{request.ExpectedPluginId}' was expected.",
                    pluginId);
            }

            var marketplaceValidation = PluginManifestValidator.Validate(
                manifest,
                new ManifestValidationOptions
                {
                    RequirePublisherKeyId = _options.RequireMarketplaceSignature,
                    HostApiVersion = _options.HostApiVersion,
                });
            if (!marketplaceValidation.IsValid)
            {
                return InstallResult.Fail(
                    InstallFailureKind.InvalidPackage,
                    $"Manifest is not valid for install: {string.Join("; ", marketplaceValidation.Errors)}",
                    pluginId,
                    warnings);
            }

            // 4. Compatibility gates (same policy as the loader, enforced at install too).
            var compatibility = CheckCompatibility(manifest);
            if (!compatibility.IsCompatible)
            {
                return InstallResult.Fail(InstallFailureKind.Incompatible, compatibility.Reason, pluginId, warnings);
            }

            if (request.ExpectedVersion is { } expected)
            {
                if (!string.Equals(expected.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
                {
                    return InstallResult.Fail(
                        InstallFailureKind.InvalidPackage,
                        $"Package version '{manifest.Version}' does not match index version '{expected.Version}'.",
                        pluginId,
                        warnings);
                }

                if (expected.Capabilities is { } expectedCapabilities)
                {
                    var actual = manifest.Capabilities.GetDeclaredCapabilityNames();
                    var changed = expectedCapabilities
                        .OrderBy(static capability => capability, StringComparer.Ordinal)
                        .SequenceEqual(actual.OrderBy(static capability => capability, StringComparer.Ordinal), StringComparer.Ordinal);
                    if (!changed)
                    {
                        warnings.Add("Capabilities differ from the signed index entry; re-review the consent sheet before installing.");
                    }
                }
            }

            // 5. Update-specific gates.
            var store = new PluginStateStore(_options.PluginRootDirectory, pluginId, _options.Logger);
            var existingState = store.ReadState();
            var previousActiveVersion = store.ReadActiveVersion() ?? existingState?.ActiveVersion;
            if (request.IsUpdate && previousActiveVersion is not null)
            {
                if (!SemanticVersion.TryParse(manifest.Version, out var newVersion) ||
                    !SemanticVersion.TryParse(previousActiveVersion, out var oldVersion))
                {
                    return InstallResult.Fail(InstallFailureKind.Incompatible, "Versions could not be parsed for update.", pluginId);
                }

                if (newVersion <= oldVersion)
                {
                    return InstallResult.Fail(
                        InstallFailureKind.Incompatible,
                        $"Update version {newVersion} must be greater than the active version {oldVersion}.",
                        pluginId);
                }
            }

            // 6. Atomic move into the versioned layout. Never overwrite a loaded version directory.
            var targetDirectory = store.GetVersionDirectory(manifest.Version);
            string? existingHash = null;
            if (existingState?.PackageHashes is { } existingHashes &&
                existingHashes.TryGetValue(manifest.Version, out var recordedHash))
            {
                existingHash = recordedHash;
            }

            if (Directory.Exists(targetDirectory))
            {
                if (existingHash is not null && string.Equals(existingHash, download.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new InstallResult
                    {
                        Success = true,
                        FailureKind = InstallFailureKind.None,
                        PluginId = pluginId,
                        Version = manifest.Version,
                        Message = "Version is already installed with the same package hash.",
                        Consent = CapabilityConsentBuilder.Build(manifest),
                        InstalledDirectory = targetDirectory,
                    };
                }

                return InstallResult.Fail(
                    InstallFailureKind.VersionAlreadyExists,
                    $"Version directory '{manifest.Version}' already exists with a different package; refusing to overwrite a loaded version.",
                    pluginId,
                    warnings);
            }

            try
            {
                var pluginDirectory = Path.GetDirectoryName(targetDirectory);
                if (!string.IsNullOrEmpty(pluginDirectory))
                {
                    Directory.CreateDirectory(pluginDirectory);
                }

                Directory.Move(extractDirectory, targetDirectory);
            }
            catch (IOException exception)
            {
                return InstallResult.Fail(
                    InstallFailureKind.IoError,
                    $"Could not move the staged package into '{targetDirectory}': {exception.Message}",
                    pluginId,
                    warnings);
            }

            // 7. Atomically switch the pointer, then activate. On failure restore the previous version.
            var now = _options.TimeProvider.GetUtcNow();
            var hashes = new Dictionary<string, string>(
                existingState?.PackageHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            {
                [manifest.Version] = download.Sha256,
            };
            store.WriteActiveVersion(manifest.Version);
            var pendingState = (existingState ?? new PluginInstallState
            {
                PluginId = pluginId,
                InstalledAt = now,
            }) with
            {
                ActiveVersion = manifest.Version,
                PreviousVersion = previousActiveVersion,
                PublisherKeyId = manifest.Publisher?.KeyId ?? existingState?.PublisherKeyId,
                UpdatedAt = now,
                RetainedVersions = [.. store.EnumerateInstalledVersions()],
                PackageHashes = hashes,
                BlockedReason = null,
                Disabled = false,
            };
            store.WriteState(pendingState);

            var activation = await ActivateAsync(pluginId, manifest, targetDirectory, cancellationToken).ConfigureAwait(false);
            if (!activation.Success)
            {
                if (!string.IsNullOrWhiteSpace(previousActiveVersion))
                {
                    store.WriteActiveVersion(previousActiveVersion);
                }
                else
                {
                    store.DeleteActivePointer();
                }

                store.WriteState(pendingState with
                {
                    ActiveVersion = previousActiveVersion,
                    Disabled = true,
                });
                TryDeleteDirectory(targetDirectory);
                await TryDeactivateAsync(pluginId, cancellationToken).ConfigureAwait(false);
                return InstallResult.Fail(
                    InstallFailureKind.ActivationFailed,
                    $"Plugin activation failed: {activation.Message ?? activation.ErrorCode}. The previous version remains active.",
                    pluginId,
                    warnings);
            }

            // 8. Commit: last-known-good + prune retained versions.
            var finalState = pendingState with
            {
                ActiveVersion = manifest.Version,
                LastGoodVersion = manifest.Version,
                PreviousVersion = previousActiveVersion,
                UpdatedAt = _options.TimeProvider.GetUtcNow(),
                RetainedVersions = PruneOldVersions(store, manifest.Version),
            };
            store.WriteState(finalState);
            _options.Logger.Info($"Plugin '{pluginId}' v{manifest.Version} installed and activated.");
            return new InstallResult
            {
                Success = true,
                FailureKind = InstallFailureKind.None,
                PluginId = pluginId,
                Version = manifest.Version,
                Message = request.IsUpdate ? "Plugin updated." : "Plugin installed.",
                Warnings = warnings,
                Consent = CapabilityConsentBuilder.Build(manifest),
                InstalledDirectory = targetDirectory,
            };
        }
        finally
        {
            TryDeleteFile(stagedPackagePath);
            TryDeleteDirectory(extractDirectory);
        }
    }

    private async Task<StageResult> StagePackageAsync(IPackageSource source, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > FbpPackageLimits.MaxCompressedBytes)
                {
                    return new StageResult(false, string.Empty, 0, InstallFailureKind.InvalidPackage,
                        $"Package exceeds the {FbpPackageLimits.MaxCompressedBytes} byte limit.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new StageResult(true, sha256, total, InstallFailureKind.None, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new StageResult(false, string.Empty, 0, InstallFailureKind.Cancelled, "Install was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            return new StageResult(false, string.Empty, 0, InstallFailureKind.DownloadFailed, $"Package could not be downloaded: {exception.Message}");
        }
    }

    private async Task<SignatureVerificationOutcome> VerifyPackageAsync(
        InstallRequest request,
        string sha256,
        long size,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var requireSignature = request.Signature is not null || _options.RequireMarketplaceSignature;
        if (request.Signature is null)
        {
            if (!request.AllowUnsigned && _options.RequireMarketplaceSignature)
            {
                return new SignatureVerificationOutcome(false, InstallFailureKind.SignatureInvalid,
                    ["No detached signature was provided and unsigned installs are disabled."]);
            }

            warnings.Add("Unsigned local install: the package was not signed by the marketplace. Only install packages you trust.");
            if (!request.AllowUnsigned)
            {
                return new SignatureVerificationOutcome(false, InstallFailureKind.SignatureInvalid, ["Unsigned install is not allowed."]);
            }

            return new SignatureVerificationOutcome(true, InstallFailureKind.None, []);
        }

        var verificationRequest = new PackageVerificationRequest
        {
            RequireMarketplaceSignature = requireSignature && _options.RequireMarketplaceSignature,
            ExpectedPublisherKeyId = request.ExpectedPublisherKeyId,
            ExpectedSha256 = request.ExpectedVersion?.Artifact?.Sha256,
            ExpectedSize = request.ExpectedVersion?.Artifact?.Size,
        };

        var verification = _signatureVerifier.Verify(sha256, size, request.Signature, verificationRequest);
        if (verification.Verified)
        {
            return new SignatureVerificationOutcome(true, InstallFailureKind.None, []);
        }

        var failureKind = verification.Errors.Any(static error => error.Contains("hash", StringComparison.OrdinalIgnoreCase))
            ? InstallFailureKind.HashMismatch
            : InstallFailureKind.SignatureInvalid;
        _options.Logger.Error($"Signature verification failed for '{request.Source.Description}': {string.Join("; ", verification.Errors)}");
        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new SignatureVerificationOutcome(false, failureKind, errors.Concat(verification.Errors).ToArray());
    }

    private VersionCompatibility CheckCompatibility(PluginManifest manifest)
    {
        if (!manifest.TryGetApiVersion(out var apiVersion) ||
            !manifest.TryGetMinAppVersion(out var minAppVersion) ||
            !manifest.TryGetMaxAppVersion(out var maxAppVersion))
        {
            return new VersionCompatibility(false, VersionCompatibilityFailure.InvalidVersion, "Manifest versions could not be parsed.");
        }

        var compatibility = PluginApiVersion.CheckCompatibility(
            apiVersion,
            minAppVersion,
            maxAppVersion,
            _options.HostApiVersion,
            _options.AppVersion);
        if (!compatibility.IsCompatible)
        {
            return compatibility;
        }

        return manifest.Dependencies?.Sdk is { } sdkRange
            ? PluginApiVersion.CheckSdkDependency(sdkRange, _options.HostApiVersion)
            : VersionCompatibility.Compatible;
    }

    private async Task<ActivationResult> ActivateAsync(string pluginId, PluginManifest manifest, string versionDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await _options.Activator
                .ActivateAsync(manifest, versionDirectory, cancellationToken)
                .WaitAsync(_options.ActivationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ActivationResult.Fail("activation.timeout", $"Activation exceeded the {_options.ActivationTimeout} timeout.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ActivationResult.Fail("activation.cancelled", "Activation was cancelled.");
        }
        catch (Exception exception)
        {
            return ActivationResult.Fail("activation.failed", $"{exception.GetType().FullName}: {exception.Message}");
        }
    }

    private async Task TryDeactivateAsync(string pluginId, CancellationToken cancellationToken)
    {
        try
        {
            await _options.Activator.DeactivateAsync(pluginId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _options.Logger.Warn($"Could not deactivate '{pluginId}': {exception.GetType().FullName}: {exception.Message}");
        }
    }

    private List<string> PruneOldVersions(PluginStateStore store, string activeVersion)
    {
        var installed = store.EnumerateInstalledVersions();
        var retained = new List<string> { activeVersion };
        var keep = Math.Max(1, _options.RetainedVersions) - 1;
        foreach (var version in installed)
        {
            if (retained.Count - 1 >= keep)
            {
                break;
            }

            if (!string.Equals(version, activeVersion, StringComparison.OrdinalIgnoreCase))
            {
                retained.Add(version);
            }
        }

        foreach (var version in installed)
        {
            if (retained.Contains(version, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                store.DeleteVersionDirectory(version);
                _options.Logger.Info($"Pruned old plugin version '{version}'.");
            }
            catch (IOException exception)
            {
                _options.Logger.Warn($"Could not prune version '{version}' (still locked): {exception.Message}");
            }
            catch (UnauthorizedAccessException exception)
            {
                _options.Logger.Warn($"Could not prune version '{version}': {exception.Message}");
            }
        }

        return retained;
    }

    private void CleanupStaleStaging()
    {
        try
        {
            if (!Directory.Exists(_stagingDirectory))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var path in Directory.EnumerateFileSystemEntries(_stagingDirectory))
            {
                try
                {
                    var created = File.GetCreationTimeUtc(path);
                    if (now - created < StaleStagingAge)
                    {
                        continue;
                    }

                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException exception)
                {
                    _options.Logger.Warn($"Could not clean stale staging entry '{path}': {exception.Message}");
                }
                catch (UnauthorizedAccessException exception)
                {
                    _options.Logger.Warn($"Could not clean stale staging entry '{path}': {exception.Message}");
                }
            }
        }
        catch (IOException exception)
        {
            _options.Logger.Warn($"Could not enumerate the staging directory: {exception.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort staging cleanup; the install result already carries the real outcome.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort staging cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort staging cleanup; a locked dir is retried by the uninstall/update path.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort staging cleanup.
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Releases the install gate (the service owns a <see cref="SemaphoreSlim"/>).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private readonly record struct StageResult(bool Success, string Sha256, long Size, InstallFailureKind FailureKind, string Message);

    private readonly record struct SignatureVerificationOutcome(bool Verified, InstallFailureKind FailureKind, IReadOnlyList<string> Errors);
}
