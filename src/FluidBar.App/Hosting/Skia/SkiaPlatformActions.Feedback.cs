using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Radios;
using FluidBar.Platform.Windows.Tasks;

namespace FluidBar.App.Hosting.Skia;

public sealed partial class SkiaPlatformActions : IIslandActionFeedbackSource, IIslandAsyncPlatformActions
{
    private readonly AsyncLocal<FeedbackInvocation?> _feedbackInvocation = new();
    private long _feedbackSequence;

    /// <inheritdoc />
    public event Action<IslandActionFeedback>? ActionFeedback;

    /// <inheritdoc />
    public IslandActionResult Invoke(in IslandPlatformAction action)
        => InvokeWithFeedback(action, out _);

    /// <inheritdoc />
    public Task<IslandActionFeedback> InvokeAndWaitAsync(IslandPlatformAction action, CancellationToken cancellationToken = default)
    {
        InvokeWithFeedback(action, out var completion);
        return completion.WaitAsync(cancellationToken);
    }

    private IslandActionResult InvokeWithFeedback(in IslandPlatformAction action, out Task<IslandActionFeedback> completion,
        Func<IslandActionResult>? execute = null)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            completion = Task.FromResult(new IslandActionFeedback(0, action,
                IslandActionFeedbackStatus.Unavailable, "the platform action seam has been disposed"));
            return IslandActionResult.Unsupported("the platform action seam has been disposed");
        }

        var previous = _feedbackInvocation.Value;
        var invocation = new FeedbackInvocation(Interlocked.Increment(ref _feedbackSequence), action);
        completion = invocation.Completion.Task;
        _feedbackInvocation.Value = invocation;
        try
        {
            var result = execute is null ? InvokeCore(action) : execute();
            if (!invocation.Started)
            {
                PublishFeedback(invocation, result.Outcome == IslandActionOutcome.Handled
                    ? IslandActionFeedbackStatus.Succeeded : IslandActionFeedbackStatus.Unavailable, result.Reason);
            }

            return result;
        }
        finally
        {
            invocation.Closed = true;
            _feedbackInvocation.Value = previous;
        }
    }

    private Task RunAsync<T>(Func<Task<T>> work, string eventId) => RunWithFeedbackAsync(work, eventId);

    private async Task RunWithFeedbackAsync<T>(Func<Task<T>> work, string eventId)
    {
        // Delayed background refreshes inherit ExecutionContext, but are not another user command.
        var invocation = _feedbackInvocation.Value is { Closed: false } current ? current : null;
        if (invocation is not null)
        {
            invocation.Started = true;
            PublishFeedback(invocation, IslandActionFeedbackStatus.Processing);
        }

        try
        {
            // Preserve the service's explicit result: a completed Task is not necessarily success.
            var result = await Task.Run(work).ConfigureAwait(false);
            var (succeeded, reason) = result switch
            {
                bool accepted => (accepted, (string?)null),
                RadioToggleResult radio => (radio.Succeeded, radio.Reason),
                FocusToggleResult focus => (focus.Succeeded, focus.Reason),
                ToggleTaskResult task => (task.Succeeded, task.FailureReason),
                _ => (true, (string?)null),
            };
            if (!succeeded)
            {
                _logger.Warning(LogCategories.Platform, eventId + "-failed",
                    reason ?? "The platform service refused the page-control action.");
            }

            PublishFeedback(invocation, succeeded
                ? IslandActionFeedbackStatus.Succeeded : IslandActionFeedbackStatus.Failed, reason);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, eventId + "-failed",
                "A page-control platform call failed; the island stays responsive.", ex);
            PublishFeedback(invocation, IslandActionFeedbackStatus.Failed, ex.Message);
        }
    }

    private void PublishFeedback(FeedbackInvocation? invocation, IslandActionFeedbackStatus status,
        string? reason = null)
    {
        if (invocation is null)
        {
            return;
        }

        var feedback = new IslandActionFeedback(invocation.Id, invocation.Action, status, reason);
        if (status != IslandActionFeedbackStatus.Processing)
        {
            invocation.Completion.TrySetResult(feedback);
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        // Dragging sliders and automatic page bookkeeping must stay quiet on success.
        if (status is IslandActionFeedbackStatus.Processing or IslandActionFeedbackStatus.Succeeded
            && invocation.Action.Kind is IslandPlatformActionKind.VolumeSet
                or IslandPlatformActionKind.BrightnessSet or IslandPlatformActionKind.MediaSeek
                or IslandPlatformActionKind.NotificationMarkAllRead or IslandPlatformActionKind.CalendarNavigate
                or IslandPlatformActionKind.ClipboardCycleFilter)
        {
            return;
        }

        ActionFeedback?.Invoke(feedback);
    }

    private sealed class FeedbackInvocation(long id, IslandPlatformAction action)
    {
        public long Id { get; } = id;
        public IslandPlatformAction Action { get; } = action;
        public bool Started { get; set; }
        public volatile bool Closed;
        public TaskCompletionSource<IslandActionFeedback> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
