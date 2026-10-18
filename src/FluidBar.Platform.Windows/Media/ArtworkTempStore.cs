using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Media;

/// <summary>Options for <see cref="ArtworkTempStore"/>.</summary>
public sealed class ArtworkTempStoreOptions
{
    /// <summary>Directory that receives materialized artwork files (e.g. <c>%TEMP%\FluidBar\art</c>).</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Maximum bytes kept on disk.</summary>
    public long MaxBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Maximum number of artwork files kept on disk.</summary>
    public int MaxFiles { get; init; } = 128;

    /// <summary>Files older than this are swept (spec: 7-day temp sweep).</summary>
    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Bounded artwork temp-file store: materializes already-validated encoded artwork bytes into files the
/// host can render (the SDK media payload exposes an <c>AlbumArtPath</c>). Budget/retention enforced on
/// every materialize and via <see cref="Sweep"/>; never decodes pixels (Platform stays imaging-free).
/// </summary>
public sealed class ArtworkTempStore : IDisposable
{
    private readonly ArtworkTempStoreOptions _options;
    private readonly IPlatformLogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _pathsByKey = new(StringComparer.Ordinal);
    private long _totalBytes;
    private int _writesSinceSweep;
    private int _disposed;

    public ArtworkTempStore(ArtworkTempStoreOptions options, IPlatformLogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DirectoryPath);
        _logger = logger ?? NullPlatformLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Directory holding the materialized files.</summary>
    public string DirectoryPath => _options.DirectoryPath;

    /// <summary>Current total bytes on disk (bounded).</summary>
    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _totalBytes;
            }
        }
    }

    /// <summary>Materializes artwork bytes to a file and returns the path (null when the store is disposed/failed).</summary>
    public async Task<string?> MaterializeAsync(string trackKey, ArtworkEntry artwork,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackKey);
        ArgumentNullException.ThrowIfNull(artwork);
        if (Volatile.Read(ref _disposed) != 0)
        {
            return null;
        }

        string fileName;
        string path;
        lock (_gate)
        {
            if (_pathsByKey.TryGetValue(trackKey, out var existing) && File.Exists(existing))
            {
                return existing;
            }

            fileName = BuildFileName(trackKey, artwork.ContentType);
            path = Path.Combine(_options.DirectoryPath, fileName);
            _pathsByKey[trackKey] = path;
        }

        try
        {
            await Task.Run(() => Directory.CreateDirectory(_options.DirectoryPath), cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, artwork.Data, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _totalBytes += artwork.Data.LongLength;
                _writesSinceSweep++;
            }

            if (Volatile.Read(ref _writesSinceSweep) >= 16)
            {
                Sweep();
            }

            return path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                _pathsByKey.Remove(trackKey);
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Media, "artwork-temp.materialize-failed",
                "Could not materialize artwork to the temp store.", ex);
            lock (_gate)
            {
                _pathsByKey.Remove(trackKey);
            }

            return null;
        }
    }

    /// <summary>Deletes expired and over-budget files. Safe to call from the thread pool.</summary>
    public void Sweep()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (!System.IO.Directory.Exists(_options.DirectoryPath))
            {
                return;
            }

            var cutoff = _timeProvider.GetUtcNow() - _options.Retention;
            var files = new DirectoryInfo(_options.DirectoryPath).GetFiles("fluidbar-art-*")
                .OrderBy(info => info.LastWriteTimeUtc)
                .ToArray();

            long keptBytes = 0;
            var keptFiles = 0;
            var ordered = files.Reverse().ToArray(); // newest first
            foreach (var file in ordered)
            {
                var expired = file.LastWriteTimeUtc < cutoff.UtcDateTime;
                var overBudget = keptBytes + file.Length > _options.MaxBytes || keptFiles >= _options.MaxFiles;
                if (expired || overBudget)
                {
                    TryDelete(file.FullName);
                    continue;
                }

                keptBytes += file.Length;
                keptFiles++;
            }

            lock (_gate)
            {
                _totalBytes = keptBytes;
                _writesSinceSweep = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(PlatformLogCategory.Media, "artwork-temp.sweep-failed",
                "Artwork temp sweep failed.", ex);
        }
    }

    /// <summary>Removes every materialized file.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pathsByKey.Clear();
            _totalBytes = 0;
        }

        try
        {
            if (System.IO.Directory.Exists(_options.DirectoryPath))
            {
                foreach (var file in Directory.GetFiles(_options.DirectoryPath, "fluidbar-art-*"))
                {
                    TryDelete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string BuildFileName(string trackKey, string contentType)
    {
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(trackKey)));
        var extension = contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".img",
        };
        return $"fluidbar-art-{hash[..32]}{extension}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Scanner holds it; next sweep retries.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Sweep();
    }
}
