using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>
/// Read-only native status provider: parses a file an agent already writes, never modifies it.
/// Providers are polled by <see cref="AgentProviderRegistry"/> and contribute plain events that the
/// registry converts into the island's normal <see cref="AgentEventEntry"/> rows.
/// </summary>
public interface IAgentStatusProvider
{
    /// <summary>Stable id ("dsh", "claude-code", "codex", "copilot", "aider", "cursor").</summary>
    string ProviderId { get; }

    /// <summary>Display name (brand name, never translated).</summary>
    string DisplayName { get; }

    /// <summary>Primary local path this provider reads (shown on the support-status page).</summary>
    string DataPath { get; }

    /// <summary>Whether the provider found its data source on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Last time a poll produced a live event (null = no data seen yet).</summary>
    DateTimeOffset? LastActivityUtc { get; }

    /// <summary>Polls the local state once and returns the events currently visible.</summary>
    IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now);
}

/// <summary>One status event a provider observed in a local file.</summary>
/// <param name="SessionId">Provider-local session id; the registry prefixes it with the provider id.</param>
/// <param name="Event">running | waiting | completed | failed.</param>
/// <param name="Title">Row title (for example "Claude Code · FluidBar").</param>
/// <param name="Message">Detail line (step, tool name or error).</param>
/// <param name="Timestamp">Observation time.</param>
/// <param name="StartedAt">Run start, for the elapsed-duration label.</param>
/// <param name="Error">Failure text, folded into the detail line.</param>
public sealed record AgentProviderEvent(
    string SessionId,
    string Event,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    DateTimeOffset? StartedAt = null,
    string? Error = null)
{
    /// <summary>Session/任务 name from the provider (DSH session title, Claude's first user prompt).</summary>
    public string? SessionTitle { get; init; }

    /// <summary>Concrete current action (tool + target) when the provider can observe one.</summary>
    public string? Action { get; init; }

    /// <summary>Project/workspace leaf name (the secondary identifier in the agent page).</summary>
    public string? Project { get; init; }
}

/// <summary>Options for the provider registry poller.</summary>
public sealed class AgentProviderRegistryOptions
{
    /// <summary>How often every available provider is polled.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Maximum rows kept in the merged snapshot.</summary>
    public int MaxRows { get; init; } = 32;
}

/// <summary>
/// Polls every read-only provider and merges their events into the same bounded snapshot the hook inbox
/// produces. Nothing here writes to an agent's files; the registry only reads.
/// </summary>
public sealed class AgentProviderRegistry : PlatformMonitor<AgentEventSnapshot>, IAgentInboxSource
{
    private readonly IReadOnlyList<IAgentStatusProvider> _providers;
    private readonly AgentProviderRegistryOptions _options;
    private readonly AgentEventHistory _history;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public AgentProviderRegistry(IReadOnlyList<IAgentStatusProvider> providers,
        AgentProviderRegistryOptions? options = null, IPlatformLogger? logger = null,
        PlatformCounters? counters = null, TimeProvider? timeProvider = null)
        : base("agent-native", logger ?? NullPlatformLogger.Instance, counters ?? new PlatformCounters(),
            timeProvider ?? TimeProvider.System)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        _options = options ?? new AgentProviderRegistryOptions();
        _history = new AgentEventHistory(Math.Max(8, _options.MaxRows));
    }

    /// <summary>Providers in registration order (support page diagnostics).</summary>
    public IReadOnlyList<IAgentStatusProvider> Providers => _providers;

    /// <inheritdoc />
    public string InboxDirectory => _providers.FirstOrDefault()?.DataPath ?? string.Empty;

    /// <summary>Runs one synchronous scan (used by tests and the first tick).</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changed = Scan(TimeProvider.GetUtcNow());
        if (changed || Current is null)
        {
            Publish(_history.Snapshot());
        }

        return Task.CompletedTask;
    }

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var interval = _options.PollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(60) : _options.PollInterval;
        _pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval, TimeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        await RefreshAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(PlatformLogCategory.Agent, "agent-native.scan-failed",
                            "Agent provider scan failed.", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }, CancellationToken.None);
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
            _cts.Dispose();
            _cts = null;
        }

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // A poller that does not stop in time is an acceptable shutdown cost.
            }

            _pollTask = null;
        }
    }

    private bool Scan(DateTimeOffset now)
    {
        var changed = false;
        foreach (var provider in _providers)
        {
            if (!provider.IsAvailable)
            {
                continue;
            }

            try
            {
                foreach (var observed in provider.Poll(now))
                {
                    changed |= AddEntry(provider, observed);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                or InvalidOperationException)
            {
                Logger.LogWarning(PlatformLogCategory.Agent, "agent-native.poll-failed",
                    "Could not poll agent provider '" + provider.ProviderId + "'.", ex);
            }
        }

        return changed;
    }

    private bool AddEntry(IAgentStatusProvider provider, AgentProviderEvent observed)
    {
        var file = new AgentInboxFile(
            SchemaVersion: 2,
            Agent: provider.ProviderId,
            Event: observed.Event,
            Title: observed.Title,
            Message: observed.Message,
            Timestamp: observed.Timestamp,
            SessionId: provider.ProviderId + ":" + observed.SessionId,
            StartedAt: observed.StartedAt,
            Error: observed.Error);
        var entry = AgentEventParser.ToEntry(file,
            Path.Combine(provider.DataPath, observed.SessionId + ".json"), observed.Timestamp)
            with
            {
                SessionTitle = observed.SessionTitle,
                Action = observed.Action,
                Project = observed.Project,
            };
        _history.Add(entry);
        return true;
    }
}
