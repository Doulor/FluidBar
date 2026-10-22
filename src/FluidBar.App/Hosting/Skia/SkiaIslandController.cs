using System.Collections.Immutable;
using FluidBar.App.Hosting.Pages;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core;
using FluidBar.Core.Activities;
using FluidBar.Core.Configuration;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Core.Presentation;
using FluidBar.Island;
using FluidBar.Sdk.Pages;
using CoreIslandPage = FluidBar.Core.Activities.PageRef;

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
    private readonly Func<SystemAppearanceSnapshot> _appearance;
    private readonly double[] _pageScroll = new double[PageRegistry.MaxPages];
    private readonly PageRegistry? _pageRegistry;
    private IReadOnlyList<PluginPageModel> _pluginPages = [];

    /// <summary>Wheel scroll chain: content first, then an overscroll threshold before paging.</summary>
    private readonly PagerScrollChainPolicy _scrollChain = new();

    /// <summary>When the last wheel page step happened (freezes the chain during the snap).</summary>
    private DateTimeOffset _lastPageStepAt = DateTimeOffset.MinValue;
    private readonly SkiaIslandMotion _motion;
    private readonly SkiaPager _pager;
    private readonly SkiaArchipelago _archipelago;

    // Prototype2 rail springs (T 0.32 s / ζ 0.85): driven here so drawing and hit-testing read the
    // same animated width/positions from the render state.
    private readonly MiniRailMotion _railMotion = new();
    private bool _railWasVisible;

    // Prototype2 rail interaction states (hover / pressed / keyboard focus). Resolved per slot and
    // projected into the strip state, so the painted glass and its click target never diverge.
    private MiniRailSlot _railHover;
    private MiniRailSlot _railPressed;
    private MiniRailSlot _railFocus;
    private int _railPendingPage = -1;
    private double _railPressXDip;
    private double _railPressYDip;
    private readonly IIslandPlatformActions? _platformActions;

    private ActivitySchedulerState _schedulerState = ActivitySchedulerState.Empty;
    private IslandStateController _controller;

    private CompactActivity _compact = new(ActivityKind.Idle, new CompactModel());
    private CompactActivity? _secondaryCompact;

    // Compact lyric mode: the setting is read live from the media plugin's store, and a line change
    // animates in over CompactLyricFadeMs so the pill does not hard-cut twice per line.
    private const double CompactLyricFadeMs = 220;
    private readonly MediaPillPreferences _mediaPill;

    // Measure-only text engine for the adaptive lyric pill width (the renderer owns its own instance).
    private readonly TextEngine _lyricMeasure = new(cacheCapacity: 64);
    private string? _compactLyricText;
    private long _compactLyricChangedAtMs = -1;

    /// <summary>Per-word timeline of the line being sung (null when the source has none).</summary>
    private KaraokeTrack? _karaokeTrack;

    /// <summary>Extrapolated playback position: turns the 250 ms poll into a per-frame value.</summary>
    private readonly MediaPositionClock _karaokeClock = new();

    /// <summary>Sweep fraction currently painted (compared with a 1/255 tolerance to avoid useless frames).</summary>
    private double _karaokeHighlight = -1;

    /// <summary>Line the monotonic sweep is locked to (a different value means "take the new value").</summary>
    private double _karaokeSweepLineStartMs = double.NaN;

    /// <summary>Page remembered from the previous session; applied once the pager knows the page count.</summary>
    private int _pendingRestoredPage = -1;

    /// <summary>Line currently committed for painting (flip-backs are suppressed).</summary>
    private string? _lyricLineCommitted;

    /// <summary>The line before it, kept to recognise a flip-back.</summary>
    private string? _lyricLinePrevious;

    /// <summary>When the committed line changed (used for the flip-back debounce).</summary>
    private long _lyricLineChangedAtMs;

    /// <summary>Whether the karaoke sweep needs frames right now.</summary>
    private bool _karaokeActive;

    // Media progress clock: the player publishes a position sample every poll, and some fallback
    // sources occasionally omit the position text. Anchoring the last sample and advancing with the
    // island's frame clock keeps the seek bar and elapsed text moving between samples.
    private readonly MediaPositionClock _mediaClock = new();
    private double _mediaDurationMs;
    private double _mediaLastSampleMs = double.NaN;
    private string? _mediaTrackIdentity;

    // Track-change transition: shrink/fade the visible shell, then spring it back. Detection lives in
    // the controller because the Core activity payload is the same key for every track refresh.
    private const double TrackTransitionCollapseMs = 150;
    private const double TrackTransitionTotalMs = 470;
    private const double TrackTransitionCollapseScale = 0.86;
    private string? _lastTrackTransitionIdentity;
    private string? _lastTrackTransitionArtwork;
    private long _trackTransitionStartedMs = -1;
    private double _trackTransitionScale = 1;
    private double _trackTransitionOpacity = 1;

    // Split-strategy previews are snapshotted from the presenter's ghost view models; the snapshot
    // outlives the point where an islet is activated so the merge animation has something to shrink.
    private ActivityViewModel[] _splitPreviews = [];
    private readonly Dictionary<string, byte[]?> _splitArtwork = new(StringComparer.Ordinal);
    private readonly SplitStackPolicy _splitPolicy = new();
    private ActivityKey? _splitPinnedKey;
    private ExpandedPageContent _pages = ExpandedPageContent.Empty;
    private IslandTheme _theme = IslandTheme.Dark;
    private string _structuralSignature = string.Empty;
    private string _dynamicSignature = string.Empty;
    private string _agentTimingSignature = string.Empty;
    private string _visibleStackSignature = string.Empty;
    private IslandRenderState _renderState;
    private bool _hasRenderState;
    private VisualRect? _accessibilityFocus;

    private bool _pendingFull = true;
    private bool _pendingDirty;
    private VisualRect _pendingDamage;

    private double _motionPhase;
    private double _agentAnimationSeconds;
    private bool _agentLoopRequested;
    private double _waveAccumulator;
    private double _pointerX;
    private double _pointerY;
    private bool _pointerInside;
    private bool _pointerInHotZone;
    private bool _pointerOnStrip;

    // Approach reveal: the hover band below the shell reveals the rail on entry and keeps it up while
    // the pointer stays. Both flags are edge-triggered, so a high-frequency stream of moves only costs
    // one rectangle test per event and never schedules a repaint while the pointer stays inside.
    private bool _pointerInHoverBand;
    private bool _pointerWasInHoverBand;
    private DateTimeOffset? _hotZoneEnteredAt;
    private bool _syncingPage;
    private bool _started;
    private bool _disposed;

    // Clipboard "copied" confirmation: the platform seam owns the copy, the island owns the 1.6 s
    // check-mark feedback. Remembered by preview text so a re-ordered history cannot mislabel a row.
    private static readonly TimeSpan CopyFeedbackDuration = TimeSpan.FromMilliseconds(1600);
    private (string Preview, DateTimeOffset At)? _copiedClipboard;

    // Peek-on-track-change: a synthetic hover peek (armed → shown → leave) driven purely by deadlines
    // so the media track change can preview the hover card without a real pointer over the island.
    private SyntheticPeekPhase _syntheticPeek;
    private DateTimeOffset _syntheticPeekDueAt;
    private string? _lastTrackKey;

    /// <summary>How long the synthetic peek stays visible before it retracts (ms).</summary>
    public const int SyntheticPeekVisibleMs = 1600;

    // Click-vs-drag: a compact press is only a candidate click; the host turns it into a window move
    // once the pointer passes SkiaDragThreshold in screen space, and the click is committed on release.
    private bool _clickPending;
    private double _clickXDip;
    private double _clickYDip;
    private bool _windowDragActive;

    // Press/hover feedback: the control held down and the control under the pointer. Both are resolved
    // by the same hit test the clicks use, so the highlight can never sit on a different rectangle.
    private IslandPageControl _pressedControl;
    private int _pressedPage = -1;
    private int _pressedIndex = -1;
    private IslandPageControl _hoverControl;
    private int _hoverPage = -1;

    /// <summary>Creates the controller (call <see cref="Start"/> to begin producing content).</summary>
    /// <param name="options">Composition options.</param>
    /// <param name="dispatcher">Optional dispatcher (defaults to a UI-thread synchronization-context dispatcher).</param>
    /// <param name="mediaPill">Media pill preferences (defaults to the media plugin's settings file).</param>
    public SkiaIslandController(SkiaIslandHostOptions options, SkiaHostDispatcher? dispatcher = null,
        MediaPillPreferences? mediaPill = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mediaPill = mediaPill ?? new MediaPillPreferences();
        _localizer = options.Localizer;
        _settings = options.Settings;
        _data = options.Data;
        _time = options.TimeProvider ?? TimeProvider.System;
        _logger = options.Logger ?? new Diagnostics.TraceAppLogger();
        _dispatcher = dispatcher ?? new SkiaHostDispatcher();
        _platformActions = options.PlatformActions;
        _appearance = options.AppearanceSnapshot ?? SystemAppearanceSnapshot.FromWindows;
        _pageRegistry = options.PageRegistry;
        _pluginPages = _pageRegistry?.BuildPluginPages() ?? [];
        _pages = _pages with { PluginPages = _pluginPages, PageSlots = BuildPageSlots() };

        _hub = new EventHub(logger: _logger, dispatcher: _dispatcher);
        _controller = CreateController(_settings.Current);
        _controller.Transitioned += OnTransition;
        _hub.Subscribe(OnIslandEvent);

        var profile = _settings.Current.BuildMotionProfile(
            SkiaIslandThemeMapper.ReducedMotion(_settings.Current, _appearance()));
        _motion = new SkiaIslandMotion(profile);
        // The page count is not final at construction time, so the restored index is remembered here and
        // applied on the first frame where the pager knows how many pages exist (see ApplyRestoredPage).
        _pendingRestoredPage = PageSelectionStore.Load();
        _pager = new SkiaPager(IndexOfPage(State.Page), Math.Max(1, _pages.PageCount));
        _pager.PageChanged += OnPagerPageChanged;
        ApplyRestoredPage();
        _archipelago = new SkiaArchipelago(profile);

        _settings.Changed += OnSettingsChanged;
        _data.Changed += OnDataChanged;
        if (_pageRegistry is not null)
        {
            _pageRegistry.Changed += OnPageRegistryChanged;
        }
    }

    /// <summary>Raised when the settings window should open (quick action / hotkey).</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the marketplace should open.</summary>
    public event Action? MarketRequested;

    /// <summary>Raised when the user asks to execute a media transport command.</summary>
    public event Action<MediaTransportCommand>? MediaCommandRequested;

    /// <summary>Raised when a plugin page row/button is activated by pointer or UIA.</summary>
    public event Action<PluginPageCommandRequest>? PluginPageCommandRequested;

    /// <summary>Raised when the rail overflow ellipsis asks the App to show the page selector.</summary>
    public event Action? PageChooserRequested;

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

    /// <summary>Whether the pointer is currently over the island surface.</summary>
    public bool IsPointerInside => _pointerInside;

    /// <summary>Whether the pointer is currently moving the island window (drag-to-move).</summary>
    public bool IsWindowDragging => _windowDragActive;

    /// <summary>Whether a peek triggered by a media track change is in flight (diagnostics/tests).</summary>
    public bool IsSyntheticPeekActive => _syntheticPeek != SyntheticPeekPhase.None;

    /// <summary>Whether a split satellite currently paints the rotating agent ring.</summary>
    public bool HasSpinningSplitRing => _hasRenderState && IslandMetrics.HasSpinningSplitRing(_renderState);

    /// <summary>Whether motion, the pager, the strip or dynamic content still needs frames.</summary>
    public bool IsAnimating => !_motion.IsAtRest || _pager.IsAnimating || _archipelago.IsAnimating
        || (_archipelago.IsVisible && _railMotion.IsAnimating) || IsWaveActive() || IsKaraokeAnimating
        || IsLyricTransitionAnimating || HasSpinningSplitRing
        || IsMediaProgressAnimating || IsTrackTransitionAnimating;

    /// <summary>Whether the track-change shrink/fade/reappear transition is running.</summary>
    public bool IsTrackTransitionAnimating => _trackTransitionStartedMs >= 0;

    /// <summary>
    /// Whether the agent spinner / indeterminate progress needs continuous 60 fps frames. True only
    /// while a running/waiting agent is actually painted (compact agent capsule or expanded page 5),
    /// and false under reduced motion, so the idle island requests no frames at all.
    /// </summary>
    public bool IsAgentAnimating
    {
        get
        {
            if (!_started || _disposed || _motion.ReducedMotion)
            {
                return false;
            }

            if (_hasRenderState && _renderState.Form == ShellForm.Expanded)
            {
                return CurrentPageIs(PageSlotKind.Agent) && HasActiveAgentRow(_renderState.Pages.Agent);
            }

            // The compact pill can be the agent itself (primary). Its ring is painted every frame and
            // must keep spinning wherever the pointer is; the old pointer gate froze it the moment the
            // mouse left the pill and it only resumed on hover (user report 2026-10-21).
            if (_compact.Kind == ActivityKind.Agent
                && _compact.Model.Status is CompactStatus.Info or CompactStatus.Warning)
            {
                return true;
            }

            // A split preview ball can carry the same ring; it is outside the pill, so the pointer being
            // elsewhere says nothing about it.
            if (HasSpinningSplitRing)
            {
                return true;
            }

            if (!_pointerInside)
            {
                return false;
            }

            return _splitPreviews.Any(preview => preview.Kind == ActivityKind.Agent && preview.IsActive);
        }
    }

    private static bool HasActiveAgentRow(AgentPageModel page)
    {
        if (page.Items is null)
        {
            return false;
        }

        foreach (var item in page.Items)
        {
            if (item.Tone is CompactStatus.Info or CompactStatus.Warning)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the karaoke sweep still needs frames: a per-word line is on screen and playing, so the
    /// highlight advances every frame instead of stepping once per position poll.
    /// </summary>
    /// <summary>
    /// Whether a lyric line change is still animating. The renderer advances the fade from its own frame
    /// clock, but the host only renders when the controller asks for a frame: without this flag the 180 ms
    /// fade was stepped by the ~1 Hz media snapshot and looked like one frame per second.
    /// </summary>
    public bool IsLyricTransitionAnimating => NowMs() < _lyricTransitionUntilMs;

    public bool IsKaraokeAnimating => _karaokeActive
        && _karaokeTrack is not null
        && _karaokeClock.IsAdvancing
        && (_hasRenderState && _renderState.Form == ShellForm.Expanded
            ? CurrentPageIs(PageSlotKind.Media)
            : _compact.Kind == ActivityKind.Media);

    /// <summary>
    /// Whether the media progress bar / elapsed text is advancing from the island clock. Only tracks
    /// with a known timeline and a playing state need continuous frames.
    /// </summary>
    public bool IsMediaProgressAnimating => _mediaDurationMs > 0 && _mediaClock.IsAdvancing
        && (_hasRenderState && _renderState.Form == ShellForm.Expanded
            ? CurrentPageIs(PageSlotKind.Media)
            : _compact.Kind == ActivityKind.Media);

    /// <summary>Whether the island is only animating the media waveform (lower frame rate is enough).</summary>
    public bool IsWaveOnly => _motion.IsAtRest && !_pager.IsAnimating && !_archipelago.IsAnimating && IsWaveActive()
        && !IsKaraokeAnimating && !IsMediaProgressAnimating;

    /// <summary>Live power mode read from the settings store; switching applies on the next frame tick.</summary>
    public IslandPowerMode PowerMode => _settings.Current.PowerMode;

    /// <summary>Current frame/media/ambient parameter table.</summary>
    public IslandPowerProfile PowerProfile => IslandPowerPolicy.For(PowerMode);

    /// <summary>
    /// Frame interval the host should use for the current activity. The split-agent ring has one extra
    /// rule: Saver runs it at 60 fps while hovered and 30 fps when the pointer is away; Performance
    /// always runs it at 60 fps. No other change to the mode table is made here.
    /// </summary>
    public TimeSpan PreferredFrameInterval => HasSpinningSplitRing
        ? (PowerMode == IslandPowerMode.Saver && !_pointerInside
            ? TimeSpan.FromMilliseconds(1000.0 / 30)
            : TimeSpan.FromMilliseconds(1000.0 / 60))
        : IsAgentOnly
            // A 20 DIP ring spinning at 0.9 s/revolution is indistinguishable at 30 fps and costs
            // half the frames of the 60/120 Hz host loop (measured ~20% of one core before this cap).
            ? TimeSpan.FromMilliseconds(1000.0 / 30)
            : PowerProfile.FrameInterval;

    /// <summary>
    /// A resting shell whose only motion is the agent spinner. The spinner is a 20 DIP ambient
    /// indicator: 10 fps is indistinguishable at this size and avoids running the full 120 fps host
    /// loop while an agent is merely reporting status.
    /// </summary>
    public bool IsAgentOnly => _motion.IsAtRest && !_pager.IsAnimating && !_archipelago.IsAnimating
        && IsAgentAnimating && !IsWaveActive() && !IsKaraokeAnimating && !IsMediaProgressAnimating;

    /// <summary>
    /// Whether the only animated thing is the karaoke sweep / progress clock on a rested shell. 30 fps is
    /// enough for text sweeping (the frame loop was running at ~120 fps, repainting the lyric strip for
    /// sub-pixel changes and making the sweep visibly uneven under load).
    /// </summary>
    public bool IsKaraokeOnly => _motion.IsAtRest && !_pager.IsAnimating && !_archipelago.IsAnimating
        && (IsKaraokeAnimating || IsMediaProgressAnimating) && !IsWaveActive();

    /// <summary>Whether a deadline timer is needed (hover/auto-collapse/scheduler/strip auto-hide).</summary>
    public bool HasPendingDeadline => HasUiDeadline || _schedulerState.HasActivities;

    /// <summary>Whether a UI deadline needs the precise 50 ms poll.</summary>
    public bool HasUiDeadline
    {
        get
        {
            var state = State;
            return state.HoverIntentDeadline is not null
                || state.LeaveDeadline is not null
                || state.PeekDwellDeadline is not null
                || state.AutoCollapseDeadline is not null
                || state.AutoHideDeadline is not null
                || _syntheticPeek != SyntheticPeekPhase.None
                || _copiedClipboard is not null
                || _archipelago.IsVisible;
        }
    }

    /// <summary>
    /// Poll cadence for lifecycle deadlines. Scheduler activity TTL only needs coarse polling;
    /// keeping the 50 ms timer for an idle scheduler was a continuous 20 Hz wake-up even with no
    /// visible deadline.
    /// </summary>
    public TimeSpan DeadlinePollInterval => HasUiDeadline
        ? TimeSpan.FromMilliseconds(50)
        : TimeSpan.FromSeconds(1);

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
        _pager.JumpToPage(IndexOfPage(State.Page));
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

        PumpSyntheticPeek(now);

        if (_copiedClipboard is { } copied && now - copied.At > CopyFeedbackDuration)
        {
            // Drop the check-mark highlight once the confirmation window elapsed and repaint the page.
            _copiedClipboard = null;
            SyncContent(full: false);
            InvalidateFull();
            StateChanged?.Invoke();
        }

        if (_schedulerState.HasActivities)
        {
            var update = _scheduler.Tick(_schedulerState, now);
            if (update.Kind != ActivityUpdateKind.None)
            {
                _schedulerState = update.State;
                ApplyInput(IslandInput.ForActivityUpdate(update));
                // Expiry can change the visible stack without changing the island state kind; a
                // transition-less update must still refresh the preview/ghost snapshots.
                SyncContent(full: false);
            }
        }

        UpdatePointerZones();
        UpdateArchipelago(now);
    }

    /// <summary>Drives the synthetic peek deadlines (armed → shown → leave → done).</summary>
    private void PumpSyntheticPeek(DateTimeOffset now)
    {
        switch (_syntheticPeek)
        {
            case SyntheticPeekPhase.Armed:
                if (now < _syntheticPeekDueAt)
                {
                    return;
                }

                ApplyInput(IslandInput.HoverIntentElapsed());
                if (State.Kind == IslandStateKind.Peek)
                {
                    _syntheticPeek = SyntheticPeekPhase.Shown;
                    _syntheticPeekDueAt = now.AddMilliseconds(SyntheticPeekVisibleMs);
                }
                else
                {
                    _syntheticPeek = SyntheticPeekPhase.None;
                }

                break;

            case SyntheticPeekPhase.Shown:
                if (now < _syntheticPeekDueAt)
                {
                    return;
                }

                ApplyInput(IslandInput.PointerLeft());
                _syntheticPeek = SyntheticPeekPhase.Leave;
                _syntheticPeekDueAt = now.AddMilliseconds(Theme.UiDurations.LeaveDebounce);
                break;

            case SyntheticPeekPhase.Leave:
                if (now < _syntheticPeekDueAt)
                {
                    return;
                }

                ApplyInput(IslandInput.LeaveDebounceElapsed());
                _syntheticPeek = SyntheticPeekPhase.None;
                break;
        }
    }

    /// <summary>Detects a media track change and, when enabled, previews the hover peek card.</summary>
    private void ObserveTrackChange(IslandContent content, AppSettings settings)
    {
        var trackKey = content.Media is { } media
            ? string.Join('|', media.Source, media.Title, media.Artist, media.Album, media.ArtworkKey)
            : null;
        if (trackKey is null)
        {
            _lastTrackKey = null;
            return;
        }

        var changed = _lastTrackKey is not null && !string.Equals(trackKey, _lastTrackKey, StringComparison.Ordinal);
        _lastTrackKey = trackKey;
        if (!changed || !settings.PeekOnTrackChange || _syntheticPeek != SyntheticPeekPhase.None)
        {
            return;
        }

        // Only a settled compact pill previews; an expanded panel already shows the track.
        if (!_started || State.Kind != IslandStateKind.Compact || State.IsHovering || State.IsSuppressed || State.IsPinned)
        {
            return;
        }

        _syntheticPeek = SyntheticPeekPhase.Armed;
        _syntheticPeekDueAt = _time.GetLocalNow().AddMilliseconds(Theme.UiDurations.HoverIntent);
        ApplyInput(IslandInput.PointerEntered());
    }

    /// <summary>
    /// Detects a real track change (title/artist, or artwork for a metadata-less source) and starts
    /// the shrink/fade → spring-back transition. A same-track artwork enrichment (null → cover) is
    /// not a track change, and a paused/resumed track keeps the same identity.
    /// </summary>
    private void ObserveTrackTransition(IslandContent content, IslandViewModel viewModel)
    {
        var media = content.Media;
        if (media is null || viewModel.Primary?.Kind != ActivityKind.Media)
        {
            _lastTrackTransitionIdentity = null;
            _lastTrackTransitionArtwork = null;
            return;
        }

        var titleArtist = string.Join('|', media.Title?.Trim(), media.Artist?.Trim());
        var hasMetadata = !string.IsNullOrWhiteSpace(media.Title) || !string.IsNullOrWhiteSpace(media.Artist);
        var identity = hasMetadata ? titleArtist : media.ArtworkKey;
        var artwork = media.ArtworkKey;
        var identityChanged = _lastTrackTransitionIdentity is not null
            && !string.Equals(identity, _lastTrackTransitionIdentity, StringComparison.Ordinal);
        var artworkChanged = hasMetadata
            && string.Equals(titleArtist, _lastTrackTransitionIdentity, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(_lastTrackTransitionArtwork)
            && !string.IsNullOrEmpty(artwork)
            && !string.Equals(artwork, _lastTrackTransitionArtwork, StringComparison.Ordinal);
        var enrichment = hasMetadata
            && string.Equals(titleArtist, _lastTrackTransitionIdentity, StringComparison.Ordinal)
            && string.IsNullOrEmpty(_lastTrackTransitionArtwork)
            && !string.IsNullOrEmpty(artwork);

        if ((identityChanged || artworkChanged) && !enrichment
            && _started
            && (HasSpinningSplitRing || _pointerInside
                || State.Kind is IslandStateKind.Compact or IslandStateKind.Peek or IslandStateKind.Expanded)
            && !_motion.ReducedMotion)
        {
            // A new track always restarts the one timeline; rapid skipping cannot stack animations.
            _trackTransitionStartedMs = NowMs();
            FrameRequested?.Invoke();
        }

        _lastTrackTransitionIdentity = identity;
        _lastTrackTransitionArtwork = artwork;
    }

    /// <summary>Steps the track transition and stores the current scale/opacity multipliers.</summary>
    private void StepTrackTransition()
    {
        if (_trackTransitionStartedMs < 0)
        {
            _trackTransitionScale = 1;
            _trackTransitionOpacity = 1;
            return;
        }

        var elapsed = NowMs() - _trackTransitionStartedMs;
        if (elapsed >= TrackTransitionTotalMs)
        {
            _trackTransitionStartedMs = -1;
            _trackTransitionScale = 1;
            _trackTransitionOpacity = 1;
            return;
        }

        if (elapsed < TrackTransitionCollapseMs)
        {
            var collapse = elapsed / TrackTransitionCollapseMs;
            var eased = collapse * collapse;
            _trackTransitionScale = 1 - ((1 - TrackTransitionCollapseScale) * eased);
            // Reach zero 30 ms before the scale bottom so at least one full frame is fully hidden
            // even at 60 fps (a fade that only reaches 0.08 is still visibly grey).
            var fade = Math.Clamp(elapsed / (TrackTransitionCollapseMs - 30), 0, 1);
            _trackTransitionOpacity = 1 - (fade * fade);
            return;
        }

        var expandSeconds = (elapsed - TrackTransitionCollapseMs) / 1000.0;
        var response = SpringStep(expandSeconds, MotionTokens.Rail.Spring.PeriodSeconds, MotionTokens.Rail.Spring.DampingRatio);
        _trackTransitionScale = TrackTransitionCollapseScale
            + ((1 - TrackTransitionCollapseScale) * response);
        _trackTransitionOpacity = Math.Clamp(expandSeconds / 0.12, 0, 1);
    }

    /// <summary>Unit step response of a damped spring (T = period, ζ = damping ratio).</summary>
    private static double SpringStep(double seconds, double periodSeconds, double dampingRatio)
    {
        if (seconds <= 0 || periodSeconds <= 0)
        {
            return 0;
        }

        var omega = 2 * Math.PI / periodSeconds;
        var zeta = Math.Clamp(dampingRatio, 0.01, 0.999);
        var damped = omega * Math.Sqrt(1 - (zeta * zeta));
        var decay = Math.Exp(-zeta * omega * seconds);
        return 1 - (decay * (Math.Cos(damped * seconds) + ((zeta / Math.Sqrt(1 - (zeta * zeta))) * Math.Sin(damped * seconds))));
    }

    private enum SyntheticPeekPhase
    {
        None,
        Armed,
        Shown,
        Leave,
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

        // Per frame, after the last content sync: a line change starts the transition window here so the
        // host keeps requesting frames instead of stepping the fade on the ~1 Hz media snapshot.
        ObserveLyricLineChange();
        if (dt <= 0)
        {
            return;
        }

        // Agent spinner / progress pulse use their own clock; it only advances while an agent is
        // actually running, so the host's frame loop stops (and CPU drops) as soon as it finishes.
        var agentWasAnimating = IsAgentAnimating;
        var ringWasAnimating = HasSpinningSplitRing;
        if (agentWasAnimating || ringWasAnimating)
        {
            _agentAnimationSeconds += dt;
        }

        var now = _time.GetLocalNow();
        var trackTransitionWasRunning = IsTrackTransitionAnimating;
        StepTrackTransition();
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
        var railWasRunning = SyncRailMotion(dt);
        var mediaProgressChanged = ApplyMediaProgress();
        BuildRenderState();

        if (mediaProgressChanged)
        {
            // Only the seek bar/elapsed text changed; repaint that strip/page, never the shell.
            InvalidateDirty(MediaProgressDamage());
        }

        if (trackTransitionWasRunning || IsTrackTransitionAnimating)
        {
            // Scale/opacity affects the whole layered surface; a full frame is cheap for < 0.5 s.
            InvalidateFull();
        }

        if (agentWasAnimating)
        {
            // The spinner/pulse phase changes every frame, but no content change arrives until the
            // next elapsed-time tick. Damage only the animated glyphs (or the agent page for the
            // full-row progress bar) so the host actually paints a new frame instead of freezing
            // between one-second content updates.
            InvalidateDirty(AgentFrameDamage());
        }

        if (ringWasAnimating)
        {
            // The split-agent ring is outside the compact shell. Without this dirty rect the
            // frame clock can run while RenderAllLocked sees no damage, leaving the ring frozen.
            InvalidateDirty(SplitRingDamage());
        }

        if (motionWasRunning)
        {
            // A shell morph damages the union of the old and new shell: full frame.
            InvalidateFull();
        }
        else if (pagerWasRunning)
        {
            InvalidateDirty(ContentDamage());
        }
        else if (archWasRunning || railWasRunning)
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

    /// <summary>Raises <see cref="PageChooserRequested"/> (rail overflow / UIA).</summary>
    public void RequestPageChooser() => PageChooserRequested?.Invoke();

    /// <summary>Raises <see cref="MarketRequested"/> (tray/hotkey seam).</summary>
    public void RequestMarket() => MarketRequested?.Invoke();

    /// <summary>Requests a page (keyboard/archipelago); the pager animates and the state follows.</summary>
    /// <param name="page">Target page.</param>
    public void HandlePageRequest(CoreIslandPage page)
    {
        if (_settings.Current.Stacking == StackingStrategySetting.Split)
        {
            // Opening an activity's page counts as "the user interacted with it": it keeps the pill.
            foreach (var record in _schedulerState.Visible)
            {
                if (record.Payload.PreferredPage == page || record.Kind.ToPage() == page)
                {
                    _splitPolicy.NoteUserInteraction(record.Key, _time.GetLocalNow());
                    break;
                }
            }
        }

        var index = IndexOfPage(page);
        if (index < 0 || index == _pager.SelectedIndex)
        {
            return;
        }

        _pager.SelectPage(index);
    }

    /// <summary>
    /// Activates the compact "+N" overflow chip: expands the island and opens the page of the oldest
    /// activity that did not fit in the visible stack (UI Automation Invoke shares this path).
    /// </summary>
    /// <returns>Whether an overflow badge exists to activate.</returns>
    public bool ActivateOverflowBadge()
    {
        if (_schedulerState.BadgeCount <= 0)
        {
            return false;
        }

        var page = OverflowPage();
        if (State.Kind != IslandStateKind.Expanded)
        {
            ApplyInput(IslandInput.PrimaryClick());
            ApplyInput(IslandInput.PageRequested(page));
        }

        HandlePageRequest(page);
        InvalidateFull();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Activates the secondary notification pill: expands the island and opens the notifications page.
    /// The primary activity keeps the main pill; only the expanded page changes.
    /// </summary>
    /// <returns>Whether a secondary notification pill is currently painted.</returns>
    public bool ActivateSecondaryNotification()
    {
        if (_secondaryCompact is null)
        {
            return false;
        }

        var page = CoreIslandPage.Notifications;
        if (State.Kind != IslandStateKind.Expanded)
        {
            ApplyInput(IslandInput.PrimaryClick());
        }

        ApplyInput(IslandInput.PageRequested(page));
        HandlePageRequest(page);
        InvalidateFull();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Activates one split preview ball: expands the island to that activity's page while the split
    /// spring runs in reverse, so the ball visibly fuses back into the pill.
    /// </summary>
    /// <param name="index">Preview index from the hit target.</param>
    /// <returns>Whether a preview existed at <paramref name="index"/>.</returns>
    public bool ActivateSplitIslet(int index)
    {
        if (index < 0 || index >= _splitPreviews.Length)
        {
            return false;
        }

        var preview = _splitPreviews[index];
        _splitPolicy.NoteUserInteraction(preview.Key, _time.GetLocalNow());
        var page = preview.Page;
        if (State.Kind != IslandStateKind.Expanded)
        {
            ApplyInput(IslandInput.PrimaryClick());
            ApplyInput(IslandInput.PageRequested(page));
        }

        HandlePageRequest(page);
        InvalidateFull();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Page of the oldest background activity (the +N chip's navigation target).</summary>
    private CoreIslandPage OverflowPage()
    {
        foreach (var record in _schedulerState.Background)
        {
            return record.Payload.PreferredPage ?? record.Kind.ToPage();
        }

        return _schedulerState.PrimaryPage;
    }

    /// <summary>Rotates the visible activity stack by <paramref name="direction"/> (+1 forwards).</summary>
    private void RotateStack(int direction)
    {
        var visible = _schedulerState.Visible;
        if (visible.Length < 2)
        {
            return;
        }

        var rotated = direction >= 0
            ? visible.RemoveAt(0).Add(visible[0])
            : visible.Insert(0, visible[^1]).RemoveAt(visible.Length);
        _schedulerState = _schedulerState with { Visible = rotated };

        PinTo(rotated[0].Key, "stack-rotate");

        // SetPinned normalizes the scheduler snapshot; restore the cyclic stack order so the next
        // wheel step continues from the visual position instead of jumping.
        var updatedByKey = _schedulerState.Visible.ToDictionary(record => record.Key);
        _schedulerState = _schedulerState with
        {
            Visible = rotated.Select(record => updatedByKey[record.Key]).ToImmutableArray(),
        };
    }

    /// <summary>Moves one page left/right (keyboard arrows).</summary>
    /// <param name="delta">Signed page delta.</param>
    public void HandlePageMove(int delta) => _pager.MoveBy(delta);

    /// <summary>
    /// Sets the logical keyboard/UIA focus ring (host-local DIP), or clears it. Repaints once; it does
    /// not raise <see cref="StateChanged"/> (the accessibility source owns its snapshot refresh).
    /// </summary>
    /// <param name="bounds">Focused element bounds in host-local DIP, or null.</param>
    /// <param name="railSlot">
    /// Preview-rail slot when the focused element is one of the rail glasses: the strip then paints the
    /// prototype `:focus-visible` accent outline itself, so the generic ring is suppressed.
    /// </param>
    public void SetAccessibilityFocus(VisualRect? bounds, MiniRailSlot railSlot = MiniRailSlot.None)
    {
        if (!_hasRenderState || (Nullable.Equals(_accessibilityFocus, bounds) && _railFocus == railSlot))
        {
            return;
        }

        _accessibilityFocus = bounds;
        _railFocus = railSlot;
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

        if (control is IslandPageControl.PluginRow or IslandPageControl.PluginRowButton)
        {
            return RaisePluginPageCommand(page, index, control == IslandPageControl.PluginRowButton);
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


        // Dragging the seek row: keep dispatching seeks while the button is held (the press already
        // committed one). The pager only owns page drags, so this is the only way a scrub works.
        if (_pressedControl == IslandPageControl.Seek && _pressedPage >= 0)
        {
            TryDragSeek();
        }        var now = _time.GetLocalNow();
        if (!_pointerInside)
        {
            _pointerInside = true;
            if (State.Kind is IslandStateKind.Compact or IslandStateKind.Peek or IslandStateKind.Expanded)
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

        // Approach reveal: only the enter/leave edge changes state. Entering the band starts the rail
        // reveal immediately (the springs animate it); leaving arms the normal 900 ms hide delay from
        // that moment instead of from the last wheel/click. Inside the band nothing is invalidated, so
        // a stream of moves costs a rectangle test and nothing else.
        if (_pointerInHoverBand && !_pointerWasInHoverBand)
        {
            if (_archipelago.IsVisible)
            {
                // Already up (wheel/click revealed it earlier): only refresh the idle timer, so
                // boundary jitter costs nothing and cannot schedule a repaint.
                _archipelago.NoteActivity(now);
            }
            else
            {
                _archipelago.Reveal(now);
                InvalidateDirty(StripDamage());
                FrameRequested?.Invoke();
            }
        }
        else if (!_pointerInHoverBand && _pointerWasInHoverBand)
        {
            _archipelago.NoteActivity(now);
        }

        _pointerWasInHoverBand = _pointerInHoverBand;

        if (_pager.IsDragging)
        {
            _pager.DragTo(xDip, NowMs());
            _archipelago.NoteActivity(now);
            InvalidateDirty(ContentDamage());
        }

        UpdateArchipelago(now);
        UpdateHoverControl();
    }

    /// <summary>Resolves the hovered control from the click hit test (hover never drifts from the target).</summary>
    private void UpdateHoverControl()
    {
        var hover = IslandPageControl.None;
        var page = -1;
        var rail = MiniRailSlot.None;
        if (_hasRenderState && _pointerInside)
        {
            var hit = SkiaIslandHitTest.Hit(_renderState, _pointerX, _pointerY);
            if (hit.Kind is IslandHitKind.PageControl or IslandHitKind.Compact)
            {
                hover = hit.Control;
                page = hit.PageIndex;
            }
            else if (hit.Kind == IslandHitKind.RailSlot)
            {
                // The prototype only gives the two neighbour glasses a hover state; the current
                // capsule is a span, so hovering it keeps the base glass.
                var railPage = _renderState.Archipelago.Rail.Active
                    ? _renderState.Archipelago.Rail.Page
                    : SkiaIslandHitTest.MeasureStrip(_renderState).Page;
                rail = hit.PageIndex < railPage
                    ? MiniRailSlot.Previous
                    : hit.PageIndex > railPage ? MiniRailSlot.Next : MiniRailSlot.None;
            }
        }

        if (hover == _hoverControl && page == _hoverPage && rail == _railHover)
        {
            return;
        }

        _hoverControl = hover;
        _hoverPage = page;
        _railHover = rail;
        InvalidateFull();
    }

    /// <summary>
    /// Whether the pointer is currently in the approach band below the shell. The layered window loses
    /// mouse tracking there (the transparent band is click-through), so the host must let the hook own
    /// the pointer state instead of treating the window's leave as "pointer left the island".
    /// </summary>
    public bool IsPointerInHoverBand => _pointerInHoverBand;

    /// <summary>Pointer left the window.</summary>
    public void HandlePointerLeft()
    {
        _pointerInside = false;
        _hoverControl = IslandPageControl.None;
        _hoverPage = -1;
        _pointerInHotZone = false;
        _pointerOnStrip = false;
        _pointerInHoverBand = false;
        _pointerWasInHoverBand = false;
        _hotZoneEnteredAt = null;
        _railHover = MiniRailSlot.None;
        _railPressed = MiniRailSlot.None;
        _railPendingPage = -1;
        CancelPendingClick();
        var now = _time.GetLocalNow();

        if (_pager.IsDragging)
        {
            _pager.EndDrag(NowMs());
            InvalidateFull();
        }

        _pager.Reset();
        _motion.SetPressed(false);

        if (State.Kind is IslandStateKind.Compact or IslandStateKind.Peek or IslandStateKind.Expanded)
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

        // A press implies the pointer is over the island. Without this the hover flag can be stale
        // (a click delivered by the global hook may arrive without a preceding move), which armed
        // auto-collapse under the user's cursor right after expanding.
        if (!State.IsHovering)
        {
            ApplyInput(IslandInput.PointerEntered());
        }

        var hit = SkiaIslandHitTest.Hit(_renderState, xDip, yDip);

        // Press feedback for every painted control (transport, toggles, list buttons).
        if (hit.Kind is IslandHitKind.PageControl or IslandHitKind.Compact)
        {
            _pressedControl = hit.Control;
            _pressedPage = hit.PageIndex;
            _pressedIndex = hit.Index;
            InvalidateFull();
        }

        switch (hit.Kind)
        {
            case IslandHitKind.SplitIslet:
                // A split ball merges back into the primary pill (the motion target drops its count)
                // and opens the previewed activity's page.
                return ActivateSplitIslet(hit.Index);
            case IslandHitKind.OverflowBadge:
                // The +N chip opens the page of the oldest background activity instead of the primary's.
                return ActivateOverflowBadge();
            case IslandHitKind.SecondaryCompact:
                return ActivateSecondaryNotification();

            case IslandHitKind.Compact:
                if (_compact.Kind == ActivityKind.Notification)
                {
                    // The compact notification card is itself the notification action: open the
                    // originating app (QQ deep link) instead of expanding the island first.
                    DispatchControl(new IslandPlatformAction(IslandPlatformActionKind.NotificationActivate, 0),
                        0);
                    return true;
                }

                if (hit.Control == IslandPageControl.PlayPause)
                {
                    DispatchControl(new IslandPlatformAction(IslandPlatformActionKind.MediaTogglePlay), hit.PageIndex);
                    return true;
                }

                // Expand on press: the compact pill is the primary affordance. Drag-to-move is a
                // secondary gesture; it must never make a normal click feel unresponsive.
                // The panel must open the page of the activity the pill actually PAINTS: the scheduler's
                // primary page can be a different (newer) activity, which opened the agent card from a
                // music pill (user report 2026-10-21).
                var paintedPage = _compact.Kind.ToPage();
                ApplyInput(IslandInput.PrimaryClick());
                if (paintedPage != State.Page)
                {
                    ApplyInput(IslandInput.PageRequested(paintedPage));
                    HandlePageRequest(paintedPage);
                }

                return true;

            case IslandHitKind.Collapse:
                ApplyInput(IslandInput.Escape());
                return true;

            case IslandHitKind.Settings:
                // Gear in the expanded chrome row (task-8): collapse first so the always-on-top island
                // does not cover the settings window for the auto-collapse delay, then open settings.
                ApplyInput(IslandInput.Escape());
                RequestSettings();
                return true;

            case IslandHitKind.PageControl:
                HandlePageControl(hit);
                return true;

            case IslandHitKind.RailOverflow:
                PageChooserRequested?.Invoke();
                return true;

            case IslandHitKind.RailSlot:
            case IslandHitKind.DotLeft:
            case IslandHitKind.DotRight:
                // Prototype rail glasses are buttons: the page is requested on release (a press that
                // drags off the pill does not navigate, exactly like a browser click). The press itself
                // paints the accent-free 'active' glass so the affordance is visible while held.
                _archipelago.NoteActivity(_time.GetLocalNow());
                _railPendingPage = hit.PageIndex;
                _railPressXDip = xDip;
                _railPressYDip = yDip;
                _railPressed = hit.Kind == IslandHitKind.RailSlot
                    ? SlotForPage(hit.PageIndex)
                    : MiniRailSlot.None;
                InvalidateDirty(StripDamage());
                FrameRequested?.Invoke();
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
        if (_railPendingPage >= 0)
        {
            var pending = _railPendingPage;
            _railPendingPage = -1;
            var hadPress = _railPressed != MiniRailSlot.None;
            _railPressed = MiniRailSlot.None;
            var isClick = !SkiaDragThreshold.Exceeds(_railPressXDip, _railPressYDip, xDip, yDip);
            if (isClick && pending >= 0)
            {
                _archipelago.NoteActivity(_time.GetLocalNow());
                HandlePageRequest(PageAt(pending));
            }

            if (hadPress)
            {
                InvalidateDirty(StripDamage());
            }

            FrameRequested?.Invoke();
        }

        if (_pressedControl != IslandPageControl.None)
        {
            _pressedControl = IslandPageControl.None;
            _pressedPage = -1;
            _pressedIndex = -1;
            InvalidateFull();
        }

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
        var wheelHit = _pointerInside
            ? SkiaIslandHitTest.Hit(_renderState, _pointerX, _pointerY).Kind
            : IslandHitKind.None;
        var stackRotateAllowed = _settings.Current.Stacking == StackingStrategySetting.Stacked
            && State.Kind is IslandStateKind.Compact or IslandStateKind.Peek
            && _schedulerState.Visible.Length > 1;
        var splitRotateAllowed = _settings.Current.Stacking == StackingStrategySetting.Split
            && State.Kind is IslandStateKind.Compact or IslandStateKind.Peek
            && _schedulerState.Visible.Length > 1;
        PinDiagnostics.Log(
            $"wheel state={State.Kind} form={_renderState.Form} stacking={_settings.Current.Stacking} hit={wheelHit} " +
            $"delta={deltaDip:0.###} horizontal={horizontal} shift={shift} visible=[{string.Join(",", _schedulerState.Visible.Select(r => r.Kind))}] " +
            $"islets={_renderState.SplitIslets.Length} stackRotateAllowed={stackRotateAllowed} splitRotateAllowed={splitRotateAllowed}");

        // Stacked strategy: the wheel over the compact pill rotates the visible stack, bringing the
        // next ghost to the front and pushing the former primary to the back (cyclic). Split mode and
        // latest-only have nothing to rotate.
        if (!horizontal && !shift
            && State.Kind is IslandStateKind.Compact or IslandStateKind.Peek
            && _schedulerState.Visible.Length > 1)
        {
            if (_settings.Current.Stacking == StackingStrategySetting.Split)
            {
                ReorderSplitVisible(deltaDip < 0 ? -1 : 1);
                _motion.PlayStackRotation();
                SyncContent(full: true);
                StateChanged?.Invoke();
                return new SkiaWheelOutcome(true, false, _pager.SelectedIndex, false, false);
            }

            if (_settings.Current.Stacking == StackingStrategySetting.Stacked)
            {
                PinDiagnostics.Log("wheel-route=stack-rotate");
                RotateStack(deltaDip < 0 ? -1 : 1);
                _motion.PlayStackRotation();
                SyncContent(full: true);
                StateChanged?.Invoke();
                return new SkiaWheelOutcome(true, false, _pager.SelectedIndex, false, false);
            }
        }

        // Wheel arbitration: scrollable content owns the wheel until it reaches an edge; at the edge
        // the gesture must accumulate overscroll (and dwell) before the pager turns one page, so a
        // clipboard or notification list scrolls natively and only then flips. Short pages flip at once.
        if (!horizontal && !shift && _renderState.Form == ShellForm.Expanded)
        {
            var page = _pager.SelectedIndex;
            var max = MaxScrollForPage(page);
            var current = page < _pageScroll.Length ? _pageScroll[page] : 0;
            var direction = deltaDip < 0 ? PagerScrollDirection.Up : PagerScrollDirection.Down;
            var boundary = max <= 0.5
                ? PagerScrollBoundary.None
                : direction == PagerScrollDirection.Down
                    ? (current >= max - 0.01 ? PagerScrollBoundary.Bottom : PagerScrollBoundary.None)
                    : (current <= 0.01 ? PagerScrollBoundary.Top : PagerScrollBoundary.None);
            var locked = now - _lastPageStepAt < TimeSpan.FromMilliseconds(PagerConstants.WheelCooldownMs);
            var decision = _scrollChain.Handle(new PagerScrollChainRequest
            {
                WheelDeltaDip = deltaDip,
                ContentScrollable = max > 0.5,
                ContentBoundary = boundary,
                CurrentPage = page,
                PageCount = _pages.PageCount,
                Now = now,
                PageAnimationLocked = locked,
            });

            if (decision.Action == PagerScrollChainAction.ScrollContent)
            {
                if (page < _pageScroll.Length)
                {
                    _pageScroll[page] = Math.Clamp(current + deltaDip, 0, max);
                }

                _pages = ApplyPageScroll(_pages);
                BuildRenderState();
                InvalidateFull();
                _archipelago.Reveal(now);
                UpdateArchipelago(now);
                return new SkiaWheelOutcome(true, false, page, true, false);
            }

            if (decision.Action is PagerScrollChainAction.PageNext or PagerScrollChainAction.PagePrevious)
            {
                _lastPageStepAt = now;
                var paged = _pager.Wheel(deltaDip, horizontal: false, shift: false, now, contentScrollable: false);
                if (paged.Paged || paged.Consumed)
                {
                    _archipelago.Reveal(now);
                    UpdateArchipelago(now);
                    InvalidateFull();
                }

                return paged;
            }

            if (decision.Consumed)
            {
                return new SkiaWheelOutcome(false, false, page, false, false);
            }
        }

        var outcome = _pager.Wheel(deltaDip, horizontal, shift, now,
            contentScrollable: MaxScrollForPage(_pager.SelectedIndex) > 0.5);
        if (outcome.Paged || outcome.Consumed)
        {
            _archipelago.Reveal(now);
            UpdateArchipelago(now);
            InvalidateFull();
        }

        return outcome;
    }

    /// <summary>
    /// Re-resolves the follow-system appearance (Windows theme / reduced motion / accent) after a live
    /// <c>WM_SETTINGCHANGE</c>. Explicit user settings are unaffected; only "system" values change.
    /// Never throws: a failure is logged and the current appearance stays in place.
    /// </summary>
    public void RefreshSystemAppearance()
    {
        try
        {
            var settings = _settings.Current;
            var appearance = _appearance();
            _theme = SkiaIslandThemeMapper.Map(settings, appearance);
            var profile = settings.BuildMotionProfile(
                SkiaIslandThemeMapper.ReducedMotion(settings, appearance));
            _motion.UpdateProfile(profile);
            BuildRenderState();
            InvalidateFull();
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "island.appearance-refresh-failed",
                "A live system-appearance change could not be applied; keeping the current theme/motion.", ex);
        }
    }

    /// <summary>Display/DPI change: snap springs and repaint everything.</summary>
    public void NotifyDisplayChanged()
    {
        _motion.SetTarget(BuildTarget(PreferNotificationPrimary(ActivityPresenter.PresentIsland(_schedulerState, State))));
        _motion.SnapToTarget();
        _pager.JumpToPage(IndexOfPage(State.Page));
        BuildRenderState();
        InvalidateFull();
        PlacementRequested?.Invoke();
    }

    /// <summary>Snaps the shell motion to the current state target and rebuilds the render state.</summary>
    public void SnapMotion()
    {
        _motion.SnapToTarget();
        BuildRenderState();
        InvalidateFull();
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
        if (_pageRegistry is not null)
        {
            _pageRegistry.Changed -= OnPageRegistryChanged;
        }

        _controller.Transitioned -= OnTransition;
        _controller.Dispose();
        _hub.Dispose();
        _motion.Dispose();
        _archipelago.Dispose();
        _lyricMeasure.Dispose();
    }

    // ------------------------------------------------------------------ pipeline

    private IslandStateController CreateController(AppSettings settings)
        => new(BuildBehaviorOptions(settings), _controller?.Current ?? IslandState.Hidden, _dispatcher);

    private static IslandBehaviorOptions BuildBehaviorOptions(AppSettings settings) => new()
    {
        // Safe mode keeps the island off the screen for activity/hover reveals; the user can still
        // summon it explicitly (hotkey/tray) to reach the settings and turn safe mode off.
        Enabled = !settings.Advanced.SafeMode,
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
        // Producer activity updates can leave the island state kind untouched (Compact -> Compact);
        // the transition callback then never fires, so refresh the content/preview snapshot here.
        SyncContent(full: false);
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
                    // Saver suppresses background nudges while the pointer is elsewhere; Performance
                    // (ambient always) and any pointer/expanded surface keep the original spring.
                    if (HasSpinningSplitRing || _pointerInside
                        || State.Kind is IslandStateKind.Expanded or IslandStateKind.Peek)
                    {
                        _motion.PlayNudge();
                        InvalidateFull();
                        FrameRequested?.Invoke();
                    }

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

    /// <summary>
    /// Rebuilds content after a data/settings event. Returns false when nothing that affects the
    /// painted island changed, so a producer heartbeating at frame rate cannot force a full repaint or
    /// restart the shell transitions on every tick.
    /// </summary>
    private bool SyncContent(bool full)
    {
        var content = _data.Snapshot();
        var settings = _settings.Current;
        if (_splitPinnedKey is { } pinned
            && !_schedulerState.Visible.Any(r => r.Key.Equals(pinned))
            && !_schedulerState.Background.Any(r => r.Key.Equals(pinned)))
        {
            ReleasePin();
        }

        if (settings.Stacking == StackingStrategySetting.Split)
        {
            // The split strategy owns the "who is the main island" decision; the scheduler order is a
            // priority suggestion, not the presentation order.
            ReorderSplitVisible();
        }

        var viewModel = ActivityPresenter.PresentIsland(_schedulerState, State);
        // Notification uses the normal island position; it is no longer demoted to a second pill.

        var presentation = PreferNotificationPrimary(viewModel);
        PinDiagnostics.Log(
            $"sync state={State.Kind} stacking={settings.Stacking} visible=[{string.Join(",", _schedulerState.Visible.Select(r => r.Kind + ":" + r.Key.Source + (r.IsPinned ? "*" : "")))}] " +
            $"ghosts={viewModel.Ghosts.Length} previews={_splitPreviews.Length} splitProgress={_motion.SplitProgress:0.00} " +
            $"primary={viewModel.Primary?.Kind}:{viewModel.Primary?.Title}");
        PinDiagnostics.Log(
            $"strategy requested={settings.Stacking} state={State.Kind} visibleCount={_schedulerState.Visible.Length} " +
            $"ghostCount={viewModel.Ghosts.Length} splitAllowed={settings.Stacking == StackingStrategySetting.Split && State.Kind != IslandStateKind.Expanded} " +
            $"stackRotateAllowed={settings.Stacking == StackingStrategySetting.Stacked && State.Kind is IslandStateKind.Compact or IslandStateKind.Peek && _schedulerState.Visible.Length > 1}");
        ObserveTrackChange(content, settings);
        ObserveTrackTransition(content, viewModel);

        // The primary's content signature does not include the ghost previews: a new activity behind
        // the pill (or a split reorder) must still refresh the target/snapshots.
        var stackSignature = string.Join(
            '|', _schedulerState.Visible.Select(record => record.Key.ToString())) +
            "#" + viewModel.Ghosts.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stackChanged = !string.Equals(stackSignature, _visibleStackSignature, StringComparison.Ordinal);
        _visibleStackSignature = stackSignature;

        var structural = SkiaIslandStateBuilder.StructuralSignature(content, presentation, _localizer, settings);
        var dynamicSignature = SkiaIslandStateBuilder.DynamicSignature(content, presentation);
        var timingSignature = SkiaIslandStateBuilder.AgentTimingSignature(content, presentation);
        var structuralChanged = !string.Equals(structural, _structuralSignature, StringComparison.Ordinal);
        var dynamicChanged = !string.Equals(dynamicSignature, _dynamicSignature, StringComparison.Ordinal);
        var timingChanged = !string.Equals(timingSignature, _agentTimingSignature, StringComparison.Ordinal);
        _structuralSignature = structural;
        _dynamicSignature = dynamicSignature;
        _agentTimingSignature = timingSignature;

        if (!full && _hasRenderState && !structuralChanged && !dynamicChanged && !timingChanged && !stackChanged)
        {
            // A producer heartbeat with identical content: no relayout, no transition retarget, no frame.
            return false;
        }

        if (!full && _hasRenderState && !structuralChanged && !dynamicChanged && timingChanged && !stackChanged)
        {
            // Elapsed-time-only update: patch the duration strings and repaint the time strip. The
            // page models, shell target and text layout stay untouched, so the one-second tick can
            // never resize the island or reflow the row.
            ApplyAgentTiming(content);
            BuildRenderState();
            InvalidateDirty(AgentTimingDamage());
            if (IsAgentAnimating)
            {
                FrameRequested?.Invoke();
            }

            return true;
        }

        SyncKaraoke(content);
        SyncMediaProgress(content);

        var peeked = State.Kind == IslandStateKind.Peek;
        // A new song shows its title/artist for three seconds before the lyric takes the pill over:
        // jumping straight to the words made the track feel unannounced (user request 2026-10-21).
        var showCompactLyrics = _mediaPill.ShowCompactLyrics;
        _compact = SkiaIslandStateBuilder.BuildCompact(presentation, content, _localizer, peeked, showCompactLyrics);
        _secondaryCompact = null;
        TrackCompactLyric();
        _pluginPages = _pageRegistry?.BuildPluginPages() ?? [];
        _pages = ApplyPageScroll(ApplyCopyFeedback(
            SkiaIslandStateBuilder.BuildPages(content, presentation, _localizer, settings),
            _time.GetLocalNow())) with
        {
            PluginPages = _pluginPages,
            PageSlots = BuildPageSlots(),
        };
        _theme = SkiaIslandThemeMapper.Map(settings, _appearance());
        UpdateSplitPreviews(presentation, settings);
        _motion.SetTarget(BuildTarget(presentation));
        BuildRenderState();

        if (full || !_hasRenderState || structuralChanged)
        {
            InvalidateFull();
        }
        else if (dynamicChanged)
        {
            InvalidateDynamic();
        }
        else if (timingChanged)
        {
            InvalidateDirty(AgentTimingDamage());
        }

        // A content change may have started an agent run: ask the host for a frame loop now so the
        // spinner starts immediately (the loop keeps itself alive while IsAgentAnimating is true).
        if (IsAgentAnimating || HasSpinningSplitRing)
        {
            InvalidateDirty(IsAgentAnimating ? ContentDamage() : SplitRingDamage());
            FrameRequested?.Invoke();
        }

        return true;
    }

    /// <summary>Updates only the elapsed-time strings on the already-built agent page model.</summary>
    /// <param name="content">Fresh non-activity snapshot (agent rows).</param>
    private void ApplyAgentTiming(IslandContent content)
    {
        var source = content.Agents;
        var items = _pages.Agent.Items;
        if (items.Count == source.Length)
        {
            var updated = new AgentItemModel[items.Count];
            for (var i = 0; i < updated.Length; i++)
            {
                updated[i] = items[i] with { DurationText = source[i].TimeText };
            }

            _pages = _pages with { Agent = _pages.Agent with { Items = updated } };
        }
    }

    /// <summary>Damage of the elapsed-time glyphs only (right time column / compact meta right edge).</summary>
    private VisualRect AgentTimingDamage()
    {
        if (!_hasRenderState)
        {
            return ContentDamage();
        }

        if (CurrentPageIs(PageSlotKind.Agent))
        {
            // Agent rows stack status over duration in a fixed right column; repaint that strip for
            // every visible row without touching the task text or the shell.
            var page = IslandMetrics.PageRect(_renderState, 4);
            return new VisualRect(page.Right - 150, page.Top, 150, page.Height).Inflate(2);
        }

        if (_compact.Kind == ActivityKind.Agent && _renderState.Form != ShellForm.Expanded)
        {
            var shell = IslandMetrics.ShellRect(_renderState);
            return new VisualRect(shell.Right - 160, shell.Top, 158, shell.Height).Inflate(2);
        }

        return ContentDamage();
    }

    /// <summary>Damage of the per-frame spinner/progress glyphs.</summary>
    private VisualRect SplitRingDamage()
    {
        if (!_hasRenderState || !IslandMetrics.HasSpinningSplitRing(_renderState))
        {
            return VisualRect.Empty;
        }

        var rects = IslandMetrics.SplitIsletRects(_renderState);
        if (rects.Length == 0)
        {
            return VisualRect.Empty;
        }

        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var rect in rects)
        {
            left = Math.Min(left, rect.Left);
            top = Math.Min(top, rect.Top);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }

        return new VisualRect(left, top, right - left, bottom - top).Inflate(2);
    }

    private VisualRect AgentFrameDamage()
    {
        if (!_hasRenderState)
        {
            return ContentDamage();
        }

        if (CurrentPageIs(PageSlotKind.Agent))
        {
            // The indeterminate progress bar spans each row, so repaint the agent page content only
            // (never the whole layered surface).
            return ContentDamage();
        }

        if (_compact.Kind == ActivityKind.Agent)
        {
            // prototype2 .agent-ring is a 20 DIP circle at x=10..30, vertically centred.
            var shell = IslandMetrics.ShellRect(_renderState);
            var ringTop = shell.Top + ((shell.Height - 20) / 2);
            return new VisualRect(shell.Left + 10, ringTop, 20, 20).Inflate(2);
        }

        return ContentDamage();
    }

    private SkiaMotionTarget BuildTarget(IslandViewModel viewModel)
    {
        var settings = _settings.Current;
        var mode = settings.IslandMode;
        // Derive the compact size from the same source the compact model is built from
        // (SkiaIslandStateBuilder.BuildCompact), so the shell width can never disagree with the
        // content: a stale Core ContentKind used to leave a wide pill showing the idle clock.
        var compactKind = viewModel.Primary?.Kind;
        var compactSize = LayoutTokens.CompactSizeFor(compactKind, mode, settings.FontScale);

        // The notification card only grows when its text actually needs the room; short messages keep
        // the 360 DIP token. The cap is 1215 DIP (the third requested +50%).
        if (compactKind == ActivityKind.Notification && _compact.Model is { } notificationModel)
        {
            var measured = MeasureNotificationWidth(notificationModel, settings.FontScale);
            var width = Math.Clamp(measured, LayoutTokens.NotificationMinWidth, LayoutTokens.NotificationMaxWidth);
            compactSize = new VisualSize(width, compactSize.Height);
        }

        // Lyric form: widen the pill so the whole line fits instead of ellipsising it. The motion driver
        // tweens the width (380 ms shell transition), so a line change grows/shrinks the pill smoothly;
        // the clamp keeps the token as the floor and the host surface as the ceiling (over-long lines
        // then fall back to the ellipsis).
        if (mode != IslandMode.Dock
            && compactKind == ActivityKind.Media
            && _compact.Model is { ShowLyrics: true } lyric
            && !string.IsNullOrEmpty(lyric.LyricLine))
        {
            var measured = MeasureCompactLyric(lyric.LyricLine, settings.FontScale);
            var width = CompactMediaLayout.AdaptiveWidth(
                measured, CompactMediaLayout.MinContentWidth, (float)LayoutTokens.ExpandedWidth);
            compactSize = new VisualSize(width, compactSize.Height);
        }
        var ghostCount = 0;
        foreach (var ghost in viewModel.Ghosts)
        {
            if (_secondaryCompact is null || ghost.Kind != ActivityKind.Notification)
            {
                ghostCount++;
            }
        }

        var ghosts = settings.Stacking == StackingStrategySetting.Stacked
            ? Math.Clamp(ghostCount, 0, LayoutTokens.MaxGhosts)
            : 0;
        var splitCount = settings.Stacking == StackingStrategySetting.Split
            && State.Kind != IslandStateKind.Expanded
            ? Math.Min(ghostCount, LayoutTokens.MaxGhosts)
            : 0;

        // The split previews need the two side lanes: cap an over-long lyric pill so both capsules
        // stay fully inside the fixed host surface (the lyric falls back to its ellipsis).
        if (splitCount > 0)
        {
            compactSize = new VisualSize(
                Math.Min(compactSize.Width, LayoutTokens.SplitPrimaryMaxWidth),
                compactSize.Height);
        }

        // With more activities than the visible stack can hold, the compact pill reserves a column for
        // the "+N" chip. Keeping the reservation here (not only in the renderer) means the shell, its
        // hit region and the chip always agree on the same width.
        if (viewModel.OverflowCount > 0)
        {
            compactSize = new VisualSize(
                compactSize.Width + LayoutTokens.OverflowBadgeReserve,
                compactSize.Height);
        }

        var form = State.Kind switch
        {
            IslandStateKind.Compact => ShellForm.Compact,
            IslandStateKind.Peek => ShellForm.Peek,
            IslandStateKind.Expanded => ShellForm.Expanded,
            IslandStateKind.Closing => ShellForm.Closing,
            _ => ShellForm.Hidden,
        };
        return new SkiaMotionTarget(
            form, mode, settings.IslandAnchor, compactSize, ghosts, splitCount,
            State.Kind == IslandStateKind.Hidden);
    }

    /// <summary>Applies the split policy's presentation order to the visible stack.</summary>
    private void ReorderSplitVisible(int wheelSteps = 0)
    {
        if (_schedulerState.Visible.Length < 2 && wheelSteps == 0)
        {
            return;
        }

        var ordered = _splitPolicy.Order(_schedulerState.Visible, _time.GetLocalNow(), wheelSteps);

        // The scheduler is the real consumer of the activity snapshot on the next heartbeat: its
        // preemption path removes a non-pinned primary before SplitStackPolicy gets a chance to
        // re-order. PinTo mirrors the wheel pin into ActivityRecord.IsPinned as well.
        if (wheelSteps != 0)
        {
            PinTo(ordered[0].Key, "wheel");
        }
        else if (_splitPinnedKey is { } oldPin && _splitPolicy.PinnedKey is null)
        {
            ReleasePin();
        }

        if (!ordered.SequenceEqual(_schedulerState.Visible))
        {
            _schedulerState = _schedulerState with { Visible = ordered };
        }
    }

    /// <summary>Pins the user-selected primary in both the policy and the scheduler snapshot.</summary>
    private void PinTo(ActivityKey key, string reason)
    {
        if (_splitPinnedKey is { } oldPin && !oldPin.Equals(key))
        {
            _schedulerState = _scheduler.SetPinned(_schedulerState, oldPin, false).State;
        }

        _splitPolicy.Pin(key);
        var pinUpdate = _scheduler.SetPinned(_schedulerState, key, true);
        if (pinUpdate.Kind != ActivityUpdateKind.None)
        {
            _schedulerState = pinUpdate.State;
        }

        _splitPinnedKey = key;
        PinDiagnostics.Log($"pin set to {key} ({reason})");
    }

    private void ReleasePin()
    {
        if (_splitPinnedKey is not { } oldPin)
        {
            return;
        }

        _schedulerState = _scheduler.SetPinned(_schedulerState, oldPin, false).State;
        _splitPolicy.Unpin();
        _splitPinnedKey = null;
        PinDiagnostics.Log($"pin released {oldPin}");
    }

    /// <summary>
    /// Promotes a notification from the ghost layers to the primary slot. Split and stacked layouts
    /// still use the same primary pill, so a notification must never be painted as a small floating
    /// preview when another activity is alive.
    /// </summary>
    private static IslandViewModel PreferNotificationPrimary(IslandViewModel viewModel)
    {
        if (viewModel.Primary?.Kind == ActivityKind.Notification)

        {
            return viewModel;
        }

        var notificationIndex = -1;
        for (var i = 0; i < viewModel.Ghosts.Length; i++)
        {
            if (viewModel.Ghosts[i].Kind == ActivityKind.Notification)
            {
                notificationIndex = i;
                break;
            }
        }

        if (notificationIndex < 0)
        {
            return viewModel;
        }

        var notification = viewModel.Ghosts[notificationIndex] with { IsGhost = false, StackIndex = 0 };
        var ghosts = viewModel.Ghosts.RemoveAt(notificationIndex);
        if (viewModel.Primary is { } previousPrimary)
        {
            ghosts = ghosts.Insert(0, previousPrimary with { IsGhost = true, StackIndex = 1 });
        }

        return viewModel with
        {
            Primary = notification,
            Ghosts = ghosts,
        };
    }

    /// <summary>Builds the compact notification pill shown below the primary island (null when none).</summary>
    private CompactActivity? BuildSecondaryCompact(IslandViewModel presentation, IslandContent content)
    {
        foreach (var ghost in presentation.Ghosts)
        {
            if (ghost.Kind != ActivityKind.Notification)
            {
                continue;
            }

            var ghostIsland = new IslandViewModel
            {
                StateKind = IslandStateKind.Compact,
                Page = ghost.Page,
                Primary = ghost,
            };
            return SkiaIslandStateBuilder.BuildCompact(
                ghostIsland, content, _localizer, peeked: false, compactLyrics: false);
        }

        return null;
    }

    /// <summary>Snapshots the split previews for the current strategy (kept while the merge animates).</summary>
    private void UpdateSplitPreviews(IslandViewModel viewModel, AppSettings settings)
    {
        // The reference notification card owns the island; hide split previews while it is primary.
        if (viewModel.Primary?.Kind == ActivityKind.Notification
            || viewModel.Ghosts.Any(ghost => ghost.Kind == ActivityKind.Notification))
        {
            _splitPreviews = [];
            return;
        }

        if (settings.Stacking == StackingStrategySetting.Split
            && State.Kind is not (IslandStateKind.Expanded or IslandStateKind.Closing))
        {
            _splitPreviews = viewModel.Ghosts
                .Where(ghost => _secondaryCompact is null || ghost.Kind != ActivityKind.Notification)
                .Take(LayoutTokens.MaxGhosts)
                .ToArray();
            return;
        }

        if (_motion.SplitProgress <= 0.02)
        {
            _splitPreviews = [];
        }
    }

    /// <summary>Builds the render-state split-islet snapshots from the cached presenter models.</summary>
    private IslandSplitIslet[] BuildSplitIslets()
    {
        PinDiagnostics.Log($"build-islets previews={_splitPreviews.Length}");
        if (_splitPreviews.Length == 0)
        {
            return [];
        }

        var islets = new IslandSplitIslet[_splitPreviews.Length];
        for (var i = 0; i < _splitPreviews.Length; i++)
        {
            var view = _splitPreviews[i];
            var label = _localizer.Get(ActivityPresenter.KindLabelKeyFor(view.Kind));
            if (string.IsNullOrWhiteSpace(label) || label == ActivityPresenter.KindLabelKeyFor(view.Kind))
            {
                label = _localizer.Get("Activity." + view.Kind);
            }

            islets[i] = new IslandSplitIslet(
                view.Kind,
                MapSplitIcon(view.Icon),
                string.IsNullOrWhiteSpace(label) ? view.Kind.ToString() : label,
                view.Title,
                view.Page,
                ParseAccent(view.AccentColorHex),
                null,
                view.Kind == ActivityKind.Agent && view.IsActive,
                SplitArtwork(view),
                view.AlbumArtPath);
        }

        return islets;
    }

    /// <summary>Reads and caches the album cover for a split preview (one decode per path).</summary>
    private byte[]? SplitArtwork(ActivityViewModel view)
    {
        if (view.Kind != ActivityKind.Media || string.IsNullOrWhiteSpace(view.AlbumArtPath))
        {
            return null;
        }

        if (_splitArtwork.TryGetValue(view.AlbumArtPath, out var cached))
        {
            return cached;
        }

        byte[]? bytes = null;
        try
        {
            if (System.IO.File.Exists(view.AlbumArtPath))
            {
                bytes = System.IO.File.ReadAllBytes(view.AlbumArtPath);
            }
        }
        catch (Exception)
        {
            bytes = null;
        }

        _splitArtwork[view.AlbumArtPath] = bytes;
        return bytes;
    }

    /// <summary>Maps the Core activity icon identity onto the Island vector icon set.</summary>
    private static FluidBar.Island.IconKind MapSplitIcon(ActivityIconKind icon) => icon switch
    {
        ActivityIconKind.Media => FluidBar.Island.IconKind.Media,
        ActivityIconKind.Clipboard => FluidBar.Island.IconKind.Clipboard,
        ActivityIconKind.Notification => FluidBar.Island.IconKind.Bell,
        ActivityIconKind.Volume => FluidBar.Island.IconKind.VolumeMedium,
        ActivityIconKind.Brightness => FluidBar.Island.IconKind.BrightnessHigh,
        ActivityIconKind.Battery => FluidBar.Island.IconKind.Battery,
        ActivityIconKind.Timer => FluidBar.Island.IconKind.Clock,
        ActivityIconKind.LockKeys => FluidBar.Island.IconKind.Lock,
        ActivityIconKind.Device => FluidBar.Island.IconKind.Usb,
        ActivityIconKind.Bluetooth => FluidBar.Island.IconKind.Bluetooth,
        ActivityIconKind.Focus => FluidBar.Island.IconKind.Focus,
        ActivityIconKind.Progress => FluidBar.Island.IconKind.Download,
        ActivityIconKind.Search => FluidBar.Island.IconKind.Search,
        ActivityIconKind.Agent => FluidBar.Island.IconKind.Agent,
        _ => FluidBar.Island.IconKind.Clock,
    };

    private static uint ParseAccent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return 0;
        }

        var span = hex.AsSpan().TrimStart('#');
        if (span.Length == 6 && uint.TryParse(span, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            return 0xFF000000u | rgb;
        }

        if (span.Length == 8 && uint.TryParse(span, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var argb))
        {
            return argb;
        }

        return 0;
    }

    /// <summary>Measures the compact lyric line at the current font scale (cached by the text engine).</summary>
    private float MeasureNotificationWidth(CompactModel model, double fontScale)
    {
        var scale = double.IsFinite(fontScale) && fontScale > 0 ? fontScale : 1.0;
        var titleStyle = new TextStyle(IslandType.NotificationTitle.Role, (float)(IslandType.NotificationTitle.Size * scale));
        var bodyStyle = new TextStyle(IslandType.NotificationBody.Role, (float)(IslandType.NotificationBody.Size * scale));
        var appStyle = new TextStyle(IslandType.CompactSmallSemiBold.Role, (float)(IslandType.CompactSmallSemiBold.Size * scale));
        var app = model.AppName ?? model.Subtitle ?? string.Empty;
        var title = model.Title ?? model.Body ?? app;
        var body = model.Body ?? string.Empty;
        var titleWidth = _lyricMeasure.Measure(title, titleStyle);
        var bodyWidth = _lyricMeasure.Measure(body, bodyStyle);
        var titleRow = titleWidth;
        if (!string.IsNullOrEmpty(app))
        {
            titleRow += (6f * (float)scale) + (12f * (float)scale)
                + _lyricMeasure.Measure(app, appStyle);
        }

        // 14 left + 35 avatar + 12 gap + 45 right padding, plus the same 2 DIP shaper slack used by
        // the media pill so the final glyph never rounds into an ellipsis.
        return (float)(61 + Math.Max(titleRow, bodyWidth) + 2);
    }

    private float MeasureCompactLyric(string lyric, double fontScale)
    {
        var style = IslandType.CompactMediaLyric;
        var scale = double.IsFinite(fontScale) && fontScale > 0 ? fontScale : 1.0;
        var scaled = new TextStyle(style.Role, (float)(style.Size * scale));
        return _lyricMeasure.Measure(lyric, scaled);
    }

    private void SyncPagerFromState()
    {
        var page = IndexOfPage(State.Page);
        if (page < 0 || page == _pager.SelectedIndex)
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

    /// <summary>Applies the remembered page once, as soon as the pager knows the real page count.</summary>
    private void ApplyRestoredPage()
    {
        if (_pendingRestoredPage < 0 || _pages.PageCount <= 0)
        {
            return;
        }

        var page = Math.Clamp(_pendingRestoredPage, 0, _pages.PageCount - 1);
        _pendingRestoredPage = -1;
        if (page != _pager.SelectedIndex)
        {
            _pager.SelectPage(page);
        }
    }

    private void OnPagerPageChanged(int page)
    {
        if (_syncingPage)
        {
            return;
        }

        ApplyInput(IslandInput.PageRequested(PageAt(page)));
        // Remember the manual choice so a restart opens on the same page (user request 2026-10-22).
        PageSelectionStore.Save(page);
        InvalidateFull();
        FrameRequested?.Invoke();
    }

    private void OnDataChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.IsOnDispatcherThread)
        {
            if (SyncContent(full: false))
            {
                StateChanged?.Invoke();
            }

            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (SyncContent(full: false))
            {
                StateChanged?.Invoke();
            }
        });
    }

    /// <summary>First three seconds of a track show the title/artist instead of the live lyric.</summary>
    private const long TrackIntroMs = 3000;

    private string? _introTrackKey;
    private long _introUntilMs;

    private bool IsTrackIntro(IslandContent content)
    {
        var media = content.Media;
        var key = media is null ? null : media.Title + "\u0000" + media.Artist;
        var now = NowMs();
        if (!string.Equals(key, _introTrackKey, StringComparison.Ordinal))
        {
            // The very first track after launch gets no intro card: the island is already showing the
            // track for the first time, and starting on the words matches the previous behaviour.
            var firstTrack = _introTrackKey is null;
            _introTrackKey = key;
            _introUntilMs = key is null || firstTrack ? 0 : now + TrackIntroMs;
        }

        return now < _introUntilMs;
    }

    private int IndexOfPage(CoreIslandPage page) => _pages.IndexOfPage(page);

    private CoreIslandPage PageAt(int index)
        => _pages.PageSlotAt(index) is { PageId: { Length: > 0 } pageId }
            ? new CoreIslandPage(pageId)
            : CoreIslandPage.Home;

    private bool CurrentPageIs(PageSlotKind kind)
        => _renderState.Form == ShellForm.Expanded
           && _pages.PageSlotAt(_renderState.PageIndex)?.Kind == kind;

    private IReadOnlyList<PageSlot> BuildPageSlots()
    {
        var plugins = _pluginPages;
        var descriptors = _pageRegistry?.BuildPageDescriptors();
        if (descriptors is null || descriptors.Count == 0)
        {
            var fallback = ExpandedPageContent.DefaultPageSlots
                .Concat(plugins.Select(static page => new PageSlot
                {
                    PageId = page.PageId,
                    Kind = PageSlotKind.Plugin,
                    TemplateId = "list",
                    Title = page.Title,
                    ShortTitle = page.ShortTitle,
                    IconToken = page.IconToken,
                    PluginPage = page,
                }))
                .ToArray();
            return fallback;
        }

        var byPageId = plugins.ToDictionary(static page => page.PageId, StringComparer.OrdinalIgnoreCase);
        var slots = new PageSlot[descriptors.Count];
        for (var i = 0; i < descriptors.Count; i++)
        {
            var descriptor = descriptors[i];
            byPageId.TryGetValue(descriptor.PageId, out var plugin);
            slots[i] = new PageSlot
            {
                PageId = descriptor.PageId,
                Kind = plugin is not null ? PageSlotKind.Plugin : KindForPageId(descriptor.PageId),
                TemplateId = descriptor.TemplateId,
                Title = plugin?.Title ?? ResolveDescriptorTitle(descriptor),
                ShortTitle = plugin?.ShortTitle ?? ResolveDescriptorShortTitle(descriptor),
                IconToken = descriptor.IconToken,
                PluginPage = plugin,
            };
        }

        return slots;
    }

    private static PageSlotKind KindForPageId(string pageId) => pageId switch
    {
        "builtin.home" => PageSlotKind.Home,
        "builtin.media" => PageSlotKind.Media,
        "builtin.notifications" => PageSlotKind.Notifications,
        "builtin.clipboard" => PageSlotKind.Clipboard,
        "builtin.agent" => PageSlotKind.Agent,
        "builtin.widgets" => PageSlotKind.Widgets,
        _ => PageSlotKind.Plugin,
    };

    private string ResolveDescriptorTitle(PageDescriptor descriptor)
    {
        var key = descriptor.PageId switch
        {
            "builtin.home" => "Page.Home.Title",
            "builtin.media" => "Page.Media.Title",
            "builtin.notifications" => "Page.Notifications.Title",
            "builtin.clipboard" => "Page.Clipboard.Title",
            "builtin.agent" => "Page.Agent.Title",
            "builtin.widgets" => "Page.Widgets.Title",
            _ => null,
        };
        return key is not null
            ? _localizer.Get(key)
            : descriptor.Title.Value ?? descriptor.Title.Translations.Values.FirstOrDefault() ?? descriptor.PageId;
    }

    private string ResolveDescriptorShortTitle(PageDescriptor descriptor)
        => descriptor.ShortTitle?.Value
           ?? descriptor.ShortTitle?.Translations.Values.FirstOrDefault()
           ?? ResolveDescriptorTitle(descriptor);

    private void OnPageRegistryChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.IsOnDispatcherThread)
        {
            RefreshPluginPages();
            return;
        }

        _dispatcher.Post(RefreshPluginPages);
    }

    private void RefreshPluginPages()
    {
        if (_disposed)
        {
            return;
        }

        var pages = _pageRegistry?.BuildPluginPages() ?? [];
        _pluginPages = pages;
        _pages = _pages with { PluginPages = pages, PageSlots = BuildPageSlots() };
        var previousCount = _pages.PageCount;
        var pageCount = _pages.PageCount;
        var clamped = _pager.SetPageCount(pageCount);

        if (clamped && previousCount > pageCount)
        {
            // The page the user was on was unregistered: return to Home instead of leaving an
            // invalid index in the pager.
            _syncingPage = true;
            try
            {
                _pager.SelectPage(0);
            }
            finally
            {
                _syncingPage = false;
            }

            ApplyInput(IslandInput.PageRequested(CoreIslandPage.Home));
        }

        SyncContent(full: true);
        StateChanged?.Invoke();
        InvalidateFull();
        FrameRequested?.Invoke();
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (!string.Equals(previous.Language, current.Language, StringComparison.OrdinalIgnoreCase)
            && _localizer is LocalizationService service)
        {
            service.SetCulture(current.Language);
        }

        var appearance = _appearance();
        var previousProfile = previous.BuildMotionProfile(SkiaIslandThemeMapper.ReducedMotion(previous, appearance));
        var currentProfile = current.BuildMotionProfile(SkiaIslandThemeMapper.ReducedMotion(current, appearance));
        if (previousProfile != currentProfile)
        {
            _motion.UpdateProfile(currentProfile);
        }

        if (!current.PeekOnTrackChange)
        {
            _syntheticPeek = SyntheticPeekPhase.None;
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

        // Appearance changes (panel radius, fill opacity, accent…) must repaint even while the shell
        // is idle: rebuild the render state and request a full frame instead of waiting for the next
        // animation tick.
        BuildRenderState();
        InvalidateFull();

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

        private long _lastSeekDragMs;
    private double _lastSeekDragFraction = -1;

    /// <summary>Commits a seek for the pointer's current X while the seek row is being dragged.</summary>
    private void TryDragSeek()
    {
        var now = NowMs();
        if (now - _lastSeekDragMs < 40)
        {
            return;
        }

        var action = SkiaIslandControlRouter.Map(
            new IslandHitTarget(IslandHitKind.PageControl, _pressedPage, IslandPageControl.Seek),
            _renderState,
            _pointerX);
        if (action is not { } seek || Math.Abs(seek.Value - _lastSeekDragFraction) < 0.004)
        {
            return;
        }

        _lastSeekDragMs = now;
        _lastSeekDragFraction = seek.Value;
        DispatchControl(seek, _pressedPage);
        InvalidateDirty(ContentDamage());
        FrameRequested?.Invoke();
    }

private void HandlePageControl(in IslandHitTarget hit)
    {
        if (hit.Control is IslandPageControl.PluginRow or IslandPageControl.PluginRowButton)
        {
            RaisePluginPageCommand(hit.PageIndex, hit.Index, hit.Control == IslandPageControl.PluginRowButton);
            return;
        }

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

    private bool RaisePluginPageCommand(int page, int rowIndex, bool trailing)
    {
        if (!_hasRenderState || _renderState.Pages.PluginPageAt(page) is not { } pageModel)
        {
            return false;
        }

        if (rowIndex < 0 || rowIndex >= pageModel.Rows.Count)
        {
            return false;
        }

        var row = pageModel.Rows[rowIndex];
        var commandId = trailing ? row.TrailingCommandId : row.RowCommandId ?? row.TrailingCommandId;
        if (string.IsNullOrWhiteSpace(commandId))
        {
            return false;
        }

        PluginPageCommandRequested?.Invoke(new PluginPageCommandRequest(
            page,
            pageModel.OwnerPluginId,
            pageModel.PageId,
            row.RowId,
            commandId));
        return true;
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

        if (action.Kind == IslandPlatformActionKind.MediaSeek)
        {
            // Our own seek knows the target immediately; waiting for the next 250 ms/1 s sample left the
            // karaoke sweep (and the line) behind the drag (user report 2026-10-21).
            ReanchorKaraokeAfterSeek(action.Value);
        }

        if (action.Kind == IslandPlatformActionKind.ClipboardCopy)
        {
            RememberCopiedClipboard(action.Index);
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

    /// <summary>Starts the "copied" confirmation for the clipboard row that was just dispatched.</summary>
    private void RememberCopiedClipboard(int index)
    {
        var items = _pages.Clipboard.Items;
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        var now = _time.GetLocalNow();
        _copiedClipboard = (items[index].Preview ?? string.Empty, now);
        _pages = ApplyCopyFeedback(_pages, now);
        InvalidateFull();
        StateChanged?.Invoke();
    }

    /// <summary>Marks the clipboard row matching the remembered preview while the confirmation lasts.</summary>
    private ExpandedPageContent ApplyCopyFeedback(ExpandedPageContent pages, DateTimeOffset now)
    {
        if (_copiedClipboard is not { } copied || now - copied.At > CopyFeedbackDuration)
        {
            return pages;
        }

        var items = pages.Clipboard.Items;
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Preview, copied.Preview, StringComparison.Ordinal))
            {
                return pages with { Clipboard = pages.Clipboard with { CopiedIndex = i } };
            }
        }

        return pages;
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

    // ------------------------------------------------------------------ page scroll

    /// <summary>
    /// Persists the vertical scroll offset of every expanded page. The page models are rebuilt from the
    /// data source on each sync, so the offset lives in the controller and is re-applied here (clamped
    /// against the font-scale aware content extent / row metrics).
    /// </summary>
    private ExpandedPageContent ApplyPageScroll(ExpandedPageContent pages)
    {
        for (var page = 0; page < pages.PageCount && page < _pageScroll.Length; page++)
        {
            _pageScroll[page] = Math.Clamp(_pageScroll[page], 0, MaxScrollForPage(page, pages));
        }

        return pages with
        {
            Home = pages.Home with { ScrollOffset = ScrollForKind(PageSlotKind.Home) },
            Media = pages.Media with { ScrollOffset = ScrollForKind(PageSlotKind.Media) },
            Notifications = pages.Notifications with { ScrollOffset = ScrollForKind(PageSlotKind.Notifications) },
            Clipboard = pages.Clipboard with { ScrollOffset = ScrollForKind(PageSlotKind.Clipboard) },
            Agent = pages.Agent with { ScrollOffset = ScrollForKind(PageSlotKind.Agent) },
            Widgets = pages.Widgets with { ScrollOffset = ScrollForKind(PageSlotKind.Widgets) },
        };
    }

    private double ScrollForKind(PageSlotKind kind)
    {
        for (var i = 0; i < _pages.PageSlots.Count && i < _pageScroll.Length; i++)
        {
            if (_pages.PageSlots[i].Kind == kind)
            {
                return _pageScroll[i];
            }
        }

        return 0;
    }

    /// <summary>Scrollable list content height minus the 232 DIP body, 0 when every row fits.</summary>
    private static double ListMaxScroll(int count, double rowHeight, double gap)
        => count <= 0 ? 0 : Math.Max(0, (count * (rowHeight + gap)) - gap - PageMetricsLayout.BodyHeight);

    /// <summary>A page rect with the expanded content size (vertical extents do not depend on x/shell).</summary>
    private static SkiaSharp.SKRect MetricPageRect() => new(
        0, (float)IslandMetrics.ContentTop,
        (float)(IslandMetrics.ContentLeft + IslandMetrics.ContentWidth),
        (float)(IslandMetrics.ContentTop + IslandMetrics.ContentHeight));

    private double MaxScrollForPage(int page, ExpandedPageContent? source = null)
    {
        source ??= _pages;
        var metrics = FontScaleMetrics.From(_settings.Current.FontScale);
        var kind = source.PageSlotAt(page)?.Kind;
        return kind switch
        {
            PageSlotKind.Home => PageMetricsLayout.MaxScroll(MetricPageRect(), PageSlotKind.Home, metrics),
            PageSlotKind.Media => PageMetricsLayout.MaxScroll(MetricPageRect(), PageSlotKind.Media, metrics),
            PageSlotKind.Notifications => ListMaxScroll(source.Notifications.Items?.Count ?? 0, PageMetricsLayout.NotificationRowHeight(metrics), 4),
            PageSlotKind.Clipboard => ListMaxScroll(source.Clipboard.Items?.Count ?? 0, PageMetricsLayout.ClipboardRowHeight(metrics), 6),
            PageSlotKind.Agent => ListMaxScroll(source.Agent.Items?.Count ?? 0, PageMetricsLayout.AgentRowHeight(metrics), 6),
            PageSlotKind.Widgets => PageMetricsLayout.MaxScroll(MetricPageRect(), PageSlotKind.Widgets, metrics, source.Widgets.Tasks?.Count ?? 0),
            _ => 0,
        };
    }

    // ------------------------------------------------------------------ render state

    /// <summary>Notes when the pill's lyric line changed so the next frames can fade it in.</summary>
    private void TrackCompactLyric()
    {
        var lyric = _compact.Model.ShowLyrics ? _compact.Model.LyricLine : null;
        if (string.Equals(lyric, _compactLyricText, StringComparison.Ordinal))
        {
            return;
        }

        _compactLyricText = lyric;
        _compactLyricChangedAtMs = lyric is null ? -1 : NowMs();
    }

    /// <summary>
    /// Keeps the karaoke sweep in step with the extrapolated position: anchors the clock on every content
    /// sample (this is where seeks/loops show up as a jump) and remembers the per-word timeline.
    /// </summary>
    /// <summary>Moves the karaoke clock to a freshly requested seek position (0..1 of the track).</summary>
    private void ReanchorKaraokeAfterSeek(double fraction)
    {
        if (_karaokeTrack is not { } track || !double.IsFinite(fraction) || fraction <= 0)
        {
            return;
        }

        if (_karaokeClock.LimitMs <= 0)
        {
            return;
        }

        var now = NowMs();
        var targetMs = Math.Clamp(fraction, 0, 1) * _karaokeClock.LimitMs;
        _karaokeClock.Anchor(targetMs, now, playing: _karaokeClock.IsAdvancing);
        _karaokeHighlight = track.Progress(targetMs);
        InvalidateKaraokeDamage();
        FrameRequested?.Invoke();
    }

    private void SyncKaraoke(IslandContent content)
    {
        var media = content.Media;
        var now = NowMs();
        if (media?.Karaoke is not { } track)
        {
            _karaokeTrack = null;
            _karaokeHighlight = -1;
            _karaokeActive = false;
            _karaokeClock.Reset();
            return;
        }

        var sameLine = _karaokeTrack is { } previous
            && previous.LineStartMs == track.LineStartMs
            && previous.TotalChars == track.TotalChars
            && previous.Spans.Length == track.Spans.Length;
        _karaokeTrack = track;
        _karaokeActive = true;

        // The user-tunable lead/lag now lives in the MEDIA PLUGIN's settings file (media.lyricOffsetMs),
        // not in the island pane; the legacy app setting stays as a fallback for old profiles.
        var offsetMs = _mediaPill.LyricOffsetMs is var pluginOffset and not 0
            ? pluginOffset
            : Math.Clamp(_settings.Current.LyricOffsetMs, -800, 800);
        media = media with { PositionMs = Math.Max(0, media.PositionMs + offsetMs) };

        _karaokeClock.SetLimit(media.DurationMs);
        // A sample that lands BEHIND our extrapolation is the player's own clock being stale (KuGou
        // rewrites its position a few seconds late). Snapping on it rewound the sweep by up to ~5 s and
        // the line appeared to lag the singing (user report 2026-10-21). Ignore such a sample while the
        // line and the play state are unchanged; a real backwards seek is far larger than this window.
        // A sample that CHANGED since the previous poll is live and must always be able to correct the
        // clock (that is the auto-correction the user asked for). Only a sample that repeats — i.e. the
        // player's clock is frozen — is ignored, so a stale value can no longer drag the sweep backwards.
        var sampleIsLive = Math.Abs(media.PositionMs - _lastSampledPositionMs) > 1;
        _lastSampledPositionMs = media.PositionMs;
        // A backward sample is never allowed to rewind the sweep any more. KuGou's clock has one-second
        // resolution plus transport latency, so every poll lands 0.3-1 s behind our extrapolation; snapping
        // to it rewound the highlight and the next frames pushed it forward again — the "flicker" the user
        // reported (2026-10-21). Only a real seek (>2.5 s) moves the clock backwards.
        var staleBackward = false;
        if (staleBackward)
        {
            // keep the anchor, keep extrapolating
        }
        else if (!sameLine || !_karaokeClock.HasAnchor
                 || Math.Abs(media.PositionMs - _karaokeClock.PositionAt(now)) > SeekThresholdMs)
        {
            // New line, first sample or a seek: snap instead of easing (a seek must not fast-forward
            // through the words between the old and the new position).
            _karaokeClock.Anchor(media.PositionMs, now, media.IsPlaying);
            _karaokeHighlight = track.Progress(media.PositionMs);
        }
        else
        {
            // Half a second off is exactly the range that used to hang forever: too small for the seek
            // path, too large to ignore. Converge by bending the playback rate a few percent — the picture
            // keeps moving forwards, so nothing flickers, and the offset is gone within ~20 s (user
            // request 2026-10-22: "1-2 s off is what breaks the sync").
            var delta = media.PositionMs - _karaokeClock.PositionAt(now);
            if (Math.Abs(delta) > 250)
            {
                _karaokeClock.Nudge(media.PositionMs, now);
            }
        }

        if (!media.IsPlaying)
        {
            // Paused: freeze the sweep on the frame the pause arrives. A later sample must not push the
            // phase forward again (the "keeps sweeping then snaps back" bug).
            _karaokeClock.SetPlaying(false, now);
            _karaokeHighlight = track.Progress(_karaokeClock.PositionAt(now));
        }
        else if (_karaokeClock.HasAnchor && !_karaokeClock.IsAdvancing)
        {
            // Resume: nothing in the Nudge/Anchor paths above un-pauses the clock (Nudge keeps the
            // playing flag), so the sweep stayed frozen while the track played on, and the accumulating
            // gap only cleared when it crossed the 2.5 s seek threshold and snapped — pause → resume →
            // pause made the lyric visibly jump every time (user report 2026-10-22). SetPlaying(true)
            // folds the frozen position into the anchor so playback continues from where it stopped.
            _karaokeClock.SetPlaying(true, now);
        }
    }

    /// <summary>
    /// Paints the karaoke sweep for the current frame. Returns true when the sweep changed enough to need a
    /// repaint (1/255 of the line, i.e. sub-pixel on a normal-width line).
    /// </summary>
    private int _lastPositionSecond = -1;

    /// <summary>
    /// Rewrites the elapsed time from the same interpolated clock the karaoke sweep uses.
    /// </summary>
    /// <remarks>
    /// The text used to be a snapshot string rebuilt once per media poll, so it lagged up to a second
    /// behind the sweep and could disagree with it by the whole source latency. One clock for both means
    /// "what the number says" and "where the highlight is" can never drift apart (user report 2026-10-21).
    /// </remarks>
    private void UpdatePositionText()
    {
        if (!_karaokeClock.HasAnchor || _pages.Media.PositionText is null)
        {
            return;
        }

        var positionMs = _karaokeClock.PositionAt(NowMs());
        var second = (int)(positionMs / 1000);
        if (second == _lastPositionSecond)
        {
            return;
        }

        _lastPositionSecond = second;
        var text = (int)(positionMs / 60000) + ":" + ((int)(positionMs / 1000) % 60).ToString("00");
        if (string.Equals(text, _pages.Media.PositionText, StringComparison.Ordinal))
        {
            return;
        }

        _pages = _pages with { Media = _pages.Media with { PositionText = text } };
        InvalidateFull();
        FrameRequested?.Invoke();
    }

    private bool ApplyKaraokeSweep()
    {
        if (_karaokeTrack is not { } track || !_karaokeActive)
        {
            return false;
        }

        if (!_karaokeClock.IsAdvancing)
        {
            // Paused: the phase is frozen, so there is nothing to advance and no frame to request.
            return false;
        }

        var position = _karaokeClock.PositionAt(NowMs());
        var highlight = track.Progress(position);
        // The sweep is monotonic INSIDE one lyric line: a lower value can only come from a lagging sample
        // or a clock wobble, and erasing painted characters is exactly the flicker the user reported
        // (2026-10-21, "same line, not the line change"). The real resets — new line, seek, new track —
        // arrive with a different LineStartMs and are taken as they are.
        if (_karaokeSweepLineStartMs != track.LineStartMs)
        {
            _karaokeSweepLineStartMs = track.LineStartMs;
            _karaokeHighlight = highlight;
        }
        else if (highlight < _karaokeHighlight)
        {
            highlight = _karaokeHighlight;
        }

        var finished = track.IsFinished(position);
        _karaokeActive = !finished;
        // A content rebuild (new snapshot, lyric refresh, track metadata) replaces the page model with
        // the freshly resolved highlight, which is -1 for one frame when the lyric source flaps. Without
        // this the page stayed on that plain value until the sweep moved by 1/255 — a visible white flash
        // of the whole line. Push our own value back whenever the page disagrees.
        var pageOutOfSync = _pages.Media.LyricLine is not null
                            && Math.Abs(_pages.Media.LyricHighlight - highlight) > KaraokeHighlightTolerance;
        var compactOutOfSync = _compact.Model is { ShowLyrics: true } compactModel
                               && Math.Abs(compactModel.LyricHighlight - highlight) > KaraokeHighlightTolerance;
        if (Math.Abs(highlight - _karaokeHighlight) < KaraokeHighlightTolerance && !finished
            && !pageOutOfSync && !compactOutOfSync)
        {
            return false;
        }

        _karaokeHighlight = highlight;
        if (_compact.Model is { ShowLyrics: true } compact)
        {
            _compact = _compact with { Model = compact with { LyricHighlight = highlight } };
        }

        if (_pages.Media.LyricLine is not null)
        {
            _pages = _pages with { Media = _pages.Media with { LyricHighlight = highlight } };
        }

        return true;
    }

    /// <summary>1/255: the smallest sweep change that can move a lit pixel on a normal-width line.</summary>
    private const double KaraokeHighlightTolerance = 1.0 / 255.0;

    /// <summary>How far behind our extrapolation a sample may sit before it is treated as a real seek.</summary>
    private const double StaleSampleWindowMs = 400;

    /// <summary>Deviation that counts as a real seek (drag / track jump) instead of clock jitter.</summary>
    private const double SeekThresholdMs = 2500;

    /// <summary>Raw position of the previous sample, used to tell a live clock from a frozen one.</summary>
    private double _lastSampledPositionMs = double.NaN;

    /// <summary>Lyric line currently painted (used to detect a line change and start the transition window).</summary>
    private string? _lyricLinePainted;

    /// <summary>End of the lyric line-change transition window (island clock, ms).</summary>
    private double _lyricTransitionUntilMs = double.NegativeInfinity;

    /// <summary>
    /// Anchors the local progress clock when a media sample arrives. Repeated samples with the same
    /// position (fallback sources whose time text occasionally stops updating) must not re-anchor:
    /// that would freeze the extrapolated seek bar at the stale sample.
    /// </summary>
    private void SyncMediaProgress(IslandContent content)
    {
        var media = content.Media;
        if (media is null || !media.HasTimeline || media.DurationMs <= 0)
        {
            _mediaClock.Reset();
            _mediaDurationMs = 0;
            _mediaLastSampleMs = double.NaN;
            _mediaTrackIdentity = null;
            return;
        }

        var now = NowMs();
        var identity = string.Join('|', media.Title, media.Artist, media.ArtworkKey);
        var trackChanged = !string.Equals(identity, _mediaTrackIdentity, StringComparison.Ordinal);
        var durationChanged = Math.Abs(media.DurationMs - _mediaDurationMs) > 1;
        var pastSample = !double.IsNaN(_mediaLastSampleMs);
        var positionChanged = pastSample && Math.Abs(media.PositionMs - _mediaLastSampleMs) > 0.5;
        if (!_mediaClock.HasAnchor || trackChanged || durationChanged || positionChanged)
        {
            _mediaClock.Anchor(media.PositionMs, now, media.IsPlaying);
        }
        else
        {
            _mediaClock.SetPlaying(media.IsPlaying, now);
        }

        _mediaTrackIdentity = identity;
        _mediaDurationMs = media.DurationMs;
        _mediaLastSampleMs = media.PositionMs;
    }

    /// <summary>
    /// Advances the seek bar/time text from the local clock. Returns true when the models changed and
    /// the media page (or compact progress strip) needs a dirty frame.
    /// </summary>
    private bool ApplyMediaProgress()
    {
        if (_mediaDurationMs <= 0 || !_mediaClock.HasAnchor)
        {
            return false;
        }

        var position = Math.Clamp(_mediaClock.PositionAt(NowMs()), 0, _mediaDurationMs);
        var percent = position / _mediaDurationMs * 100;
        var positionText = FormatMediaTime(position);
        // The length arrives later than the track (the player's clock text, then the catalogue), so it has
        // to be pushed into the page model here — otherwise the island keeps rendering the placeholder
        // length it was first built with (user report 2026-10-22: "灵动岛 3:53, 酷狗 3:57").
        var durationText = FormatMediaTime(_mediaDurationMs);
        var changed = false;

        if (_compact.Kind == ActivityKind.Media
            && Math.Abs(_compact.Model.ProgressPercent - percent) > 0.01)
        {
            _compact = _compact with { Model = _compact.Model with { ProgressPercent = percent } };
            changed = true;
        }

        if (_pages.Media.HasSession
            && (Math.Abs(_pages.Media.ProgressPercent - percent) > 0.01
                || !string.Equals(_pages.Media.PositionText, positionText, StringComparison.Ordinal)
                || !string.Equals(_pages.Media.DurationText, durationText, StringComparison.Ordinal)))
        {
            _pages = _pages with
            {
                Media = _pages.Media with
                {
                    ProgressPercent = percent,
                    PositionText = positionText,
                    DurationText = durationText,
                },
            };
            changed = true;
        }

        return changed;
    }

    private static string FormatMediaTime(double milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }

    private VisualRect MediaProgressDamage()
    {
        var shell = IslandMetrics.ShellRect(_renderState);
        if (_renderState.Form == ShellForm.Compact)
        {
            return new VisualRect(shell.Left, shell.Bottom - 4, shell.Width, 4);
        }

        return ContentDamage();
    }

    /// <summary>
    /// Applies the lyric fade-in to the compact model and keeps the text column repainting while the
    /// line is still fading. Returns true when the pill's lyric text needs a repaint this frame.
    /// </summary>
    private bool ApplyCompactLyricFade()
    {
        if (!_compact.Model.ShowLyrics || _compact.Model.LyricLine is null)
        {
            return false;
        }

        var fade = 1f;
        if (_compactLyricChangedAtMs >= 0 && !_motion.ReducedMotion)
        {
            var elapsed = NowMs() - _compactLyricChangedAtMs;
            fade = (float)Math.Clamp(elapsed / CompactLyricFadeMs, 0, 1);
        }

        if (Math.Abs(_compact.Model.LyricFade - fade) < 0.01f)
        {
            return fade < 1f;
        }

        _compact = _compact with { Model = _compact.Model with { LyricFade = fade } };
        return true;
    }

    private void BuildRenderState()
    {
        SyncRailMotion(0);
        UpdatePositionText();

        if (ApplyKaraokeSweep() && _hasRenderState)
        {
            // The sweep only repaints the lyric text, never the whole island.
            InvalidateKaraokeDamage();
        }

        if (ApplyCompactLyricFade() && _hasRenderState)
        {
            // The text column sits outside the wave damage box, so repaint it explicitly while fading.
            InvalidateDirty(CompactLyricDamage());
        }

        var mode = _settings.Current.IslandMode;
        _motion.PanelRadius = _settings.Current.PanelRadius;
        var form = ResolveVisualForm();
        var expanded = form == ShellForm.Expanded;
        var shellRadii = !expanded && _compact.Kind == ActivityKind.Notification
            ? CornerRadii.Uniform(LayoutTokens.NotificationCornerRadius)
            : _motion.ShellRadii;
        _renderState = new IslandRenderState
        {
            Mode = mode,
            Form = form,
            ShellWidth = _motion.Width,
            ShellHeight = _motion.Height,
            ShellRadii = shellRadii,
            ShellMorphing = !_motion.IsAtRest,
            ShellOrigin = _motion.ShellOrigin,
            Opacity = _motion.Opacity * _trackTransitionOpacity,
            PressScale = _motion.PressScale * _trackTransitionScale,
            ContentOpacity = expanded ? _motion.ExpandedContentOpacity : _motion.CompactContentOpacity,
            // Prototype2 renders the compact layer and the expanded layer as two siblings with their own
            // CSS transitions; the chrome (title/collapse) fades independently. All three come from the
            // motion driver's CSS-equivalent tweens so the host never has to re-derive them.
            CompactLayerOpacity = _motion.CompactContentOpacity,
            ExpandedLayerOpacity = _motion.ExpandedContentOpacity,
            ChromeOpacity = _motion.ChromeOpacity,
            NudgeOffsetY = _motion.NudgeOffsetY,
            PressedControl = _pressedControl,
            PressedPage = _pressedPage,
            PressedIndex = _pressedIndex,
            HoverControl = _hoverControl,
            HoverPage = _hoverPage,
            HasPointer = _pointerInside,
            PointerX = _pointerInside ? _pointerX : double.NaN,
            PointerY = _pointerInside ? _pointerY : double.NaN,
            GhostCount = expanded ? 0 : _motion.Target.GhostCount,
            OverflowCount = expanded ? 0 : _schedulerState.BadgeCount,
            SplitIslets = expanded && _motion.SplitProgress <= 0.02 ? [] : BuildSplitIslets(),
            SplitProgress = _motion.SplitProgress,
            SplitIsletHeight = _motion.Target.CompactSize.Height,
            StackSpin = _motion.StackSpin,
            FontScale = _settings.Current.FontScale,
            Pager = PagerRenderState.FromPosition(
                _pager.PagePosition, _pages.PageCount, _pager.IsDragging, _pager.IsAnimating),
            Compact = _compact,
            SecondaryCompact = null,
            Pages = _pages,
            Theme = _theme,
            Archipelago = _options.EnableArchipelago
                ? _archipelago.ToState(_pager.PagePosition, _pages.PageCount) with
                {
                    Rail = _railMotion.Values,
                    RailHover = _railHover,
                    RailPressed = _railPressed,
                    RailFocus = _railFocus,
                }
                : ArchipelagoState.HiddenState,
            ReducedMotion = _motion.ReducedMotion,
            MotionPhase = _motionPhase,
            AgentAnimationSeconds = _agentAnimationSeconds,
            // A focused rail glass paints its own prototype accent outline in the strip renderer, so
            // the generic page-control ring stays off for those slots.
            FocusRing = _railFocus == MiniRailSlot.None ? _accessibilityFocus : null,
        };
        if (_renderState.SplitIslets.Length > 0)
        {
            var isletRects = IslandMetrics.SplitIsletRects(_renderState);
            PinDiagnostics.Log(
                $"split-rects shell={IslandMetrics.ShellRect(_renderState)} islets=[{string.Join(",", isletRects.Select(r => r.ToString()))}]");
        }

        _hasRenderState = true;

        // The agent page can become visible without a content change (page switch / expand): start
        // the 60 fps frame loop here too, and remember that we asked so the idle path stays quiet.
        if (IsAgentAnimating)
        {
            if (!_agentLoopRequested)
            {
                _agentLoopRequested = true;
                FrameRequested?.Invoke();
            }
        }
        else
        {
            _agentLoopRequested = false;
        }
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
        if (!_hasRenderState || _motion.ReducedMotion || _renderState.Form == ShellForm.Expanded
            || _renderState.Form == ShellForm.Hidden)
        {
            // A hidden island paints nothing: keep requesting 30 fps wave frames for an invisible
            // surface was pure idle CPU burn (and contradicted the "idle means zero frames" contract).
            return false;
        }

        // A design-time/fallback media model can report IsActive without a real timeline. Without a
        // duration the wave is a frozen decorative loop that wakes the host forever; only animate it
        // when the media clock has a real timeline.
        if (_compact.Kind == ActivityKind.Media && _mediaDurationMs <= 0)
        {
            return false;
        }

        return (_compact.Kind == ActivityKind.Media && _compact.Model.IsActive)
            || (_compact.Kind == ActivityKind.Notification && _compact.Model.ShowsAudioWave);
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

        if (CurrentPageIs(PageSlotKind.Media))
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
            if (_compact.Model is { ShowLyrics: true })
            {
                // The karaoke sweep and the lyric text sit outside the wave box: damage them too, or a
                // line whose highlight advances would look frozen.
                InvalidateDirty(CompactLyricDamage());
            }

            return;
        }

        InvalidateFull();
    }

    /// <summary>Damage of whatever the karaoke sweep paints (compact lyric column or the media lyric card).</summary>
    /// <summary>
    /// Starts the transition window when the painted lyric line changes, so the host keeps requesting frames
    /// for its duration instead of waiting for the next media snapshot.
    /// </summary>
    private void ObserveLyricLineChange()
    {
        // Only the expanded media page paints the lyric band transition; the compact pill has its own fade.
        if (!_hasRenderState || _renderState.Form != ShellForm.Expanded || !CurrentPageIs(PageSlotKind.Media))
        {
            return;
        }

        var line = _pages.Media.LyricLine;
        if (string.Equals(line, _lyricLinePainted, StringComparison.Ordinal))
        {
            return;
        }

        // Near a line boundary the sampled position wobbles across it, so the selector hands line N+1 and
        // then line N again. Every flip restarted the fade and the WHOLE line blinked (user report
        // 2026-10-22). A flip-back within a second is ignored and the committed line is restored.
        var nowForLineMs = NowMs();
        var flipBack = _lyricLinePrevious is { } previousLine
            && string.Equals(line, previousLine, StringComparison.Ordinal)
            && nowForLineMs - _lyricLineChangedAtMs < 900;
        if (flipBack)
        {
            if (!string.IsNullOrEmpty(_lyricLineCommitted))
            {
                _pages = _pages with { Media = _pages.Media with { LyricLine = _lyricLineCommitted } };
                if (_compact.Model is { ShowLyrics: true } compactModel)
                {
                    _compact = _compact with { Model = compactModel with { LyricLine = _lyricLineCommitted } };
                }
            }

            return;
        }

        _lyricLinePrevious = _lyricLineCommitted;
        _lyricLineCommitted = line;
        _lyricLineChangedAtMs = nowForLineMs;

        var firstObservation = _lyricLinePainted is null;
        _lyricLinePainted = line;
        if (firstObservation)
        {
            // The first painted line is not a "change": do not start an animation for it.
            return;
        }

        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        // Slightly longer than the fade so the last frame (opacity 1, old line gone) is still requested.
        _lyricTransitionUntilMs = NowMs()
            + Math.Max(FluidBar.Island.LyricTransition.OutMs, FluidBar.Island.LyricTransition.InMs) + 32;
    }

    private void InvalidateKaraokeDamage()
    {
        if (!_hasRenderState)
        {
            InvalidateFull();
            return;
        }

        if (_renderState.Form == ShellForm.Expanded)
        {
            if (CurrentPageIs(PageSlotKind.Media))
            {
                var page = IslandMetrics.PageRect(_renderState, 1);
                // Same geometry the renderer paints (font scale + squeeze applied); the old PageLayout
                // constant was a fixed 400×46 box that did not match what was drawn.
                var lyric = PageMetricsLayout.MediaLyricCard(page,
                    FontScaleMetrics.From(_settings.Current.FontScale));
                InvalidateDirty(new VisualRect(lyric.Left, lyric.Top, lyric.Width, lyric.Height).Inflate(2));
            }

            return;
        }

        if (_compact.Kind == ActivityKind.Media && _compact.Model is { ShowLyrics: true })
        {
            InvalidateDirty(CompactLyricDamage());
        }
    }

    private VisualRect ContentDamage()
    {
        if (!_hasRenderState || _renderState.Form != ShellForm.Expanded)
        {
            // Page content is not painted by the compact/peek surface. Returning the expanded
            // 400x264 rect from a compact state woke the host for an off-screen region on every
            // background poll and kept the frame loop alive while the pill was idle.
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

        if (!_renderState.Archipelago.Visible)
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
        if (_compact.Kind == ActivityKind.Notification)
        {
            // The notification card paints its spectrum at the far right, not in the media text lane.
            var scale = (float)_renderState.FontScale;
            return new VisualRect(shell.Right - (45f * scale), shell.Top + 3,
                48f * scale, Math.Max(24f, shell.Height - 6));
        }

        // Same rectangle the compact media renderer paints the wave in (it tracks the adaptive width).
        var wave = CompactMediaLayout.WaveRect((float)shell.Width, (float)shell.Height);
        return new VisualRect(
            shell.Left + wave.Left - 3, shell.Top + wave.Top - 3, wave.Width + 6, wave.Height + 6);
    }

    /// <summary>Damage box of the compact media text column (the lyric line when lyric mode is on).</summary>
    private VisualRect CompactLyricDamage()
    {
        if (!_hasRenderState)
        {
            return VisualRect.Empty;
        }

        var shell = IslandMetrics.ShellRect(_renderState);
        var left = shell.Left + CompactMediaLayout.TextLeft;
        var right = Math.Max(left + 1, shell.Left + CompactMediaLayout.TextRight((float)shell.Width));
        return new VisualRect(left - 1, shell.Top + 2, right - left + 2, shell.Height - 4);
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

        // Approach band: one rectangle test per move event (no region rebuild, no hit-test pass).
        var hoverBand = ArchipelagoHoverBand.For(_renderState);
        _pointerInHoverBand = _pointerInside
            && State.Kind is IslandStateKind.Expanded or IslandStateKind.Closing
            && !hoverBand.IsEmpty
            && hoverBand.Contains(_pointerX, _pointerY);
    }

    /// <summary>
    /// Advances the prototype2 preview-rail springs. The rail snaps whenever it is revealed or the
    /// motion setting is reduced (prototype <c>showRail</c> + <c>snapRailSprings</c>); otherwise the
    /// targets follow the pager's committed page and each frame integrates one spring step.
    /// </summary>
    /// <param name="deltaSeconds">Frame delta in seconds (0 = targets only, no integration).</param>
    /// <returns>Whether the rail is still animating.</returns>
    private bool SyncRailMotion(double deltaSeconds)
    {
        var page = RailPage();
        var targets = MiniRailTargetsFactory.Create(page, _pages, _settings.Current.FontScale);
        var visible = _archipelago.IsVisible;
        var reduced = _motion.ReducedMotion;
        if (!visible || !_railWasVisible || reduced)
        {
            _railMotion.Snap(targets);
            _railWasVisible = visible;
            return false;
        }

        _railMotion.Retarget(targets);
        _railWasVisible = visible;
        return deltaSeconds > 0 && _railMotion.Step(deltaSeconds);
    }

    /// <summary>Page the preview rail currently represents (the drag position wins while dragging).</summary>
    private int RailPage() => _pager.IsDragging ? MiniRailLayout.PageAt(_pager.PagePosition, _pages.PageCount) : _pager.SelectedIndex;

    /// <summary>Rail slot that would navigate to a page from the current rail page.</summary>
    /// <param name="page">Page index of the hit rail slot.</param>
    private MiniRailSlot SlotForPage(int page)
    {
        var current = RailPage();
        return page < current
            ? MiniRailSlot.Previous
            : page > current ? MiniRailSlot.Next : MiniRailSlot.None;
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
        _archipelago.Update(now, expanded, _pointerInHotZone, _pointerOnStrip, dwell, _pager.IsDragging, _pointerInHoverBand);
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


