using System.Runtime.InteropServices;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>One row from WorkBuddy's local sessions table (read from a copy of the SQLite database).</summary>
/// <param name="Id">Session id.</param>
/// <param name="Title">Session title (custom title preferred).</param>
/// <param name="Status">Raw status ("running"/"in_progress"/"pending"/"completed"/"error").</param>
/// <param name="Cwd">Workspace path.</param>
/// <param name="UpdatedAtMs">Updated timestamp (Unix ms).</param>
/// <param name="LastActivityMs">Last activity timestamp (Unix ms).</param>
public sealed record WorkBuddySessionRow(
    string Id,
    string Title,
    string Status,
    string Cwd,
    long UpdatedAtMs,
    long LastActivityMs);

/// <summary>
/// Native WorkBuddy support: WorkBuddy (the Electron agent client at ~/.workbuddy) keeps its sessions in
/// a SQLite database (<c>workbuddy.db</c>). To avoid ever touching the live database, the provider copies
/// the database (plus WAL) to a temp directory first and queries that copy read-write (SQLite may need to
/// recover the WAL); the original file is never opened for writing.
/// </summary>
public sealed class WorkBuddySessionProvider : IAgentStatusProvider
{
    private readonly string _dataDirectory;
    private readonly Func<string, IReadOnlyList<WorkBuddySessionRow>> _reader;
    private readonly TimeSpan _activeThreshold;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public WorkBuddySessionProvider(string dataDirectory,
        Func<string, IReadOnlyList<WorkBuddySessionRow>>? reader = null,
        TimeSpan? activeThreshold = null)
    {
        _dataDirectory = dataDirectory;
        _reader = reader ?? WorkBuddySqliteReader.Read;
        _activeThreshold = activeThreshold ?? TimeSpan.FromSeconds(120);
    }

    /// <inheritdoc />
    public string ProviderId => "workbuddy";

    /// <inheritdoc />
    public string DisplayName => "WorkBuddy";

    /// <inheritdoc />
    public string DataPath => Path.Combine(_dataDirectory, "workbuddy.db");

    /// <inheritdoc />
    public bool IsAvailable => File.Exists(Path.Combine(_dataDirectory, "workbuddy.db"));

    /// <inheritdoc />
    public DateTimeOffset? LastActivityUtc => _lastActivity;

