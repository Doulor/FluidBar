using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;
using FluidBar.Sdk.Pages;

namespace FluidBar.Plugins.Timer;

public sealed class TimerPlugin : IFluidBarPlugin
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _commands = [];
    private IPluginContext? _context;
    private Countdown? _timer;
    private IPluginLease? _lease;
    private IDisposable? _tick;
    private bool _notified;
    private bool _started;
    private long _revision;
    private const string PageId = "fluidbar-timer.page";

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _timer = new Countdown(context.Time);
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var context = _context ?? throw new InvalidOperationException("Plugin is not initialized.");
            if (_started) return ValueTask.CompletedTask;
            _started = true;
            _lease = context.Leases.CreateLease("countdown");
            foreach (var name in new[] { "start", "pause", "resume", "stop" })
            {
                var action = name;
                _commands.Add(context.Commands.Register(context.PluginId + "." + name, (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Execute(action);
                    return ValueTask.CompletedTask;
                }));
            }
            context.Islands.RegisterPage(new PageDescriptor
            {
                PageId = PageId, Order = 100, IconToken = "timer", TemplateId = PageTemplates.List,
                Title = LocalizedString.From(context.Localization.Get("title")),
            });
            Publish();
        }
        return ValueTask.CompletedTask;
    }

    private void Execute(string action)
    {
        lock (_gate)
        {
            if (!_started) return;
            var timer = _timer!;
            if (action == "start")
            {
                timer.Start(TimeSpan.FromMinutes(Math.Clamp(_context!.Settings.GetInt32("minutes", 25), 1, 180)));
                _notified = false;
            }
            else if (action == "pause") timer.Pause();
            else if (action == "resume") timer.Resume();
            else if (action == "stop") timer.Stop();
            UpdateTick();
            Publish();
        }
    }

    private void UpdateTick()
    {
        _tick?.Dispose();
        _tick = null;
        if (_timer!.Read().Running)
            _tick = _lease!.SchedulePeriodic(TimeSpan.FromSeconds(1), _ =>
            {
                lock (_gate)
                {
                    if (!_started) return ValueTask.CompletedTask;
                    var state = _timer.Read();
                    // A tick already queued when Pause() runs must not publish a paused snapshot: the
                    // pause command already published it, and a late event made pause observably racy.
                    if (!state.Running && !state.Completed)
                    {
                        _tick?.Dispose();
                        _tick = null;
                        return ValueTask.CompletedTask;
                    }

                    Publish();
                    if (!state.Running) { _tick?.Dispose(); _tick = null; }
                }
                return ValueTask.CompletedTask;
            });
    }

    private void Publish()
    {
        var context = _context!;
        var state = _timer!.Read();
        var seconds = (int)Math.Ceiling(state.Remaining.TotalSeconds);
        var remaining = $"{seconds / 60:00}:{seconds % 60:00}";
        string L(string key) => context.Localization.Get(key);
        var status = state.Completed ? L("complete") : state.Running ? L("running")
            : seconds > 0 ? L("paused") : L("idle");
        context.Islands.PublishPage(new PageData
        {
            PageId = PageId, TemplateId = PageTemplates.List, Revision = ++_revision,
            List = new PageListData { Rows =
            [
                new() { RowId = "status", Title = remaining, Subtitle = status, Progress = state.Progress,
                    TrailingLabel = L("start"), TrailingCommandId = context.PluginId + ".start" },
                new() { RowId = "pause", Title = state.Running ? L("pause") : L("resume"),
                    RowCommandId = context.PluginId + (state.Running ? ".pause" : ".resume") },
                new() { RowId = "stop", Title = L("stop"), RowCommandId = context.PluginId + ".stop" },
            ] },
        });
        if (state.Remaining > TimeSpan.Zero)
            context.Islands.TryPublish(new IslandEvent
            {
                Source = context.PluginId, Title = remaining, Content = status, IconKind = "timer",
                Kind = IslandEventKind.Progress, LayoutId = context.PluginId + ".compact",
                Payload = new IslandEventPayload { Progress = state.Progress, IsActive = true,
                    Fields = new Dictionary<string, string?> { ["title"] = remaining, ["subtitle"] = status,
                        ["progress"] = state.Progress.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
            });
        else context.Islands.TryRetract(context.PluginId);
        if (state.Completed && !_notified)
        {
            _notified = true;
            context.Islands.TryPublish(new IslandEvent
            {
                Source = context.PluginId, Title = L("complete"), Content = L("title"), IconKind = "timer",
                Kind = IslandEventKind.Notification, Payload = new IslandEventPayload { SourceName = L("title") },
            });
            context.Notifications.TryPost(new PluginNotification { Title = L("title"), Body = L("complete"), Kind = PluginNotificationKind.Success });
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _started = false;
            _tick?.Dispose(); _tick = null;
            _lease?.Dispose(); _lease = null;
            foreach (var registration in _commands) registration.Dispose();
            _commands.Clear();
            _timer?.Stop();
            _context?.Islands.TryRetract(_context.PluginId);
            _context?.Islands.UnregisterPage(PageId);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}
