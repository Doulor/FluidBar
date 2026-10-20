using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// First-party agent-status plugin: publishes agent inbox events and exposes hook install/repair commands.
/// </summary>
/// <remarks>
/// Hook installation is <b>consent-gated</b>: the handler only calls the platform
/// <see cref="IAgentHookInstaller"/> when the command comes from the settings UI <b>and</b> carries
/// <c>consent=true</c>. There is no timer and no automatic install path — nothing rewrites external agent
/// configuration without an explicit user action.
/// </remarks>
public sealed class AgentStatusPlugin : BuiltinPluginBase
{
    private readonly IAgentInboxSource _inbox;
    private readonly IAgentHookInstaller _hookInstaller;
    private readonly object _stateGate = new();
    private string _lastEntryId = string.Empty;

    public AgentStatusPlugin(IAgentInboxSource inbox, IAgentHookInstaller hookInstaller)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _hookInstaller = hookInstaller ?? throw new ArgumentNullException(nameof(hookInstaller));
    }

    public override string PluginId => BuiltinIds.AgentPlugin;

    public static PluginContributions Contributions => BuiltinContributions.ForAgent();

    protected override void OnStarted()
    {
        TrackSubscription(() => _inbox.SnapshotChanged += OnSnapshotChanged,
            () => _inbox.SnapshotChanged -= OnSnapshotChanged);

        RegisterCommand(BuiltinIds.AgentInstallHooks, (context, cancellationToken) =>
            RunHookOperationAsync("install", context, cancellationToken));
        RegisterCommand(BuiltinIds.AgentRepairHooks, (context, cancellationToken) =>
            RunHookOperationAsync("repair", context, cancellationToken));
        RegisterCommand(BuiltinIds.AgentUninstallHooks, (context, cancellationToken) =>
            RunHookOperationAsync("uninstall", context, cancellationToken));

        PublishSnapshot(_inbox.Current);
    }

    /// <summary>Consent rule shared with tests: settings-surface invocation plus explicit consent argument.</summary>
    public static bool IsConsentGranted(PluginCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Source == CommandInvocationSource.Settings
               && context.Arguments is not null
               && context.Arguments.TryGetValue("consent", out var consent)
               && string.Equals(consent, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask RunHookOperationAsync(string operation, PluginCommandContext context,
        CancellationToken cancellationToken)
    {
        if (!IsConsentGranted(context))
        {
            Context.Log.Warning(
                $"{PluginId}: '{operation}' ignored — hook changes require an explicit settings action with consent.");
            Context.Notifications.TryPost(new PluginNotification
            {
                Title = "agent-hooks-consent-required",
                Body = operation,
                Kind = PluginNotificationKind.Warning,
            });
            return;
        }

        var result = operation == "uninstall"
            ? await _hookInstaller.UninstallAsync(userConfirmed: true, cancellationToken).ConfigureAwait(false)
            : await _hookInstaller.InstallAsync(userConfirmed: true, cancellationToken).ConfigureAwait(false);

        Context.Notifications.TryPost(new PluginNotification
        {
            Title = result.Success ? $"agent-hooks-{operation}-ok" : "agent-hooks-operation-failed",
            Body = result.FailureReason,
            Kind = result.Success ? PluginNotificationKind.Success : PluginNotificationKind.Error,
        });
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<AgentEventSnapshot> args)
    {
        if (!IsStarted)
        {
            return;
        }

        PublishSnapshot(args.Snapshot);
    }

    internal void PublishSnapshot(AgentEventSnapshot? snapshot)
    {
        var latest = snapshot?.Latest;
        if (latest is null)
        {
            lock (_stateGate)
            {
                if (_lastEntryId.Length == 0)
                {
                    return;
                }

                _lastEntryId = string.Empty;
            }

            Retract();
            return;
        }

        var agentToken = latest.Agent switch
        {
            AgentKind.Claude => "claude",
            AgentKind.Codex => "codex",
            AgentKind.DeepSeek => "deepseek",
            AgentKind.Cursor => "cursor",
            AgentKind.Windsurf => "windsurf",
            AgentKind.Aider => "aider",
            AgentKind.Copilot => "copilot",
            AgentKind.Zcode => "zcode",
            AgentKind.Qoder => "qoder",
            AgentKind.WorkBuddy => "workbuddy",
            _ => "generic",
        };
        var kindToken = latest.Kind switch
        {
            AgentEventKind.Completed => "completed",
            AgentEventKind.Failed => "failed",
            AgentEventKind.WaitingForInput => "waiting",
            AgentEventKind.Running => "running",
            _ => "event",
        };

        // While an agent is running the platform keeps emitting the same session/status pair; those
        // heartbeats must republish the activity so its TTL (5 s by default) never expires while the
        // session is still alive - otherwise the compact capsule would fall back to media/idle and the
        // user would think the agent stopped. Terminal rows keep the dedupe so they do not repeat.
        var active = latest.Kind is AgentEventKind.Running or AgentEventKind.WaitingForInput;
        lock (_stateGate)
        {
            // The session id is stable across running → completed, so the dedupe key includes the
            // status token: a finished session must still replace the live "running" activity.
            var stateKey = latest.Id + "|" + kindToken;
            if (!active && string.Equals(stateKey, _lastEntryId, StringComparison.Ordinal))
            {
                return;
            }

            _lastEntryId = stateKey;
        }
        // The compact capsule shows what the agent is doing, not its brand: action (tool + target) →
        // session/task name → status description (the presenter localizes the empty case). The brand stays
        // in the expanded row (AgentContentMapper.Name / the page's tool line).
        var capsuleText = !string.IsNullOrWhiteSpace(latest.Action)
            ? latest.Action!
            : !string.IsNullOrWhiteSpace(latest.SessionTitle)
                ? latest.SessionTitle!
                : IsBrandTitle(latest.Title) ? string.Empty : latest.Title ?? string.Empty;
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = "agent",
            ["title"] = capsuleText,
            ["agent"] = agentToken,
            ["body"] = latest.Message,
            ["badge"] = kindToken,
            ["actions"] = null,
        };

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = capsuleText,
            Content = latest.Message,
            IconKind = "agent",
            Kind = IslandEventKind.Agent,
            LayoutId = BuiltinIds.AgentCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = agentToken,
                Badge = kindToken,
                SourceName = agentToken,
                IsActive = latest.Kind is AgentEventKind.WaitingForInput or AgentEventKind.Running,
                DetailLines = [$"{agentToken}:{kindToken}"],
                Fields = fields,
            },
        });
    }

    /// <summary>
    /// True when a legacy inbox row's Title is just the agent's brand (or "Brand · project"): the compact
    /// capsule must show the status instead of that brand. Emitters that send a task name keep it.
    /// </summary>
    private static bool IsBrandTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        string[] brands =
        [
            "DeepSeek Harness", "Claude Code", "Codex", "Cursor", "Windsurf", "Aider",
            "GitHub Copilot", "zcode", "Qoder", "WorkBuddy", "Agent",
        ];
        foreach (var brand in brands)
        {
            if (title.Equals(brand, StringComparison.OrdinalIgnoreCase)
                || title.StartsWith(brand + " · ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastEntryId = string.Empty;
        }
    }
}
