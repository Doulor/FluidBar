namespace FluidBar.Core.Settings;

/// <summary>Kind of file-system operation requested through <see cref="ISettingsFileSystem"/>.</summary>
public enum SettingsFileOperationKind
{
    /// <summary>Directory creation.</summary>
    CreateDirectory,

    /// <summary>File read.</summary>
    Read,

    /// <summary>Plain file write (used for temp files and backups).</summary>
    Write,

    /// <summary>Atomic replace of a destination file with a source file.</summary>
    Replace,

    /// <summary>Move/rename.</summary>
    Move,

    /// <summary>File deletion.</summary>
    Delete,
}

/// <summary>Recorded file operation (in-memory implementation; diagnostics/tests).</summary>
/// <param name="Kind">Operation kind.</param>
/// <param name="Path">Primary path.</param>
/// <param name="Destination">Destination path for replace/move.</param>
public readonly record struct SettingsFileOperation(SettingsFileOperationKind Kind, string Path, string? Destination = null);

/// <summary>
/// The only I/O surface the core settings code is allowed to use. Platform/App supplies the real
/// implementation (<c>System.IO</c>); tests use <see cref="InMemorySettingsFileSystem"/>.
/// The store composes these primitives into an atomic write protocol
/// (temp write -&gt; replace -&gt; backup), matching spec 8.1.
/// </summary>
public interface ISettingsFileSystem
{
    /// <summary>Whether a file exists.</summary>
    bool FileExists(string path);

    /// <summary>Reads a UTF-8 text file; throws when missing/unreadable.</summary>
    string ReadAllText(string path);

    /// <summary>Ensures a directory exists (recursive).</summary>
    void CreateDirectory(string path);

    /// <summary>Writes a UTF-8 text file, truncating; implementations should flush to disk.</summary>
    void WriteAllText(string path, string contents);

    /// <summary>
    /// Atomically replaces <paramref name="destination"/> with <paramref name="source"/>,
    /// optionally preserving the previous destination as <paramref name="backupPath"/>.
    /// </summary>
    void ReplaceFile(string source, string destination, string? backupPath);

    /// <summary>Moves/renames a file.</summary>
    void MoveFile(string source, string destination, bool overwrite);

    /// <summary>Deletes a file; missing files are ignored by convention.</summary>
    void DeleteFile(string path);
}

/// <summary>
/// Deterministic in-memory file system for unit tests and headless hosts. Records every
/// operation and supports fault injection so failure paths are tested, not hoped for.
/// </summary>
public sealed class InMemorySettingsFileSystem : ISettingsFileSystem
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly List<SettingsFileOperation> _operations = [];

    /// <summary>When set, <see cref="WriteAllText"/> throws this exception.</summary>
    public Exception? FailOnWrite { get; set; }

    /// <summary>When set, <see cref="ReplaceFile"/> throws this exception.</summary>
    public Exception? FailOnReplace { get; set; }

    /// <summary>When set, <see cref="MoveFile"/> throws this exception.</summary>
    public Exception? FailOnMove { get; set; }

    /// <summary>When set, <see cref="ReadAllText"/> throws this exception.</summary>
    public Exception? FailOnRead { get; set; }

    /// <summary>Snapshot of recorded operations (oldest first).</summary>
    public IReadOnlyList<SettingsFileOperation> Operations
    {
        get
        {
            lock (_gate)
            {
                return _operations.ToArray();
            }
        }
    }

    /// <summary>Seeds a file without recording an operation (test setup).</summary>
    public void SeedFile(string path, string contents)
    {
        lock (_gate)
        {
            _files[path] = contents;
            var dir = DirectoryOf(path);
            if (dir.Length > 0)
            {
                _directories.Add(dir);
            }
        }
    }

    /// <summary>Reads a file without recording an operation (assertions).</summary>
    public string? PeekFile(string path)
    {
        lock (_gate)
        {
            return _files.TryGetValue(path, out var value) ? value : null;
        }
    }

    /// <summary>Removes all recorded operations.</summary>
    public void ClearOperations()
    {
        lock (_gate)
        {
            _operations.Clear();
        }
    }

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        lock (_gate)
        {
            return _files.ContainsKey(path);
        }
    }

    /// <inheritdoc />
    public string ReadAllText(string path)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.Read, path));
            if (FailOnRead is not null)
            {
                throw FailOnRead;
            }

            if (!_files.TryGetValue(path, out var contents))
            {
                throw new FileNotFoundException($"Missing in-memory file: {path}");
            }

            return contents;
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.CreateDirectory, path));
            _directories.Add(path);
        }
    }

    /// <inheritdoc />
    public void WriteAllText(string path, string contents)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.Write, path));
            if (FailOnWrite is not null)
            {
                throw FailOnWrite;
            }

            _files[path] = contents;
        }
    }

    /// <inheritdoc />
    public void ReplaceFile(string source, string destination, string? backupPath)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.Replace, source, destination));
            if (FailOnReplace is not null)
            {
                throw FailOnReplace;
            }

            if (!_files.TryGetValue(source, out var contents))
            {
                throw new FileNotFoundException($"Missing in-memory file: {source}");
            }

            if (!_files.TryGetValue(destination, out var previousDestination))
            {
                throw new FileNotFoundException($"Missing replace destination: {destination}");
            }

            if (backupPath is not null)
            {
                _files[backupPath] = previousDestination;
            }

            _files[destination] = contents;
            _files.Remove(source);
        }
    }

    /// <inheritdoc />
    public void MoveFile(string source, string destination, bool overwrite)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.Move, source, destination));
            if (FailOnMove is not null)
            {
                throw FailOnMove;
            }

            if (!_files.TryGetValue(source, out var contents))
            {
                throw new FileNotFoundException($"Missing in-memory file: {source}");
            }

            if (!overwrite && _files.ContainsKey(destination))
            {
                throw new IOException($"Destination exists: {destination}");
            }

            _files[destination] = contents;
            _files.Remove(source);
        }
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        lock (_gate)
        {
            _operations.Add(new SettingsFileOperation(SettingsFileOperationKind.Delete, path));
            _files.Remove(path);
        }
    }

    private static string DirectoryOf(string path)
    {
        var index = path.LastIndexOfAny(['/', '\\']);
        return index <= 0 ? string.Empty : path[..index];
    }
}
