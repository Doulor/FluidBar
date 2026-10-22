using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using FluidBar.App.Diagnostics;
using FluidBar.App.Island;
using FluidBar.App.Island.Expanded;
using FluidBar.App.Localization;
using FluidBar.App.Rendering;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.App.Theme;
using FluidBar.Core;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Core.Presentation;
using CoreIslandPage = FluidBar.Core.Activities.PageRef;

namespace FluidBar.App.Hosting;

/// <summary>Composition options for <see cref="IslandHost"/>; all seams are injectable for tests.</summary>
public sealed record IslandHostOptions
{
    /// <summary>Localization service.</summary>
    public required ILocalizer Localizer { get; init; }

    /// <summary>Settings controller.</summary>
    public required SettingsController Settings { get; init; }

    /// <summary>Non-activity content source.</summary>
    public required IIslandDataSource Data { get; init; }

    /// <summary>Window backend factory (defaults to the Platform.Windows interop).</summary>
    public Func<IIslandWindowBackend>? BackendFactory { get; init; }

    /// <summary>Frame clock factory (defaults to the Avalonia compositor frame hook).</summary>
    public Func<TopLevel, IFrameClock>? FrameClockFactory { get; init; }

    /// <summary>Structured logger.</summary>
    public IAppLogger? Logger { get; init; }

    /// <summary>Injected time source.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>Whether to show the island immediately on startup (settings can disable it).</summary>
    public bool StartVisible { get; init; } = true;
}

/// <summary>
/// Owns the island lifecycle: fixed-size host windows per monitor, the Core event hub → activity
/// scheduler → island state controller pipeline, data-effect execution on the frame loop, pointer
/// input, hover/auto-collapse timers, and live settings/language application.
/// </summary>
public sealed class IslandHost : IDisposable
{
    private const int TimerIntervalMs = 50;
    private static readonly TimeSpan TopmostInterval = TimeSpan.FromSeconds(1);

    private readonly IslandHostOptions _options;
    private readonly IAppLogger _logger;
    private readonly ILocalizer _localizer;
    private readonly SettingsController _settings;
    private readonly IIslandDataSource _data;
    private readonly TimeProvider _time;
    private readonly AvaloniaDispatcher _dispatcher = AvaloniaDispatcher.Instance;
    private readonly EventHub _hub;
    private readonly ActivityScheduler _scheduler = new();
    private readonly List<WindowEntry> _windows = [];
    private readonly DispatcherTimer _deadlineTimer;
    private DispatcherTimer? _topmostTimer;

