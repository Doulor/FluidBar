using System.Text.Json;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Shared helpers for JSONL transcript providers (Claude Code, Codex, Copilot CLI).</summary>
internal static class TranscriptReader
{
    /// <summary>Reads at most the last <paramref name="maxLines"/> lines of a file (tolerant of a trailing partial line).</summary>
    public static IReadOnlyList<string> Tail(FileInfo file, int maxLines)
    {
        var lines = new Queue<string>(maxLines + 1);
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            lines.Enqueue(line);
            if (lines.Count > maxLines)
            {
                lines.Dequeue();
            }
        }

        return lines.ToArray();
    }

    public static string LeafName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    public static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    public static string? String(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    public static JsonElement? Object(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                return value;
            }
        }

        return null;
    }

    public static double? EpochSeconds(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            _ => null,
        };
    }
}

/// <summary>
/// Native Claude Code status: parses the JSONL transcripts Claude Code already writes under
/// <c>~/.claude/projects/**</c>. No hooks, no settings edits; the transcript is only read.
/// </summary>
public sealed class ClaudeTranscriptProvider : IAgentStatusProvider
{
    private readonly string _projectsDirectory;
    private readonly TimeSpan _activeWindow;
    private readonly TimeSpan _activeThreshold;
    private readonly int _maxSessions;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public ClaudeTranscriptProvider(string projectsDirectory, TimeSpan? activeWindow = null,
        TimeSpan? activeThreshold = null, int maxSessions = 6)
    {
        _projectsDirectory = projectsDirectory;
        _activeWindow = activeWindow ?? TimeSpan.FromMinutes(10);
        _activeThreshold = activeThreshold ?? TimeSpan.FromSeconds(120);
        _maxSessions = maxSessions;
    }

    /// <inheritdoc />
    public string ProviderId => "claude-code";

    /// <inheritdoc />
    public string DisplayName => "Claude Code";

    /// <inheritdoc />
    public string DataPath => _projectsDirectory;