    /// <inheritdoc />
    public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
    {
        if (!IsAvailable)
        {
            return [];
        }

        IReadOnlyList<WorkBuddySessionRow> rows;
        try
        {
            rows = _reader(_dataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException)
        {
            // Graceful degradation: a machine without winsqlite3.dll (or an unreadable DB) simply yields no
            // WorkBuddy rows instead of throwing on the monitor thread.
            return [];
        }

        var events = new List<AgentProviderEvent>();
        _knownSessions ??= new HashSet<string>(
            rows.Where(static row => IsTerminal(row.Status)).Select(static row => row.Id), StringComparer.Ordinal);
        foreach (var row in rows)
        {
            try
            {
                var observed = Observe(row, now);
                if (observed is not null)
                {
                    events.Add(observed);
                }
            }
            catch (Exception ex) when (ex is ArithmeticException or ArgumentException)
            {
                // One malformed row must not take the provider down.
            }
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private AgentProviderEvent? Observe(WorkBuddySessionRow row, DateTimeOffset now)
    {
        var firstSight = !_states.TryGetValue(row.Id, out var state);
        if (firstSight)
        {
            state = new SessionState();
            _states[row.Id] = state;
            state.CreatedDuringSession = !_knownSessions!.Contains(row.Id);
        }

        var project = LeafName(row.Cwd);
        var title = string.IsNullOrEmpty(project) ? "WorkBuddy" : "WorkBuddy · " + Trim(project, 32);
        var detail = string.IsNullOrWhiteSpace(row.Title) ? "会话" : Trim(row.Title.ReplaceLineEndings(" "), 80);
        var activity = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(row.LastActivityMs, row.UpdatedAtMs));
        state.Project = project;
        state.LastSeen = now;
        _knownSessions!.Add(row.Id);

        if (string.Equals(row.Status, "running", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, "in_progress", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, "pending", StringComparison.OrdinalIgnoreCase)
            || (!IsTerminal(row.Status) && now - activity <= _activeThreshold))
        {
            state.WasRunning = true;
            state.CompletionEmitted = false;
            state.StartedAt = activity;
            return new AgentProviderEvent(row.Id, "running", title, detail, now, now);
        }

        if (string.Equals(row.Status, "error", StringComparison.OrdinalIgnoreCase))
        {
            if (state.CompletionEmitted && !state.WasRunning)
            {
                return null;
            }

            state.WasRunning = false;
            state.CompletionEmitted = true;
            return new AgentProviderEvent(row.Id, "failed", title, "会话失败", now, now, detail);
        }

        if (string.Equals(row.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            if (state.CompletionEmitted)
            {
                return null;
            }

            var shouldEmit = state.WasRunning || state.CreatedDuringSession;
            state.WasRunning = false;
            state.CompletionEmitted = true;
            return shouldEmit
                ? new AgentProviderEvent(row.Id, "completed", title, "已完成", now, now)
                : null;
        }

        // Unknown status: treat a recent write as activity, otherwise adopt silently.
        if (now - activity <= _activeThreshold)
        {
            state.WasRunning = true;
            return new AgentProviderEvent(row.Id, "running", title, detail, now, now);
        }

        state.WasRunning = false;
        return null;
    }

    private static bool IsTerminal(string status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase);

    private static string LeafName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private sealed class SessionState
    {
        public DateTimeOffset? StartedAt { get; set; }

        public bool WasRunning { get; set; }

        public bool CompletionEmitted { get; set; }

        public bool CreatedDuringSession { get; set; }

        public string Project { get; set; } = string.Empty;

        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Minimal read-only SQLite reader for WorkBuddy: copies <c>workbuddy.db</c> (+ WAL) into a temp folder
/// and queries the copy through the Windows-provided <c>winsqlite3.dll</c>. The live file is never opened.
/// Results are cached by database/WAL timestamp so a 2 s poll does not repeatedly copy the file.
/// </summary>
internal static class WorkBuddySqliteReader
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteReadWrite = 2;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(DateTime dbWrite, DateTime walWrite, IReadOnlyList<WorkBuddySessionRow> Rows);

    public static IReadOnlyList<WorkBuddySessionRow> Read(string dataDirectory)
    {
        var dbPath = Path.Combine(dataDirectory, "workbuddy.db");
        var walPath = dbPath + "-wal";
        var dbWrite = File.GetLastWriteTimeUtc(dbPath);
        var walWrite = File.Exists(walPath) ? File.GetLastWriteTimeUtc(walPath) : DateTime.MinValue;
        lock (Gate)
        {
            if (Cache.TryGetValue(dataDirectory, out var cached)
                && cached.dbWrite == dbWrite && cached.walWrite == walWrite)
            {
                return cached.Rows;
            }
        }

        var rows = ReadSqlite(dbPath, walPath);
        lock (Gate)
        {
            Cache[dataDirectory] = new CacheEntry(dbWrite, walWrite, rows);
        }

        return rows;
    }

    private static IReadOnlyList<WorkBuddySessionRow> ReadSqlite(string dbPath, string walPath)
    {
        var copyDirectory = Path.Combine(Path.GetTempPath(), "FluidBar", "workbuddy-db");
        Directory.CreateDirectory(copyDirectory);
        var copyPath = Path.Combine(copyDirectory, "workbuddy.db");
        File.Copy(dbPath, copyPath, overwrite: true);
        var copyWal = copyPath + "-wal";
        if (File.Exists(walPath))
        {
            File.Copy(walPath, copyWal, overwrite: true);
        }
        else if (File.Exists(copyWal))
        {
            File.Delete(copyWal);
        }

        var db = IntPtr.Zero;
        var statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(copyPath, out db, SqliteReadWrite, IntPtr.Zero) != SqliteOk || db == IntPtr.Zero)
            {
                return [];
            }

            const string sql = """
                SELECT id, COALESCE(custom_title, title, ''), status, cwd,
                       COALESCE(updated_at, 0), COALESCE(last_activity_at, 0)
                FROM sessions
                WHERE deleted_at IS NULL
                ORDER BY COALESCE(last_activity_at, updated_at) DESC
                LIMIT 16
                """;
            if (sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero) != SqliteOk || statement == IntPtr.Zero)
            {
                return [];
            }

            var rows = new List<WorkBuddySessionRow>();
            while (sqlite3_step(statement) == SqliteRow)
            {
                var id = Text(statement, 0);
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                rows.Add(new WorkBuddySessionRow(id, Text(statement, 1), Text(statement, 2), Text(statement, 3),
                    sqlite3_column_int64(statement, 4), sqlite3_column_int64(statement, 5)));
            }

            return rows;
        }
        finally
        {
            if (statement != IntPtr.Zero)
            {
                sqlite3_finalize(statement);
            }

            if (db != IntPtr.Zero)
            {
                sqlite3_close_v2(db);
            }
        }
    }

    private static string Text(IntPtr statement, int column)
    {
        var pointer = sqlite3_column_text(statement, column);
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db,
        int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int sqlite3_prepare_v2(IntPtr db,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int numBytes, out IntPtr statement,
        IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int column);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close_v2(IntPtr db);
}
