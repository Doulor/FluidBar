using System.Globalization;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Telemetry;
using FluidBar.Platform.Windows.Telemetry;

namespace FluidBar.App.Hosting;

/// <summary>
/// Owns the anonymous-usage-data pipeline: one session marker per run, a bounded local queue, and best-effort
/// uploads to the FluidBar Worker.
/// </summary>
/// <remarks>
/// What leaves the machine is fixed by <see cref="TelemetryPayload"/> — launch count, session length, startup time,
/// app/OS version, architecture and UI language. No song title, lyric, notification text, window title, file path or
/// plugin content is ever placed in a batch. Turning the setting off calls <see cref="DiscardPending"/>, so the
/// pending data disappears instead of being sent later.
/// </remarks>
internal sealed class TelemetryService : ITelemetryControls, IDisposable
{
    private const string QueueFileName = "queue.tsv";
    private const string InstallIdFileName = "install-id";
    private const string SessionMarkerName = "session.tsv";

    private readonly string _directory;
    private readonly TelemetryQueue _queue;
    private readonly HttpTelemetrySink _sink;
    private readonly TelemetryEnvironment _environment;
    private readonly IAppLogger _logger;
    private readonly Func<DateTimeOffset> _clock;
    private string _installId;
    private DateTimeOffset _startedAt;
    private int _startupMs;
    private int _stopped;
    private int _flushing;

    /// <summary>Creates the service (no I/O beyond reading the install id).</summary>
    /// <param name="appDataRoot">The app data root (<c>%APPDATA%\FluidBar</c>).</param>
    /// <param name="appVersion">Version stamp of the running build.</param>
    /// <param name="selfContained">True when the build ships its own runtime.</param>
    /// <param name="locale">UI language in effect.</param>
    /// <param name="logger">Diagnostics logger.</param>
    /// <param name="sink">Upload transport (tests inject a fake).</param>
    /// <param name="clock">Clock (tests inject a fake).</param>
    public TelemetryService(
        string appDataRoot,
        string appVersion,
        bool selfContained,
        string locale,
        IAppLogger logger,
        HttpTelemetrySink? sink = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _directory = Path.Combine(appDataRoot, "telemetry");
        _queue = new TelemetryQueue(Path.Combine(_directory, QueueFileName));
        _sink = sink ?? new HttpTelemetrySink();
        _environment = WindowsTelemetryEnvironment.Create(appVersion, selfContained, locale);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _installId = TelemetryInstallId.GetOrCreate(Path.Combine(_directory, InstallIdFileName));
    }

    /// <summary>Anonymous install identifier currently in use.</summary>
    public string InstallId => _installId;

    /// <summary>Endpoint batches are posted to.</summary>
    public Uri Endpoint => _sink.Endpoint;

    /// <summary>Runs waiting for the next upload.</summary>
    public int PendingRuns => _queue.Count;

    /// <summary>Starts a session: adopts the marker of a previous unclean run, then writes this run's marker.</summary>
    public void Begin()
    {
        AdoptCrashedSession();
        _startedAt = _clock();
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(
                Path.Combine(_directory, SessionMarkerName),
                _startedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            // A missing marker only costs the crash count for this run.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _logger.Information(LogCategories.Lifecycle, "telemetry.session-begin",
            "Anonymous usage data session started.", data: new Dictionary<string, object?>
            {
                ["pending"] = PendingRuns,
                ["version"] = _environment.AppVersion,
                ["channel"] = _environment.Channel,
            });
    }

    /// <summary>Records how long the app took to become usable (called when the island is up).</summary>
    public void MarkReady()
    {
        if (_startedAt == default)
        {
            return;
        }

        var elapsed = (_clock() - _startedAt).TotalMilliseconds;
        _startupMs = (int)Math.Clamp(elapsed, 0, 120_000);
    }