    /// <inheritdoc />
    public bool IsAvailable => Directory.Exists(_projectsDirectory);

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
        var cutoff = now - _activeWindow;
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_projectsDirectory)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .Where(file => file.LastWriteTimeUtc >= cutoff.UtcDateTime)
                .Where(file => !file.DirectoryName!.Contains("subagents", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(8, _maxSessions * 3))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (_knownSessions is null)
        {
            _knownSessions = new HashSet<string>(
                files.Select(file => Path.GetFileNameWithoutExtension(file.Name)), StringComparer.Ordinal);
        }

        foreach (var file in files)
        {
            try
            {
                Scan(file, now, events);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // A transcript being appended to mid-read is normal; retry next poll.
            }
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private void Scan(FileInfo file, DateTimeOffset now, List<AgentProviderEvent> events)
    {
        var sessionId = Path.GetFileNameWithoutExtension(file.Name);
        var lines = TranscriptReader.Tail(file, 256);
        var project = string.Empty;
        var started = (DateTimeOffset?)null;
        var lastTimestamp = new DateTimeOffset(file.LastWriteTime);
        var lastTool = (string?)null;
        var lastStopReason = (string?)null;
        var sawAssistant = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (TranscriptReader.String(root, "cwd") is { Length: > 0 } cwd)
                {
                    project = TranscriptReader.LeafName(cwd);
                }

                if (TranscriptReader.String(root, "timestamp") is { } timestamp
                    && DateTimeOffset.TryParse(timestamp, out var parsedTimestamp))
                {
                    lastTimestamp = parsedTimestamp;
                    started ??= parsedTimestamp;
                }

                if (TranscriptReader.String(root, "type") != "assistant")
                {
                    continue;
                }

                sawAssistant = true;
                if (TranscriptReader.Object(root, "message") is { } message)
                {
                    lastStopReason = TranscriptReader.String(message, "stop_reason");
                    if (message.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.Array)
                    {
                        for (var index = content.GetArrayLength() - 1; index >= 0; index--)
                        {
                            var block = content[index];
                            if (TranscriptReader.String(block, "type") == "tool_use")
                            {
                                lastTool = TranscriptReader.String(block, "name");
                                break;
                            }
                        }
                    }
                }
            }
        }

        var firstSight = !_states.TryGetValue(sessionId, out var state);
        if (firstSight)
        {
            state = new SessionState();
            _states[sessionId] = state;
            state.CreatedDuringSession = !_knownSessions!.Contains(sessionId);
        }

        state.Project = string.IsNullOrEmpty(project) ? state.Project : project;
        state.LastSeen = now;
        _knownSessions!.Add(sessionId);
        state.StartedAt ??= started;

        // "tool_use" means Claude Code paused for a tool and will continue, so the row stays running.
        var active = sawAssistant
                     && (lastStopReason is null || string.Equals(lastStopReason, "tool_use", StringComparison.Ordinal))
                     && now - lastTimestamp <= _activeThreshold;
        var title = "Claude Code" + (string.IsNullOrEmpty(state.Project) ? string.Empty : " · " + TranscriptReader.Trim(state.Project, 32));
        if (active)
        {
            state.WasRunning = true;
            state.CompletionEmitted = false;
            var detail = lastTool is { Length: > 0 } ? "正在使用 " + lastTool : "会话进行中";
            events.Add(new AgentProviderEvent(sessionId, "running", title, detail, now, state.StartedAt));
        }
        else if (state.WasRunning)
        {
            state.WasRunning = false;
            state.CompletionEmitted = true;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, state.StartedAt));
        }
        else if (state.CreatedDuringSession && !state.CompletionEmitted && sawAssistant)
        {
            state.CompletionEmitted = true;
            // A transcript that was already old when the row appeared is still a real session record; the
            // elapsed label uses the observation time instead of a week-old start.
            var startedAt = state.StartedAt is { } recorded && now - recorded <= TimeSpan.FromHours(6)
                ? recorded
                : file.CreationTimeUtc;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, startedAt));
        }
    }

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
/// Native Codex status: parses the rollout JSONL files Codex already writes under
/// <c>~/.codex/sessions/**</c> (task_started / task_complete / function_call events).
/// </summary>
public sealed class CodexSessionProvider : IAgentStatusProvider
{
    private readonly string _sessionsDirectory;
    private readonly TimeSpan _activeThreshold;
    private readonly int _maxSessions;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public CodexSessionProvider(string sessionsDirectory, TimeSpan? activeThreshold = null, int maxSessions = 6)
    {
        _sessionsDirectory = sessionsDirectory;
        _activeThreshold = activeThreshold ?? TimeSpan.FromSeconds(120);
        _maxSessions = maxSessions;
    }

    public string ProviderId => "codex";

    public string DisplayName => "Codex";

    public string DataPath => _sessionsDirectory;

    public bool IsAvailable => Directory.Exists(_sessionsDirectory);

    public DateTimeOffset? LastActivityUtc => _lastActivity;

