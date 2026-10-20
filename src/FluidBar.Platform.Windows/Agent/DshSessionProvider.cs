using System.Text.Json;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Options for the native DeepSeek Harness session provider.</summary>
public sealed class DshSessionOptions
{
    /// <summary>DSH's session projection cache (read-only for FluidBar).</summary>
    public required string ProjectionDirectory { get; init; }

    /// <summary>Only projection files changed within this window are considered.</summary>
    public TimeSpan ActiveWindow { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Maximum sessions kept as rows.</summary>
    public int MaxSessions { get; init; } = 8;

    /// <summary>Disables the provider (tests/headless hosts).</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Native DeepSeek Harness status: reads DSH's local session projection cache and derives running/
/// waiting/completed rows. No plugin, no script, no configuration - the files already exist because DSH
/// writes them for its own UI. Nothing is ever written back.
/// </summary>
public sealed class DshSessionProvider : IAgentStatusProvider
{
    private const string DefaultTitle = "DeepSeek Harness";

    private readonly DshSessionOptions _options;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public DshSessionProvider(DshSessionOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProjectionDirectory);
    }

    /// <inheritdoc />
    public string ProviderId => "dsh";

    /// <inheritdoc />
    public string DisplayName => "DeepSeek Harness";

    /// <inheritdoc />
    public string DataPath => _options.ProjectionDirectory;

    /// <inheritdoc />
    public bool IsAvailable => _options.Enabled && Directory.Exists(_options.ProjectionDirectory);

    /// <inheritdoc />
    public DateTimeOffset? LastActivityUtc => _lastActivity;

    /// <inheritdoc />
    public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var events = new List<AgentProviderEvent>();
        try
        {
            var cutoff = now - _options.ActiveWindow;
            var allFiles = new DirectoryInfo(_options.ProjectionDirectory)
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .Where(file => file.LastWriteTimeUtc >= cutoff.UtcDateTime)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            if (_knownSessions is null)
            {
                // Everything that already exists when FluidBar starts is adopted silently; only sessions
                // that appear later belong to a run this app actually watched.
                _knownSessions = new HashSet<string>(
                    allFiles.Select(file => Path.GetFileNameWithoutExtension(file.Name)), StringComparer.Ordinal);
            }

            foreach (var file in allFiles.Take(Math.Max(8, _options.MaxSessions * 4)))
            {
                try
                {
                    ScanSession(file, now, events);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // A file being rewritten mid-read is normal; the next poll sees the complete version.
                }
            }

            PruneStates(cutoff);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Directory unavailable; report nothing this tick.
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private void ScanSession(FileInfo file, DateTimeOffset now, List<AgentProviderEvent> events)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
        if (!document.RootElement.TryGetProperty("record", out var record)
            || !record.TryGetProperty("rows", out var rows)
            || rows.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var sessionId = Path.GetFileNameWithoutExtension(file.Name);
        var project = LeafName(ReadString(rows, "workingDirectory"));
        var sessionTitle = ReadSessionTitle(rows);
        var openStep = ReadOpenStep(rows);
        var stepKind = ReadLastStepKind(rows);
        var lastTurn = ReadLastTurn(rows);
        var question = ReadActiveQuestion(rows);
        // While a tool executes the projection briefly has no openStep but the step boundary is still
        // "start"; that is running, not completed.
        var stepOpen = openStep is not null || string.Equals(stepKind, "start", StringComparison.OrdinalIgnoreCase);

        var firstSight = !_states.TryGetValue(sessionId, out var state);
        if (firstSight)
        {
            state = new SessionState();
            _states[sessionId] = state;
            state.CreatedDuringSession = !_knownSessions!.Contains(sessionId);
        }

        state.Project = project;
        state.SessionTitle = sessionTitle ?? state.SessionTitle;
        state.LastSeen = now;
        _knownSessions!.Add(sessionId);

        if (stepOpen)
        {
            state.WasRunning = true;
            state.IdleScans = 0;
            state.LastTurn = Math.Max(state.LastTurn, lastTurn);
            state.StartedAt ??= openStep?.StartedAt;

            var (eventName, message) = question is { Length: > 0 }
                ? ("waiting", "等待输入：" + question)
                : openStep is { PendingTool: { Length: > 0 } pendingTool }
                    ? ("running", "正在使用 " + pendingTool)
                    : openStep is { } step
                        ? ("running", "第 " + step.Turn + " 轮 · 第 " + step.Step + " 步")
                        : ("running", "工具执行中");

            events.Add(new AgentProviderEvent(sessionId, eventName, TitleFor(project), message, now,
                state.StartedAt ?? openStep?.StartedAt)
            {
                SessionTitle = state.SessionTitle,
                Project = project,
                Action = openStep?.PendingTool is { Length: > 0 } tool ? DescribeTool(tool) : null,
            });
        }
        else if (state.WasRunning && lastTurn > state.AdoptedTurn)
        {
            state.WasRunning = false;
            state.AdoptedTurn = lastTurn;
            events.Add(new AgentProviderEvent(sessionId, "completed", TitleFor(state.Project), "已完成", now,
                state.StartedAt)
            {
                SessionTitle = state.SessionTitle,
                Project = state.Project,
            });
        }
        else if (state.CreatedDuringSession && !state.CompletionEmitted && lastTurn > state.AdoptedTurn)
        {
            // Headless DSH runs publish only the closed projection (no sessionStats row), so the row is
            // created directly as completed - but only for runs that appeared while FluidBar watched.
            state.AdoptedTurn = lastTurn;
            state.CompletionEmitted = true;
            var startedAt = state.StartedAt ?? file.CreationTimeUtc;
            events.Add(new AgentProviderEvent(sessionId, "completed", TitleFor(state.Project), "已完成", now,
                startedAt)
            {
                SessionTitle = state.SessionTitle,
                Project = state.Project,
            });
        }
        else if (!state.WasRunning)
        {
            state.AdoptedTurn = Math.Max(state.AdoptedTurn, lastTurn);
        }
    }

    private void PruneStates(DateTimeOffset cutoff)
    {
        if (_states.Count <= _options.MaxSessions * 4)
        {
            return;
        }

        foreach (var pair in _states.ToArray())
        {
            if (!pair.Value.WasRunning && pair.Value.LastSeen < cutoff)
            {
                _states.Remove(pair.Key);
                _knownSessions?.Remove(pair.Key);
            }
        }
    }

    private static string TitleFor(string project)
        => string.IsNullOrWhiteSpace(project) ? DefaultTitle : DefaultTitle + " · " + Trim(project, 32);

    /// <summary>
    /// Session name for the compact capsule: DSH's projection "title" row, else the first user request
    /// ("titleInput.first.text"). Trimmed so an overlong prompt never dominates the pill.
    /// </summary>
    private static string? ReadSessionTitle(JsonElement rows)
    {
        var title = ReadString(rows, "title");
        if (!string.IsNullOrWhiteSpace(title))
        {
            return Trim(title.Trim().ReplaceLineEndings(" "), 60);
        }

        if (ReadVal(rows, "titleInput") is { ValueKind: JsonValueKind.Object } input
            && input.TryGetProperty("first", out var first)
            && first.ValueKind == JsonValueKind.Object
            && first.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
            && text.GetString() is { Length: > 0 } prompt)
        {
            return Trim(prompt.Trim().ReplaceLineEndings(" "), 60);
        }

        return null;
    }

    /// <summary>Localized verb for the pending tool id; unknown tools pass through unchanged.</summary>
    private static string DescribeTool(string tool) => tool.ToLowerInvariant() switch
    {
        "read" or "read_file" or "readfile" => "读取文件",
        "write" or "write_file" or "edit" or "edit_file" or "multiedit" or "apply_patch" => "编辑文件",
        "bash" or "shell" or "run" or "run_command" or "run_code" or "powershell" => "运行命令",
        "grep" or "search" or "rg" => "搜索代码",
        "glob" or "find" or "list" or "ls" => "查找文件",
        _ => tool,
    };

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";

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

    private static string? ReadString(JsonElement rows, string rowName)
        => ReadVal(rows, rowName) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? ReadVal(JsonElement rows, string rowName)
    {
        if (!rows.TryGetProperty(rowName, out var row)
            || row.ValueKind != JsonValueKind.Object
            || !row.TryGetProperty("val", out var val))
        {
            return null;
        }

        return val;
    }

    private static DshOpenStep? ReadOpenStep(JsonElement rows)
    {
        if (ReadVal(rows, "sessionStats") is not { ValueKind: JsonValueKind.Object } stats
            || !stats.TryGetProperty("openStep", out var openStep)
            || openStep.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var started = ReadLong(openStep, "startTime") is > 0 and var startTime
            ? DateTimeOffset.FromUnixTimeMilliseconds(startTime)
            : (DateTimeOffset?)null;
        string? pendingTool = null;
        if (stats.TryGetProperty("pendingCalls", out var pending) && pending.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in pending.EnumerateObject())
            {
                pendingTool = property.Name;
                break;
            }
        }

        return new DshOpenStep(ReadInt(openStep, "turn"), ReadInt(openStep, "step"), started, pendingTool);
    }

    private static string? ReadLastStepKind(JsonElement rows)
    {
        if (ReadVal(rows, "turnBoundary") is not { ValueKind: JsonValueKind.Object } boundary
            || !boundary.TryGetProperty("lastStepBoundary", out var lastStep)
            || lastStep.ValueKind != JsonValueKind.Object
            || !lastStep.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return kind.GetString();
    }

    private static int ReadLastTurn(JsonElement rows)
    {
        if (ReadVal(rows, "turnBoundary") is { ValueKind: JsonValueKind.Object } boundary)
        {
            var turn = ReadInt(boundary, "lastTurn");
            if (turn > 0)
            {
                return turn;
            }
        }

        return ReadVal(rows, "sessionStats") is { ValueKind: JsonValueKind.Object } stats
            ? ReadInt(stats, "lastTurn")
            : 0;
    }

    private static string? ReadActiveQuestion(JsonElement rows)
    {
        if (ReadVal(rows, "userQuestions") is not { ValueKind: JsonValueKind.Object } questions
            || !questions.TryGetProperty("questions", out var inner)
            || inner.ValueKind != JsonValueKind.Object
            || !inner.TryGetProperty("active", out var active)
            || active.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in active.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in new[] { "question", "title", "text" })
            {
                if (item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return Trim(value.GetString()!.ReplaceLineEndings(" ").Trim(), 160);
                }
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var real) => (int)real,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static long? ReadLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.Number when value.TryGetDouble(out var real) => (long)real,
            JsonValueKind.String when long.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private readonly record struct DshOpenStep(int Turn, int Step, DateTimeOffset? StartedAt, string? PendingTool);

    private sealed class SessionState
    {
        public DateTimeOffset? StartedAt { get; set; }

        public int LastTurn { get; set; }

        public int AdoptedTurn { get; set; }

        public int IdleScans { get; set; }

        public bool WasRunning { get; set; }

        public bool CreatedDuringSession { get; set; }

        public bool CompletionEmitted { get; set; }

        public string Project { get; set; } = string.Empty;

        public string? SessionTitle { get; set; }

        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
}