    /// <summary>Attempts to upload everything queued, without blocking the caller for long.</summary>
    /// <param name="cancellationToken">Cancellation for shutdown paths.</param>
    /// <returns>True when the queue is empty afterwards.</returns>
    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _flushing, 1) != 0)
        {
            return false;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = _queue.TakeBatch();
                if (batch.Count == 0)
                {
                    return true;
                }

                var payload = TelemetryPayload.Serialize(_installId, _environment, batch, _clock());
                var result = await _sink.SendAsync(payload, _installId, _environment.AppVersion, cancellationToken)
                    .ConfigureAwait(false);
                if (result.Status == TelemetrySendStatus.Sent)
                {
                    continue;
                }

                if (result.ShouldRetry)
                {
                    // Put the batch back so the next launch retries it; the queue keeps the newest runs only.
                    _queue.EnqueueRange(batch);
                    _logger.Debug(LogCategories.Lifecycle, "telemetry.upload-deferred",
                        "Anonymous usage data could not be uploaded; it will be retried.", data: new Dictionary<string, object?>
                        {
                            ["status"] = result.Status.ToString(),
                            ["code"] = result.StatusCode,
                            ["detail"] = result.Detail,
                        });
                    return false;
                }

                // Rejected or endpoint not configured: dropping the batch is the honest outcome.
                _logger.Warning(LogCategories.Lifecycle, "telemetry.upload-rejected",
                    "Anonymous usage data was rejected and dropped.", data: new Dictionary<string, object?>
                    {
                        ["status"] = result.Status.ToString(),
                        ["code"] = result.StatusCode,
                        ["detail"] = result.Detail,
                    });
                return _queue.Count == 0;
            }

            return false;
        }
        finally
        {
            Volatile.Write(ref _flushing, 0);
        }
    }

    /// <summary>Ends the session, enqueues it and tries one last upload.</summary>
    /// <param name="reason">Why the session ended.</param>
    /// <param name="timeout">Hard ceiling; shutdown must never hang on the network.</param>
    /// <param name="cancellationToken">Cancellation for the upload.</param>
    public async Task StopAsync(
        TelemetryStopReason reason = TelemetryStopReason.User,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        if (_startedAt != default)
        {
            _queue.Enqueue(new TelemetryRun
            {
                InstallId = _installId,
                StartedAt = _startedAt,
                StoppedAt = _clock(),
                StartupMs = _startupMs,
                FirstRun = false,
                Reason = reason,
            });
        }

        TryDeleteMarker();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        try
        {
            await FlushAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown deadline reached; the queue survives to the next launch.
        }
    }

    /// <summary>Writes exactly what would be uploaded next to a readable file and returns its path.</summary>
    /// <returns>Absolute path of the preview file.</returns>
    public string WritePreview()
    {
        var runs = _queue.TakeBatch();
        var payload = TelemetryPayload.Serialize(_installId, _environment, runs, _clock());
        _queue.EnqueueRange(runs);
        var path = Path.Combine(Path.GetTempPath(), "fluidbar-telemetry-preview.json");
        File.WriteAllBytes(path, payload);
        return path;
    }

    /// <summary>Forgets the anonymous identifier and drops everything queued.</summary>
    public void ResetInstallId()
    {
        TelemetryInstallId.Reset(Path.Combine(_directory, InstallIdFileName));
        _installId = TelemetryInstallId.GetOrCreate(Path.Combine(_directory, InstallIdFileName));
        _queue.Clear();
        _logger.Information(LogCategories.Lifecycle, "telemetry.install-id-reset",
            "The anonymous install identifier was reset and pending data dropped.");
    }

    /// <summary>Drops everything queued (setting switched off).</summary>
    public void DiscardPending()
    {
        _queue.Clear();
        TryDeleteMarker();
        _logger.Information(LogCategories.Lifecycle, "telemetry.pending-discarded",
            "Pending anonymous usage data was discarded.");
    }

    /// <summary>Records a run that never reached <see cref="StopAsync"/> (previous process died).</summary>
    private void AdoptCrashedSession()
    {
        var marker = Path.Combine(_directory, SessionMarkerName);
        try
        {
            if (!File.Exists(marker))
            {
                return;
            }

            var text = File.ReadAllText(marker).Trim();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started))
            {
                var stopped = _clock();
                if (stopped > started)
                {
                    _queue.Enqueue(new TelemetryRun
                    {
                        InstallId = _installId,
                        StartedAt = started,
                        StoppedAt = stopped,
                        StartupMs = 0,
                        FirstRun = false,
                        Reason = TelemetryStopReason.Crash,
                    });
                }
            }

            File.Delete(marker);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void TryDeleteMarker()
    {
        try
        {
            var marker = Path.Combine(_directory, SessionMarkerName);
            if (File.Exists(marker))
            {
                File.Delete(marker);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_startedAt == default)
            {
                Interlocked.Exchange(ref _stopped, 0);
                Begin();
            }

            _ = Task.Run(() => FlushAsync());
            return;
        }

        DiscardPending();
        Interlocked.Exchange(ref _stopped, 1);
        _startedAt = default;
    }

    /// <inheritdoc />
    public void Dispose() => _sink.Dispose();
}
