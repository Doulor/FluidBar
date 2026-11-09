using System.Globalization;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.App.Hosting.Pages;

/// <summary>Bounded, deduplicated background file inspection. Painting consumes only immutable results.</summary>
public sealed class FileVisualCache
{
    private const int Capacity = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<FileVisualMetadata>> _loads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileVisualMetadata> _fallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _workers = new(2);
    public event EventHandler? Changed;

    public FileVisualMetadata Get(string path, string? revision = null)
    {
        var task = LoadAsync(path, revision);
        if (task.IsCompletedSuccessfully) return task.Result;
        lock (_gate) return _fallbacks.TryGetValue(CacheKey(path, revision), out var fallback) ? fallback : FileVisualMetadata.FromPath(path);
    }

    private static string CacheKey(string path, string? revision) => revision is null ? path : path + '\0' + revision;

    public Task<FileVisualMetadata> LoadAsync(string path, string? revision = null)
    {
        var key = CacheKey(path, revision);
        lock (_gate)
        {
            if (_loads.TryGetValue(key, out var existing)) return existing;
            if (_loads.Count >= Capacity)
            {
                // Evict only completed work: queued decodes cannot multiply on repeated snapshots.
                var old = _loads.FirstOrDefault(pair => pair.Value.IsCompleted);
                if (old.Key is not null) { _loads.Remove(old.Key); _fallbacks.Remove(old.Key); }
                else return Task.FromResult(FileVisualMetadata.FromPath(path));
            }
            var fallback = FileVisualMetadata.FromPath(path);
            _fallbacks[key] = fallback;
            var work = Task.Run(async () =>
            {
                await _workers.WaitAsync().ConfigureAwait(false);
                try { return Read(path, fallback); }
                finally { _workers.Release(); }
            });
            _loads[key] = work;
            _ = work.ContinueWith(_ => Changed?.Invoke(this, EventArgs.Empty), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            return work;
        }
    }

    private static FileVisualMetadata Read(string path, FileVisualMetadata fallback)
    {
        try
        {
            if (System.IO.Directory.Exists(path))
                return fallback with { TypeLabel = "DIR", Kind = FileVisualKind.Folder, Exists = true };
            var info = new FileInfo(path);
            if (!info.Exists) return fallback;
            byte[]? thumbnail = null;
            if (fallback.Kind == FileVisualKind.Image && info.Length <= 24 * 1024 * 1024)
            {
                using var stream = File.OpenRead(path);
                using var codec = SKCodec.Create(stream);
                // Bound the fallback decode; huge or malformed images retain the type badge.
                if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0
                    && (long)codec.Info.Width * codec.Info.Height <= 16_000_000)
                {
                    using var bitmap = SKBitmap.Decode(codec);
                    if (bitmap is not null)
                    {
                        var scale = Math.Min(1d, 256d / Math.Max(bitmap.Width, bitmap.Height));
                        using var scaled = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)),
                            Math.Max(1, (int)(bitmap.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear));
                        using var encoded = scaled?.Encode(SKEncodedImageFormat.Png, 90);
                        thumbnail = encoded?.ToArray();
                    }
                }
            }
            return fallback with { Exists = true, SizeText = FormatSize(info.Length), ThumbnailPng = thumbnail,
                ImageKey = thumbnail is null ? null : $"file:{path}:{info.LastWriteTimeUtc.Ticks}:{info.Length}" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return fallback;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        < 1024 * 1024 * 1024 => (bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " GB",
    };
}
