using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>Configuration for <see cref="RollingFileLogger"/>.</summary>
public sealed class RollingLogOptions
{
    /// <summary>Directory that receives the JSONL files (typically <c>%APPDATA%\FluidBar\logs</c>).</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>File name prefix; files are named <c>{prefix}-yyyyMMdd-HHmmss-fff-{seq}.jsonl</c>.</summary>
    public string FilePrefix { get; init; } = "fluidbar";

    /// <summary>Rotate to a new file once the current one would exceed this many bytes.</summary>
    public long MaxFileBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Maximum number of log files kept on disk (oldest deleted first).</summary>
    public int MaxFiles { get; init; } = 5;

    /// <summary>Files older than this are deleted even when fewer than <see cref="MaxFiles"/> remain.</summary>
    public int RetentionDays { get; init; } = 7;

    /// <summary>Per-category, per-second entry limit. Error and above bypass the limit.</summary>
    public int RateLimitPerCategoryPerSecond { get; init; } = 20;

    /// <summary>Bounded in-memory queue. When full, non-error entries are dropped and counted.</summary>
    public int ChannelCapacity { get; init; } = 4096;

    /// <summary>Entries below this level are discarded by <see cref="RollingFileLogger.IsEnabled"/>.</summary>
    public PlatformLogLevel MinimumLevel { get; init; } = PlatformLogLevel.Debug;

    /// <summary>Entry size guard; oversized entries are truncated rather than dropped.</summary>
    public int MaxEntryBytes { get; init; } = 8 * 1024;
}

/// <summary>
/// Structured JSON Lines log sink with bounded queue, bounded files, rotation and per-category
/// rate limiting (spec §9.3: <c>ts, level, cat, evtId, message, data</c>; 20/s/category; Error never lost).
/// </summary>
/// <remarks>
/// <para>Threading: <see cref="Log"/> never blocks on disk I/O. A single background writer drains the
/// channel. Only <see cref="StartAsync"/> and <see cref="StopAsync"/> touch the filesystem on the
/// caller's thread for directory creation / final flush; production starts the sink from the app
/// bootstrap thread and never logs from a hot path.</para>
/// <para>Disposal: <see cref="DisposeAsync"/> is idempotent, completes the channel, flushes and joins
/// the writer with a bounded timeout.</para>
/// </remarks>
public sealed class RollingFileLogger : IPlatformLogger, IAsyncDisposable
{
    private sealed class FlushRequest
    {
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly RollingLogOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly PlatformCounters _counters;
    private readonly Channel<object> _channel;
    private readonly object _fileLock = new();
    private readonly Dictionary<PlatformLogCategory, RateWindow> _rateWindows = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task? _writerTask;
    private FileStream? _stream;
    private int _fileSequence;
    private int _disposed;

