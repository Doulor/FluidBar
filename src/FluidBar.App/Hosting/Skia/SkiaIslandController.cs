using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Core.Presentation;
using FluidBar.Island;
using CoreIslandPage = FluidBar.Core.Activities.IslandPage;

namespace FluidBar.App.Hosting.Skia;

/// <summary>One frame invalidation produced by the controller.</summary>
/// <param name="FullFrame">Whether the whole surface must be repainted/uploaded.</param>
/// <param name="DamageDip">Damage rectangle in host-local DIP (dirty frames only).</param>
public readonly record struct SkiaIslandRenderChange(bool FullFrame, VisualRect DamageDip)
{
    /// <summary>Full-surface repaint.</summary>
    public static SkiaIslandRenderChange Full { get; } = new(true, VisualRect.Empty);
}

/// <summary>
/// Window-free island pipeline for the Skia host: Core event hub → activity scheduler → island state
/// controller, deadline timers, shell/pager/archipelago springs, content mapping and input routing.
/// <para>
/// It owns no HWND and no Skia surface, so it is fully testable headless. The host creates one
/// instance and drives it with <see cref="PumpTimers"/>/<see cref="Advance"/>, then renders the
/// <see cref="RenderState"/> into every monitor's layered surface.
/// </para>
/// </summary>
public sealed class SkiaIslandController : IDisposable
{
    private readonly SkiaIslandHostOptions _options;
    private readonly IAppLogger _logger;
    private readonly ILocalizer _localizer;
    private readonly SettingsController _settings;
    private readonly IIslandDataSource _data;
    private readonly TimeProvider _time;
    private readonly SkiaHostDispatcher _dispatcher;
    private readonly EventHub _hub;
    private readonly ActivityScheduler _scheduler = new();
    private readonly SkiaIslandMotion _motion;
    private readonly SkiaPager _pager;
    private readonly SkiaArchipelago _archipelago;
    private readonly IIslandPlatformActions? _platformActions;

    private ActivitySchedulerState _schedulerState = ActivitySchedulerState.Empty;
    private IslandStateController _controller;

    private CompactActivity _compact = new(ActivityKind.Idle, new CompactModel());
    private ExpandedPageContent _pages = ExpandedPageContent.Empty;
    private IslandTheme _theme = IslandTheme.Dark;
    private string _structuralSignature = string.Empty;
    private string _dynamicSignature = string.Empty;
    private IslandRenderState _renderState;
    private bool _hasRenderState;
    private VisualRect? _accessibilityFocus;

    private bool _pendingFull = true;
    private bool _pendingDirty;
    private VisualRect _pendingDamage;

    private double _motionPhase;
    private double _waveAccumulator;
    private double _pointerX;
    private double _pointerY;
    private bool _pointerInside;
    private bool _pointerInHotZone;
    private bool _pointerOnStrip;
    private DateTimeOffset? _hotZoneEnteredAt;
    private bool _syncingPage;
    private bool _started;
    private bool _disposed;

    // Click-vs-drag: a compact press is only a candidate click; the host turns it into a window move
    // once the pointer passes SkiaDragThreshold in screen space, and the click is committed on release.
    private bool _clickPending;
    private double _clickXDip;
    private double _clickYDip;
    private bool _windowDragActive;

    /// <summary>Creates the controller (call <see cref="Start"/> to begin producing content).</summary>
    /// <param name="options">Composition options.</param>
    /// <param name="dispatcher">Optional dispatcher (defaults to a UI-thread synchronization-context dispatcher).</param>
    public SkiaIslandController(SkiaIslandHostOptions options, SkiaHostDispatcher? dispatcher = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _localizer = options.Localizer;
        _settings = options.Settings;
        _data = options.Data;
        _time = options.TimeProvider ?? TimeProvider.System;
        _logger = options.Logger ?? new Diagnostics.TraceAppLogger();
        _dispatcher = dispatcher ?? new SkiaHostDispatcher();
        _platformActions = options.PlatformActions;

        _hub = new EventHub(logger: _logger, dispatcher: _dispatcher);
        _controller = CreateController(_settings.Current);
        _controller.Transitioned += OnTransition;
        _hub.Subscribe(OnIslandEvent);

        var profile = _settings.Current.BuildMotionProfile(SkiaIslandThemeMapper.ReducedMotion(_settings.Current));
        _motion = new SkiaIslandMotion(profile);
        _pager = new SkiaPager((int)State.Page);
        _pager.PageChanged += OnPagerPageChanged;
        _archipelago = new SkiaArchipelago(profile);

        _settings.Changed += OnSettingsChanged;
        _data.Changed += OnDataChanged;
    }

    /// <summary>Raised when the settings window should open (quick action / hotkey).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the marketplace should open.</summary>
    public event Action? MarketRequested;

    /// <summary>Raised when the user asks to execute a media transport command.</summary>
    public event Action<MediaTransportCommand>? MediaCommandRequested;

    /// <summary>Raised after a state/content change so the host can refresh placement and the hit region.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when the host must re-place windows (mode/anchor/display change).</summary>
    public event Action? PlacementRequested;

    /// <summary>Raised when animation starts so the host can start its frame clock immediately.</summary>
    public event Action? FrameRequested;

    /// <summary>
    /// Raised when Core asks for the placement to be persisted (<see cref="IslandEffectKind.PersistPlacement"/>,
    /// i.e. after a drag ends). The host owns the drop geometry: it snapshots the pending anchor/offset
    /// before ending the drag and writes it on this callback. Fired on every environment change that
    /// carries the effect; the host ignores it when it has no pending drop.
    /// </summary>
    public event Action? PlacementPersistRequested;

