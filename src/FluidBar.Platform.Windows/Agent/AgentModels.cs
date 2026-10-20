using System.Collections.Immutable;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Which AI coding agent produced the event.</summary>
public enum AgentKind
{
    Generic,
    Claude,
    Codex,

    /// <summary>DeepSeek Harness and compatible CLI agents.</summary>
    DeepSeek,

    /// <summary>Cursor IDE/agent.</summary>
    Cursor,

    /// <summary>Windsurf (Codeium) agent.</summary>
    Windsurf,

    /// <summary>Aider pair-programming CLI.</summary>
    Aider,

    /// <summary>GitHub Copilot CLI/agent.</summary>
    Copilot,

    /// <summary>Zhipu zcode CLI.</summary>
    Zcode,

    /// <summary>Qoder agent.</summary>
    Qoder,

    /// <summary>WorkBuddy agent.</summary>
    WorkBuddy,
}

/// <summary>Normalized agent lifecycle event.</summary>
public enum AgentEventKind
{
    Other,

    /// <summary>The session is actively running (optionally with a tool detail).</summary>
    Running,

    Completed,
    Failed,
    WaitingForInput,
}

/// <summary>Immutable agent event surfaced as an island activity.</summary>
/// <param name="Id">Stable id; session-scoped events share one id so a session merges into one row.</param>
/// <param name="Agent">Agent kind.</param>
/// <param name="Kind">Normalized lifecycle kind.</param>
/// <param name="Title">Row title (agent display name or session title).</param>
/// <param name="Message">Detail line; failure details include the error text.</param>
/// <param name="Timestamp">Last time this session was updated.</param>
/// <param name="SourceFile">Inbox file that produced the entry.</param>
/// <param name="SessionId">Agent session id when the emitter supplies one (merge key).</param>
/// <param name="StartedAt">When the session/run started (duration base).</param>
public sealed record AgentEventEntry(
    string Id,
    AgentKind Agent,
    AgentEventKind Kind,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    string SourceFile,
    string? SessionId = null,
    DateTimeOffset? StartedAt = null);

/// <summary>Immutable bounded snapshot of recent agent events (newest first).</summary>
public sealed record AgentEventSnapshot(ImmutableArray<AgentEventEntry> Recent)
{
    public static readonly AgentEventSnapshot Empty = new(ImmutableArray<AgentEventEntry>.Empty);

    public AgentEventEntry? Latest => Recent.IsEmpty ? null : Recent[0];
}

/// <summary>
/// Raw inbox payload written by a hook script (one JSON file per event). Schema v2 adds the optional
/// <see cref="SessionId"/>/<see cref="StartedAt"/>/<see cref="Error"/> fields; v1 payloads stay valid.
/// </summary>
/// <param name="SchemaVersion">Payload schema version (1 or 2).</param>
/// <param name="Agent">Agent id ("claude-code", "dsh", "zcode", ...).</param>
/// <param name="Event">Hook event name ("running", "completed", "failed", "waiting", ...).</param>
/// <param name="Title">Optional row title.</param>
/// <param name="Message">Optional detail line.</param>
/// <param name="Timestamp">Event time (defaults to file time).</param>
/// <param name="SessionId">Agent session id; consecutive events with the same id merge into one row.</param>
/// <param name="StartedAt">Run start time for the elapsed-duration display.</param>
/// <param name="Error">Failure text; folded into the detail line.</param>
public sealed record AgentInboxFile(
    uint SchemaVersion,
    string Agent,
    string Event,
    string? Title,
    string? Message,
    DateTimeOffset? Timestamp,
    string? SessionId = null,
    DateTimeOffset? StartedAt = null,
    string? Error = null);

/// <summary>Pure inbox JSON parser; invalid payloads are quarantined, never displayed.</summary>
public static class AgentEventParser
{
    /// <summary>Parses one inbox file. Returns false (with a reason) for malformed/unknown payloads.</summary>
    public static bool TryParse(string? json, out AgentInboxFile file, out string? error)
    {
        file = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Empty inbox file.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Inbox payload is not a JSON object.";
                return false;
            }

