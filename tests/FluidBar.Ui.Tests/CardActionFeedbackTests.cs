using FluidBar.App.Hosting.Skia;
using FluidBar.Platform.Windows.Threading;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Diagnostics;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Focus;
using FluidBar.Platform.Windows.Radios;
using FluidBar.Platform.Windows.Tasks;
using System.Collections.Concurrent;
using Xunit;

namespace FluidBar.Ui.Tests;

public sealed class CardActionFeedbackTests
{
    [Fact]
    public void PlatformActions_ExposeANarrowFeedbackSource()
    {
        Assert.Contains(typeof(SkiaPlatformActions).GetInterfaces(),
            type => type.Name == "IIslandActionFeedbackSource");
    }
    [Theory]
    [InlineData(IslandPlatformActionKind.VolumeSet)]
    [InlineData(IslandPlatformActionKind.BrightnessSet)]
    [InlineData(IslandPlatformActionKind.MediaSeek)]
    public async Task SynchronousRejection_ReportsEvenSliderFailures(IslandPlatformActionKind kind)
    {
        using var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);
        var feedback = new List<IslandActionFeedback>();
        actions.ActionFeedback += feedback.Add;
        var result = actions.Invoke(new IslandPlatformAction(kind, Value: 0.5));
        Assert.Equal(IslandActionOutcome.Unsupported, result.Outcome);
        var rejected = Assert.Single(feedback);
        Assert.Equal(IslandActionFeedbackStatus.Unavailable, rejected.Status);
        Assert.Equal(kind, rejected.Action.Kind);
        Assert.Equal(result.Reason, rejected.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RadioCompletion_UsesTheExplicitResult(bool succeeds)
    {
        using var settings = CreateSettings();
        var radio = new ControlledRadioService();
        await using var actions = CreateActions(settings, radio);
        var feedback = new ConcurrentQueue<IslandActionFeedback>();
        var terminal = new TaskCompletionSource<IslandActionFeedback>(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.ActionFeedback += value =>
        {
            feedback.Enqueue(value);
            if (value.Status != IslandActionFeedbackStatus.Processing)
                terminal.TrySetResult(value);
        };
        actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle, Toggle: IslandQuickToggle.Wifi));
        Assert.Equal(IslandActionFeedbackStatus.Processing, Assert.Single(feedback).Status);
        radio.Completion.SetResult(succeeds ? RadioToggleResult.Success(radio.Current!)
            : RadioToggleResult.AccessDenied("system policy"));
        var completed = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(succeeds ? IslandActionFeedbackStatus.Succeeded : IslandActionFeedbackStatus.Failed, completed.Status);
        Assert.Equal(feedback.First().RequestId, completed.RequestId);
        Assert.Equal(succeeds ? null : "system policy", completed.Reason);
    }

