using System.Collections.Immutable;
using System.Text.Json;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Which AI coding agent produced the event.</summary>
public enum AgentKind
{
    Generic,
    Claude,
    Codex,
}

/// <summary>Normalized agent lifecycle event.</summary>
public enum AgentEventKind
{
    Other,
    Completed,
    Failed,
    WaitingForInput,
}

/// <summary>Immutable agent event surfaced as an island activity.</summary>
public sealed record AgentEventEntry(
    string Id,
    AgentKind Agent,
    AgentEventKind Kind,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    string SourceFile);

/// <summary>Immutable bounded snapshot of recent agent events (newest first).</summary>
public sealed record AgentEventSnapshot(ImmutableArray<AgentEventEntry> Recent)
{
    public static readonly AgentEventSnapshot Empty = new(ImmutableArray<AgentEventEntry>.Empty);

    public AgentEventEntry? Latest => Recent.IsEmpty ? null : Recent[0];
}

/// <summary>Raw inbox payload written by a hook script (one JSON file per event).</summary>
public sealed record AgentInboxFile(
    uint SchemaVersion,
    string Agent,
    string Event,
    string? Title,
    string? Message,
    DateTimeOffset? Timestamp);

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

            file = new AgentInboxFile(schemaVersion, agent, eventName,
                GetString(document.RootElement, "title"),
                GetString(document.RootElement, "message"),
                timestamp);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid inbox JSON: {ex.Message}";
            return false;
        }
    }

    /// <summary>Converts a parsed file into a bounded entry (stable id = agent + event + source file).</summary>
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
                _ => file.Event,
            };
        }

        var message = Truncate(file.Message, maxTextCharacters * 2);
        return new AgentEventEntry(
            Id: $"{file.Agent}:{file.Event}:{Path.GetFileNameWithoutExtension(sourceFile)}",
            Agent: agent,
            Kind: kind,
            Title: title,
            Message: message,
            Timestamp: file.Timestamp ?? fallbackTimestamp,
            SourceFile: sourceFile);
    }

    /// <summary>Maps agent id strings from hooks.</summary>
    public static AgentKind MapAgent(string? agent) => agent?.Trim().ToLowerInvariant() switch
    {
        "claude" or "claude-code" or "claude_code" => AgentKind.Claude,
        "codex" or "openai-codex" or "codex-cli" => AgentKind.Codex,
        _ => AgentKind.Generic,
    };

    /// <summary>Maps event id strings from hooks.</summary>
    public static AgentEventKind MapKind(string? eventName) => eventName?.Trim().ToLowerInvariant() switch
    {
        "completed" or "complete" or "done" or "stop" or "success" => AgentEventKind.Completed,
        "failed" or "failure" or "error" or "errored" => AgentEventKind.Failed,
        "waiting" or "waiting_for_input" or "waitingforinput" or "input" or "notification" =>
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

    /// <summary>Adds an entry unless its id was already seen. Returns the current snapshot.</summary>
    public AgentEventSnapshot Add(AgentEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            if (!_ids.Add(entry.Id))
            {
                return BuildSnapshotLocked();
            }

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
