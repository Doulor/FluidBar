namespace FluidBar.Platform.Windows.Agent;

/// <summary>How well FluidBar can natively observe an agent (no plugin, no configuration).</summary>
public enum AgentSupportKind
{
    /// <summary>Read-only native state parser exists and found its data source.</summary>
    Native,

    /// <summary>Only coarse activity is available (for example Aider's history file).</summary>
    Partial,

    /// <summary>The agent is not installed / no local state exists on this machine.</summary>
    NotDetected,

    /// <summary>No readable local state; real-time status is not possible.</summary>
    Infeasible,
}

/// <summary>One row of the support-status settings page.</summary>
/// <param name="Id">Stable agent id.</param>
/// <param name="DisplayName">Brand display name.</param>
/// <param name="Kind">Native/partial support or the reason it is unavailable.</param>
/// <param name="DataPath">Local file/directory the provider reads.</param>
/// <param name="NoteCode">Stable note code the UI localizes (no-activity/sqlite-state/ide-only/electron-only/no-public-state).</param>
public sealed record AgentSupportStatus(
    string Id,
    string DisplayName,
    AgentSupportKind Kind,
    string? DataPath,
    string? NoteCode);

/// <summary>
/// Builds the support-status rows from the registered native providers plus the known-infeasible agents.
/// This page is informational only: nothing is installed and no configuration is modified.
/// </summary>
public static class AgentSupportCatalog
{
    /// <summary>Builds the row list from the active providers (order: native first, then unsupported).</summary>
    public static IReadOnlyList<AgentSupportStatus> Create(IReadOnlyList<IAgentStatusProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var statuses = new List<AgentSupportStatus>();
        foreach (var provider in providers)
        {
            var kind = provider.ProviderId switch
            {
                "aider" when provider.IsAvailable => AgentSupportKind.Partial,
                _ when provider.IsAvailable => AgentSupportKind.Native,
                _ => AgentSupportKind.NotDetected,
            };
            statuses.Add(new AgentSupportStatus(provider.ProviderId, provider.DisplayName, kind,
                provider.DataPath, kind == AgentSupportKind.NotDetected ? "not-detected" : null));
        }

        statuses.AddRange(
        [
            new AgentSupportStatus("cursor", "Cursor", AgentSupportKind.Infeasible, null, "sqlite-state"),
            new AgentSupportStatus("windsurf", "Windsurf", AgentSupportKind.Infeasible, null, "ide-only"),
            new AgentSupportStatus("qoder", "Qoder", AgentSupportKind.Infeasible, null, "ide-only"),
            new AgentSupportStatus("zcode", "zcode", AgentSupportKind.Infeasible, null, "no-public-state"),
            new AgentSupportStatus("workbuddy", "WorkBuddy", AgentSupportKind.Infeasible, null, "electron-only"),
        ]);
        return statuses;
    }

    /// <summary>Convenience overload using the production providers for the current user.</summary>
    public static IReadOnlyList<AgentSupportStatus> CreateDefault() => Create(NativeAgentProviders.CreateDefault());
}
