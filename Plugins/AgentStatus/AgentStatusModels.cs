using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluidBar;

public sealed record AgentHookEvent(
    string Tool,
    string Status,
    string Project,
    string Summary,
    string? Branch = null,
    long? DurationMs = null,
    string? SessionId = null,
    string? Error = null,
    string? ToolName = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AgentHookEvent Parse(string json)
    {
        return JsonSerializer.Deserialize<AgentHookEvent>(json, JsonOptions)
               ?? throw new InvalidOperationException("Agent hook event is empty.");
    }
}

public static class AgentStatusIslandEventFactory
{
    public static IslandEvent FromHook(AgentHookEvent hook)
    {
        var toolName = FriendlyToolName(hook.Tool);
        var status = FriendlyStatus(hook.Status);
        var isRunning = hook.Status.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                        hook.Status.Equals("waiting", StringComparison.OrdinalIgnoreCase);
        var title = isRunning && !string.IsNullOrWhiteSpace(hook.ToolName)
            ? $"{toolName} - {hook.ToolName}"
            : $"{toolName} {status}";
        var summary = string.IsNullOrWhiteSpace(hook.Summary)
            ? status
            : hook.Summary;
        var details = BuildDetailLines(hook).ToArray();
        var badge = isRunning && !string.IsNullOrWhiteSpace(hook.ToolName)
            ? hook.ToolName
            : status;

        return new IslandEvent(
            Source: "agent-status",
            Title: title,
            Content: summary,
            IconKind: "agent",
            Payload: new IslandEventPayload(
                Kind: IslandEventKind.Agent,
                Subtitle: hook.Project,
                Badge: badge,
                SourceName: toolName,
                ShowsAudioWave: isRunning,
                IsActive: isRunning,
                DetailLines: details));
    }

    public static string FriendlyToolName(string tool)
    {
        var normalized = tool.Trim().ToLowerInvariant();
        return normalized switch
        {
            "claude" or "claude-code" or "claude_code" => "Claude Code",
            "codex" or "codex-cli" or "openai-codex" => "Codex",
            _ => string.IsNullOrWhiteSpace(tool) ? "Agent" : tool
        };
    }

    private static string FriendlyStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "completed" or "complete" or "success" or "succeeded" => "完成",
            "failed" or "error" => "失败",
            "cancelled" or "canceled" => "已取消",
            "running" => "运行中",
            "waiting" => "等待确认",
            _ => string.IsNullOrWhiteSpace(status) ? "状态更新" : status
        };
    }

    private static IEnumerable<string> BuildDetailLines(AgentHookEvent hook)
    {
        if (!string.IsNullOrWhiteSpace(hook.Branch))
            yield return $"分支 {hook.Branch}";
        if (hook.DurationMs is > 0)
            yield return $"耗时 {FormatDuration(hook.DurationMs.Value)}";
        if (!string.IsNullOrWhiteSpace(hook.SessionId))
            yield return $"会话 {hook.SessionId}";
        if (!string.IsNullOrWhiteSpace(hook.Error))
            yield return hook.Error!;
    }

    private static string FormatDuration(long milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(milliseconds);
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}m {span.Seconds}s";
        return $"{Math.Max(1, (int)Math.Round(span.TotalSeconds))}s";
    }
}

