using FluidBar.Platform.Windows.Agent;
using FluidBar.Sdk;
using FluidBar.Sdk.Pages;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// Agent plugin-side presentation mapper. The host only receives declarative list rows: icon, text,
/// status and duration never leak into host agent-specific models or renderers.
/// </summary>
internal static class AgentPageMapper
{
    public const int MaxRows = 8;

    public static IReadOnlyList<PageListRow> Map(
        AgentEventSnapshot? snapshot,
        DateTimeOffset now,
        IPluginLocalization localizer)
    {
        if (snapshot is null || snapshot.Recent.IsEmpty)
        {
            return [];
        }

        var rows = new List<PageListRow>(Math.Min(MaxRows, snapshot.Recent.Length));
        foreach (var entry in snapshot.Recent)
        {
            if (rows.Count >= MaxRows)
            {
                break;
            }

            var running = entry.Kind is AgentEventKind.Running or AgentEventKind.Other;
            var statusKey = entry.Kind switch
            {
                AgentEventKind.Completed => "agent.status.completed",
                AgentEventKind.Failed => "agent.status.failed",
                AgentEventKind.WaitingForInput => "agent.status.waiting",
                _ => "agent.status.running",
            };
            var statusFallback = entry.Kind switch
            {
                AgentEventKind.Completed => "Completed",
                AgentEventKind.Failed => "Failed",
                AgentEventKind.WaitingForInput => "Waiting",
                _ => "Running",
            };
            var statusText = localizer.TryGet(statusKey, out var localized) ? localized : statusFallback;
            var primary = FirstNonEmpty(entry.Action, entry.SessionTitle, statusText);
            var project = ProjectOf(entry);
            var subtitle = string.IsNullOrWhiteSpace(project) ? null : project;
            var duration = FormatDuration(entry, now, running);
            var tone = entry.Kind switch
            {
                AgentEventKind.Completed => "success",
                AgentEventKind.Failed => "danger",
                AgentEventKind.WaitingForInput => "warning",
                _ => "info",
            };

            rows.Add(new PageListRow
            {
                RowId = entry.Id,
                IconToken = entry.Kind switch
                {
                    AgentEventKind.Completed => "check",
                    AgentEventKind.Failed => "alert",
                    AgentEventKind.WaitingForInput => "pause",
                    _ => "agent",
                },
                Title = primary,
                Subtitle = subtitle,
                Value = duration,
                Badge = statusText,
                Tone = tone,
            });
        }

        return rows;
    }

    public static string AgentDisplayName(AgentKind agent) => agent switch
    {
        AgentKind.Claude => "Claude Code",
        AgentKind.Codex => "Codex",
        AgentKind.DeepSeek => "DeepSeek Harness",
        AgentKind.Cursor => "Cursor",
        AgentKind.Windsurf => "Windsurf",
        AgentKind.Aider => "Aider",
        AgentKind.Copilot => "GitHub Copilot",
        AgentKind.Zcode => "zcode",
        AgentKind.Qoder => "Qoder",
        AgentKind.WorkBuddy => "WorkBuddy",
        _ => "Agent",
    };

    private static string? ProjectOf(AgentEventEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Project))
        {
            return entry.Project.Trim();
        }

        var title = entry.Title;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var separator = title.LastIndexOf(" · ", StringComparison.Ordinal);
        return separator >= 0 && separator + 3 < title.Length ? title[(separator + 3)..].Trim() : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    public static string? FormatDuration(AgentEventEntry entry, DateTimeOffset now, bool running)
    {
        if (entry.StartedAt is not { } startedAt)
        {
            return null;
        }

        var elapsed = (running ? now : entry.Timestamp) - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var seconds = (int)elapsed.TotalSeconds;
        return seconds >= 60 ? $"{seconds / 60:00}:{seconds % 60:00}" : $"0:{seconds:00}";
    }
}
