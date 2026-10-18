using System.Threading.Channels;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Options for the agent inbox watcher.</summary>
public sealed class AgentInboxOptions
{
    /// <summary>Directory the hook scripts drop one JSON file per event into.</summary>
    public required string InboxDirectory { get; init; }

    /// <summary>Maximum recent events kept in the published snapshot.</summary>
    public int MaxRecentEvents { get; init; } = 100;

    /// <summary>Maximum processed files kept on disk (older files are swept on start).</summary>
    public int MaxProcessedFiles { get; init; } = 100;

    /// <summary>Maximum accepted inbox file size.</summary>
    public int MaxFileBytes { get; init; } = 64 * 1024;

    /// <summary>How long to wait for a hook script to finish writing before reading.</summary>
    public TimeSpan WriteSettleDelay { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Maximum pending files buffered in memory (bounded delivery).</summary>
    public int ChannelCapacity { get; init; } = 64;
}

/// <summary>
/// Event-driven agent hook inbox: <see cref="FileSystemWatcher"/> on the inbox directory feeds a
/// bounded channel; valid events are published as an immutable bounded snapshot, malformed files are
/// quarantined and processed files are retained with a 100-file cap. No polling, no timer.
/// </summary>
public sealed class AgentInboxWatcher : PlatformMonitor<AgentEventSnapshot>, IAgentInboxSource
{
    private const string ProcessedDirectoryName = "processed";
    private const string QuarantineDirectoryName = "quarantine";

    private readonly AgentInboxOptions _options;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly AgentEventHistory _history;
    private readonly Channel<string> _pending;
private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _consumerCts;
    private Task? _consumerTask;

    public AgentInboxWatcher(AgentInboxOptions options, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("agent-inbox", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.InboxDirectory);
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _history = new AgentEventHistory(options.MaxRecentEvents);
        _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(Math.Max(8, options.ChannelCapacity))
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Inbox directory the user hooks write into.</summary>
    public string InboxDirectory => _options.InboxDirectory;

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        var root = _options.InboxDirectory;
        var processedDirectory = Path.Combine(root, ProcessedDirectoryName);
        var quarantineDirectory = Path.Combine(root, QuarantineDirectoryName);

        await Task.Run(() =>
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(processedDirectory);
            Directory.CreateDirectory(quarantineDirectory);
            SweepRetention(processedDirectory, _options.MaxProcessedFiles);
            SweepRetention(quarantineDirectory, 20);
        }, cancellationToken).ConfigureAwait(false);

        // Files that arrived while the app was closed are replayed from the inbox (bounded batch).
        await Task.Run(() => ArchiveExistingInbox(root), cancellationToken)
            .ConfigureAwait(false);

        _consumerCts = new CancellationTokenSource();
        _consumerTask = Task.Run(() => ConsumeAsync(_consumerCts.Token), CancellationToken.None);

        var watcher = new FileSystemWatcher(root, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            InternalBufferSize = 16 * 1024,
        };
        watcher.Created += OnInboxFileChanged;
        watcher.Renamed += OnInboxFileRenamed;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;

        Publish(_history.Snapshot());
        _logger.LogInfo(PlatformLogCategory.Agent, "agent.watcher-started", "Agent inbox watcher started.",
            new Dictionary<string, object?> { ["directory"] = root });
    }

    private void ArchiveExistingInbox(string root)
    {
        try
        {
            foreach (var file in Directory.GetFiles(root, "*.json").OrderBy(File.GetLastWriteTimeUtc).Take(32))
            {
                if (!_pending.Writer.TryWrite(file))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Agent, "agent.archive-failed",
                "Could not archive existing agent inbox files.", ex);
        }
    }

    private void OnInboxFileChanged(object sender, FileSystemEventArgs args)
    {
        if (!_pending.Writer.TryWrite(args.FullPath))
        {
            _counters.IncrementAgentEvents();
            _logger.LogDebug(PlatformLogCategory.Agent, "agent.queue-full",
                "Agent inbox queue is saturated; dropping the oldest pending file.");
        }
    }

