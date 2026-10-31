namespace FluidBar.App.Hosting.Skia;

/// <summary>Non-modal progress and completion for a single user action.</summary>
public enum IslandActionFeedbackStatus
{
    /// <summary>The platform request is running.</summary>
    Processing,
    /// <summary>The platform accepted the request.</summary>
    Succeeded,
    /// <summary>The platform rejected or failed the request.</summary>
    Failed,
    /// <summary>The action could not be dispatched.</summary>
    Unavailable,
}

/// <summary>Structured feedback; diagnostic reasons are never painted directly into the card.</summary>
public sealed record IslandActionFeedback(
    long RequestId,
    IslandPlatformAction Action,
    IslandActionFeedbackStatus Status,
    string? Reason = null);

/// <summary>Optional action feedback, independent of the platform action dispatch contract.</summary>
public interface IIslandActionFeedbackSource
{
    /// <summary>May arrive on a worker thread; consumers marshal to their UI dispatcher.</summary>
    event Action<IslandActionFeedback>? ActionFeedback;
}

/// <summary>Optional completion seam for App-owned actions that must await the actual platform result.</summary>
public interface IIslandAsyncPlatformActions : IIslandPlatformActions
{
    /// <summary>Dispatches immediately and completes with the platform result; cancellation stops waiting.</summary>
    Task<IslandActionFeedback> InvokeAndWaitAsync(IslandPlatformAction action, CancellationToken cancellationToken = default);
}
