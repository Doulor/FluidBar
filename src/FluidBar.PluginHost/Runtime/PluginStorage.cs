using System.Text;
using FluidBar.Sdk;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// Per-plugin file storage rooted at <c>$PLUGIN_DATA</c>. Paths are package-relative style strings; absolute
/// paths, drive letters and <c>..</c> traversal are rejected before any I/O happens.
/// </summary>
internal sealed class PluginStorage : IPluginStorage
{
    private const int MaxReadTextBytes = 16 * 1024 * 1024;
    private const int MaxRelativePathLength = 1_024;

    private readonly string _root;

    internal PluginStorage(string dataDirectory)
    {
        _root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(_root);
    }

    public string DataDirectory => _root;

    public string GetDataPath(string relativePath)
    {
        if (!TryResolve(relativePath, out var fullPath))
        {
            throw new ArgumentException($"Path '{relativePath}' is outside the plugin data directory.", nameof(relativePath));
        }

        return fullPath;
    }

    public bool Exists(string relativePath) => File.Exists(GetDataPath(relativePath));

    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = GetDataPath(relativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length > MaxReadTextBytes)
        {
            throw new IOException($"File '{relativePath}' is {info.Length} bytes; the plugin storage read limit is {MaxReadTextBytes}.");
        }

        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PrepareWrite(relativePath);
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public async Task<byte[]?> ReadBytesAsync(string relativePath, int maxBytes, CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "maxBytes must be positive.");
        }

        var path = GetDataPath(relativePath);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length > maxBytes)
        {
            throw new IOException($"File '{relativePath}' is {info.Length} bytes; the requested limit is {maxBytes}.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        var path = PrepareWrite(relativePath);
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = GetDataPath(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PrepareWrite(string relativePath)
    {
        var path = GetDataPath(relativePath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return path;
    }

    private bool TryResolve(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > MaxRelativePathLength)
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Split('/').Any(static segment => segment is ".." or "."))
        {
            return false;
        }

        var root = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }
}
