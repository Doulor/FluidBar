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
                "aider" or "workbuddy" when provider.IsAvailable => AgentSupportKind.Partial,
                _ when provider.IsAvailable => AgentSupportKind.Native,
                _ => AgentSupportKind.NotDetected,
            };
            statuses.Add(new AgentSupportStatus(provider.ProviderId, provider.DisplayName, kind,
                provider.DataPath, kind == AgentSupportKind.NotDetected ? "not-detected" : null));
        }

        // Rows without a provider: either documented-infeasible, or not installed on this machine (the
        // paths below are the documented locations that were checked).
        var known = statuses.Select(static status => status.Id).ToHashSet(StringComparer.Ordinal);
        void Add(string id, string name, AgentSupportKind kind, string? path, string? note)
        {
            if (known.Add(id))
            {
                statuses.Add(new AgentSupportStatus(id, name, kind, path, note));
            }
        }

        Add("cursor", "Cursor", AgentSupportKind.Infeasible, null, "sqlite-state");
        Add("windsurf", "Windsurf", AgentSupportKind.Infeasible, null, "ide-only");
        Add("qoder", "Qoder", AgentSupportKind.NotDetected,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Qoder"),
            "not-installed");
        Add("zcode", "zcode", AgentSupportKind.NotDetected,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "zcode"),
            "not-installed");
        return statuses;
    }

    /// <summary>Convenience overload using the production providers for the current user.</summary>
    public static IReadOnlyList<AgentSupportStatus> CreateDefault() => Create(NativeAgentProviders.CreateDefault());
}