    public RollingFileLogger(RollingLogOptions options, TimeProvider? timeProvider = null, PlatformCounters? counters = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DirectoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _counters = counters ?? new PlatformCounters();
        _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(Math.Max(64, options.ChannelCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Path of the file currently being written, or the directory before start.</summary>
    public string ActiveFilePath
    {
        get
        {
            lock (_fileLock)
            {
                return _stream?.Name ?? _options.DirectoryPath;
            }
        }
    }

    /// <summary>Creates the directory, sweeps old files and starts the background writer.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_writerTask is not null)
        {
            return Task.CompletedTask;
        }

        _writerTask = Task.Run(() => RunWriterAsync(_stopCts.Token), CancellationToken.None);
        return _started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    /// <summary>Enqueues an entry without blocking. Errors are written synchronously if the queue is full.</summary>
    public void Log(in PlatformLogEntry entry)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsEnabled(entry.Level))
        {
            return;
        }

        if (!TryReserveRateBudget(entry.Category, entry.Level))
        {
            _counters.IncrementLogRateLimited();
            return;
        }

        if (!_channel.Writer.TryWrite(entry))
        {
            if (entry.Level >= PlatformLogLevel.Error)
            {
                // Error+ must never be silently lost (spec §9.3); rare enough for a direct write.
                WriteEntrySynchronously(entry);
            }
            else
            {
                _counters.IncrementLogDropped();
            }
        }
    }

    public bool IsEnabled(PlatformLogLevel level) => level >= _options.MinimumLevel;

    /// <summary>Waits until every entry enqueued before this call has been handed to the file.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_writerTask is null)
        {
            return;
        }

        var request = new FlushRequest();
        if (!_channel.Writer.TryWrite(request))
        {
            // Queue saturated: report rather than block. The caller can retry.
            throw new InvalidOperationException("Log channel is saturated; flush request could not be queued.");
        }

        await request.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool TryReserveRateBudget(PlatformLogCategory category, PlatformLogLevel level)
    {
        if (level >= PlatformLogLevel.Error || _options.RateLimitPerCategoryPerSecond <= 0)
        {
            return true;
        }

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        lock (_rateWindows)
        {
            if (!_rateWindows.TryGetValue(category, out var window)
                || nowTicks - window.WindowStartTicks >= TimeSpan.TicksPerSecond)
            {
                _rateWindows[category] = new RateWindow(nowTicks, 1);
                return true;
            }

            if (window.Count >= _options.RateLimitPerCategoryPerSecond)
            {
                return false;
            }

            window.Count++;
            _rateWindows[category] = window; // RateWindow is a struct: persist the incremented copy
            return true;
        }
    }

    private void WriteEntrySynchronously(in PlatformLogEntry entry)
    {
        var payload = SerializeEntry(entry);
        lock (_fileLock)
        {
            if (_stream is null)
            {
                _counters.IncrementLogDropped();
                return;
            }

            try
            {
                EnsureCapacityLocked(payload.Length);
                WriteLocked(payload);
                _stream.Flush(flushToDisk: entry.Level >= PlatformLogLevel.Critical);
            }
            catch (Exception)
            {
                _counters.IncrementLogWriteFailures();
            }
        }
    }

    private async Task RunWriterAsync(CancellationToken stopToken)
    {
        try
        {
            Directory.CreateDirectory(_options.DirectoryPath);
            lock (_fileLock)
            {
                SweepOldFilesLocked();
                OpenNextFileLocked();
            }

            _started.TrySetResult();
        }
        catch (Exception ex)
        {
            _started.TrySetException(new IOException($"Could not initialise log directory '{_options.DirectoryPath}'.", ex));
            return;
        }

        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(stopToken).ConfigureAwait(false))
            {
                if (item is FlushRequest flush)
                {
                    lock (_fileLock)
                    {
                        try
                        {
                            _stream?.Flush();
                        }
                        catch (Exception)
                        {
                            _counters.IncrementLogWriteFailures();
                        }
                    }

                    flush.Completion.TrySetResult();
                    continue;
                }

                if (item is PlatformLogEntry entry)
                {
                    var payload = SerializeEntry(entry);
                    lock (_fileLock)
                    {
                        try
                        {
                            EnsureCapacityLocked(payload.Length);
                            WriteLocked(payload);
                        }
                        catch (Exception)
                        {
                            _counters.IncrementLogWriteFailures();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            lock (_fileLock)
            {
                try
                {
                    _stream?.Flush();
                    _stream?.Dispose();
                }
                catch (Exception)
                {
                    _counters.IncrementLogWriteFailures();
                }

                _stream = null;
            }

            // Complete any flush request that raced shutdown.
            while (_channel.Reader.TryRead(out var pending))
            {
                if (pending is FlushRequest flush)
                {
                    flush.Completion.TrySetResult();
                }
            }
        }
    }

    private void EnsureCapacityLocked(int pendingBytes)
    {
        if (_stream is null)
        {
            OpenNextFileLocked();
            return;
        }

        if (_stream.Length > 0 && _stream.Length + pendingBytes > _options.MaxFileBytes)
        {
            _stream.Flush();
            _stream.Dispose();
            _stream = null;
            SweepOldFilesLocked();
            OpenNextFileLocked();
        }
    }

    private void WriteLocked(byte[] payload)
    {
        _stream!.Write(payload, 0, payload.Length);
        _stream.WriteByte((byte)'\n');
        _counters.IncrementLogEntries();
    }

    private void OpenNextFileLocked()
    {
        var now = _timeProvider.GetLocalNow();
        var sequence = Interlocked.Increment(ref _fileSequence);
        var name = $"{_options.FilePrefix}-{now:yyyyMMdd}-{now:HHmmss}-{now.Millisecond:D3}-{sequence:D5}.jsonl";
        var path = Path.Combine(_options.DirectoryPath, name);
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024, FileOptions.SequentialScan);
    }

    private void SweepOldFilesLocked()
    {
        try
        {
            var files = Directory.GetFiles(_options.DirectoryPath, $"{_options.FilePrefix}-*.jsonl");
            if (files.Length == 0)
            {
                return;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, _options.RetentionDays));
            var removable = new List<string>();
            var keep = Math.Max(1, _options.MaxFiles);
            var excess = files.Length - keep;
            for (var i = 0; i < files.Length; i++)
            {
                var tooOld = File.GetLastWriteTimeUtc(files[i]) < cutoff;
                if (i < excess || tooOld)
                {
                    removable.Add(files[i]);
                }
            }

            foreach (var path in removable)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Another process may hold the file; it will be retried next start.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception)
        {
            // Sweeping is best effort; a failure must not stop logging.
            _counters.IncrementLogWriteFailures();
        }
    }

    private byte[] SerializeEntry(in PlatformLogEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", entry.Timestamp);
            writer.WriteString("level", entry.Level.ToString());
            writer.WriteString("cat", entry.Category.ToString());
            writer.WriteString("evtId", entry.EventId);
            writer.WriteString("msg", Truncate(entry.Message, 2048));

            if (entry.Data is { Count: > 0 })
            {
                writer.WriteStartObject("data");
                foreach (var pair in entry.Data)
                {
                    writer.WritePropertyName(pair.Key);
                    WriteValue(writer, pair.Value);
                }

                writer.WriteEndObject();
            }

            if (!string.IsNullOrEmpty(entry.Exception))
            {
                writer.WriteString("exception", Truncate(entry.Exception, 4096));
            }

            writer.WriteEndObject();
        }

        var payload = buffer.WrittenSpan.ToArray();
        if (payload.Length <= _options.MaxEntryBytes)
        {
            return payload;
        }

        // Rebuild a minimal entry rather than emitting an oversized line.
        buffer.Clear();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", entry.Timestamp);
            writer.WriteString("level", entry.Level.ToString());
            writer.WriteString("cat", entry.Category.ToString());
            writer.WriteString("evtId", entry.EventId);
            writer.WriteString("msg", Truncate(entry.Message, 256));
            writer.WriteBoolean("truncated", true);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : string.Concat(value.AsSpan(0, maxChars), "…");

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(Truncate(s, 1024));
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case byte or sbyte or short or ushort or int or uint:
                writer.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case Enum e:
                writer.WriteStringValue(e.ToString());
                break;
            case TimeSpan ts:
                writer.WriteStringValue($"{ts.TotalMilliseconds:F1}ms");
                break;
            default:
                writer.WriteStringValue(Truncate(value.ToString() ?? string.Empty, 1024));
                break;
        }
    }

    /// <summary>Stops the writer and flushes all remaining entries. Idempotent.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        if (_writerTask is not null)
        {
            try
            {
                await _writerTask.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Writer is stuck (disk stall). Do not block shutdown forever.
            }
            catch (Exception)
            {
                _counters.IncrementLogWriteFailures();
            }
        }

        _stopCts.Cancel();
        _stopCts.Dispose();
    }

    private struct RateWindow
    {
        public long WindowStartTicks;
        public int Count;

        public RateWindow(long windowStartTicks, int count)
        {
            WindowStartTicks = windowStartTicks;
            Count = count;
        }
    }
}