    private ActivitySchedulerState _schedulerState = ActivitySchedulerState.Empty;
    private IslandStateController _controller;
    private ShellMotionDriver? _motion;
    private CoreIslandPage _requestedPage = CoreIslandPage.Home;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates the host (call <see cref="StartAsync"/> to create windows and begin).</summary>
    public IslandHost(IslandHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _localizer = options.Localizer;
        _settings = options.Settings;
        _data = options.Data;
        _time = options.TimeProvider ?? TimeProvider.System;
        _logger = options.Logger ?? new TraceAppLogger();

        _hub = new EventHub(logger: _logger, dispatcher: _dispatcher);
        _controller = CreateController(_settings.Current);
        _controller.Transitioned += OnTransition;
        _hub.Subscribe(OnIslandEvent);
        _settings.Changed += OnSettingsChanged;
        _data.Changed += OnDataChanged;

        _deadlineTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TimerIntervalMs) };
        _deadlineTimer.Tick += OnDeadlineTick;
    }

    /// <summary>Raised when the user asks to execute a media transport command (host/platform wires it).</summary>
    public event Action<MediaTransportCommand>? MediaCommandRequested;

    /// <summary>Raised when the settings window should open (quick action / hotkey).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the marketplace should open.</summary>
    public event Action? MarketRequested;

    /// <summary>Current island state snapshot.</summary>
    public IslandState State => _controller.Current;

    /// <summary>Current activity scheduler snapshot.</summary>
    public ActivitySchedulerState SchedulerState => _schedulerState;

    /// <summary>Motion driver (tests step it with a manual clock).</summary>
    public ShellMotionDriver? Motion => _motion;

    /// <summary>Live windows (one per target monitor).</summary>
    public IReadOnlyList<IslandWindow> Windows => _windows.Select(static entry => entry.Window).ToArray();

    /// <summary>Primary window used for frame pacing/diagnostics.</summary>
    public IslandWindow? PrimaryWindow => _windows.Count > 0 ? _windows[0].Window : null;

    /// <summary>Publishes an activity event from any thread (platform/plugin producers use this).</summary>
    public bool Publish(in IslandEvent islandEvent) => _hub.TryPublish(islandEvent);

    /// <summary>Human-readable window/shell diagnostics (smoke runs, bug reports).</summary>
    public string DescribeWindows()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var entry in _windows)
        {
            var window = entry.Window;
            var frame = window.Shell.Frame;
            builder.AppendLine(
                $"hwnd=0x{window.Hwnd:X} attached={window.IsNativeAttached} visible={window.IsVisible} opacity={window.Opacity:0.00} " +
                $"pos={window.Position} size={window.Width}x{window.Height} hint={string.Join("|", window.TransparencyLevelHint)} actual={window.ActualTransparencyLevel} fallback={window.TransparencyBackgroundFallback} " +
                $"monitor={entry.Monitor?.Id ?? "?"} scale={entry.Monitor?.Scale ?? 0:0.00} work={entry.Monitor?.WorkArea} bounds={entry.Monitor?.Bounds} client={window.ClientSize} renderScaling={window.RenderScaling} " +
                $"frame=({frame.Width:0.0}x{frame.Height:0.0} origin=({frame.OriginX:0.0},{frame.OriginY:0.0}) hidden={frame.IsHidden} " +
                $"compactA={frame.CompactOpacity:0.00} expandedA={frame.ExpandedOpacity:0.00} ghosts={frame.GhostCount} shellOpacity={frame.ShellOpacity:0.00} shellBounds={window.Shell.Bounds} effectiveVisible={window.IsEffectivelyVisible})");
        }

        builder.AppendLine($"state={State} schedulerCount={_schedulerState.ActivityCount} motionRunning={_motion?.IsRunning ?? false}");
        return builder.ToString();
    }
    /// <summary>Current monitor snapshots the windows are placed on.</summary>
    public IReadOnlyList<FluidBar.Core.Layout.MonitorInfo> Monitors
        => _windows.Count == 0 ? [] : _windows.Select(static entry => entry.Monitor).ToArray();

    /// <summary>Starts the host: creates the per-monitor windows, shows them and enters the idle state.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _logger.Information(LogCategories.Lifecycle, "island.start", "Starting island host.");
        _data.Start();

        var settings = _settings.Current;
        var mode = settings.IslandMode;
        var anchor = settings.IslandAnchor;
        var firstOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var primary = CreateWindow(mode, firstOpened, isFirst: true);
        primary.Window.Show();
        var opened = await Task.WhenAny(firstOpened.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(true);
        if (opened != firstOpened.Task)
        {
            _logger.Warning(LogCategories.Lifecycle, "island.opened-timeout",
                "Window Opened callback did not fire; continuing with best-effort native attach.");
            AttachAndPlace(_windows[0]);
        }

        // Additional per-monitor windows when the user asked for all displays.
        if (settings.DisplayOn == DisplayTarget.All)
        {
            foreach (var monitor in EnumerateTargetMonitors(primary.Window).Skip(1))
            {
                var entry = CreateWindow(mode, opened: null, isFirst: false);
                entry.PendingMonitor = monitor;
                entry.Window.Show();
                AttachAndPlace(entry);
            }
        }

        var clock = _options.FrameClockFactory?.Invoke(primary.Window)
            ?? new AvaloniaFrameClock(primary.Window);
        _motion = new ShellMotionDriver(clock, settings.BuildMotionProfile(SystemPrefersReducedMotion()));
        _motion.FrameUpdated += OnFrameUpdated;
        _motion.Settled += OnMotionSettled;

        if (_options.StartVisible && settings.Advanced.SafeMode == false)
        {
            ApplyInput(IslandInput.ShowHotkey());
        }
        else
        {
            ApplyInput(IslandInput.HideHotkey());
        }

        SyncView();
        _motion.SnapToTarget();
        ApplyFrameToWindows(_motion.Frame);
        StartTopmostTimer();
        EnsureDeadlineTimer();
        _logger.Information(LogCategories.Lifecycle, "island.started",
            "Island host started.",
            data: new Dictionary<string, object?>
            {
                ["mode"] = mode.ToString(),
                ["monitors"] = _windows.Count,
                ["anchor"] = anchor.ToString(),
            });
    }

    /// <summary>Runs one deadline/timer pass synchronously (tests with a virtual clock).</summary>
    public void PumpTimers() => OnDeadlineTick(this, EventArgs.Empty);

    /// <summary>Simulates a pointer enter on the primary window (headless tests).</summary>
    public void SimulatePointerEntered() => ApplyInput(IslandInput.PointerEntered());

    /// <summary>Simulates a pointer leave on the primary window (headless tests).</summary>
    public void SimulatePointerLeft() => ApplyInput(IslandInput.PointerLeft());

    /// <summary>Simulates a primary click on the pill (headless tests).</summary>
    public void SimulatePrimaryClick() => ApplyInput(IslandInput.PrimaryClick());

    /// <summary>Simulates Escape.</summary>
    public void SimulateEscape() => ApplyInput(IslandInput.Escape());

    /// <summary>Simulates the toggle hotkey.</summary>
    public void SimulateToggleHotkey() => ApplyInput(IslandInput.ToggleHotkey());

    /// <summary>Simulates a rail page request (keyboard/gutter).</summary>
    public void SimulatePageRequested(CoreIslandPage page) => ApplyInput(IslandInput.PageRequested(page));

    /// <summary>Requests the hover-intent deadline input directly (deterministic tests).</summary>
    public void SimulateHoverIntentElapsed() => ApplyInput(IslandInput.HoverIntentElapsed());

    /// <summary>Requests the auto-collapse deadline input directly.</summary>
    public void SimulateAutoCollapseElapsed() => ApplyInput(IslandInput.AutoCollapseElapsed());

    /// <summary>Hit-tests the primary window in client DIPs (click-through matrix tests).</summary>
    public bool HitTestPrimary(double dipX, double dipY)
        => PrimaryWindow?.HitTestDip(dipX, dipY) ?? false;

    /// <summary>Applies one reducer input and executes the emitted effects (public for tests/tools).</summary>
    public IslandTransition ApplyInput(in IslandInput input)
    {
        var transition = _controller.Apply(input, _time.GetLocalNow());
        return transition;
    }

    /// <summary>Publishes a demo activity (developer runs/tests without platform producers).</summary>
    public bool PublishActivity(ActivityKind kind, ActivityPayload payload, long sequence, string source = "demo")
        => Publish(IslandEvent.New(new ActivityKey(kind, source), payload, sequence, TimeSpan.Zero));

    /// <summary>Retracts a demo activity.</summary>
    public bool RetractActivity(ActivityKind kind, long sequence, string source = "demo")
        => Publish(IslandEvent.Retract(new ActivityKey(kind, source), sequence, TimeSpan.Zero));

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.Information(LogCategories.Lifecycle, "island.stop", "Stopping island host.");
        _deadlineTimer.Stop();
        _topmostTimer?.Stop();
        _topmostTimer = null;
        _settings.Changed -= OnSettingsChanged;
        _data.Changed -= OnDataChanged;
        _data.Stop();
        if (_motion is not null)
        {
            _motion.FrameUpdated -= OnFrameUpdated;
            _motion.Settled -= OnMotionSettled;
            _motion.Dispose();
            _motion = null;
        }

        _controller.Transitioned -= OnTransition;
        _controller.Dispose();
        _hub.Dispose();
        foreach (var entry in _windows)
        {
            entry.Window.Dispose();
        }

        _windows.Clear();
    }

    private IslandStateController CreateController(AppSettings settings)
        => new(BuildBehaviorOptions(settings), _controller?.Current ?? IslandState.Hidden, _dispatcher);

    private static IslandBehaviorOptions BuildBehaviorOptions(AppSettings settings) => new()
    {
        Enabled = true,
        HoverPeek = settings.HoverPeek,
        HoverReveal = true,
        AutoCollapseMs = settings.AutoCollapseMs,
        AutoHideMs = settings.AutoHideMs,
        HoverIntentMs = UiDurations.HoverIntent,
        LeaveDebounceMs = UiDurations.LeaveDebounce,
        PeekDwellMs = 2400,
    };

    private WindowEntry CreateWindow(IslandMode mode, TaskCompletionSource? opened, bool isFirst)
    {
        var backend = _options.BackendFactory?.Invoke()
            ?? new PlatformWindowBackend(new FluidBar.Platform.Windows.Hosting.IslandWindowInterop());
        var window = new IslandWindow(backend);
        window.SetDockMode(mode == IslandMode.Dock);
        var entry = new WindowEntry(window);
        _windows.Add(entry);

        window.Opened += (_, _) =>
        {
            AttachAndPlace(entry);
            opened?.TrySetResult();
        };

        window.Shell.PointerEntered += (_, _) => ApplyInput(IslandInput.PointerEntered());
        window.Shell.PointerExited += (_, _) => ApplyInput(IslandInput.PointerLeft());
        window.Shell.PointerPressed += (_, e) => OnShellPointerPressed(entry, e);
        window.Shell.PointerReleased += (_, _) => _motion?.SetPressed(false);
        window.Panel.ActionRequested += OnPageActionRequested;
        window.Panel.PageRequested += OnPageRequested;
        window.KeyDown += OnWindowKeyDown;
        return entry;
    }

    private void AttachAndPlace(WindowEntry entry)
    {
        entry.Window.AttachNative();
        var monitor = entry.PendingMonitor ?? ResolvePrimaryMonitor(entry.Window);
        entry.Monitor = monitor;
        if (monitor is not null)
        {
            var settings = _settings.Current;
            entry.Window.SetDockMode(settings.IslandMode == IslandMode.Dock);
            entry.Window.PlaceOn(monitor, settings.IslandMode, settings.IslandAnchor);
        }
    }

    private FluidBar.Core.Layout.MonitorInfo? ResolvePrimaryMonitor(Window window)
    {
        var screens = window.Screens;
        var primary = ScreenMapper.PrimaryScreen(screens);
        return primary is null ? null : ScreenMapper.ToCoreMonitor(primary, 0, true);
    }

    private IReadOnlyList<FluidBar.Core.Layout.MonitorInfo> EnumerateTargetMonitors(Window window)
    {
        var screens = window.Screens;
        var list = new List<FluidBar.Core.Layout.MonitorInfo>();
        for (var i = 0; i < screens.All.Count; i++)
        {
            var screen = screens.All[i];
            list.Add(ScreenMapper.ToCoreMonitor(screen, i, screen.IsPrimary));
        }

        if (list.Count == 0)
        {
            // Headless/fallback: synthetic 1920×1080 primary work area.
            list.Add(new FluidBar.Core.Layout.MonitorInfo
            {
                Id = "virtual",
                Bounds = new FluidBar.Core.Layout.PixelRect(0, 0, 1920, 1080),
                WorkArea = new FluidBar.Core.Layout.PixelRect(0, 0, 1920, 1040),
                Dpi = 96,
                IsPrimary = true,
            });
        }

        return list;
    }

    private void OnIslandEvent(IslandEvent islandEvent)
    {
        if (!islandEvent.Key.IsValid)
        {
            return;
        }

        var update = _scheduler.Update(_schedulerState, islandEvent, _time.GetLocalNow());
        _schedulerState = update.State;
        if (update.Kind == ActivityUpdateKind.None)
        {
            return;
        }

        _logger.Debug(LogCategories.Activity, "island.event", $"Activity {islandEvent.Kind}.",
            data: new Dictionary<string, object?>
            {
                ["key"] = islandEvent.Key.ToString(),
                ["update"] = update.Kind.ToString(),
                ["count"] = update.State.ActivityCount,
            });
        ApplyInput(IslandInput.ForActivityUpdate(update));
        EnsureDeadlineTimer();
    }

    private void OnTransition(IslandTransition transition)
    {
        if (transition.Reason == IslandTransitionReason.NoChange && transition.Effects.Length == 0)
        {
            return;
        }

        _logger.Debug(LogCategories.Activity, "island.transition",
            $"{transition.Previous.Kind} -> {transition.State.Kind} ({transition.Reason}).");
        ExecuteEffects(transition.Effects);
        SyncView();
        EnsureDeadlineTimer();
    }

    private void ExecuteEffects(ImmutableArray<IslandEffect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case IslandEffectKind.ShowWindow:
                case IslandEffectKind.HideWindow:
                    // Windows stay alive; the shell fades and click-through is synced below.
                    break;
                case IslandEffectKind.StartFrameLoop:
                    _motion?.StartLoop();
                    break;
                case IslandEffectKind.StopFrameLoop:
                    _motion?.StopLoop();
                    break;
                case IslandEffectKind.SnapSprings:
                    _motion?.SnapToTarget();
                    ApplyFrameToWindows(_motion?.Frame ?? default);
                    break;
                case IslandEffectKind.Relayout:
                case IslandEffectKind.UpdateContent:
                    SyncView();
                    break;
                case IslandEffectKind.Retarget:
                    break;
                case IslandEffectKind.PlayNudge:
                    _motion?.PlayNudge();
                    break;
                case IslandEffectKind.SyncClickThrough:
                    ApplyClickThrough(State.Kind == IslandStateKind.Hidden);
                    break;
                case IslandEffectKind.FocusRail:
                    PrimaryWindow?.Panel.Focus();
                    break;
                case IslandEffectKind.UpdateTray:
                    break;
                case IslandEffectKind.PersistPlacement:
                    _logger.Debug(LogCategories.Settings, "island.persist-placement",
                        "Placement persistence is wired to Core settings once the migration lands.");
                    break;
                case IslandEffectKind.PauseProducers:
                    _data.Stop();
                    break;
                case IslandEffectKind.ResumeProducers:
                    _data.Start();
                    break;
            }
        }
    }

    private void SyncView()
    {
        var state = _controller.Current;
        var content = _data.Snapshot();
        var viewModel = ActivityPresenter.PresentIsland(_schedulerState, state);
        var peeked = state.Kind == IslandStateKind.Peek;
        var compactModel = IslandPresenter.ToCompact(viewModel, content, _localizer, peeked);
        var pageContext = new ExpandedPageContext
        {
            Content = content,
            Island = viewModel,
            Settings = _settings.Current,
            Localizer = _localizer,
            SelectedPage = state.Page,
        };

        foreach (var entry in _windows)
        {
            EnsureCompactSurface(entry, compactModel);
            entry.Window.Panel.Update(pageContext);
        }

        _motion?.SetTarget(BuildMotionTarget(state, viewModel));
    }

    private void EnsureCompactSurface(WindowEntry entry, CompactContentModel model)
    {
        var size = LayoutTokens.CompactSize(model.Kind, _settings.Current.IslandMode);
        var designSize = new Size(Math.Min(size.Width, LayoutTokens.ExpandedWidth), size.Height);
        if (entry.Surface is null || entry.Surface.Model.Style != model.Style)
        {
            var surface = CompactSurfaceFactory.CreateFor(model);
            surface.UpdateModel(model);
            surface.ApplyLocalization(_localizer);
            entry.Surface = surface;
            entry.Window.Shell.SetCompactLayer(surface, designSize);
        }
        else
        {
            entry.Surface.UpdateModel(model);
            entry.Window.Shell.SetCompactLayer(entry.Surface, designSize);
        }
    }

    private ShellMotionTarget BuildMotionTarget(IslandState state, IslandViewModel viewModel)
    {
        var settings = _settings.Current;
        var mode = settings.IslandMode;
        var compactKind = state.ContentKind ?? viewModel.Primary?.Kind;
        var compact = LayoutTokens.CompactSize(compactKind);
        var stacking = settings.Stacking == StackingStrategySetting.Stacked; // split islets: 见 Skia host
        var ghosts = stacking ? Math.Clamp(viewModel.Ghosts.Length, 0, LayoutTokens.MaxGhosts) : 0;
        var form = state.Kind switch
        {
            IslandStateKind.Compact => ShellForm.Compact,
            IslandStateKind.Peek => ShellForm.Peek,
            IslandStateKind.Expanded => ShellForm.Expanded,
            IslandStateKind.Closing => ShellForm.Closing,
            _ => ShellForm.Hidden,
        };

        return new ShellMotionTarget(
            form,
            mode,
            settings.IslandAnchor,
            compact,
            ghosts,
            settings.Colors,
            settings.FillOpacity,
            settings.Material != IslandMaterial.Obsidian,
            state.Kind == IslandStateKind.Hidden,
            mode == IslandMode.Dock ? IslandWindow.DockWindowHeight : UiMetrics.WindowHeight);
    }

    private void OnFrameUpdated(ShellFrame frame) => ApplyFrameToWindows(frame);

    private void ApplyFrameToWindows(in ShellFrame frame)
    {
        foreach (var entry in _windows)
        {
            entry.Window.ApplyFrame(frame);
        }
    }

    private void OnMotionSettled(object? sender, EventArgs e)
    {
        foreach (var entry in _windows)
        {
            entry.Window.UpdateRegionOnSettle();
        }

        var state = _controller.Current;
        ApplyClickThrough(state.Kind == IslandStateKind.Hidden);
        if (state.Kind == IslandStateKind.Closing)
        {
            ApplyInput(IslandInput.CloseSettled());
        }
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        foreach (var entry in _windows)
        {
            entry.Window.ApplyClickThrough(clickThrough);
        }
    }

    private void OnDeadlineTick(object? sender, EventArgs e)
    {
        var now = _time.GetLocalNow();
        var state = _controller.Current;

        if (state.HoverIntentDeadline is { } hover && now >= hover)
        {
            ApplyInput(IslandInput.HoverIntentElapsed());
        }

        if (state.LeaveDeadline is { } leave && now >= leave)
        {
            ApplyInput(IslandInput.LeaveDebounceElapsed());
        }

        if (state.PeekDwellDeadline is { } dwell && now >= dwell)
        {
            ApplyInput(IslandInput.PeekDwellElapsed());
        }

        if (state.AutoCollapseDeadline is { } collapse && now >= collapse)
        {
            ApplyInput(IslandInput.AutoCollapseElapsed());
        }

        if (state.AutoHideDeadline is { } hide && now >= hide)
        {
            ApplyInput(IslandInput.AutoHideElapsed());
        }

        if (_schedulerState.HasActivities)
        {
            var update = _scheduler.Tick(_schedulerState, now);
            if (update.Kind != ActivityUpdateKind.None)
            {
                _schedulerState = update.State;
                ApplyInput(IslandInput.ForActivityUpdate(update));
            }
        }

        EnsureDeadlineTimer();
    }

    private void EnsureDeadlineTimer()
    {
        if (_disposed)
        {
            return;
        }

        var state = _controller.Current;
        var needed = state.HoverIntentDeadline is not null || state.LeaveDeadline is not null
            || state.PeekDwellDeadline is not null || state.AutoCollapseDeadline is not null
            || state.AutoHideDeadline is not null || _schedulerState.HasActivities;
        if (needed && !_deadlineTimer.IsEnabled)
        {
            _deadlineTimer.Start();
        }
        else if (!needed && _deadlineTimer.IsEnabled)
        {
            _deadlineTimer.Stop();
        }
    }

    private void StartTopmostTimer()
    {
        _topmostTimer?.Stop();
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TopmostInterval };
        _topmostTimer.Tick += (_, _) => ReassertTopmost();
        _topmostTimer.Start();
    }

    private void ReassertTopmost()
    {
        if (State.Kind == IslandStateKind.Hidden)
        {
            return;
        }

        foreach (var entry in _windows)
        {
            entry.Window.ReassertTopmost();
        }
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        // The design/platform data sources may raise Changed from a timer or producer thread
        // (DesignIslandDataSource.ScheduleMinuteTick uses System.Threading.Timer). All visual-tree
        // mutation must run on the Avalonia UI thread or LayoutManager throws VerifyAccess
        // (crash 0xE0434352 observed in release perf sampling).
        if (Dispatcher.UIThread.CheckAccess())
        {
            SyncView();
            return;
        }

        Dispatcher.UIThread.Post(SyncView, DispatcherPriority.Background);
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (!string.Equals(previous.Language, current.Language, StringComparison.OrdinalIgnoreCase))
        {
            if (_localizer is LocalizationService service)
            {
                service.SetCulture(current.Language);
            }

            RelocalizeViews();
        }

        var previousProfile = previous.BuildMotionProfile(SystemPrefersReducedMotion());
        var currentProfile = current.BuildMotionProfile(SystemPrefersReducedMotion());
        if (previousProfile != currentProfile)
        {
            _motion?.UpdateProfile(currentProfile);
        }

        if (previous.IslandMode != current.IslandMode)
        {
            foreach (var entry in _windows)
            {
                entry.Window.SetDockMode(current.IslandMode == IslandMode.Dock);
                if (entry.Monitor is not null)
                {
                    entry.Window.PlaceOn(entry.Monitor, current.IslandMode, current.IslandAnchor);
                }
            }

            ApplyInput(IslandInput.ModeChanged());
            _motion?.SnapToTarget();
        }
        else if (previous.IslandAnchor != current.IslandAnchor)
        {
            foreach (var entry in _windows)
            {
                if (entry.Monitor is not null)
                {
                    entry.Window.PlaceOn(entry.Monitor, current.IslandMode, current.IslandAnchor);
                }
            }

            ApplyInput(IslandInput.DisplayChanged());
            _motion?.SnapToTarget();
        }

        // Behavior switches (hover peek/auto-hide/auto-collapse) need a controller with the new options.
        if (previous.HoverPeek != current.HoverPeek || previous.AutoCollapseMs != current.AutoCollapseMs
            || previous.AutoHideMs != current.AutoHideMs)
        {
            RecreateController(current);
            ApplyInput(IslandInput.SettingsChanged());
        }

        SyncView();
        RelocalizeViews();
    }

    private void RecreateController(AppSettings settings)
    {
        var current = _controller.Current;
        _controller.Transitioned -= OnTransition;
        _controller.Dispose();
        _controller = new IslandStateController(BuildBehaviorOptions(settings), current, _dispatcher);
        _controller.Transitioned += OnTransition;
    }

    private void RelocalizeViews()
    {
        foreach (var entry in _windows)
        {
            entry.Surface?.ApplyLocalization(_localizer);
            entry.Window.Panel.ApplyLocalization(_localizer);
        }
    }

    private bool SystemPrefersReducedMotion()
    {
        // Windows exposes SPI_GETCLIENTAREAANIMATION; App cannot P/Invoke (spec 5.2), so the platform
        // adapter supplies this once available. Default to animations enabled (spec: tri-state system).
        return false;
    }

    private void OnWindowKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape)
        {
            ApplyInput(IslandInput.Escape());
            e.Handled = true;
            return;
        }

        if (State.Kind == IslandStateKind.Expanded && PrimaryWindow?.Panel.HandleKey(e.Key) == true)
        {
            e.Handled = true;
        }
    }

    private void OnPageRequested(CoreIslandPage page)
    {
        _requestedPage = page;
        ApplyInput(IslandInput.PageRequested(page));
    }

    private void OnShellPointerPressed(WindowEntry entry, Avalonia.Input.PointerPressedEventArgs e)
    {
        _motion?.SetPressed(true);
        var state = _controller.Current;
        if (state.Kind is IslandStateKind.Compact or IslandStateKind.Peek)
        {
            var position = e.GetPosition(entry.Window.Shell);
            if (entry.Surface is not null && entry.Window.Shell.TryCompactLocalPoint(position, out var local))
            {
                var action = entry.Surface.HitTest(local, state.Kind == IslandStateKind.Peek);
                switch (action)
                {
                    case CompactAction.TogglePlay:
                        MediaCommandRequested?.Invoke(MediaTransportCommand.TogglePlay);
                        e.Handled = true;
                        return;
                    case CompactAction.Previous:
                        MediaCommandRequested?.Invoke(MediaTransportCommand.Previous);
                        e.Handled = true;
                        return;
                    case CompactAction.Next:
                        MediaCommandRequested?.Invoke(MediaTransportCommand.Next);
                        e.Handled = true;
                        return;
                }
            }

            ApplyInput(IslandInput.PrimaryClick());
        }
    }

    private void OnPageActionRequested(IslandPageActionRequest request)
    {
        switch (request.Action)
        {
            case IslandPageAction.OpenSettings:
                SettingsRequested?.Invoke();
                break;
            case IslandPageAction.OpenMarket:
                MarketRequested?.Invoke();
                break;
            case IslandPageAction.HideIsland:
                ApplyInput(IslandInput.HideHotkey());
                break;
            case IslandPageAction.TogglePlay:
                MediaCommandRequested?.Invoke(MediaTransportCommand.TogglePlay);
                break;
            case IslandPageAction.PreviousTrack:
                MediaCommandRequested?.Invoke(MediaTransportCommand.Previous);
                break;
            case IslandPageAction.NextTrack:
                MediaCommandRequested?.Invoke(MediaTransportCommand.Next);
                break;
            case IslandPageAction.ClearNotifications:
                if (_data is DesignIslandDataSource design)
                {
                    design.Update(content => content with { Notifications = [], UnreadCount = 0 });
                }

                break;
        }
    }

    private sealed class WindowEntry(IslandWindow window)
    {
        public IslandWindow Window { get; } = window;

        public CompactSurface? Surface { get; set; }

        public FluidBar.Core.Layout.MonitorInfo? PendingMonitor { get; set; }

        public FluidBar.Core.Layout.MonitorInfo? Monitor { get; set; }
    }
}

/// <summary>Media transport commands requested by island UI interactions.</summary>
public enum MediaTransportCommand
{
    /// <summary>Play/pause.</summary>
    TogglePlay,

    /// <summary>Previous track.</summary>
    Previous,

    /// <summary>Next track.</summary>
    Next,
}