    public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var events = new List<AgentProviderEvent>();
        var cutoff = now - TimeSpan.FromDays(2);
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_sessionsDirectory)
                .EnumerateFiles("rollout-*.jsonl", SearchOption.AllDirectories)
                .Where(file => file.LastWriteTimeUtc >= cutoff.UtcDateTime)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(8, _maxSessions * 3))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (_knownSessions is null)
        {
            _knownSessions = new HashSet<string>(
                files.Select(file => Path.GetFileNameWithoutExtension(file.Name)), StringComparer.Ordinal);
        }

        foreach (var file in files)
        {
            try
            {
                Scan(file, now, events);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private void Scan(FileInfo file, DateTimeOffset now, List<AgentProviderEvent> events)
    {
        var sessionId = Path.GetFileNameWithoutExtension(file.Name);
        var lines = TranscriptReader.Tail(file, 256);
        var project = string.Empty;
        var started = (DateTimeOffset?)null;
        var lastTimestamp = new DateTimeOffset(file.LastWriteTime);
        var lastKind = string.Empty;
        var lastTool = (string?)null;
        var completed = false;
        var sawTask = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (TranscriptReader.String(root, "timestamp") is { } timestamp
                    && DateTimeOffset.TryParse(timestamp, out var parsed)) 
                {
                    lastTimestamp = parsed;
                }

                if (TranscriptReader.Object(root, "payload") is not { } payload)
                {
                    continue;
                }

                if (TranscriptReader.String(payload, "cwd") is { Length: > 0 } cwd)
                {
                    project = TranscriptReader.LeafName(cwd);
                }

                if (TranscriptReader.String(payload, "type") is not { } kind)
                {
                    continue;
                }

                if (kind == "task_started")
                {
                    sawTask = true;
                    completed = false;
                    lastKind = kind;
                    if (TranscriptReader.EpochSeconds(payload, "started_at") is { } startedAt)
                    {
                        started ??= DateTimeOffset.FromUnixTimeSeconds((long)startedAt);
                    }
                }
                else if (kind == "task_complete")
                {
                    sawTask = true;
                    completed = true;
                    lastKind = kind;
                }
                else if (kind == "function_call")
                {
                    lastTool = TranscriptReader.String(payload, "name");
                    lastKind = kind;
                }
            }
        }

        var firstSight = !_states.TryGetValue(sessionId, out var state);
        if (firstSight)
        {
            state = new SessionState();
            _states[sessionId] = state;
            state.CreatedDuringSession = !_knownSessions!.Contains(sessionId);
        }

        state.Project = string.IsNullOrEmpty(project) ? state.Project : project;
        state.LastSeen = now;
        _knownSessions!.Add(sessionId);
        state.StartedAt ??= started;

        var active = sawTask && !completed && now - lastTimestamp <= _activeThreshold;
        var title = "Codex" + (string.IsNullOrEmpty(state.Project) ? string.Empty : " · " + TranscriptReader.Trim(state.Project, 32));
        if (active)
        {
            state.WasRunning = true;
            state.CompletionEmitted = false;
            var detail = lastTool is { Length: > 0 } ? "正在使用 " + lastTool : "会话进行中";
            events.Add(new AgentProviderEvent(sessionId, "running", title, detail, now, state.StartedAt));
        }
        else if (state.WasRunning)
        {
            state.WasRunning = false;
            state.CompletionEmitted = true;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, state.StartedAt));
        }
        else if (state.CreatedDuringSession && !state.CompletionEmitted && sawTask)
        {
            state.CompletionEmitted = true;
            var startedAt = state.StartedAt is { } recorded && now - recorded <= TimeSpan.FromHours(6)
                ? recorded
                : file.CreationTimeUtc;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, startedAt));
        }
    }

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
/// Native GitHub Copilot CLI status: parses <c>~/.copilot/session-state/&lt;id&gt;/events.jsonl</c>
/// (session.start / tool events / session.end).
/// </summary>
public sealed class CopilotSessionProvider : IAgentStatusProvider
{
    private readonly string _stateDirectory;
    private readonly TimeSpan _activeThreshold;
    private readonly int _maxSessions;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public CopilotSessionProvider(string stateDirectory, TimeSpan? activeThreshold = null, int maxSessions = 6)
    {
        _stateDirectory = stateDirectory;
        _activeThreshold = activeThreshold ?? TimeSpan.FromSeconds(120);
        _maxSessions = maxSessions;
    }

    public string ProviderId => "copilot";

    public string DisplayName => "GitHub Copilot";

    public string DataPath => _stateDirectory;

    public bool IsAvailable => Directory.Exists(_stateDirectory);

    public DateTimeOffset? LastActivityUtc => _lastActivity;

