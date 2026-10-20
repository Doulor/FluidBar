using System.Net;
using System.Net.Http.Headers;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Update;

/// <summary>
/// Async update-check client: one injected <see cref="HttpClient"/>, conditional GET (ETag), semantic
/// version comparison, explicit-only installer download with mandatory SHA-256 + Authenticode
/// verification before an install action is exposed. It never executes the downloaded binary.
/// </summary>
/// <remarks>
/// State is published as an immutable <see cref="UpdateStatusSnapshot"/> (settings About page and
/// Core <c>update:available/downloading/ready</c> activity). Checks/downloads are single-flight and
/// cancellable; HTTP responses are size-capped; failures degrade quietly (logged + counted).
/// </remarks>
public sealed class UpdateCheckClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly UpdateClientOptions _options;
    private readonly Version _currentVersion;
    private readonly IUpdatePackageVerifier _verifier;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly SnapshotPublisher<UpdateStatusSnapshot> _publisher;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    private string? _lastETag;
    private string? _downloadedFilePath;
    private string? _downloadedInstallerTag;
    private UpdateStatusSnapshot? _currentSnapshot;
    private int _disposed;

    public UpdateCheckClient(HttpClient httpClient, UpdateClientOptions options, Version currentVersion,
        IUpdatePackageVerifier verifier, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _publisher = new SnapshotPublisher<UpdateStatusSnapshot>("update", _logger, _counters);
    }

    /// <summary>Raised (thread pool) whenever the update state changes.</summary>
    public event EventHandler<SnapshotEventArgs<UpdateStatusSnapshot>>? Changed
    {
        add => _publisher.Changed += value;
        remove => _publisher.Changed -= value;
    }

    /// <summary>
    /// Latest state (settings About page reads this). Updated synchronously by every publish so a
    /// caller that awaited a check/download never observes an older state while the coalescing
    /// publisher has not dispatched yet.
    /// </summary>
    public UpdateStatusSnapshot Current
        => Volatile.Read(ref _currentSnapshot) ?? UpdateStatusSnapshot.Idle(_currentVersion);

    /// <summary>
    /// Creates the one shared <see cref="HttpClient"/> for the app. Do not create one per check:
    /// socket exhaustion and stale DNS are the classic bugs. The caller disposes it.
    /// </summary>
    public static HttpClient CreateHttpClient(UpdateClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = new HttpClient
        {
            // Per-request timeouts are applied with a linked CancellationTokenSource.
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestHeaders =
            {
                UserAgent = { ProductInfoHeaderValue.Parse(SanitizeProduct(options.UserAgent)) },
                Accept = { new MediaTypeWithQualityHeaderValue("application/vnd.github+json") },
            },
        };
        return client;
    }

    /// <summary>Checks the release feed. Returns an explicit result; never throws for network/parse errors.</summary>
    public async Task<UpdateCheckResult> CheckAsync(UpdateCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);

        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return UpdateCheckResult.Failed("An update check is already running.");
        }

        try
        {
            Publish(new UpdateStatusSnapshot(UpdateState.Checking, _currentVersion, Current.AvailableRelease, null,
                UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, null));

            if (_options.ReleaseFeedUrl.IsFile)
            {
                // Local folder feed (dev/offline verification): a real JSON manifest plus real asset
                // files on disk. No HTTP, no ETag — the manifest is re-read on every check.
                return await CheckFileFeedAsync(request, cancellationToken).ConfigureAwait(false);
            }

            var etag = request.ETag ?? _lastETag;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, _options.ReleaseFeedUrl);
            if (!string.IsNullOrWhiteSpace(etag))
            {
                httpRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            var responseEtag = response.Headers.ETag?.Tag;
            if (responseEtag is not null)
            {
                _lastETag = responseEtag;
            }

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                var now = _timeProvider.GetUtcNow();
                Publish(new UpdateStatusSnapshot(UpdateState.UpToDate, _currentVersion, null, null,
                    UpdateVerificationStatus.NotVerified, now, null));
                return new UpdateCheckResult(UpdateCheckStatus.NotModified, null, _lastETag);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"Update feed returned HTTP {(int)response.StatusCode}.", _lastETag);
            }

            var json = await ReadCappedStringAsync(response, _options.MaxManifestBytes, timeout.Token)
                .ConfigureAwait(false);
            if (json is null)
            {
                return Fail("Update manifest exceeded the size limit.", _lastETag);
            }

            var release = ReleaseManifestParser.SelectRelease(json, request.Channel,
                _options.InstallerAssetPattern);
            if (release is null)
            {
                return Fail("No usable release found in the update feed.", _lastETag);
            }

            var now2 = _timeProvider.GetUtcNow();
            if (!release.Version.IsNewerThan(request.CurrentVersion))
            {
                Publish(new UpdateStatusSnapshot(UpdateState.UpToDate, _currentVersion, release, null,
                    UpdateVerificationStatus.NotVerified, now2, null));
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, release, _lastETag);
            }

            Publish(new UpdateStatusSnapshot(UpdateState.Available, _currentVersion, release, null,
                UpdateVerificationStatus.NotVerified, now2, null));
            _counters.IncrementUpdateChecks();
            _logger.LogInfo(PlatformLogCategory.Update, "update.available",
                "A newer release is available.",
                new Dictionary<string, object?>
                {
                    ["current"] = _currentVersion.ToString(),
                    ["latest"] = release.Version.ToString(),
                    ["channel"] = request.Channel.ToString(),
                });
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, _lastETag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Fail("Update check timed out.", _lastETag);
        }
        catch (HttpRequestException ex)
        {
            return Fail("Update check network error.", _lastETag, ex);
        }
        catch (Exception ex)
        {
            return Fail("Update check failed.", _lastETag, ex);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    /// <summary>
    /// Reads a local <c>file://</c> release feed: a real JSON manifest plus real asset files on disk.
    /// Used for offline/dev verification of the whole check → download → verify chain without HTTP.
    /// </summary>
    private async Task<UpdateCheckResult> CheckFileFeedAsync(UpdateCheckRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = ToLocalPath(_options.ReleaseFeedUrl);
            if (!File.Exists(path))
            {
                return Fail($"Update feed '{path}' does not exist.", null);
            }

            if (new FileInfo(path).Length > _options.MaxManifestBytes)
            {
                return Fail("Update manifest exceeded the size limit.", null);
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var release = ReleaseManifestParser.SelectRelease(json, request.Channel,
                _options.InstallerAssetPattern);
            if (release is null)
            {
                return Fail("No usable release found in the update feed.", null);
            }

            var now = _timeProvider.GetUtcNow();
            if (!release.Version.IsNewerThan(request.CurrentVersion))
            {
                Publish(new UpdateStatusSnapshot(UpdateState.UpToDate, _currentVersion, release, null,
                    UpdateVerificationStatus.NotVerified, now, null));
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, release, null);
            }

            Publish(new UpdateStatusSnapshot(UpdateState.Available, _currentVersion, release, null,
                UpdateVerificationStatus.NotVerified, now, null));
            _counters.IncrementUpdateChecks();
            _logger.LogInfo(PlatformLogCategory.Update, "update.available",
                "A newer release is available.",
                new Dictionary<string, object?>
                {
                    ["current"] = _currentVersion.ToString(),
                    ["latest"] = release.Version.ToString(),
                    ["channel"] = request.Channel.ToString(),
                    ["feed"] = path,
                });
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fail("Update check failed.", null, ex);
        }
    }

    /// <summary>Converts a <c>file://</c> URI into a local filesystem path.</summary>
    private static string ToLocalPath(Uri uri) => Uri.UnescapeDataString(uri.LocalPath);

    /// <summary>
    /// Downloads the installer to <see cref="UpdateClientOptions.DownloadDirectory"/> and verifies it.
    /// Must be called only from an explicit user action; the result's <see cref="UpdateDownloadResult.CanInstall"/>
    /// gates the install button.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadInstallerAsync(UpdateRelease release,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(release);

        if (release.Installer is null)
        {
            return DownloadFailure("The release has no installer asset.");
        }

        if (release.Installer.SizeBytes > _options.MaxInstallerBytes)
        {
            return DownloadFailure("The installer exceeds the configured size limit.");
        }

        if (IsCachedInstallerValid(release) && _downloadedFilePath is not null)
        {
            return new UpdateDownloadResult(true, _downloadedFilePath, UpdateVerificationStatus.Verified,
                release.InstallerSha256Hex);
        }

        if (!await _downloadGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return DownloadFailure("A download is already running.");
        }

        string? temporaryFilePath = null;
        try
        {
            Publish(new UpdateStatusSnapshot(UpdateState.Downloading, _currentVersion, release, null,
                UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, null));

            var directory = _options.DownloadDirectory;
            await Task.Run(() => Directory.CreateDirectory(directory), cancellationToken).ConfigureAwait(false);

            var installerName = SanitizeFileName(release.Installer.Name);
            var finalPath = Path.Combine(directory, installerName);
            temporaryFilePath = finalPath + ".part";

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(10)); // large installer; still bounded

            long received;
            if (release.Installer.Url.IsFile)
            {
                // Local asset (offline verification path): a real file copied through the same
                // .part → verify → move pipeline as an HTTP download.
                received = await CopyLocalAssetAsync(release.Installer.Url, temporaryFilePath,
                    _options.MaxInstallerBytes, progress, timeout.Token).ConfigureAwait(false);
            }
            else
            {
                using var response = await _httpClient.GetAsync(release.Installer.Url,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return DownloadFailure($"Installer download returned HTTP {(int)response.StatusCode}.");
                }

                var declaredLength = response.Content.Headers.ContentLength;
                if (declaredLength is > 0 && declaredLength > _options.MaxInstallerBytes)
                {
                    return DownloadFailure("Installer exceeds the configured size limit.");
                }

                received = await CopyToFileAsync(response, temporaryFilePath, declaredLength ?? 0, progress,
                    timeout.Token).ConfigureAwait(false);
            }

            var verification = await _verifier.VerifyAsync(temporaryFilePath, release, cancellationToken)
                .ConfigureAwait(false);
            if (verification != UpdateVerificationStatus.Verified)
            {
                _counters.IncrementUpdateCheckFailures();
                _logger.LogWarning(PlatformLogCategory.Update, "update.verification-failed",
                    "Downloaded installer failed verification; it will not be offered for install.",
                    data: new Dictionary<string, object?>
                    {
                        ["release"] = release.Version.ToString(),
                        ["verification"] = verification.ToString(),
                    });
                DeleteFileQuietly(temporaryFilePath);
                Publish(new UpdateStatusSnapshot(UpdateState.Failed, _currentVersion, release, null, verification,
                    Current.LastCheckedAtUtc, $"Installer verification failed ({verification}).",
                    UpdateFailureKind.Verification));
                return new UpdateDownloadResult(false, null, verification, null,
                    $"Installer verification failed ({verification}).");
            }

            await Task.Run(() => File.Move(temporaryFilePath, finalPath, overwrite: true), cancellationToken)
                .ConfigureAwait(false);
            temporaryFilePath = null;
            _downloadedFilePath = finalPath;
            _downloadedInstallerTag = release.Tag;

            Publish(new UpdateStatusSnapshot(UpdateState.ReadyToInstall, _currentVersion, release, finalPath,
                UpdateVerificationStatus.Verified, Current.LastCheckedAtUtc, null));
            _logger.LogInfo(PlatformLogCategory.Update, "update.ready",
                "Verified installer downloaded and ready for the explicit install action.",
                new Dictionary<string, object?>
                {
                    ["release"] = release.Version.ToString(),
                    ["bytes"] = received,
                });

            return new UpdateDownloadResult(true, finalPath, UpdateVerificationStatus.Verified,
                release.InstallerSha256Hex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return DownloadFailure("Installer download timed out.");
        }
        catch (HttpRequestException ex)
        {
            return DownloadFailure("Installer download network error.", ex);
        }
        catch (Exception ex)
        {
            return DownloadFailure("Installer download failed.", ex);
        }
        finally
        {
            if (temporaryFilePath is not null)
            {
                DeleteFileQuietly(temporaryFilePath);
            }

            _downloadGate.Release();
        }
    }

    /// <summary>Forgets the cached download (e.g. after the user discards the update) and resets the state.</summary>
    public void ClearDownloadedInstaller()
    {
        if (_downloadedFilePath is not null)
        {
            DeleteFileQuietly(_downloadedFilePath);
        }

        _downloadedFilePath = null;
        _downloadedInstallerTag = null;
        Publish(new UpdateStatusSnapshot(UpdateState.Idle, _currentVersion, null, null,
            UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, null));
    }

    private bool IsCachedInstallerValid(UpdateRelease release)
        => _downloadedFilePath is not null
           && string.Equals(_downloadedInstallerTag, release.Tag, StringComparison.Ordinal)
           && File.Exists(_downloadedFilePath);

    /// <summary>Copies a local <c>file://</c> asset into the download directory (bounded, cancellable).</summary>
    private static async Task<long> CopyLocalAssetAsync(Uri source, string filePath, long maxBytes,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var sourcePath = ToLocalPath(source);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("Installer asset does not exist.", sourcePath);
        }

        if (sourceInfo.Length > maxBytes)
        {
            throw new InvalidOperationException("Installer exceeds the configured size limit.");
        }

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var declaredLength = sourceInfo.Length;
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (declaredLength > 0)
            {
                progress?.Report(Math.Clamp(total / (double)declaredLength, 0, 1));
            }
        }

        return total;
    }

    private async Task<long> CopyToFileAsync(HttpResponseMessage response, string filePath, long declaredLength,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > _options.MaxInstallerBytes)
            {
                throw new InvalidOperationException("Installer exceeded the configured size limit while downloading.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (declaredLength > 0)
            {
                progress?.Report(Math.Clamp(total / (double)declaredLength, 0, 1));
            }
        }

        return total;
    }

    private static async Task<string?> ReadCappedStringAsync(HttpResponseMessage response, int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private UpdateCheckResult Fail(string reason, string? etag, Exception? exception = null)
    {
        _counters.IncrementUpdateCheckFailures();
        _logger.LogWarning(PlatformLogCategory.Update, "update.check-failed", reason, exception);
        Publish(new UpdateStatusSnapshot(UpdateState.Failed, _currentVersion, Current.AvailableRelease, null,
            UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, reason, UpdateFailureKind.Check));
        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, etag, reason);
    }

    private UpdateDownloadResult DownloadFailure(string reason, Exception? exception = null)
    {
        _counters.IncrementUpdateCheckFailures();
        _logger.LogWarning(PlatformLogCategory.Update, "update.download-failed", reason, exception);
        Publish(new UpdateStatusSnapshot(UpdateState.Failed, _currentVersion, Current.AvailableRelease, null,
            UpdateVerificationStatus.NotVerified, Current.LastCheckedAtUtc, reason, UpdateFailureKind.Download));
        return new UpdateDownloadResult(false, null, UpdateVerificationStatus.NotVerified, null, reason);
    }

    private void Publish(UpdateStatusSnapshot snapshot)
    {
        Volatile.Write(ref _currentSnapshot, snapshot);
        _publisher.Publish(snapshot);
    }

    private static void DeleteFileQuietly(string path)
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
            // Locked by a scanner; the temp sweep will remove it later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "FluidBar-setup.exe" : sanitized;
    }

    private static string SanitizeProduct(string userAgent)
    {
        var sanitized = new string(userAgent.Where(c => c is > ' ' and <= '~').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "FluidBar-UpdateCheck" : sanitized;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _publisher.Dispose();
        _checkGate.Dispose();
        _downloadGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