    /// <summary>Current island state snapshot.</summary>
    public IslandState State => _controller.Current;

    /// <summary>Current activity scheduler snapshot.</summary>
    public ActivitySchedulerState SchedulerState => _schedulerState;

    /// <summary>Shell spring driver.</summary>
    public SkiaIslandMotion Motion => _motion;

    /// <summary>Horizontal page pager.</summary>
    public SkiaPager Pager => _pager;

    /// <summary>Detached Page Archipelago strip.</summary>
    public SkiaArchipelago Archipelago => _archipelago;

    /// <summary>Last built render state.</summary>
    public IslandRenderState RenderState => _renderState;

    /// <summary>Whether a render state has been built at least once.</summary>
    public bool HasRenderState => _hasRenderState;

    /// <summary>Whether the host started the pipeline.</summary>
    public bool IsStarted => _started;

    /// <summary>Whether a compact press is waiting to resolve into a click or a window drag.</summary>
    public bool HasPendingClick => _clickPending;

    /// <summary>Whether the pointer is currently moving the island window (drag-to-move).</summary>
    public bool IsWindowDragging => _windowDragActive;

    /// <summary>Whether motion, the pager, the strip or dynamic content still needs frames.</summary>
    public bool IsAnimating => !_motion.IsAtRest || _pager.IsAnimating || _archipelago.IsAnimating || IsWaveActive();

    /// <summary>Whether the island is only animating the media waveform (lower frame rate is enough).</summary>
    public bool IsWaveOnly => _motion.IsAtRest && !_pager.IsAnimating && !_archipelago.IsAnimating && IsWaveActive();

    /// <summary>Frame interval the host should use for the current activity.</summary>
    public TimeSpan PreferredFrameInterval => IsWaveOnly
        ? TimeSpan.FromMilliseconds(33)
        : TimeSpan.FromMilliseconds(1000.0 / _options.ResolvedFrameRate);

    /// <summary>Whether a deadline timer is needed (hover/auto-collapse/scheduler/strip auto-hide).</summary>
    public bool HasPendingDeadline
    {
        get
        {
            var state = State;
            return state.HoverIntentDeadline is not null
                || state.LeaveDeadline is not null
                || state.PeekDwellDeadline is not null
                || state.AutoCollapseDeadline is not null
                || state.AutoHideDeadline is not null
                || _schedulerState.HasActivities
                || _archipelago.IsVisible;
        }
    }

    /// <summary>Starts the data source, reveals the island and snaps the springs to their targets.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _logger.Information(LogCategories.Lifecycle, "island.skia.start", "Starting Skia island controller.");
        _data.Start();

        SyncContent(full: true);
        var settings = _settings.Current;
        if (_options.StartVisible && !settings.Advanced.SafeMode)
        {
            ApplyInput(IslandInput.ShowHotkey());
        }
        else
        {
            ApplyInput(IslandInput.HideHotkey());
        }