    public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
    {
        if (!IsAvailable)
        {
            return [];
        }

        var events = new List<AgentProviderEvent>();
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_stateDirectory)
                .EnumerateFiles("events.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(8, _maxSessions * 3))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (_knownSessions is null)
        {
            _knownSessions = new HashSet<string>(
                files.Select(file => file.Directory!.Name), StringComparer.Ordinal);
        }

        foreach (var file in files)
        {
            try
            {
                Scan(file, now, events);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private void Scan(FileInfo file, DateTimeOffset now, List<AgentProviderEvent> events)
    {
        var sessionId = file.Directory!.Name;
        var lines = TranscriptReader.Tail(file, 256);
        var project = string.Empty;
        var started = (DateTimeOffset?)null;
        var lastTimestamp = new DateTimeOffset(file.LastWriteTime);
        var lastTool = (string?)null;
        var ended = false;
        var sawSession = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (TranscriptReader.String(root, "timestamp") is { } timestamp
                    && DateTimeOffset.TryParse(timestamp, out var parsed))
                {
                    lastTimestamp = parsed;
                    started ??= parsed;
                }

                if (TranscriptReader.String(root, "type") is not { } type)
                {
                    continue;
                }

                var data = TranscriptReader.Object(root, "data");
                if (type == "session.start")
                {
                    sawSession = true;
                    if (data is { } start && TranscriptReader.Object(start, "context") is { } context
                        && TranscriptReader.String(context, "cwd") is { Length: > 0 } cwd)
                    {
                        project = TranscriptReader.LeafName(cwd);
                    }
                }
                else if (type == "session.end")
                {
                    ended = true;
                }
                else if (type.StartsWith("tool", StringComparison.OrdinalIgnoreCase) && data is { } toolData)
                {
                    lastTool = TranscriptReader.String(toolData, "toolName", "name", "tool");
                }
            }
        }

        var firstSight = !_states.TryGetValue(sessionId, out var state);
        if (firstSight)
        {
            state = new SessionState();
            _states[sessionId] = state;
            state.CreatedDuringSession = !_knownSessions!.Contains(sessionId);
        }

        state.Project = string.IsNullOrEmpty(project) ? state.Project : project;
        state.LastSeen = now;
        _knownSessions!.Add(sessionId);
        state.StartedAt ??= started;

        var active = sawSession && !ended && now - lastTimestamp <= _activeThreshold;
        var title = "GitHub Copilot" + (string.IsNullOrEmpty(state.Project) ? string.Empty : " · " + TranscriptReader.Trim(state.Project, 32));
        if (active)
        {
            state.WasRunning = true;
            state.CompletionEmitted = false;
            var detail = lastTool is { Length: > 0 } ? "正在使用 " + lastTool : "会话进行中";
            events.Add(new AgentProviderEvent(sessionId, "running", title, detail, now, state.StartedAt));
        }
        else if (state.WasRunning)
        {
            state.WasRunning = false;
            state.CompletionEmitted = true;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, state.StartedAt));
        }
        else if (state.CreatedDuringSession && !state.CompletionEmitted && sawSession)
        {
            state.CompletionEmitted = true;
            var startedAt = state.StartedAt is { } recorded && now - recorded <= TimeSpan.FromHours(6)
                ? recorded
                : file.CreationTimeUtc;
            events.Add(new AgentProviderEvent(sessionId, "completed", title, "已完成", now, startedAt));
        }
    }

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
/// Native Aider status: Aider has no structured event file, but it does append to
/// <c>~/.aider.chat.history.md</c> while a chat is active. This provider reports coarse activity
/// (running while the file changed within the last 90 seconds, completed afterwards).
/// </summary>
public sealed class AiderSessionProvider : IAgentStatusProvider
{
    private readonly string _historyPath;
    private readonly TimeSpan _activeThreshold;
    private readonly Dictionary<string, SessionState> _states = new(StringComparer.Ordinal);
    private HashSet<string>? _knownSessions;
    private DateTimeOffset? _lastActivity;

