using System.Text;
using FluidBar.App.Localization;
using FluidBar.App.Views.Market;
using FluidBar.Marketplace.Install;
using FluidBar.Marketplace.Index;
using FluidBar.Marketplace.Signing;
using FluidBar.PluginHost.Loader;
using FluidBar.Sdk.Versioning;
using PluginHostType = FluidBar.PluginHost.Loader.PluginHost;

namespace FluidBar.App.Plugins;

/// <summary>Raised when a marketplace operation fails; carries the stable install failure kind for the UI.</summary>
public sealed class PluginInstallException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PluginInstallException(InstallFailureKind failureKind, string message)
        : base(message)
        => FailureKind = failureKind;

    /// <summary>Stable failure class from the install state machine.</summary>
    public InstallFailureKind FailureKind { get; }
}

/// <summary>
/// Production <c>IPluginInstallOperations</c>: resolves the signed artifact + detached signature from the
/// index entry, installs through <see cref="PluginInstallService"/> (hash/signature verification, versioned
/// directory, pointer switch, activation) and forwards the real phase progress to the UI.
/// </summary>
public sealed class PluginInstallOperations : IPluginInstallOperations
{
    private const int MaxSignatureBytes = 1024 * 1024;

    private readonly PluginInstallService _installs;
    private readonly PluginHostType _host;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly TimeSpan _timeout;

    /// <summary>Creates the operations adapter.</summary>
    public PluginInstallOperations(
        PluginInstallService installs,
        PluginHostType host,
        HttpClient http,
        TimeProvider? timeProvider = null,
        TimeSpan? timeout = null)
    {
        _installs = installs ?? throw new ArgumentNullException(nameof(installs));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = timeProvider ?? TimeProvider.System;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
    }

    /// <inheritdoc />
    public string? GetInstalledVersion(string pluginId)
        => _installs.GetState(pluginId)?.ActiveVersion ?? _host.Find(pluginId)?.Manifest.Version;

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledVersions(string pluginId) => _installs.GetInstalledVersions(pluginId);

    /// <inheritdoc />
    public bool IsPluginEnabled(string pluginId) => _host.Find(pluginId)?.State == PluginLifecycleState.Active;

    /// <inheritdoc />
    public async Task InstallAsync(
        MarketplacePluginEntry entry,
        MarketplaceVersionInfo version,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(progress);

        if (entry.Status == PluginListingStatus.Blocked)
        {
            throw new PluginInstallException(
                InstallFailureKind.Blocked,
                entry.Blocked?.Reason ?? $"Plugin '{entry.Id}' is blocked by the marketplace.");
        }

        if (version.Yanked)
        {
            throw new PluginInstallException(
                InstallFailureKind.Yanked,
                version.YankReason ?? $"Version '{version.Version}' was withdrawn.");
        }

        var artifact = version.Artifact
            ?? throw new PluginInstallException(InstallFailureKind.InvalidPackage, $"Version '{version.Version}' has no artifact.");
        if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PluginInstallException(InstallFailureKind.InvalidPackage, "The package URL is not an absolute HTTPS URL.");
        }

        var signature = await DownloadSignatureAsync(artifact.SignatureUrl, cancellationToken).ConfigureAwait(false);
        var state = _installs.GetState(entry.Id);

        var request = new InstallRequest
        {
            Source = new HttpPackageSource(_http, packageUri),
            Signature = signature,
            ExpectedVersion = version,
            ExpectedPublisherKeyId = state?.PublisherKeyId,
            ExpectedPluginId = entry.Id,
            IsUpdate = state?.ActiveVersion is not null,
            Progress = new PhaseProgress(progress),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        InstallResult result;
        try
        {
            result = await _installs.InstallAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PluginInstallException(InstallFailureKind.Cancelled, $"Install exceeded the {_timeout.TotalSeconds:0} s timeout.");
        }

        if (!result.Success)
        {
            throw new PluginInstallException(result.FailureKind, result.Message);
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(
        string pluginId,
        string version,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var result = await _installs
            .RollbackAsync(pluginId, version, cancellationToken, new PhaseProgress(progress))
            .ConfigureAwait(false);
        if (!result.Success)
        {
            throw new PluginInstallException(InstallFailureKind.RollbackFailed, result.Message);
        }
    }

    /// <inheritdoc />
    public async Task UninstallAsync(string pluginId, bool keepSettings, CancellationToken cancellationToken)
    {
        var result = await _installs.UninstallAsync(pluginId, keepSettings, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new PluginInstallException(InstallFailureKind.NotInstalled, result.Message);
        }
    }

    private async Task<PackageSignatureDocument> DownloadSignatureAsync(string signatureUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(signatureUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PluginInstallException(InstallFailureKind.SignatureInvalid, "The signature URL is not an absolute HTTPS URL.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PluginInstallException(
                    InstallFailureKind.SignatureInvalid,
                    $"Signature download failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxSignatureBytes)
                {
                    throw new PluginInstallException(InstallFailureKind.SignatureInvalid, "The signature document is oversized.");
                }

                memory.Write(buffer, 0, read);
            }

            return PackageSignatureDocument.Parse(Encoding.UTF8.GetString(memory.ToArray()));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PluginInstallException(InstallFailureKind.DownloadFailed, "The signature download timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new PluginInstallException(InstallFailureKind.DownloadFailed, exception.Message);
        }
        catch (PackageSignatureException exception)
        {
            throw new PluginInstallException(InstallFailureKind.SignatureInvalid, exception.Message);
        }
    }

    /// <summary>Maps the state-machine phase onto the UI's localized stage rows (no fake intermediate states).</summary>
    private sealed class PhaseProgress(IProgress<InstallProgress> inner) : IProgress<InstallPhase>
    {
        public void Report(InstallPhase value)
        {
            var (token, key, percent) = value switch
            {
                InstallPhase.Downloading => ("download", LocKeys.Market.Stage.Download, 10),
                InstallPhase.Verifying => ("verify", LocKeys.Market.Stage.Verify, 35),
                InstallPhase.Extracting => ("extract", LocKeys.Market.Stage.Extract, 60),
                InstallPhase.Staged => ("staged", LocKeys.Market.Stage.Prepare, 75),
                InstallPhase.Activating => ("activate", LocKeys.Market.Stage.Activate, 90),
                InstallPhase.Active => ("active", LocKeys.Market.Stage.Activate, 100),
                _ => ("available", LocKeys.Market.Stage.Prepare, 0),
            };
            inner.Report(new InstallProgress(token, key, percent));
        }
    }
}
