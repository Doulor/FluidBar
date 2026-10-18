using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Agent inbox contract consumed by first-party plugins. Implemented by <see cref="AgentInboxWatcher"/>.</summary>
public interface IAgentInboxSource
{
    AgentEventSnapshot? Current { get; }

    event EventHandler<SnapshotEventArgs<AgentEventSnapshot>>? SnapshotChanged;

    string InboxDirectory { get; }
}

/// <summary>Explicit hook install/repair contract; the plugin must only invoke this from a consent-gated command.</summary>
public interface IAgentHookInstaller
{
    Task<HookInstallResult> InstallAsync(bool userConfirmed, CancellationToken cancellationToken = default);

    Task<HookInstallResult> UninstallAsync(bool userConfirmed, CancellationToken cancellationToken = default);

    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);
}