    public AiderSessionProvider(string historyPath, TimeSpan? activeThreshold = null)
    {
        _historyPath = historyPath;
        _activeThreshold = activeThreshold ?? TimeSpan.FromSeconds(90);
    }

    public string ProviderId => "aider";

    public string DisplayName => "Aider";

    public string DataPath => _historyPath;

    public bool IsAvailable => File.Exists(_historyPath);

    public DateTimeOffset? LastActivityUtc => _lastActivity;

    public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
    {
        if (!IsAvailable)
        {
            return [];
        }

        FileInfo file;
        try
        {
            file = new FileInfo(_historyPath);
            if (!file.Exists)
            {
                return [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var sessionId = Path.GetFileNameWithoutExtension(_historyPath);
        var lastWrite = file.LastWriteTimeUtc;
        _knownSessions ??= new HashSet<string>(StringComparer.Ordinal);
        var firstSight = !_states.TryGetValue(sessionId, out var state);
        if (firstSight)
        {
            state = new SessionState { CreatedDuringSession = _knownSessions.Count > 0 };
            _states[sessionId] = state;
        }

        _knownSessions.Add(sessionId);
        state.LastSeen = now;
        var events = new List<AgentProviderEvent>();
        var active = now.UtcDateTime - lastWrite <= _activeThreshold;
        if (active)
        {
            state.StartedAt ??= lastWrite;
            state.WasRunning = true;
            events.Add(new AgentProviderEvent(sessionId, "running", "Aider", "对话进行中", now, state.StartedAt));
        }
        else if (state.WasRunning || state.CreatedDuringSession)
        {
            state.WasRunning = false;
            state.CompletionEmitted = true;
            events.Add(new AgentProviderEvent(sessionId, "completed", "Aider", "已完成", now,
                state.StartedAt ?? lastWrite));
        }

        if (events.Count > 0)
        {
            _lastActivity = now;
        }

        return events;
    }

    private sealed class SessionState
    {
        public DateTimeOffset? StartedAt { get; set; }

        public bool WasRunning { get; set; }

        public bool CompletionEmitted { get; set; }

        public bool CreatedDuringSession { get; set; }

        public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    }
}

/// <summary>Builds the production provider list from the current user's known local paths.</summary>
public static class NativeAgentProviders
{
    /// <summary>Creates the read-only providers for the current user (missing paths simply report unavailable).</summary>
    public static IReadOnlyList<IAgentStatusProvider> CreateDefault(string? userProfile = null)
    {
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dshHome = Environment.GetEnvironmentVariable("DSH_HOME");
        if (string.IsNullOrWhiteSpace(dshHome))
        {
            dshHome = Path.Combine(profile, ".dsh");
        }

        var projection = Environment.GetEnvironmentVariable("FLUIDBAR_DSH_PROJECTION");
        if (string.IsNullOrWhiteSpace(projection))
        {
            projection = Path.Combine(dshHome, "storages", "session_projcache", "sessions");
        }

        return
        [
            new DshSessionProvider(new DshSessionOptions
            {
                ProjectionDirectory = projection,
                Enabled = Environment.GetEnvironmentVariable("FLUIDBAR_DSH_DISABLED") != "1",
            }),
            new ClaudeTranscriptProvider(Environment.GetEnvironmentVariable("FLUIDBAR_CLAUDE_PROJECTS")
                ?? Path.Combine(profile, ".claude", "projects")),
            new CodexSessionProvider(Environment.GetEnvironmentVariable("FLUIDBAR_CODEX_SESSIONS")
                ?? Path.Combine(profile, ".codex", "sessions")),
            new CopilotSessionProvider(Environment.GetEnvironmentVariable("FLUIDBAR_COPILOT_STATE")
                ?? Path.Combine(profile, ".copilot", "session-state")),
            new AiderSessionProvider(Environment.GetEnvironmentVariable("FLUIDBAR_AIDER_HISTORY")
                ?? Path.Combine(profile, ".aider.chat.history.md")),
        ];
    }
}