        _motion.SnapToTarget();
        _pager.JumpToPage((int)State.Page);
        InvalidateFull();
        BuildRenderState();
        StateChanged?.Invoke();
    }

    /// <summary>Pumps the deadline timers and the scheduler tick (host timer / tests).</summary>
    public void PumpTimers()
    {
        if (_disposed)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var state = State;

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

        UpdatePointerZones();
        UpdateArchipelago(now);
    }

    /// <summary>Steps motion/pager/strip and accumulates render invalidation for one frame.</summary>
    /// <param name="delta">Frame delta.</param>
    public void Advance(TimeSpan delta)
    {
        if (_disposed || !_started)
        {
            return;
        }

        var dt = delta.TotalSeconds;
        if (dt <= 0)
        {
            return;
        }

        var now = _time.GetLocalNow();
        var motionWasRunning = !_motion.IsAtRest;
        _motion.Step(dt);

        if (State.Kind == IslandStateKind.Closing && _motion.IsAtRest)
        {
            ApplyInput(IslandInput.CloseSettled());
        }

        var pagerWasRunning = _pager.IsAnimating;
        if (pagerWasRunning)
        {
            _pager.Step(dt);
        }

        var archWasRunning = _archipelago.IsAnimating;
        if (archWasRunning)
        {
            _archipelago.Step(dt);
        }

        UpdatePointerZones();
        UpdateArchipelago(now);
        BuildRenderState();

        if (motionWasRunning)
        {
            // A shell morph damages the union of the old and new shell: full frame.
            InvalidateFull();
        }
        else if (pagerWasRunning)
        {
            InvalidateDirty(ContentDamage());
        }
        else if (archWasRunning)
        {
            InvalidateDirty(StripDamage());
        }

        if (IsWaveActive())
        {
            _waveAccumulator += dt;
            if (_waveAccumulator >= 1.0 / 30.0)
            {
                _waveAccumulator = 0;
                _motionPhase += 1.0 / 30.0;
                InvalidateDirty(WaveDamage());
            }
        }
    }

    /// <summary>Takes the accumulated invalidation (full wins over dirty).</summary>
    /// <param name="change">The pending change.</param>
    /// <returns>Whether a frame should be rendered.</returns>
    public bool TakeInvalidation(out SkiaIslandRenderChange change)
    {
        if (_pendingFull)
        {
            _pendingFull = false;
            _pendingDirty = false;
            _pendingDamage = VisualRect.Empty;
            change = SkiaIslandRenderChange.Full;
            return true;
        }

        if (_pendingDirty)
        {
            _pendingDirty = false;
            change = new SkiaIslandRenderChange(false, _pendingDamage);
            _pendingDamage = VisualRect.Empty;
            return true;
        }

        change = default;
        return false;
    }

    /// <summary>Requests a full-surface repaint on the next frame.</summary>
    public void InvalidateFull()
    {
        _pendingFull = true;
        _pendingDirty = false;
        _pendingDamage = VisualRect.Empty;
    }

    /// <summary>Applies one Core reducer input and executes the emitted effects.</summary>
    /// <param name="input">Reducer input.</param>
    public IslandTransition ApplyInput(in IslandInput input) => _controller.Apply(input, _time.GetLocalNow());

    /// <summary>Publishes an activity event from any thread.</summary>
    /// <param name="islandEvent">Event to publish.</param>
    public bool Publish(in IslandEvent islandEvent) => _hub.TryPublish(islandEvent);

    /// <summary>Publishes a demo activity (developer runs/tests without platform producers).</summary>
    /// <param name="kind">Activity kind.</param>
    /// <param name="payload">Activity payload.</param>
    /// <param name="sequence">Per-key sequence.</param>
    /// <param name="source">Producer identity.</param>
    public bool PublishActivity(ActivityKind kind, ActivityPayload payload, long sequence, string source = "demo")
        => Publish(IslandEvent.New(new ActivityKey(kind, source), payload, sequence, TimeSpan.Zero));

    /// <summary>Retracts a demo activity.</summary>
    /// <param name="kind">Activity kind.</param>
    /// <param name="sequence">Per-key sequence.</param>
    /// <param name="source">Producer identity.</param>
    public bool RetractActivity(ActivityKind kind, long sequence, string source = "demo")
        => Publish(IslandEvent.Retract(new ActivityKey(kind, source), sequence, TimeSpan.Zero));

    /// <summary>Applies Escape (collapse).</summary>
    public void HandleEscape()
    {
        CancelPendingClick();
        ApplyInput(IslandInput.Escape());
    }

    /// <summary>Applies the toggle hotkey (expand/collapse).</summary>
    public void HandleToggleHotkey() => ApplyInput(IslandInput.ToggleHotkey());

    /// <summary>Applies the show hotkey.</summary>
    public void HandleShowHotkey() => ApplyInput(IslandInput.ShowHotkey());

    /// <summary>Applies the hide hotkey.</summary>
    public void HandleHideHotkey() => ApplyInput(IslandInput.HideHotkey());

    /// <summary>Raises <see cref="SettingsRequested"/> (tray/hotkey seam).</summary>
    public void RequestSettings() => SettingsRequested?.Invoke();

    /// <summary>Raises <see cref="MarketRequested"/> (tray/hotkey seam).</summary>
    public void RequestMarket() => MarketRequested?.Invoke();

    /// <summary>Requests a page (keyboard/archipelago); the pager animates and the state follows.</summary>
    /// <param name="page">Target page.</param>
    public void HandlePageRequest(CoreIslandPage page)
    {
        if ((int)page == _pager.SelectedIndex)
        {
            return;
        }

        _pager.SelectPage((int)page);
    }

    /// <summary>Moves one page left/right (keyboard arrows).</summary>
    /// <param name="delta">Signed page delta.</param>
    public void HandlePageMove(int delta) => _pager.MoveBy(delta);

    /// <summary>
    /// Sets the logical keyboard/UIA focus ring (host-local DIP), or clears it. Repaints once; it does
    /// not raise <see cref="StateChanged"/> (the accessibility source owns its snapshot refresh).
    /// </summary>
    /// <param name="bounds">Focused element bounds in host-local DIP, or null.</param>
    public void SetAccessibilityFocus(VisualRect? bounds)
    {
        if (!_hasRenderState || Nullable.Equals(_accessibilityFocus, bounds))
        {
            return;
        }

        _accessibilityFocus = bounds;
        BuildRenderState();
        InvalidateFull();
        FrameRequested?.Invoke();
    }

    /// <summary>
    /// Invokes one expanded page control from UI Automation/keyboard without a pointer position.
    /// Reuses the same <see cref="SkiaIslandControlRouter"/> mapping as a click.
    /// </summary>
    /// <param name="control">Page control identity.</param>
    /// <param name="page">Page index the control belongs to.</param>
    /// <param name="index">Row/toggle index when applicable.</param>
    /// <returns>Whether an action was dispatched.</returns>
    public bool ActivateAccessibleControl(IslandPageControl control, int page, int index = -1)
    {
        if (!_hasRenderState || control is IslandPageControl.None or IslandPageControl.Scroll)
        {
            return false;
        }

        if (control == IslandPageControl.MediaCard)
        {
            HandlePageRequest(CoreIslandPage.Media);
            return true;
        }

        var hit = new IslandHitTarget(IslandHitKind.PageControl, page, control, index);
        var action = SkiaIslandControlRouter.Map(hit, _renderState, 0);
        if (action is null)
        {
            return false;
        }

        DispatchControl(action.Value, page);
        return true;
    }

    /// <summary>Sets a slider through the platform seam (UIA RangeValue / keyboard arrows).</summary>
    /// <param name="control">Volume, brightness or seek.</param>
    /// <param name="page">Page index the slider belongs to.</param>
    /// <param name="fraction">Value fraction 0..1.</param>
    /// <returns>Whether an action was dispatched.</returns>
    public bool SetAccessibleSlider(IslandPageControl control, int page, double fraction)
    {
        var kind = control switch
        {
            IslandPageControl.VolumeSlider => IslandPlatformActionKind.VolumeSet,
            IslandPageControl.BrightnessSlider => IslandPlatformActionKind.BrightnessSet,
            IslandPageControl.Seek => IslandPlatformActionKind.MediaSeek,
            _ => (IslandPlatformActionKind?)null,
        };
        if (kind is null)
        {
            return false;
        }

        var value = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        DispatchControl(new IslandPlatformAction(kind.Value, Value: value), page);
        return true;
    }

    /// <summary>Pointer moved over the island (host-local DIP).</summary>
    /// <param name="xDip">Client X in DIP.</param>
    /// <param name="yDip">Client Y in DIP.</param>
    public void HandlePointerMoved(double xDip, double yDip)
    {
        _pointerX = xDip;
        _pointerY = yDip;
        var now = _time.GetLocalNow();
        if (!_pointerInside)
        {
            _pointerInside = true;
            if (State.Kind is IslandStateKind.Compact or IslandStateKind.Peek)
            {
                ApplyInput(IslandInput.PointerEntered());
            }
        }

        UpdatePointerZones();
        if (_pointerInHotZone)
        {
            _hotZoneEnteredAt ??= now;
        }
        else
        {
            _hotZoneEnteredAt = null;
        }

        if (_pager.IsDragging)
        {
            _pager.DragTo(xDip, NowMs());
            _archipelago.NoteActivity(now);
            InvalidateDirty(ContentDamage());
        }

        UpdateArchipelago(now);
    }

    /// <summary>Pointer left the window.</summary>
    public void HandlePointerLeft()
    {
        _pointerInside = false;
        _pointerInHotZone = false;
        _pointerOnStrip = false;
        _hotZoneEnteredAt = null;
        CancelPendingClick();
        var now = _time.GetLocalNow();

        if (_pager.IsDragging)
        {
            _pager.EndDrag(NowMs());
            InvalidateFull();
        }

        _pager.Reset();
        _motion.SetPressed(false);

        if (State.Kind is IslandStateKind.Compact or IslandStateKind.Peek)
        {
            ApplyInput(IslandInput.PointerLeft());
        }

        UpdateArchipelago(now);
    }

    /// <summary>Primary button pressed at a host-local DIP point; returns whether the island handled it.</summary>
    /// <param name="xDip">Client X in DIP.</param>
    /// <param name="yDip">Client Y in DIP.</param>
    /// <remarks>
    /// A compact-shell press is <b>deferred</b>: it becomes a click on release only when the pointer
    /// stayed within <see cref="SkiaDragThreshold.ThresholdDip"/>. The host calls
    /// <see cref="BeginWindowDrag"/> once the pointer passes the (screen-space) threshold, which cancels
    /// the pending click and moves the window instead. Dock mode disables dragging, so its press still
    /// expands immediately.
    /// </remarks>
    public bool HandlePointerPressed(double xDip, double yDip)
    {
        _pointerX = xDip;
        _pointerY = yDip;
        _pointerInside = true;
        _motion.SetPressed(true);
        CancelPendingClick();
        if (!_hasRenderState)
        {
            return false;
        }

        var hit = SkiaIslandHitTest.Hit(_renderState, xDip, yDip);
        switch (hit.Kind)
        {
            case IslandHitKind.Compact:
                if (hit.Control == IslandPageControl.PlayPause)
                {
                    DispatchControl(new IslandPlatformAction(IslandPlatformActionKind.MediaTogglePlay), hit.PageIndex);
                    return true;
                }

                // Expand on press: the compact pill is the primary affordance. Drag-to-move is a
                // secondary gesture; it must never make a normal click feel unresponsive.
                ApplyInput(IslandInput.PrimaryClick());
                return true;

            case IslandHitKind.Collapse:
                ApplyInput(IslandInput.Escape());
                return true;

            case IslandHitKind.PageControl:
                HandlePageControl(hit);
                return true;

            case IslandHitKind.RailSlot:
            case IslandHitKind.DotLeft:
            case IslandHitKind.DotRight:
                _archipelago.NoteActivity(_time.GetLocalNow());
                if (hit.PageIndex >= 0)
                {
                    HandlePageRequest((CoreIslandPage)hit.PageIndex);
                }

                return true;

            default:
                return false;
        }
    }

    /// <summary>Primary button released.</summary>
    /// <param name="xDip">Client X in DIP.</param>
    /// <param name="yDip">Client Y in DIP.</param>
    public void HandlePointerReleased(double xDip, double yDip)
    {
        _pointerX = xDip;
        _pointerY = yDip;
        _motion.SetPressed(false);
        if (_pager.IsDragging)
        {
            _pager.EndDrag(NowMs());
            InvalidateFull();
        }

        // A click is committed on release. A drag that crossed the threshold already cancelled the
        // pending click in BeginWindowDrag, and one that stayed below it is still a click.
        if (_clickPending)
        {
            var isClick = !SkiaDragThreshold.Exceeds(_clickXDip, _clickYDip, xDip, yDip);
            CancelPendingClick();
            if (isClick)
            {
                ApplyInput(IslandInput.PrimaryClick());
            }
        }
    }

    /// <summary>Drag gesture started (host capture acquired).</summary>
    /// <param name="xDip">Pointer client X in DIP.</param>
    /// <param name="yDip">Pointer client Y in DIP.</param>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds.</param>
    /// <returns>What the drag means: a pager drag, a window-move candidate, or nothing.</returns>
    /// <remarks>
    /// The expanded panel owns a horizontal pager drag. A compact/peek shell press is a
    /// <see cref="SkiaDragKind.WindowCandidate"/>: the host measures the screen-space threshold and
    /// calls <see cref="BeginWindowDrag"/> once it is crossed, so a click can still be committed on
    /// release. Dock mode disables moving.
    /// </remarks>
    public SkiaDragKind HandleDragStarted(double xDip, double yDip, long timestampMs)
    {
        if (!_hasRenderState)
        {
            return SkiaDragKind.None;
        }

        var hit = SkiaIslandHitTest.Hit(_renderState, xDip, yDip);
        if (_renderState.Form == ShellForm.Expanded && hit.Kind is IslandHitKind.Page or IslandHitKind.Shell)
        {
            _pager.BeginDrag(xDip, timestampMs);
            var now = _time.GetLocalNow();
            _archipelago.Reveal(now);
            UpdateArchipelago(now);
            InvalidateFull();
            return SkiaDragKind.Pager;
        }

        if (_renderState.Form is ShellForm.Compact or ShellForm.Peek
            && hit.Kind == IslandHitKind.Compact
            && hit.Control != IslandPageControl.PlayPause
            && IslandPlacementDrag.AllowsDrag(_settings.Current.IslandMode))
        {
            return SkiaDragKind.WindowCandidate;
        }

        return SkiaDragKind.None;
    }

    /// <summary>Drag gesture moved.</summary>
    /// <param name="xDip">Pointer client X in DIP.</param>
    /// <param name="yDip">Pointer client Y in DIP.</param>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds.</param>
    public void HandleDragMoved(double xDip, double yDip, long timestampMs)
    {
        _pointerX = xDip;
        _pointerY = yDip;
        if (!_pager.IsDragging)
        {
            return;
        }

        _pager.DragTo(xDip, timestampMs);
        var now = _time.GetLocalNow();
        _archipelago.NoteActivity(now);
        UpdateArchipelago(now);
        InvalidateDirty(ContentDamage());
    }

    /// <summary>Drag gesture ended or was cancelled.</summary>
    /// <param name="xDip">Pointer client X in DIP.</param>
    /// <param name="yDip">Pointer client Y in DIP.</param>
    /// <param name="timestampMs">Monotonic timestamp in milliseconds.</param>
    public void HandleDragEnded(double xDip, double yDip, long timestampMs)
    {
        _pointerX = xDip;
        _pointerY = yDip;
        if (!_pager.IsDragging)
        {
            return;
        }

        _pager.EndDrag(timestampMs);
        InvalidateFull();
    }

    /// <summary>
    /// Starts moving the island window: cancels the pending click, marks Core's drag state and lets the
    /// host follow the pointer. Called once per gesture, when the screen-space threshold is crossed.
    /// </summary>
    /// <returns>Whether a window drag actually started (false when the state/mode does not allow it).</returns>
    public bool BeginWindowDrag()
    {
        if (_windowDragActive || !_hasRenderState)
        {
            return false;
        }

        if (_renderState.Form is not (ShellForm.Compact or ShellForm.Peek)
            || !IslandPlacementDrag.AllowsDrag(_settings.Current.IslandMode))
        {
            return false;
        }

        CancelPendingClick();
        _windowDragActive = true;
        ApplyInput(IslandInput.DragStarted());
        return true;
    }

    /// <summary>
    /// Ends a window-move drag: applies Core's <see cref="IslandInput.DragEnded"/>, whose
    /// <see cref="IslandEffectKind.PersistPlacement"/> effect raises
    /// <see cref="PlacementPersistRequested"/>. The host must snapshot the drop placement before calling.
    /// </summary>
    /// <returns>Whether a window drag was active.</returns>
    public bool EndWindowDrag()
    {
        if (!_windowDragActive)
        {
            return false;
        }

        _windowDragActive = false;
        CancelPendingClick();
        ApplyInput(IslandInput.DragEnded());
        return true;
    }

    /// <summary>Wheel/trackpad input; returns what the pager did.</summary>
    /// <param name="deltaDip">Wheel delta in DIP (positive = towards the next page).</param>
    /// <param name="horizontal">Whether the event came from the horizontal axis.</param>
    /// <param name="shift">Whether Shift was held.</param>
    public SkiaWheelOutcome HandleWheel(double deltaDip, bool horizontal, bool shift)
    {
        if (!_hasRenderState)
        {
            return SkiaWheelOutcome.None;
        }

        var now = _time.GetLocalNow();
        var outcome = _pager.Wheel(deltaDip, horizontal, shift, now, contentScrollable: false);
        if (outcome.Paged || outcome.Consumed)
        {
            _archipelago.Reveal(now);
            UpdateArchipelago(now);
            InvalidateFull();
        }

        return outcome;
    }

    /// <summary>Display/DPI change: snap springs and repaint everything.</summary>
    public void NotifyDisplayChanged()
    {
        _motion.SetTarget(BuildTarget(ActivityPresenter.PresentIsland(_schedulerState, State)));
        _motion.SnapToTarget();
        _pager.JumpToPage((int)State.Page);
        BuildRenderState();
        InvalidateFull();
        PlacementRequested?.Invoke();
    }

    /// <summary>Human-readable diagnostics (smoke runs, bug reports).</summary>
    public string Describe()
    {
        var arch = _renderState.Archipelago;
        return $"state={State} page={_pager.SelectedIndex} pos={_pager.PagePosition:0.00} " +
               $"shell={_motion.Width:0.0}x{_motion.Height:0.0} opacity={_motion.Opacity:0.00} " +
               $"strip={arch.Visible}/{arch.Opacity:0.00} dots={arch.ResolvedFarLeftDots}+{arch.ResolvedFarRightDots} " +
               $"motion={(_motion.IsAtRest ? "rest" : "running")} pager={(_pager.IsAnimating ? "running" : "rest")} " +
               $"activities={_schedulerState.ActivityCount}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _data.Changed -= OnDataChanged;
        _data.Stop();
        _pager.PageChanged -= OnPagerPageChanged;
        _controller.Transitioned -= OnTransition;
        _controller.Dispose();
        _hub.Dispose();
        _motion.Dispose();
        _archipelago.Dispose();
    }

    // ------------------------------------------------------------------ pipeline

    private IslandStateController CreateController(AppSettings settings)
        => new(BuildBehaviorOptions(settings), _controller?.Current ?? IslandState.Hidden, _dispatcher);

    private static IslandBehaviorOptions BuildBehaviorOptions(AppSettings settings) => new()
    {
        Enabled = true,
        HoverPeek = settings.HoverPeek,
        HoverReveal = true,
        AutoCollapseMs = settings.AutoCollapseMs,
        AutoHideMs = settings.AutoHideMs,
        HoverIntentMs = Theme.UiDurations.HoverIntent,
        LeaveDebounceMs = Theme.UiDurations.LeaveDebounce,
        PeekDwellMs = 2400,
    };

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

        ApplyInput(IslandInput.ForActivityUpdate(update));
    }

    private void OnTransition(IslandTransition transition)
    {
        if (transition.Reason == IslandTransitionReason.NoChange && transition.Effects.Length == 0)
        {
            return;
        }

        _logger.Debug(LogCategories.Activity, "island.skia.transition",
            $"{transition.Previous.Kind} -> {transition.State.Kind} ({transition.Reason}).");
        ExecuteEffects(transition.Effects);
        SyncContent(full: false);
        SyncPagerFromState();
        StateChanged?.Invoke();
    }

    private void ExecuteEffects(System.Collections.Immutable.ImmutableArray<IslandEffect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect.Kind)
            {
                case IslandEffectKind.ShowWindow:
                case IslandEffectKind.HideWindow:
                    break;
                case IslandEffectKind.StartFrameLoop:
                    FrameRequested?.Invoke();
                    break;
                case IslandEffectKind.StopFrameLoop:
                    break;
                case IslandEffectKind.SnapSprings:
                    _motion.SnapToTarget();
                    InvalidateFull();
                    break;
                case IslandEffectKind.Relayout:
                case IslandEffectKind.UpdateContent:
                    SyncContent(full: false);
                    break;
                case IslandEffectKind.Retarget:
                    break;
                case IslandEffectKind.PlayNudge:
                    _motion.PlayNudge();
                    InvalidateFull();
                    FrameRequested?.Invoke();
                    break;
                case IslandEffectKind.SyncClickThrough:
                    StateChanged?.Invoke();
                    break;
                case IslandEffectKind.FocusRail:
                    // TODO(skia-mvp): no keyboard focus/UIA in the Skia path yet.
                    break;
                case IslandEffectKind.UpdateTray:
                    break;
                case IslandEffectKind.PersistPlacement:
                    // The host owns the drop geometry: it snapshots (monitorId, anchor, offsetX,
                    // offsetY) from the final window origin before calling EndWindowDrag, then writes
                    // it through SettingsController on this callback. The effect also fires on
                    // display/settings changes, where the host simply has no pending drop to write.
                    PlacementPersistRequested?.Invoke();
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

    private void SyncContent(bool full)
    {
        var content = _data.Snapshot();
        var viewModel = ActivityPresenter.PresentIsland(_schedulerState, State);
        var settings = _settings.Current;

        var structural = SkiaIslandStateBuilder.StructuralSignature(content, viewModel, _localizer, settings);
        var dynamicSignature = SkiaIslandStateBuilder.DynamicSignature(content, viewModel);
        var structuralChanged = !string.Equals(structural, _structuralSignature, StringComparison.Ordinal);
        var dynamicChanged = !string.Equals(dynamicSignature, _dynamicSignature, StringComparison.Ordinal);
        _structuralSignature = structural;
        _dynamicSignature = dynamicSignature;

        var peeked = State.Kind == IslandStateKind.Peek;
        _compact = SkiaIslandStateBuilder.BuildCompact(viewModel, content, _localizer, peeked);
        _pages = SkiaIslandStateBuilder.BuildPages(content, viewModel, _localizer, settings);
        _theme = SkiaIslandThemeMapper.Map(settings);
        _motion.SetTarget(BuildTarget(viewModel));
        BuildRenderState();

        if (full || !_hasRenderState || structuralChanged)
        {
            InvalidateFull();
        }
        else if (dynamicChanged)
        {
            InvalidateDynamic();
        }
    }

    private SkiaMotionTarget BuildTarget(IslandViewModel viewModel)
    {
        var settings = _settings.Current;
        var mode = settings.IslandMode;
        var compactKind = State.ContentKind ?? viewModel.Primary?.Kind;
        var compactSize = LayoutTokens.CompactSize(compactKind, mode);
        var ghosts = settings.Stacking == StackingStrategySetting.Stacked
            ? Math.Clamp(viewModel.Ghosts.Length, 0, LayoutTokens.MaxGhosts)
            : 0;
        var form = State.Kind switch
        {
            IslandStateKind.Compact => ShellForm.Compact,
            IslandStateKind.Peek => ShellForm.Peek,
            IslandStateKind.Expanded => ShellForm.Expanded,
            IslandStateKind.Closing => ShellForm.Closing,
            _ => ShellForm.Hidden,
        };
        return new SkiaMotionTarget(form, mode, settings.IslandAnchor, compactSize, ghosts, State.Kind == IslandStateKind.Hidden);
    }

    private void SyncPagerFromState()
    {
        var page = (int)State.Page;
        if (page == _pager.SelectedIndex)
        {
            return;
        }

        _syncingPage = true;
        try
        {
            _pager.SelectPage(page);
        }
        finally
        {
            _syncingPage = false;
        }

        InvalidateFull();
    }

    private void OnPagerPageChanged(int page)
    {
        if (_syncingPage)
        {
            return;
        }

        ApplyInput(IslandInput.PageRequested((CoreIslandPage)page));
        InvalidateFull();
        FrameRequested?.Invoke();
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.IsOnDispatcherThread)
        {
            SyncContent(full: false);
            StateChanged?.Invoke();
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            SyncContent(full: false);
            StateChanged?.Invoke();
        });
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (!string.Equals(previous.Language, current.Language, StringComparison.OrdinalIgnoreCase)
            && _localizer is LocalizationService service)
        {
            service.SetCulture(current.Language);
        }

        var previousProfile = previous.BuildMotionProfile(SkiaIslandThemeMapper.ReducedMotion(previous));
        var currentProfile = current.BuildMotionProfile(SkiaIslandThemeMapper.ReducedMotion(current));
        if (previousProfile != currentProfile)
        {
            _motion.UpdateProfile(currentProfile);
        }

        if (previous.HoverPeek != current.HoverPeek
            || previous.AutoCollapseMs != current.AutoCollapseMs
            || previous.AutoHideMs != current.AutoHideMs)
        {
            RecreateController(current);
            ApplyInput(IslandInput.SettingsChanged());
        }

        var modeOrAnchorChanged = previous.IslandMode != current.IslandMode
            || previous.IslandAnchor != current.IslandAnchor
            || previous.DisplayOn != current.DisplayOn;
        if (previous.IslandMode != current.IslandMode)
        {
            ApplyInput(IslandInput.ModeChanged());
        }
        else if (previous.IslandAnchor != current.IslandAnchor)
        {
            ApplyInput(IslandInput.DisplayChanged());
        }

        SyncContent(full: false);
        if (modeOrAnchorChanged)
        {
            _motion.SnapToTarget();
            InvalidateFull();
            PlacementRequested?.Invoke();
        }

        StateChanged?.Invoke();
    }

    private void RecreateController(AppSettings settings)
    {
        var current = _controller.Current;
        _controller.Transitioned -= OnTransition;
        _controller.Dispose();
        _controller = new IslandStateController(BuildBehaviorOptions(settings), current, _dispatcher);
        _controller.Transitioned += OnTransition;
    }

    /// <summary>Drops a deferred compact click (drag, leave, escape, re-press).</summary>
    private void CancelPendingClick() => _clickPending = false;

    private void HandlePageControl(in IslandHitTarget hit)
    {
        if (hit.Control == IslandPageControl.MediaCard)
        {
            HandlePageRequest(CoreIslandPage.Media);
            return;
        }

        var action = SkiaIslandControlRouter.Map(hit, _renderState, _pointerX);
        if (action is { } value)
        {
            DispatchControl(value, hit.PageIndex);
        }
    }

    /// <summary>
    /// Dispatches one semantic page-control action through the injected platform seam. Without a
    /// seam, media transport falls back to the legacy <see cref="MediaCommandRequested"/> event and
    /// every other action is reported as unsupported (never silently swallowed).
    /// </summary>
    private void DispatchControl(in IslandPlatformAction action, int page)
    {
        if (_platformActions is null)
        {
            if (TryRaiseLegacyMediaCommand(action.Kind))
            {
                return;
            }

            LogControlOutcome(action, page, "island.skia.control-unsupported",
                "no IIslandPlatformActions seam is configured for this host");
            return;
        }

        var result = _platformActions.Invoke(action);
        switch (result.Outcome)
        {
            case IslandActionOutcome.Handled:
                break;
            case IslandActionOutcome.Deferred:
                LogControlOutcome(action, page, "island.skia.control-deferred",
                    result.Reason ?? "the action was emitted as an App event for a later owner");
                break;
            default:
                LogControlOutcome(action, page, "island.skia.control-unsupported",
                    result.Reason ?? "no platform service backs this control");
                break;
        }
    }

    private bool TryRaiseLegacyMediaCommand(IslandPlatformActionKind kind)
    {
        var command = kind switch
        {
            IslandPlatformActionKind.MediaTogglePlay => MediaTransportCommand.TogglePlay,
            IslandPlatformActionKind.MediaPrevious => MediaTransportCommand.Previous,
            IslandPlatformActionKind.MediaNext => MediaTransportCommand.Next,
            _ => (MediaTransportCommand?)null,
        };

        if (command is null || MediaCommandRequested is null)
        {
            return false;
        }

        MediaCommandRequested.Invoke(command.Value);
        return true;
    }

    private void LogControlOutcome(
        in IslandPlatformAction action,
        int page,
        string eventId,
        string reason)
    {
        _logger.Debug(LogCategories.Activity, eventId,
            $"Page control {action} on page {page}: {reason}",
            data: new Dictionary<string, object?>
            {
                ["control"] = action.Kind.ToString(),
                ["page"] = page,
                ["index"] = action.Index,
                ["value"] = action.Value,
                ["toggle"] = action.Toggle.ToString(),
                ["reason"] = reason,
            });
    }

    // ------------------------------------------------------------------ render state

    private void BuildRenderState()
    {
        var mode = _settings.Current.IslandMode;
        var form = ResolveVisualForm();
        var expanded = form == ShellForm.Expanded;
        _renderState = new IslandRenderState
        {
            Mode = mode,
            Form = form,
            ShellWidth = _motion.Width,
            ShellHeight = _motion.Height,
            ShellRadii = _motion.ShellRadii,
            ShellOrigin = _motion.ShellOrigin,
            Opacity = _motion.Opacity,
            ContentOpacity = expanded ? _motion.ExpandedContentOpacity : _motion.CompactContentOpacity,
            NudgeOffsetY = _motion.NudgeOffsetY,
            PressScale = _motion.PressScale,
            GhostCount = expanded ? 0 : _motion.Target.GhostCount,
            Pager = PagerRenderState.FromPosition(_pager.PagePosition, _pager.IsDragging, _pager.IsAnimating),
            Compact = _compact,
            Pages = _pages,
            Theme = _theme,
            Archipelago = _options.EnableArchipelago
                ? _archipelago.ToState(_pager.PagePosition)
                : ArchipelagoState.HiddenState,
            ReducedMotion = _motion.ReducedMotion,
            MotionPhase = _motionPhase,
            FocusRing = _accessibilityFocus,
        };
        _hasRenderState = true;
    }

    private ShellForm ResolveVisualForm()
    {
        var kind = State.Kind;
        return kind switch
        {
            IslandStateKind.Hidden => ShellForm.Hidden,
            IslandStateKind.Expanded => _motion.ExpandedContentOpacity >= 0.5 ? ShellForm.Expanded : ShellForm.Compact,
            IslandStateKind.Closing => _motion.ExpandedContentOpacity >= 0.5 ? ShellForm.Expanded : ShellForm.Compact,
            IslandStateKind.Peek => ShellForm.Peek,
            _ => ShellForm.Compact,
        };
    }

    private bool IsWaveActive()
    {
        if (!_hasRenderState || _motion.ReducedMotion || _renderState.Form == ShellForm.Expanded)
        {
            return false;
        }

        return _compact.Kind == ActivityKind.Media && _compact.Model.IsActive;
    }

    private void InvalidateDirty(VisualRect damage)
    {
        if (_pendingFull || damage.IsEmpty)
        {
            return;
        }

        _pendingDamage = _pendingDirty ? Union(_pendingDamage, damage) : damage;
        _pendingDirty = true;
    }

    private void InvalidateDynamic()
    {
        if (!_hasRenderState)
        {
            InvalidateFull();
            return;
        }

        if (_renderState.Form == ShellForm.Expanded && _renderState.PageIndex == 1)
        {
            var page = IslandMetrics.PageRect(_renderState, 1);
            var lyric = PageLayout.MediaLyricCard(page);
            var seek = PageLayout.MediaSeekRow(page);
            var damage = Union(
                new VisualRect(lyric.Left, lyric.Top, lyric.Width, lyric.Height),
                new VisualRect(seek.Left, seek.Top, seek.Width, seek.Height)).Inflate(2);
            InvalidateDirty(damage);
            return;
        }

        if (_renderState.Form != ShellForm.Expanded && _compact.Kind == ActivityKind.Media)
        {
            InvalidateDirty(WaveDamage());
            return;
        }

        InvalidateFull();
    }

    private VisualRect ContentDamage()
    {
        if (!_hasRenderState)
        {
            return VisualRect.Empty;
        }

        var content = IslandMetrics.ContentRect(IslandMetrics.ShellRect(_renderState));
        return new VisualRect(content.Left, content.Top, content.Width, content.Height).Inflate(2);
    }

    private VisualRect StripDamage()
    {
        if (!_hasRenderState)
        {
            return VisualRect.Empty;
        }

        var host = HostSurface.ForMode(_renderState.Mode);
        var top = host.ArchipelagoTopLocal;
        var height = Math.Max(0, host.Height - top);
        return new VisualRect(0, top, host.Width, height).Inflate(2);
    }

    private VisualRect WaveDamage()
    {
        if (!_hasRenderState)
        {
            return VisualRect.Empty;
        }

        var shell = IslandMetrics.ShellRect(_renderState);
        return new VisualRect(shell.Right - 62, shell.Top + 11, 26, 18);
    }

    // ------------------------------------------------------------------ pointer zones

    private void UpdatePointerZones()
    {
        if (!_hasRenderState)
        {
            _pointerInHotZone = false;
            _pointerOnStrip = false;
            return;
        }

        var shell = IslandMetrics.ShellRect(_renderState);
        var hotZone = new VisualRect(
            shell.Left,
            shell.Bottom - ArchipelagoLayout.HotZoneHeight,
            shell.Width,
            ArchipelagoLayout.HotZoneHeight);
        _pointerInHotZone = _pointerInside
            && State.Kind is IslandStateKind.Expanded or IslandStateKind.Closing
            && hotZone.Contains(_pointerX, _pointerY);

        var stripTop = HostSurface.ForMode(_renderState.Mode).ArchipelagoTopLocal;
        var strip = new VisualRect(shell.MidX - 200, stripTop, 400, IslandMetrics.StripHeight);
        _pointerOnStrip = _archipelago.IsVisible && _pointerInside && strip.Contains(_pointerX, _pointerY);
    }

    private void UpdateArchipelago(DateTimeOffset now)
    {
        if (!_options.EnableArchipelago)
        {
            _archipelago.Hide();
            return;
        }

        var expanded = State.Kind is IslandStateKind.Expanded or IslandStateKind.Closing;
        var dwell = _hotZoneEnteredAt is { } start ? now - start : TimeSpan.Zero;
        _archipelago.Update(now, expanded, _pointerInHotZone, _pointerOnStrip, dwell, _pager.IsDragging);
    }

    private long NowMs() => (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);

    private static VisualRect Union(in VisualRect a, in VisualRect b)
    {
        if (a.IsEmpty)
        {
            return b;
        }

        if (b.IsEmpty)
        {
            return a;
        }

        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.Right, b.Right);
        var bottom = Math.Max(a.Bottom, b.Bottom);
        return new VisualRect(left, top, right - left, bottom - top);
    }
}