    private void OnInboxFileRenamed(object sender, RenamedEventArgs args)
    {
        if (args.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            _ = _pending.Writer.TryWrite(args.FullPath);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        _counters.IncrementWin32Failures();
        _logger.LogWarning(PlatformLogCategory.Agent, "agent.watcher-error",
            "FileSystemWatcher reported an error; some agent events may be missed.", args.GetException());
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var file in _pending.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await ProcessFileAsync(file, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _counters.IncrementAgentEvents();
                    _logger.LogError(PlatformLogCategory.Agent, "agent.process-failed",
                        $"Failed to process agent inbox file '{Path.GetFileName(file)}'.", ex);
                    MoveToCategory(file, QuarantineDirectoryName);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task ProcessFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var info = new FileInfo(filePath);
        if (info.Length > _options.MaxFileBytes)
        {
            _logger.LogWarning(PlatformLogCategory.Agent, "agent.file-too-large",
                $"Agent inbox file exceeded {_options.MaxFileBytes} bytes; quarantined.");
            MoveToCategory(filePath, QuarantineDirectoryName);
            return;
        }

        // Let the producing hook finish writing before reading (bounded retry, not a poll loop).
        string? text = null;
        for (var attempt = 0; attempt < 5 && text is null; attempt++)
        {
            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096, FileOptions.Asynchronous);
                using var reader = new StreamReader(stream);
                text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(_options.WriteSettleDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        if (text is null)
        {
            _logger.LogWarning(PlatformLogCategory.Agent, "agent.file-unreadable",
                "Agent inbox file stayed locked; quarantined.");
            MoveToCategory(filePath, QuarantineDirectoryName);
            return;
        }

        if (!AgentEventParser.TryParse(text, out var parsed, out var error))
        {
            _logger.LogWarning(PlatformLogCategory.Agent, "agent.file-invalid",
                $"Invalid agent inbox payload quarantined: {error}");
            MoveToCategory(filePath, QuarantineDirectoryName);
            return;
        }

        var entry = AgentEventParser.ToEntry(parsed, filePath, _timeProvider.GetUtcNow());
        var snapshot = _history.Add(entry);
        Publish(snapshot);
        _counters.IncrementAgentEvents();
        MoveToCategory(filePath, ProcessedDirectoryName);
        _logger.LogInfo(PlatformLogCategory.Agent, "agent.event",
            "Agent event captured.", new Dictionary<string, object?>
            {
                ["agent"] = entry.Agent.ToString(),
                ["kind"] = entry.Kind.ToString(),
            });
    }

    private void MoveToCategory(string filePath, string categoryDirectory)
    {
        try
        {
            var category = Path.Combine(_options.InboxDirectory, categoryDirectory);
            Directory.CreateDirectory(category);
            var destination = Path.Combine(category,
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{Path.GetExtension(filePath)}");
            File.Move(filePath, destination, overwrite: false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Agent, "agent.move-failed",
                $"Could not move agent inbox file to '{categoryDirectory}'.", ex);
        }
    }

    private static void SweepRetention(string directory, int keepNewest)
    {
        try
        {
            var files = Directory.GetFiles(directory)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToArray();
            foreach (var stale in files.Skip(Math.Max(1, keepNewest)))
            {
                try
                {
                    stale.Delete();
                }
                catch (IOException)
                {
                    // Scanner holds the file; next start retries.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnInboxFileChanged;
            watcher.Renamed -= OnInboxFileRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }

        _pending.Writer.TryComplete();
        var cts = _consumerCts;
        _consumerCts = null;
        cts?.Cancel();
        var consumer = _consumerTask;
        _consumerTask = null;
        if (consumer is not null)
        {
            try
            {
                await consumer.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(PlatformLogCategory.Agent, "agent.consumer-timeout",
                    "Agent inbox consumer did not stop within the timeout.");
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
        _history.Clear();
        _logger.LogInfo(PlatformLogCategory.Agent, "agent.watcher-stopped", "Agent inbox watcher stopped.");
    }
}
