using System.Collections.Immutable;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.App.State;

/// <summary>
/// Maps the bounded agent inbox snapshot onto island agent-page rows. Pure: formatting only (status
/// labels, elapsed-duration text, tones), so the platform watcher and the UI never disagree.
/// </summary>
public static class AgentContentMapper
{
    /// <summary>Rows the agent page paints (newest first).</summary>
    public const int MaxRows = 8;

    /// <summary>Maps the snapshot to page rows; empty when no session exists.</summary>
    /// <param name="snapshot">Agent inbox snapshot (null before the watcher produced one).</param>
    /// <param name="now">Reference time for a running session's elapsed duration.</param>
    /// <param name="localizer">Localizer; English fallbacks when null (tests/headless).</param>
    public static ImmutableArray<AgentItemData> Map(
        AgentEventSnapshot? snapshot, DateTimeOffset now, ILocalizer? localizer = null)
    {
        if (snapshot is null || snapshot.Recent.IsEmpty)
        {
            return ImmutableArray<AgentItemData>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<AgentItemData>(Math.Min(MaxRows, snapshot.Recent.Length));
        foreach (var entry in snapshot.Recent)
        {
            if (builder.Count >= MaxRows)
            {
                break;
            }

            var running = entry.Kind is AgentEventKind.Running or AgentEventKind.Other;
            var statusKey = entry.Kind switch
            {
                AgentEventKind.Completed => LocKeys.Agent.Completed,
                AgentEventKind.Failed => LocKeys.Agent.Failed,
                AgentEventKind.WaitingForInput => LocKeys.Agent.Waiting,
                _ => LocKeys.Agent.Running,
            };
            var statusFallback = entry.Kind switch
            {
                AgentEventKind.Completed => "Completed",
                AgentEventKind.Failed => "Failed",
                AgentEventKind.WaitingForInput => "Waiting",
                _ => "Running",
            };

            builder.Add(new AgentItemData
            {
                Name = string.IsNullOrWhiteSpace(entry.Title)
                    ? AgentDisplayName(entry.Agent)
                    : entry.Title,
                StatusText = localizer?.Get(statusKey) ?? statusFallback,
                Tone = entry.Kind switch
                {
                    AgentEventKind.Completed => StatusTone.Success,
                    AgentEventKind.Failed => StatusTone.Danger,
                    AgentEventKind.WaitingForInput => StatusTone.Warning,
                    _ => StatusTone.Info,
                },
                TimeText = FormatDuration(entry, now, running, localizer)
                    ?? entry.Timestamp.ToLocalTime().ToString("HH:mm"),
                Detail = entry.Message,
                Kind = entry.Agent,
                SessionId = entry.SessionId,
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>Display name for an agent kind (brand names are not translated).</summary>
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

    /// <summary>
    /// Elapsed-duration label ("已运行 3 分 12 秒", "耗时 2 分 5 秒"); null when the emitter supplied no
    /// start time (legacy v1 payloads), so the row falls back to its HH:mm timestamp.
    /// </summary>
    public static string? FormatDuration(AgentEventEntry entry, DateTimeOffset now, bool running,
        ILocalizer? localizer = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.StartedAt is not { } startedAt)
        {
            return null;
        }

        var end = running ? now : entry.Timestamp;
        var elapsed = end - startedAt;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var minutes = (int)elapsed.TotalMinutes;
        var seconds = elapsed.Seconds;
        return running
            ? localizer?.Format(LocKeys.Agent.DurationRunning, ("minutes", minutes), ("seconds", seconds))
                ?? $"Running {minutes}m {seconds}s"
            : localizer?.Format(LocKeys.Agent.DurationDone, ("minutes", minutes), ("seconds", seconds))
                ?? $"Took {minutes}m {seconds}s";
    }
}
