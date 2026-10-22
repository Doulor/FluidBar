using System.Diagnostics;
using FluidBar.Core.Diagnostics;

namespace FluidBar.App.Settings;

/// <summary>
/// Persistence adapter for <see cref="AppSettings"/>. All file I/O is asynchronous/off the UI thread
/// (spec 5.8 rule 2); saving is debounced and atomic (temp + replace, spec 8.1).
/// </summary>
public interface IUiSettingsStore
{
    /// <summary>Loads settings off the UI thread; returns defaults when the file is missing/corrupt.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Queues a debounced save; never blocks the caller.</summary>
    void QueueSave(AppSettings settings);

    /// <summary>Flushes any pending save (application exit).</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>In-memory store for tests and headless runs.</summary>
public sealed class InMemoryUiSettingsStore : IUiSettingsStore
{
    private AppSettings _settings;

    /// <summary>Creates a store with the supplied initial settings.</summary>
    public InMemoryUiSettingsStore(AppSettings? initial = null) => _settings = (initial ?? AppSettings.Default).Normalize();

    /// <summary>Number of completed saves.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Last saved snapshot.</summary>
    public AppSettings LastSaved => _settings;

    /// <inheritdoc />
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    /// <inheritdoc />
    public void QueueSave(AppSettings settings)
    {
        _settings = settings.Normalize();
        SaveCount++;
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Atomic JSON file store for the App settings adapter. Writes to
/// <c>%APPDATA%\FluidBar\settings.ui.json</c> until Core's <c>ISettingsStore</c> replaces it.
/// </summary>
public sealed class JsonUiSettingsStore : IUiSettingsStore, IDisposable
{
    private readonly string _path;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private AppSettings? _pending;
    private long _pendingSequence;
    private long _writtenSequence;
    private ITimer? _debounceTimer;
    private Task _writeTask = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Creates a file store.</summary>
    /// <param name="path">Absolute settings path.</param>
    /// <param name="logger">Structured logger (defaults to a no-op).</param>
    /// <param name="debounce">Save debounce interval (spec 8.1: 500 ms).</param>
    public JsonUiSettingsStore(string path, IAppLogger? logger = null, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _logger = logger ?? NullAppLogger.Instance;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
    }

    /// <summary>Default settings path for the App adapter.</summary>
    public static string DefaultPath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FluidBar",
            "settings.ui.json");

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(
                async () =>
                {
                    // A fresh file can be locked for a few milliseconds by an indexer/AV scanner;
                    // retrying briefly avoids silently resetting the user's settings on a transient
                    // sharing violation (a real observed failure mode).
                    for (var attempt = 0; ; attempt++)
                    {
                        try
                        {
                            if (!File.Exists(_path))
                            {
                                return AppSettings.Default;
                            }

                            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
                            var parsed = AppSettings.TryParse(json, out var error);
                            if (parsed is null)
                            {
                                _logger.Warning(LogCategories.Settings, "settings.ui.corrupt",
                                    "UI settings file could not be parsed; starting from defaults.",
                                    data: new Dictionary<string, object?> { ["error"] = error, ["path"] = _path });
                                return AppSettings.Default;
                            }

                            return parsed;
                        }
                        catch (IOException) when (attempt < 3)
                        {
                            await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.Warning(LogCategories.Settings, "settings.ui.read-failed",
                "Failed to read UI settings; starting from defaults.", ex,
                new Dictionary<string, object?> { ["path"] = _path });
            return AppSettings.Default;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warning(LogCategories.Settings, "settings.ui.read-denied",
                "Access denied reading UI settings; starting from defaults.", ex,
                new Dictionary<string, object?> { ["path"] = _path });
            return AppSettings.Default;
        }
    }

    /// <inheritdoc />
    public void QueueSave(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = settings.Normalize();
            _pendingSequence++;
            _debounceTimer?.Dispose();
            _debounceTimer = TimeProvider.System.CreateTimer(
                static state => _ = ((JsonUiSettingsStore)state!).WritePendingAsync(),
                this,
                _debounce,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // A debounce timer can already be queued when it is disposed. Wait until the pending sequence
        // has actually reached disk instead of assuming one WritePendingAsync call won the race.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Task writeTask;
            lock (_gate)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
                writeTask = _writeTask;
            }

            if (writeTask is not null)
            {
                await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            await WritePendingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_pending is null || _pendingSequence == _writtenSequence)
                {
                    return;
                }
            }

            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private Task WritePendingAsync()
    {
        AppSettings? snapshot;
        long sequence;
        lock (_gate)
        {
            if (_pending is null || _pendingSequence == _writtenSequence)
            {
                return _writeTask;
            }

            snapshot = _pending;
            sequence = _pendingSequence;
        }

        if (snapshot is null)
        {
            return _writeTask;
        }

        _writeTask = Task.Run(async () =>
        {
            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                await File.WriteAllTextAsync(temporary, snapshot.ToJson()).ConfigureAwait(false);
                if (File.Exists(_path))
                {
                    File.Replace(temporary, _path, _path + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, _path, overwrite: true);
                }

                lock (_gate)
                {
                    _writtenSequence = Math.Max(_writtenSequence, sequence);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.Error(LogCategories.Settings, "settings.ui.write-failed",
                    "Failed to persist UI settings.", ex,
                    new Dictionary<string, object?> { ["path"] = _path });
            }
            finally
            {
                _ioGate.Release();
            }
        });
        return _writeTask;
    }
}

/// <summary>
/// Owns the current <see cref="AppSettings"/>, normalizes updates and raises <see cref="Changed"/>
/// so the island and every open window can apply changes live. Persistence is fire-and-forget.
/// </summary>
public sealed class SettingsController : IDisposable
{
    private readonly IUiSettingsStore _store;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private AppSettings _current;
    private bool _disposed;

    /// <summary>Creates a controller with the initial settings snapshot.</summary>
    public SettingsController(AppSettings initial, IUiSettingsStore store, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(store);
        _current = initial.Normalize();
        _store = store;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>Raised with (previous, current) after any accepted change.</summary>
    public event Action<AppSettings, AppSettings>? Changed;

    /// <summary>Current immutable snapshot.</summary>
    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Applies a mutation (normalized) and persists it; returns the new snapshot.</summary>
    public AppSettings Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        AppSettings previous;
        AppSettings next;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _current;
            next = mutate(_current).Normalize();
            if (next == previous)
            {
                return previous;
            }

            _current = next;
        }

        _logger.Debug(LogCategories.Settings, "settings.ui.changed", "UI settings changed.");
        _store.QueueSave(next);
        Changed?.Invoke(previous, next);
        return next;
    }

    /// <summary>Replaces the whole snapshot (reset/import); persists it.</summary>
    public AppSettings Replace(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Update(_ => settings);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Changed = null;
    }
}
