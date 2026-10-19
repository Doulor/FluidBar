using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Tasks;

/// <summary>State of the local task file.</summary>
public enum TaskListStatus
{
    /// <summary>The file parsed (it may contain zero tasks).</summary>
    Ok = 0,

    /// <summary>The file does not exist yet.</summary>
    Missing = 1,

    /// <summary>The file exists but could not be parsed as a task array.</summary>
    Invalid = 2,
}

/// <summary>One task row from <c>%APPDATA%\\FluidBar\\tasks.json</c>.</summary>
/// <param name="Title">Task title (never blank).</param>
/// <param name="Done">Checkbox state.</param>
public readonly record struct TaskItemData(string Title, bool Done);

/// <summary>Immutable snapshot of the task file.</summary>
/// <param name="Items">Tasks in file order (the card paints the first three).</param>
/// <param name="Status">Ok / Missing / Invalid.</param>
/// <param name="FilePath">Path that was read (for the empty-state hint and diagnostics).</param>
/// <param name="FailureReason">Why the file could not be used (diagnostics; never rendered as data).</param>
public sealed record TaskListSnapshot(
    ImmutableArray<TaskItemData> Items,
    TaskListStatus Status,
    string? FilePath,
    string? FailureReason)
{
    /// <summary>Whether the file was read successfully (possibly with zero tasks).</summary>
    public bool IsAvailable => Status == TaskListStatus.Ok;

    /// <summary>Number of tasks in the file.</summary>
    public int Count => Items.IsDefault ? 0 : Items.Length;

    /// <summary>Number of completed tasks.</summary>
    public int DoneCount
    {
        get
        {
            if (Items.IsDefault)
            {
                return 0;
            }

            var done = 0;
            foreach (var item in Items)
            {
                if (item.Done)
                {
                    done++;
                }
            }

            return done;
        }
    }
}

/// <summary>Result of a checkbox toggle.</summary>
/// <param name="Succeeded">Whether the file was rewritten.</param>
/// <param name="FailureReason">Why the toggle failed (diagnostics).</param>
/// <param name="Snapshot">Snapshot after the write attempt.</param>
public sealed record ToggleTaskResult(bool Succeeded, string? FailureReason, TaskListSnapshot Snapshot);

/// <summary>Configuration for <see cref="TaskListService"/>.</summary>
public sealed class TaskListOptions
{
    /// <summary>Default file (<c>%APPDATA%\\FluidBar\\tasks.json</c>).</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "tasks.json");

    /// <summary>Task file path.</summary>
    public string FilePath { get; init; } = DefaultFilePath;

    /// <summary>Reload cadence (external edits appear within this window).</summary>
    public TimeSpan ReloadInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Safety cap on parsed tasks (the card paints three).</summary>
    public int MaxTasks { get; init; } = 100;

    /// <summary>Reads the <c>FLUIDBAR_TASKS_PATH</c> diagnostics override (used by the evidence run).</summary>
    public static TaskListOptions FromEnvironment() => new()
    {
        FilePath = Environment.GetEnvironmentVariable("FLUIDBAR_TASKS_PATH") is { Length: > 0 } path
            ? path
            : DefaultFilePath,
    };
}

/// <summary>
/// Local task list: reads <c>%APPDATA%\\FluidBar\\tasks.json</c> (<c>[{"title":"...","done":false}]</c>)
/// and writes toggles back atomically (temp file + replace). Missing/invalid files stay explicit empty
/// states; nothing is ever fabricated in memory.
/// </summary>
public sealed class TaskListService : PlatformMonitor<TaskListSnapshot>
{
    private readonly TaskListOptions _options;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private BackgroundPoller? _poller;
    private TaskListSnapshot? _lastPublished;

    public TaskListService(IPlatformLogger logger, PlatformCounters counters,
        TaskListOptions? options = null, TimeProvider? timeProvider = null)
        : base("tasks", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _options = options ?? new TaskListOptions();
    }

