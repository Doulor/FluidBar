using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

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

    public const string AgentPageId = "builtin.agent";

    private const string PageTitleKey = "agent.page.title";

    private bool _pageRegistered;

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

        _pageRegistered = Context.Islands.RegisterPage(new PageDescriptor
        {
            PageId = AgentPageId,
            Order = 40,
            Title = new LocalizedString("Agent"),
            ShortTitle = new LocalizedString("Agent"),
            IconToken = "agent",
            TemplateId = PageTemplates.List,
        });
        if (!_pageRegistered)
        {
            Context.Log.Warning($"{PluginId}: failed to register the agent page.");
        }

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
        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_AGENT_AUDIT_EMPTY"), "1", StringComparison.Ordinal))
        {
            Context.Islands.TryRetract(PluginId);
            PublishPageRows([]);
            return;
        }

        if (IsAuditMode)
        {
            // Keep the visual audit deterministic even when a developer machine has a live inbox.
            PublishPageRows(AuditRows());
            return;
        }

        var latest = snapshot?.Latest;
        if (latest is null)
        {
            lock (_stateGate)
            {
                _lastEntryId = string.Empty;
            }

            Context.Islands.TryRetract(PluginId);
            // Audit/demo runs have no real inbox: publish representative rows so the Agent page is
            // exercised instead of falling through to the renderer's development placeholder.
            PublishPageRows(IsAuditMode ? AuditRows() : []);
            return;
        }

        var active = latest.Kind is AgentEventKind.Running or AgentEventKind.WaitingForInput;
        var statusText = latest.Kind switch
        {
            AgentEventKind.Completed => Context.Localization.TryGet("agent.status.completed", out var completed) ? completed : "Completed",
            AgentEventKind.Failed => Context.Localization.TryGet("agent.status.failed", out var failed) ? failed : "Failed",
            AgentEventKind.WaitingForInput => Context.Localization.TryGet("agent.status.waiting", out var waiting) ? waiting : "Waiting",
            _ => Context.Localization.TryGet("agent.status.running", out var running) ? running : "Running",
        };
        var kindToken = latest.Kind switch
        {
            AgentEventKind.Completed => "completed",
            AgentEventKind.Failed => "failed",
            AgentEventKind.WaitingForInput => "waiting",
            _ => "running",
        };

        // Running heartbeats must keep the compact activity alive; only terminal duplicates are deduped.
        var stateKey = latest.Id + "|" + kindToken;
        lock (_stateGate)
        {
            if (!active && string.Equals(stateKey, _lastEntryId, StringComparison.Ordinal))
            {
                return;
            }

            _lastEntryId = stateKey;
        }

        var capsuleText = !string.IsNullOrWhiteSpace(latest.Action)
            ? latest.Action!
            : !string.IsNullOrWhiteSpace(latest.SessionTitle)
                ? latest.SessionTitle!
                : IsBrandTitle(latest.Title) ? statusText : latest.Title ?? statusText;

        PublishPageRows(AgentPageMapper.Map(snapshot, Context.Time.GetLocalNow(), Context.Localization));

        Context.Islands.PublishActivity(new IslandActivity
        {
            Source = PluginId,
            Title = string.IsNullOrWhiteSpace(capsuleText) ? statusText : capsuleText,
            Kind = IslandActivityKind.Status,
            Subtitle = statusText,
            Body = latest.Message,
            IconToken = "agent",
            Badge = kindToken,
            StatusText = statusText,
            IsActive = active,
        });
    }

    private void PublishPageRows(IReadOnlyList<PageListRow> rows)
    {
        if (!_pageRegistered)
        {
            return;
        }

        Context.Islands.PublishPage(new PageData
        {
            PageId = AgentPageId,
            Revision = 1,
            TemplateId = PageTemplates.List,
            List = new PageListData
            {
                EmptyTitle = Localize("Agent.Empty", "No agent activity", "暂无 Agent 活动"),
                EmptyHint = Localize("Agent.EmptyHint",
                    "Connect DeepSeek Harness, Claude Code, or Codex to see sessions here.",
                    "接入 DeepSeek Harness、Claude Code 或 Codex 后，会话会显示在这里。"),
                Rows = rows,
            },
        });
    }

    private static bool IsAuditMode =>
        string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_AUDIT"), "1", StringComparison.Ordinal);

    private string Localize(string key, string fallback, string? chineseFallback = null)
    {
        if (Context.Localization.TryGet(key, out var value))
        {
            return value;
        }

        return Context.Localization.CurrentLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            && chineseFallback is not null
            ? chineseFallback
            : fallback;
    }

    /// <summary>Deterministic three-row sample used only by the developer visual audit.</summary>
    internal static IReadOnlyList<PageListRow> AuditRows() =>
    [
        new PageListRow
        {
            RowId = "audit-agent-1",
            IconToken = "agent",
            Title = "重构岛内字体排印",
            Subtitle = "FluidBar · src/FluidBar.Island",
            Value = "运行 02:14",
            Badge = "运行中",
            Tone = "info",
        },
        new PageListRow
        {
            RowId = "audit-agent-2",
            IconToken = "check",
            Title = "检查插件页面空态",
            Subtitle = "FluidBar · src/FluidBar.App",
            Value = "完成 01:08",
            Badge = "已完成",
            Tone = "success",
        },
        new PageListRow
        {
            RowId = "audit-agent-3",
            IconToken = "pause",
            Title = "等待确认发布说明",
            Subtitle = "FluidBar · docs",
            Value = "等待 00:42",
            Badge = "等待输入",
            Tone = "warning",
        },
    ];

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

        Context.Islands.TryRetract(PluginId);
        if (_pageRegistered)
        {
            Context.Islands.UnregisterPage(AgentPageId);
            _pageRegistered = false;
        }
    }
}
