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

        lock (_stateGate)
        {
            if (string.Equals(latest.Id, _lastEntryId, StringComparison.Ordinal))
            {
                return;
            }

            _lastEntryId = latest.Id;
        }

        var agentToken = latest.Agent switch
        {
            AgentKind.Claude => "claude",
            AgentKind.Codex => "codex",
            _ => "generic",
        };
        var kindToken = latest.Kind switch
        {
            AgentEventKind.Completed => "completed",
            AgentEventKind.Failed => "failed",
            AgentEventKind.WaitingForInput => "waiting",
            _ => "event",
        };
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = "agent",
            ["title"] = latest.Title,
            ["agent"] = agentToken,
            ["body"] = latest.Message,
            ["badge"] = kindToken,
            ["actions"] = null,
        };

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = string.IsNullOrWhiteSpace(latest.Title) ? kindToken : latest.Title,
            Content = latest.Message,
            IconKind = "agent",
            Kind = IslandEventKind.Agent,
            LayoutId = BuiltinIds.AgentCompactLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = agentToken,
                Badge = kindToken,
                SourceName = agentToken,
                IsActive = latest.Kind == AgentEventKind.WaitingForInput,
                DetailLines = [$"{agentToken}:{kindToken}"],
                Fields = fields,
            },
        });
    }

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastEntryId = string.Empty;
        }
    }
}