            var agent = GetString(document.RootElement, "agent")?.Trim();
            var eventName = GetString(document.RootElement, "event")?.Trim();
            if (string.IsNullOrEmpty(agent) || string.IsNullOrEmpty(eventName))
            {
                error = "Inbox payload is missing 'agent' or 'event'.";
                return false;
            }

            uint schemaVersion = 1;
            if (document.RootElement.TryGetProperty("schemaVersion", out var schema)
                && schema.ValueKind == JsonValueKind.Number && schema.TryGetUInt32(out var parsedSchema))
            {
                schemaVersion = parsedSchema;
            }

            DateTimeOffset? timestamp = null;
            var timestampText = GetString(document.RootElement, "timestamp");
            if (DateTimeOffset.TryParse(timestampText, out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            DateTimeOffset? startedAt = null;
            var startedAtText = GetString(document.RootElement, "startedAt");
            if (DateTimeOffset.TryParse(startedAtText, out var parsedStartedAt))
            {
                startedAt = parsedStartedAt;
            }

            file = new AgentInboxFile(schemaVersion, agent, eventName,
                GetString(document.RootElement, "title"),
                GetString(document.RootElement, "message"),
                timestamp,
                SessionId: SanitizeId(GetString(document.RootElement, "sessionId")),
                StartedAt: startedAt,
                Error: GetString(document.RootElement, "error"));
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid inbox JSON: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Converts a parsed file into a bounded entry. Session-scoped events keep one stable id
    /// (<c>session:&lt;id&gt;</c>) so the history merges repeated running/completed updates into one row;
    /// legacy v1 payloads keep the per-file id.
    /// </summary>
    public static AgentEventEntry ToEntry(AgentInboxFile file, string sourceFile, DateTimeOffset fallbackTimestamp,
        int maxTextCharacters = 512)
    {
        ArgumentNullException.ThrowIfNull(file);
        var agent = MapAgent(file.Agent);
        var kind = MapKind(file.Event);
        var title = Truncate(file.Title, maxTextCharacters);
        if (string.IsNullOrEmpty(title))
        {
            title = kind switch
            {
                AgentEventKind.Completed => "completed",
                AgentEventKind.Failed => "failed",
                AgentEventKind.WaitingForInput => "waiting",
                AgentEventKind.Running => "running",
                _ => file.Event,
            };
        }

        // Failure details carry the error text into the detail line; the message stays useful when the
        // hook supplies both ("tool crashed · <error>").
        var message = Truncate(file.Message, maxTextCharacters * 2);
        var errorText = Truncate(file.Error, maxTextCharacters * 2);
        if (!string.IsNullOrEmpty(errorText))
        {
            message = string.IsNullOrEmpty(message) ? errorText : $"{message} · {errorText}";
            if (message.Length > maxTextCharacters * 2)
            {
                message = Truncate(message, maxTextCharacters * 2);
            }
        }

        var sessionId = SanitizeId(file.SessionId);
        var id = sessionId is null
            ? $"{file.Agent}:{file.Event}:{Path.GetFileNameWithoutExtension(sourceFile)}"
            : $"session:{sessionId}";
        var timestamp = file.Timestamp ?? fallbackTimestamp;
        return new AgentEventEntry(
            Id: id,
            Agent: agent,
            Kind: kind,
            Title: title,
            Message: message,
            Timestamp: timestamp,
            SourceFile: sourceFile,
            SessionId: sessionId,
            StartedAt: file.StartedAt ?? timestamp);
    }

    private static string? SanitizeId(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 128)
        {
            return string.IsNullOrEmpty(text) ? null : text[..128];
        }

        return text;
    }

    /// <summary>Maps agent id strings from hooks.</summary>
    public static AgentKind MapAgent(string? agent) => agent?.Trim().ToLowerInvariant() switch
    {
        "claude" or "claude-code" or "claude_code" => AgentKind.Claude,
        "codex" or "openai-codex" or "codex-cli" => AgentKind.Codex,
        "deepseek" or "deepseek-harness" or "deepseek_harness" or "dsh" or "harness" => AgentKind.DeepSeek,
        "cursor" or "cursor-agent" or "cursor-cli" => AgentKind.Cursor,
        "windsurf" or "codeium" or "windsurf-agent" => AgentKind.Windsurf,
        "aider" or "aider-chat" or "aider_cli" => AgentKind.Aider,
        "copilot" or "github-copilot" or "copilot-cli" or "gh-copilot" => AgentKind.Copilot,
        "zcode" or "zed-code" or "zhipu-zcode" => AgentKind.Zcode,
        "qoder" or "qoder-cli" => AgentKind.Qoder,
        "workbuddy" or "work-buddy" or "work_buddy" => AgentKind.WorkBuddy,
        _ => AgentKind.Generic,
    };

    /// <summary>Maps event id strings from hooks.</summary>
    public static AgentEventKind MapKind(string? eventName) => eventName?.Trim().ToLowerInvariant() switch
    {
        "running" or "started" or "start" or "working" or "progress" or "tool" or "tool_call" or "step" =>
            AgentEventKind.Running,
        "completed" or "complete" or "done" or "stop" or "success" or "finished" or "idle" => AgentEventKind.Completed,
        "failed" or "failure" or "error" or "errored" => AgentEventKind.Failed,
        "waiting" or "waiting_for_input" or "waitingforinput" or "input" or "notification" or "permission" =>
            AgentEventKind.WaitingForInput,
        _ => AgentEventKind.Other,
    };

    private static string Truncate(string? value, int maxCharacters)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value.ReplaceLineEndings(" ").Trim();
        if (sanitized.Length <= maxCharacters)
        {
            return sanitized;
        }

        return string.Concat(sanitized.AsSpan(0, maxCharacters), "…");
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Pure bounded agent-event history (newest first, duplicate-id suppression).</summary>
public sealed class AgentEventHistory
{
    private readonly object _gate = new();
    private readonly LinkedList<AgentEventEntry> _entries = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public AgentEventHistory(int maxEntries = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        MaxEntries = maxEntries;
    }

    public int MaxEntries { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long Evictions { get; private set; }

    /// <summary>
    /// Adds an entry unless its id was already seen. A session-scoped entry (same
    /// <see cref="AgentEventEntry.SessionId"/>) replaces the existing row in place instead of adding a
    /// second one, so running → completed/failed updates never flood the list; the earliest start time
    /// and the original row position are preserved.
    /// </summary>
    public AgentEventSnapshot Add(AgentEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            var existing = FindNode(entry.Id);
            if (existing is not null)
            {
                if (!string.Equals(entry.SessionId, null, StringComparison.Ordinal))
                {
                    var merged = entry with
                    {
                        StartedAt = existing.Value.StartedAt ?? entry.StartedAt,
                    };
                    existing.Value = merged;
                }

                return BuildSnapshotLocked();
            }

            _ids.Add(entry.Id);
            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                var removed = _entries.Last!.Value;
                _entries.RemoveLast();
                _ids.Remove(removed.Id);
                Evictions++;
            }

            return BuildSnapshotLocked();
        }
    }

    private LinkedListNode<AgentEventEntry>? FindNode(string id)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value.Id, id, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    /// <summary>Current immutable snapshot (newest first).</summary>
    public AgentEventSnapshot Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotLocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _ids.Clear();
        }
    }

    private AgentEventSnapshot BuildSnapshotLocked()
    {
        if (_entries.Count == 0)
        {
            return AgentEventSnapshot.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AgentEventEntry>(_entries.Count);
        foreach (var entry in _entries)
        {
            builder.Add(entry);
        }

        return new AgentEventSnapshot(builder.MoveToImmutable());
    }
}
