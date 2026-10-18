using System.Collections.Immutable;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// Deterministic plugin lifecycle states. A plugin moves through <see cref="PluginLifecycleTransitions"/>;
/// invalid transitions are programming errors and fail fast instead of corrupting host state.
/// </summary>
public enum PluginLifecycleState
{
    /// <summary>A package directory was found.</summary>
    Discovered = 0,

    /// <summary>The manifest is being parsed/validated.</summary>
    Validating = 1,

    /// <summary>The manifest is valid but the plugin has not been loaded yet.</summary>
    Validated = 2,

    /// <summary>An AssemblyLoadContext is being created and the entry assembly loaded.</summary>
    Loading = 3,

    /// <summary>The entry assembly and plugin instance are constructed.</summary>
    Loaded = 4,

    /// <summary><c>InitializeAsync</c> is running.</summary>
    Initializing = 5,

    /// <summary><c>InitializeAsync</c> completed.</summary>
    Initialized = 6,

    /// <summary><c>StartAsync</c> is running.</summary>
    Starting = 7,

    /// <summary>The plugin is running and may publish events.</summary>
    Active = 8,

    /// <summary><c>StopAsync</c> is running.</summary>
    Stopping = 9,

    /// <summary>The plugin is stopped but still loaded (reload/start is possible).</summary>
    Stopped = 10,

    /// <summary>Services and the AssemblyLoadContext are being released.</summary>
    Unloading = 11,

    /// <summary>Everything was released; the handle may be reloaded.</summary>
    Unloaded = 12,

    /// <summary>Loading or activating failed; the host stays alive and the plugin is isolated.</summary>
    Failed = 13,

    /// <summary>Administratively disabled (user setting / crash budget exceeded / blocked by policy).</summary>
    Disabled = 14,

    /// <summary>The manifest is valid but incompatible with this app/SDK version.</summary>
    Incompatible = 15,
}

/// <summary>Allowed lifecycle transitions. Kept public so tests and the App can assert loader determinism.</summary>
public static class PluginLifecycleTransitions
{
    private static readonly ImmutableDictionary<PluginLifecycleState, ImmutableHashSet<PluginLifecycleState>> Allowed =
        new Dictionary<PluginLifecycleState, ImmutableHashSet<PluginLifecycleState>>
        {
            [PluginLifecycleState.Discovered] = Set(PluginLifecycleState.Validating, PluginLifecycleState.Failed),
            [PluginLifecycleState.Validating] = Set(PluginLifecycleState.Validated, PluginLifecycleState.Incompatible, PluginLifecycleState.Failed),
            [PluginLifecycleState.Validated] = Set(PluginLifecycleState.Loading, PluginLifecycleState.Disabled, PluginLifecycleState.Failed),
            [PluginLifecycleState.Loading] = Set(PluginLifecycleState.Loaded, PluginLifecycleState.Failed),
            [PluginLifecycleState.Loaded] = Set(PluginLifecycleState.Initializing, PluginLifecycleState.Unloading, PluginLifecycleState.Failed),
            [PluginLifecycleState.Initializing] = Set(PluginLifecycleState.Initialized, PluginLifecycleState.Failed, PluginLifecycleState.Unloading),
            [PluginLifecycleState.Initialized] = Set(PluginLifecycleState.Starting, PluginLifecycleState.Stopping, PluginLifecycleState.Unloading, PluginLifecycleState.Failed),
            [PluginLifecycleState.Starting] = Set(PluginLifecycleState.Active, PluginLifecycleState.Stopping, PluginLifecycleState.Failed, PluginLifecycleState.Unloading),
            [PluginLifecycleState.Active] = Set(PluginLifecycleState.Stopping, PluginLifecycleState.Failed, PluginLifecycleState.Disabled, PluginLifecycleState.Unloading),
            [PluginLifecycleState.Stopping] = Set(PluginLifecycleState.Stopped, PluginLifecycleState.Failed, PluginLifecycleState.Unloading),
            [PluginLifecycleState.Stopped] = Set(PluginLifecycleState.Starting, PluginLifecycleState.Unloading, PluginLifecycleState.Failed, PluginLifecycleState.Disabled),
            [PluginLifecycleState.Unloading] = Set(PluginLifecycleState.Unloaded, PluginLifecycleState.Failed, PluginLifecycleState.Disabled),
            [PluginLifecycleState.Unloaded] = Set(PluginLifecycleState.Loading, PluginLifecycleState.Discovered, PluginLifecycleState.Disabled, PluginLifecycleState.Validating),
            [PluginLifecycleState.Failed] = Set(PluginLifecycleState.Loading, PluginLifecycleState.Unloading, PluginLifecycleState.Unloaded, PluginLifecycleState.Disabled, PluginLifecycleState.Validating),
            [PluginLifecycleState.Disabled] = Set(PluginLifecycleState.Loading, PluginLifecycleState.Unloading, PluginLifecycleState.Unloaded, PluginLifecycleState.Validating),
            [PluginLifecycleState.Incompatible] = Set(PluginLifecycleState.Validating, PluginLifecycleState.Unloading, PluginLifecycleState.Unloaded, PluginLifecycleState.Disabled),
        }.ToImmutableDictionary();

    /// <summary>Returns <see langword="true"/> when <paramref name="to"/> is reachable from <paramref name="from"/>.</summary>
    public static bool CanTransition(PluginLifecycleState from, PluginLifecycleState to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Throws <see cref="InvalidOperationException"/> for an invalid transition.</summary>
    public static void EnsureTransition(PluginLifecycleState from, PluginLifecycleState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid plugin lifecycle transition '{from}' -> '{to}'.");
        }
    }

    /// <summary>Terminal-ish states that never run plugin code again without an explicit reload.</summary>
    public static bool IsTerminal(PluginLifecycleState state) =>
        state is PluginLifecycleState.Unloaded or PluginLifecycleState.Failed or PluginLifecycleState.Disabled or PluginLifecycleState.Incompatible;

    private static ImmutableHashSet<PluginLifecycleState> Set(params PluginLifecycleState[] states) =>
        states.ToImmutableHashSet();
}

/// <summary>Raised on every lifecycle transition so the App can update the plugins UI.</summary>
public sealed class PluginStateChangedEventArgs : EventArgs
{
    public PluginStateChangedEventArgs(string pluginId, PluginLifecycleState previousState, PluginLifecycleState newState, string? error)
    {
        PluginId = pluginId;
        PreviousState = previousState;
        NewState = newState;
        Error = error;
    }

    public string PluginId { get; }

    public PluginLifecycleState PreviousState { get; }

    public PluginLifecycleState NewState { get; }

    /// <summary>Formatted error text; deliberately a string so UI state never roots a plugin exception.</summary>
    public string? Error { get; }
}
