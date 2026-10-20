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

            var statusText = localizer?.Get(statusKey) ?? statusFallback;
            builder.Add(new AgentItemData
            {
                // Brand = the expanded row's tool line; the compact capsule never shows it.
                Name = AgentDisplayName(entry.Agent),
                Project = ProjectOf(entry),
                // Priority (user requirement + prototype): current action (tool + target) → session/task
                // name → status description. Brand names are deliberately not part of this chain.
                PrimaryText = FirstNonEmpty(entry.Action, entry.SessionTitle, statusText),
                StatusText = statusText,
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

    /// <summary>Project leaf shown in the expanded row head, falling back to the legacy "Brand · project" title.</summary>
    private static string? ProjectOf(AgentEventEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Project))
        {
            return entry.Project!.Trim();
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
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Elapsed-duration label in prototype2's compact numeric form ("02:14", "48s"); null when the emitter
    /// supplied no start time (legacy v1 payloads), so the row falls back to its HH:mm timestamp.
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

        var totalSeconds = (int)elapsed.TotalSeconds;
        // prototype2 numeric duration: "02:14" (mm:ss), and "0:45" under a minute - never a bare
        // seconds count ("75s" is wrong; a 75 s row must read "01:15").
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes > 0 ? $"{minutes:00}:{seconds:00}" : $"0:{seconds:00}";
    }
}