    [Fact]
    public async Task Exception_ProducesFailureFeedback()
    {
        using var settings = CreateSettings();
        var radio = new ControlledRadioService();
        await using var actions = CreateActions(settings, radio);
        var failure = new TaskCompletionSource<IslandActionFeedback>(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.ActionFeedback += value =>
        {
            if (value.Status == IslandActionFeedbackStatus.Failed)
                failure.TrySetResult(value);
        };
        actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle));
        radio.Completion.SetException(new InvalidOperationException("radio failed"));
        Assert.Equal("radio failed", (await failure.Task.WaitAsync(TimeSpan.FromSeconds(5))).Reason);
    }

    [Fact]
    public async Task AwaitableAction_WaitsForTheRealResult()
    {
        using var settings = CreateSettings();
        var radio = new ControlledRadioService();
        await using var actions = CreateActions(settings, radio);
        var completion = actions.InvokeAndWaitAsync(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle));
        Assert.False(completion.IsCompleted);
        radio.Completion.SetResult(RadioToggleResult.Failed("denied"));
        var result = await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(IslandActionFeedbackStatus.Failed, result.Status);
        Assert.Equal("denied", result.Reason);
    }

    [Fact]
    public async Task CancellingTheWait_DoesNotCancelThePlatformAction()
    {
        using var settings = CreateSettings();
        var radio = new ControlledRadioService();
        await using var actions = CreateActions(settings, radio);
        using var cancellation = new CancellationTokenSource();
        var completion = actions.InvokeAndWaitAsync(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle), cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
        Assert.False(radio.Completion.Task.IsCompleted);
        radio.Completion.SetResult(RadioToggleResult.Success(radio.Current!));
    }

    [Fact]
    public async Task QuietSuccessfulActions_StillCompleteTheirAwaitableResult()
    {
        using var settings = CreateSettings();
        await using var actions = new SkiaPlatformActions(settings);
        var feedback = new List<IslandActionFeedback>();
        actions.ActionFeedback += feedback.Add;
        var result = await actions.InvokeAndWaitAsync(new IslandPlatformAction(IslandPlatformActionKind.CalendarNavigate, Value: 1));
        Assert.Equal(IslandActionFeedbackStatus.Succeeded, result.Status);
        Assert.Empty(feedback);
    }

    [Fact]
    public async Task TaskMutationFailure_IsNotReportedAsSuccess()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluidbar-feedback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "tasks.json");
        try
        {
            await File.WriteAllTextAsync(path, "[{\"title\":\"Task\",\"done\":false}]");
            using var settings = CreateSettings();
            await using var actions = new SkiaPlatformActions(settings, null, null, null,
                taskOptions: new TaskListOptions { FilePath = path, ReloadInterval = TimeSpan.FromDays(1) });
            await actions.TaskSource.StartAsync();
            var published = DateTime.UtcNow.AddSeconds(5);
            while (actions.TaskSource.Current is null && DateTime.UtcNow < published) await Task.Delay(10);
            Assert.True(actions.TaskSource.Current!.IsAvailable);
            // Freeze the observer after capturing its valid snapshot. This isolates the mutation's
            // explicit failure from a competing initial poll and from Windows file-sharing timing.
            await actions.TaskSource.StopAsync();
            await File.WriteAllTextAsync(path, "invalid");
            var terminal = new TaskCompletionSource<IslandActionFeedback>(TaskCreationOptions.RunContinuationsAsynchronously);
            actions.ActionFeedback += value =>
            {
                if (value.Status != IslandActionFeedbackStatus.Processing)
                    terminal.TrySetResult(value);
            };
            var result = actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.TaskToggle, Index: 0));
            Assert.Equal(IslandActionOutcome.Handled, result.Outcome);
            Assert.Equal(IslandActionFeedbackStatus.Failed,
                (await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            Assert.Equal("invalid", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task Dispose_SuppressesLatePlatformCompletion()
    {
        using var settings = CreateSettings();
        var radio = new ControlledRadioService();
        var actions = CreateActions(settings, radio);
        var feedback = new ConcurrentQueue<IslandActionFeedback>();
        actions.ActionFeedback += feedback.Enqueue;
        actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.QuickToggle));
        await actions.DisposeAsync();
        radio.Completion.SetResult(RadioToggleResult.Success(radio.Current!));
        await Task.Delay(50);
        Assert.Equal(IslandActionFeedbackStatus.Processing, Assert.Single(feedback).Status);
        actions.Invoke(new IslandPlatformAction(IslandPlatformActionKind.VolumeMute));
        Assert.Single(feedback);
    }

    [Fact]
    public void Feedback_ExpiresWithoutRequestingContinuousFrames()
    {
        var actions = new FeedbackActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        var frames = 0;
        fixture.Controller.FrameRequested += () => frames++;
        actions.Publish(1, IslandActionFeedbackStatus.Failed);
        Assert.Equal("Feedback.Failed", fixture.Controller.RenderState.ActionFeedbackText);
        Assert.True(fixture.Controller.RenderState.ActionFeedbackIsError);
        Assert.True(fixture.Controller.HasUiDeadline);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        fixture.Controller.PumpTimers();
        Assert.NotNull(fixture.Controller.RenderState.ActionFeedbackText);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        fixture.Controller.PumpTimers();
        Assert.Null(fixture.Controller.RenderState.ActionFeedbackText);
        Assert.False(fixture.Controller.HasUiDeadline);
        Assert.Equal(0, frames);
    }

    [Fact]
    public void OlderCompletion_DoesNotReplaceTheLatestAction()
    {
        var actions = new FeedbackActions();
        using var fixture = ControllerFixture.Create(actions: actions);
        actions.Publish(2, IslandActionFeedbackStatus.Succeeded);
        actions.Publish(1, IslandActionFeedbackStatus.Failed);
        Assert.Equal("Feedback.Success", fixture.Controller.RenderState.ActionFeedbackText);
        fixture.Controller.Dispose();
        Assert.Equal(0, actions.Subscribers);
        actions.Publish(3, IslandActionFeedbackStatus.Failed);
        Assert.Equal("Feedback.Success", fixture.Controller.RenderState.ActionFeedbackText);
    }

    [Fact]
    public async Task WorkerFeedback_IsMarshalledToTheCapturedDispatcher()
    {
        var context = new QueueContext();
        var previous = SynchronizationContext.Current;
        var actions = new FeedbackActions();
        ControllerFixture fixture;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            fixture = ControllerFixture.Create(actions: actions);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        using (fixture)
        {
            await Task.Run(() => actions.Publish(1, IslandActionFeedbackStatus.Succeeded));
            Assert.Null(fixture.Controller.RenderState.ActionFeedbackText);
            context.Drain();
            Assert.Equal("Feedback.Success", fixture.Controller.RenderState.ActionFeedbackText);
        }
    }

    private static SettingsController CreateSettings()
    {
        var store = new InMemoryUiSettingsStore(AppSettings.Default);
        return new SettingsController(store.LastSaved, store, NullAppLogger.Instance);
    }

    private static SkiaPlatformActions CreateActions(SettingsController settings, ControlledRadioService radio)
        => new(settings, null, null, new IslandQuickToggleServices(radio,
            new FakeRadioToggleService(RadioSnapshot.NoAdapter(PlatformRadioKind.Bluetooth)),
            new FakeFocusAssistService(new FocusAssistSnapshot(false, FocusAssistAvailability.Available))));

    private sealed class FeedbackActions : IIslandPlatformActions, IIslandActionFeedbackSource
    {
        private Action<IslandActionFeedback>? _feedback;
        public int Subscribers { get; private set; }
        public event Action<IslandActionFeedback>? ActionFeedback
        {
            add { _feedback += value; Subscribers++; }
            remove { _feedback -= value; Subscribers--; }
        }
        public IslandActionResult Invoke(in IslandPlatformAction action) => IslandActionResult.Handled;
        public void Publish(long id, IslandActionFeedbackStatus status)
            => _feedback?.Invoke(new IslandActionFeedback(id, new IslandPlatformAction(IslandPlatformActionKind.QuickToggle), status));
    }

    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();
        public override void Post(SendOrPostCallback d, object? state) => _callbacks.Enqueue((d, state));
        public void Drain()
        {
            while (_callbacks.TryDequeue(out var callback))
                callback.Callback(callback.State);
        }
    }

    private sealed class ControlledRadioService : IRadioToggleService
    {
        public TaskCompletionSource<RadioToggleResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PlatformRadioKind Kind => PlatformRadioKind.Wifi;
        public PlatformMonitorState State => PlatformMonitorState.Running;
        public RadioSnapshot? Current { get; } = new(PlatformRadioKind.Wifi, RadioAvailability.Available, true, "WLAN");
        public bool? IsEnabled => Current?.IsEnabled;
        public event EventHandler<SnapshotEventArgs<RadioSnapshot>>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RadioToggleResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Completion.Task;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