    /// <summary>Configured task file path.</summary>
    public string FilePath => _options.FilePath;

    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        PublishIfChanged(Load());
        _poller ??= new BackgroundPoller("tasks", _options.ReloadInterval, PollTick, Logger);
        _poller.Start();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        _poller?.Dispose();
        _poller = null;
        return ValueTask.CompletedTask;
    }

    private void PollTick() => PublishIfChanged(Load());

    /// <summary>Reads the file right now (no publishing).</summary>
    public TaskListSnapshot Load()
    {
        try
        {
            if (!File.Exists(_options.FilePath))
            {
                return new TaskListSnapshot([], TaskListStatus.Missing, _options.FilePath, "task file not found");
            }

            var json = File.ReadAllText(_options.FilePath, Encoding.UTF8);
            var snapshot = Parse(json, fileExists: true, _options.FilePath, _options.MaxTasks);
            if (!snapshot.IsAvailable)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.invalid",
                    "tasks.json is not a valid task array; the card shows the explicit empty state.",
                    data: new Dictionary<string, object?>
                    {
                        ["path"] = _options.FilePath,
                        ["reason"] = snapshot.FailureReason,
                    });
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, "tasks.read-failed",
                "tasks.json could not be read.", ex);
            return new TaskListSnapshot([], TaskListStatus.Invalid, _options.FilePath,
                $"read-failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Flips one checkbox and rewrites the file atomically (temp file + replace). Returns an explicit
    /// failure when the file is missing/invalid or the index is out of range; the original file is left
    /// untouched in every failure path.
    /// </summary>
    /// <param name="index">Zero-based task index (file order).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<ToggleTaskResult> ToggleAsync(int index, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = Load();
            if (!snapshot.IsAvailable)
            {
                return new ToggleTaskResult(false,
                    snapshot.Status == TaskListStatus.Missing ? "tasks.json does not exist" : "tasks.json is not valid",
                    snapshot);
            }

            if (index < 0 || index >= snapshot.Items.Length)
            {
                return new ToggleTaskResult(false, "task index out of range", snapshot);
            }

            var items = snapshot.Items.ToBuilder();
            var current = items[index];
            items[index] = current with { Done = !current.Done };

            string json;
            try
            {
                json = Serialize(items);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.serialize-failed",
                    "The task list could not be serialized; the file is left unchanged.", ex);
                return new ToggleTaskResult(false, $"serialize-failed: {ex.GetType().Name}", snapshot);
            }

            try
            {
                WriteAtomic(json);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.write-failed",
                    "tasks.json could not be rewritten; the file is left unchanged.", ex);
                return new ToggleTaskResult(false, $"write-failed: {ex.GetType().Name}", snapshot);
            }

            var updated = Parse(json, fileExists: true, _options.FilePath, _options.MaxTasks);
            PublishIfChanged(updated);
            Logger.LogInfo(PlatformLogCategory.Platform, "tasks.toggled",
                "Task checkbox written back to tasks.json.", new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["title"] = current.Title,
                    ["done"] = items[index].Done,
                });
            return new ToggleTaskResult(true, null, updated);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Parses the task file content; public so the tolerance rules are testable.</summary>
    /// <param name="json">File content.</param>
    /// <param name="fileExists">Whether the file exists (missing files are an explicit empty state).</param>
    /// <param name="path">Path used for the hint/diagnostics.</param>
    /// <param name="maxTasks">Safety cap.</param>
    public static TaskListSnapshot Parse(string? json, bool fileExists, string? path, int maxTasks = 100)
    {
        if (!fileExists)
        {
            return new TaskListSnapshot([], TaskListStatus.Missing, path, "task file not found");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            // An existing empty file simply has no tasks: that is data, not an error.
            return new TaskListSnapshot([], TaskListStatus.Ok, path, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new TaskListSnapshot([], TaskListStatus.Invalid, path, "root is not an array");
            }

            var builder = ImmutableArray.CreateBuilder<TaskItemData>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (builder.Count >= Math.Max(1, maxTasks))
                {
                    break;
                }

                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("title", out var titleElement)
                    || titleElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(titleElement.GetString()))
                {
                    // Rows without a usable title are skipped rather than shown as fabricated placeholders.
                    continue;
                }

                var done = element.TryGetProperty("done", out var doneElement)
                           && doneElement.ValueKind == JsonValueKind.True;
                builder.Add(new TaskItemData(titleElement.GetString()!.Trim(), done));
            }

            return new TaskListSnapshot(builder.ToImmutable(), TaskListStatus.Ok, path, null);
        }
        catch (JsonException ex)
        {
            return new TaskListSnapshot([], TaskListStatus.Invalid, path, $"invalid json: {ex.Message}");
        }
    }

    /// <summary>Serializes tasks in the documented shape (indented, diffable).</summary>
    /// <param name="items">Tasks to write.</param>
    public static string Serialize(IEnumerable<TaskItemData> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var payload = items.Select(item => new { title = item.Title, done = item.Done }).ToArray();
        return JsonSerializer.Serialize(payload, JsonSerializerOptions) + Environment.NewLine;
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    private void WriteAtomic(string json)
    {
        var directory = Path.GetDirectoryName(_options.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _options.FilePath + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, _options.FilePath, overwrite: true);
    }

    private void PublishIfChanged(TaskListSnapshot snapshot)
    {
        var last = _lastPublished;
        if (last is not null
            && last.Status == snapshot.Status
            && last.FilePath == snapshot.FilePath
            && SequenceEqual(last.Items, snapshot.Items))
        {
            return;
        }

        _lastPublished = snapshot;
        Publish(snapshot);
    }

    private static bool SequenceEqual(ImmutableArray<TaskItemData> left, ImmutableArray<TaskItemData> right)
    {
        if (left.IsDefault || right.IsDefault)
        {
            return left.IsDefault && right.IsDefault;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
