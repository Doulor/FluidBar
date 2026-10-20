using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using FluidBar.App.Diagnostics;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.State;
using FluidBar.Core.Activities;
using FluidBar.Core.Diagnostics;
using FluidBar.Core.Events;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Island;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Hosting;
using FluidBar.Platform.Windows.Monitors;
using FluidBar.Platform.Windows.Shell;
using FluidBar.Platform.Windows.Threading;
using SkiaSharp;
using CoreMonitorInfo = FluidBar.Core.Layout.MonitorInfo;
using ActivityKind = FluidBar.Core.Activities.ActivityKind;

namespace FluidBar.App.Hosting.Skia;

/// <summary>Frame/commit counters exposed by <see cref="SkiaIslandHost"/>.</summary>
/// <param name="Frames">Frames rendered (all windows).</param>
/// <param name="DirtyFrames">Frames rendered through the dirty-rect path.</param>
/// <param name="LastRenderMilliseconds">Wall time of the last Skia render.</param>
/// <param name="TotalRenderMilliseconds">Accumulated Skia render time.</param>
/// <param name="Commits">Successful <c>UpdateLayeredWindow</c> commits.</param>
/// <param name="CommitFailures">Rejected commits.</param>
/// <param name="TotalUploadMicroseconds">Accumulated Win32 upload time.</param>
/// <param name="Windows">Layered hosts (one per target monitor).</param>
/// <param name="Idle">Whether the host is currently not requesting frames.</param>
public readonly record struct SkiaIslandHostStats(
    long Frames,
    long DirtyFrames,
    double LastRenderMilliseconds,
    double TotalRenderMilliseconds,
    long Commits,
    long CommitFailures,
    double TotalUploadMicroseconds,
    int Windows,
    bool Idle)
{
    /// <summary>Mean Skia render time per frame in milliseconds.</summary>
    public double MeanRenderMilliseconds => Frames == 0 ? 0 : TotalRenderMilliseconds / Frames;

    /// <summary>Mean Win32 upload time per commit in microseconds.</summary>
    public double MeanUploadMicroseconds => Commits == 0 ? 0 : TotalUploadMicroseconds / Commits;
}

/// <summary>
/// Production Skia island host: one Win32 per-pixel-alpha layered window per target monitor (no
/// Avalonia window), Core state pipeline from <see cref="SkiaIslandController"/>, a UI-thread frame
/// loop that renders with <see cref="IslandRenderer"/> straight into the layered DIB, and pointer/wheel
/// input mapped onto the Core pager/strip policies.
/// <para>
/// The window is created at the mode's fixed surface size (488×384 float/flush, 488×444 dock) and is
/// never resized or moved during animation; only mode/anchor/DPI changes re-place it through Core's
/// <see cref="WindowPlacementConverter"/>. Monitor add/remove/display changes are reconciled on the
/// UI thread by <see cref="SkiaMonitorPlanner"/> (create/dispose one window per target monitor), and
/// <c>WM_DPICHANGED</c> rebuilds the surface at <c>ceil(DIP × scale)</c> and re-places. Idle means
/// zero frames: the clock stops as soon as the springs, pager, strip and dynamic content are at rest.
/// </para>
/// <summary>
/// Per-frame probe for the compact karaoke sweep (diagnostics only): records the frame interval, the render
/// cost, the allocation delta and the invalidation kind/area so a stutter can be attributed to layout
/// rebuilds, full-frame repaints or allocation churn instead of guessed.
/// </summary>
internal static class IslandFrameProbe
{
    private static readonly string? DirectoryPath = Environment.GetEnvironmentVariable("FLUIDBAR_FRAME_PROBE");
    private static readonly object Gate = new();
    private static long _lastTicks;

    /// <summary>Whether the probe is enabled.</summary>
    public static bool Enabled => !string.IsNullOrEmpty(DirectoryPath);

    /// <summary>Appends one frame record.</summary>
    /// <param name="renderMs">Time spent rendering this frame.</param>
    /// <param name="allocBytes">Managed bytes allocated while rendering this frame.</param>
    /// <param name="fullFrame">Whether the frame repainted the whole surface.</param>
    /// <param name="damageW">Damage width in DIP (0 for a full frame).</param>
    /// <param name="damageH">Damage height in DIP (0 for a full frame).</param>
    public static void Record(double renderMs, long allocBytes, bool fullFrame, double damageW, double damageH)
    {
        if (!Enabled)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastTicks);
        Interlocked.Exchange(ref _lastTicks, now);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath!);
                File.AppendAllText(Path.Combine(DirectoryPath!, "frames-probe.jsonl"),
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        at = DateTimeOffset.UtcNow,
                        intervalMs = last == 0 ? 0 : Math.Round((now - last) * 1000.0 / Stopwatch.Frequency, 3),
                        renderMs = Math.Round(renderMs, 3),
                        allocBytes,
                        fullFrame,
                        damageW = Math.Round(damageW, 1),
                        damageH = Math.Round(damageH, 1),
                    }) + "\n");
            }
        }
        catch (Exception)
        {
        }
    }
}


/// </summary>
public sealed class SkiaIslandHost : IDisposable, IFrameStatsSource
{
    private readonly SkiaIslandHostOptions _options;
    private readonly SkiaFrameStatsRecorder _frameStats = new();
    private readonly IAppLogger _logger;
    private readonly IPlatformLogger _platformLogger;
    private readonly PlatformCounters _platformCounters;
    private readonly TimeProvider _time;
    private readonly SkiaHostDispatcher _dispatcher;
    private readonly SkiaIslandController _controller;
    private readonly SkiaIslandUiaSource _uiaSource;
    private readonly IslandRenderer _renderer;
    private readonly IslandWindowInterop _interop;
    private readonly IFrameClock _frameClock;
    private readonly UiThreadTicker _deadlineTicker;
    private readonly SkiaHostPerfProbe? _perfProbe;
    private UiThreadTicker? _perfTicker;
    private long _probeAdvanceAlloc;
    private long _probeRenderAllocStart;
    private readonly List<WindowEntry> _windows = [];

    private const int FollowMousePollMs = 250;

    private PlatformMessageHost? _messageHost;
    private SystemThemeMonitor? _themeMonitor;
    private PlatformMessageHost? _mouseHookHost;
    private IslandMouseHook? _mouseHook;
    private MonitorService? _monitorService;
    private RegionSignature _regionSignature;

    // Reused buffer for the global-hook rectangles: they are rebuilt every frame while the preview
    // rail springs, so the animated pills stay inside the click-consuming envelope without allocating.
    private readonly List<IslandMouseHook.Rect> _hookRects = [];

    // Watch-only rectangles (hover band below the shell): movement is forwarded so the rail can reveal
    // on approach, clicks stay with the desktop. Rebuilt with the hook rectangles, never per move.
    private readonly List<IslandMouseHook.Rect> _watchRects = [];
    private string? _primaryMonitorId;
    private string? _cursorMonitorId;
    private ITimer? _followMouseTimer;
    private ITimer? _idleTrimTimer;
    private GCLatencyMode? _savedLatencyMode;
    private bool _timerResolutionRaised;
    private int _idleTrimCount;
    private long _idleTrimHeapBefore;
    private long _idleTrimHeapAfter;
    private PendingDrop? _pendingDrop;
    private bool _started;
    private bool _disposed;
    private bool _rendering;
    private bool _hookDragActive;
    private float _hookDragX;
    private float _hookDragY;
    private long _frames;
    private long _dirtyFrames;
    private long _commits;
    private long _commitFailures;
    private double _lastRenderMs;
    private double _totalRenderMs;
    private double _totalUploadMicroseconds;

    /// <summary>Creates the host (call <see cref="StartAsync"/> to create windows and begin).</summary>
    /// <param name="options">Composition options.</param>
    public SkiaIslandHost(SkiaIslandHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = options.Logger ?? new TraceAppLogger();
        _time = options.TimeProvider ?? TimeProvider.System;
        _platformLogger = new AppLoggerPlatformAdapter(_logger);
        _platformCounters = new PlatformCounters();
        _dispatcher = new SkiaHostDispatcher();
        _interop = new IslandWindowInterop(_platformLogger, _platformCounters, _time);
        _controller = new SkiaIslandController(options, _dispatcher);
        _uiaSource = new SkiaIslandUiaSource(_controller, _options.Localizer, _dispatcher);
        _renderer = new IslandRenderer(IslandTheme.Dark);
        _frameClock = options.FrameClockFactory?.Invoke() ?? new SkiaFrameClock(_time);
        _deadlineTicker = new UiThreadTicker(_time);
        if (SkiaHostPerfProbe.IsEnabled)
        {
            _perfProbe = new SkiaHostPerfProbe(_logger, _renderer,
                () => _windows.Count, SurfaceBytes, _time);
        }

        _controller.SettingsRequested += () => SettingsRequested?.Invoke();
        _controller.MarketRequested += () => MarketRequested?.Invoke();
        _controller.MediaCommandRequested += command => MediaCommandRequested?.Invoke(command);
        _controller.PluginPageCommandRequested += request => PluginPageCommandRequested?.Invoke(request);
        _controller.PageChooserRequested += () => PageChooserRequested?.Invoke();
        _controller.StateChanged += OnStateChanged;
        _controller.PlacementRequested += OnPlacementRequested;
        _controller.PlacementPersistRequested += OnPlacementPersistRequested;
        _controller.FrameRequested += EnsureFrameLoop;

        // The controller re-places on a DisplayOn change; the host additionally has to create or
        // destroy per-monitor windows. Subscribed after the controller so its handler runs second.
        _options.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>Raised when the settings window should open.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the marketplace should open.</summary>
    public event Action? MarketRequested;

    /// <summary>Raised when the user asks to execute a media transport command.</summary>
    public event Action<MediaTransportCommand>? MediaCommandRequested;

    /// <summary>Raised when a plugin page row/button is activated.</summary>
    public event Action<PluginPageCommandRequest>? PluginPageCommandRequested;

    /// <summary>Raised when the rail overflow ellipsis requests the App page selector.</summary>
    public event Action? PageChooserRequested;

    /// <summary>Horizontal page pager (diagnostics/page chooser).</summary>
    public SkiaPager Pager => _controller.Pager;

    /// <summary>Current ordered page slots (localized titles used by the page chooser).</summary>
    public IReadOnlyList<FluidBar.Island.PageSlot> PageSlots
        => _controller.HasRenderState ? _controller.RenderState.Pages.PageSlots : [];

    /// <summary>Current island state snapshot.</summary>
    public IslandState State => _controller.State;

    /// <summary>Current activity scheduler snapshot.</summary>
    public ActivitySchedulerState SchedulerState => _controller.SchedulerState;

    /// <summary>Live layered hosts (one per target monitor).</summary>
    public IReadOnlyList<LayeredWindowHost> Windows => _windows.Select(static entry => entry.Host).ToArray();

    /// <summary>Primary layered host (diagnostics/evidence), or null before start.</summary>
    public LayeredWindowHost? PrimaryWindow => _windows.Count > 0 ? _windows[0].Host : null;

    /// <summary>Latest frame-timing sample for the advanced diagnostics setting (spec §9.3).</summary>
    public FrameStatsSnapshot FrameStats => _frameStats.Snapshot();

    /// <summary>Number of completed idle trims (font cache purge + full GC while no frame is needed).</summary>
    public int IdleTrimCount => Volatile.Read(ref _idleTrimCount);

    /// <summary>Managed heap bytes before the first completed idle trim (0 until it runs).</summary>
    public long IdleTrimHeapBefore => Interlocked.Read(ref _idleTrimHeapBefore);

    /// <summary>Managed heap bytes after the most recent idle trim.</summary>
    public long IdleTrimHeapAfter => Interlocked.Read(ref _idleTrimHeapAfter);

    /// <summary>Frame/commit counters.</summary>
    public SkiaIslandHostStats Stats => new(
        Interlocked.Read(ref _frames),
        Interlocked.Read(ref _dirtyFrames),
        _lastRenderMs,
        _totalRenderMs,
        Interlocked.Read(ref _commits),
        Interlocked.Read(ref _commitFailures),
        _totalUploadMicroseconds,
        _windows.Count,
        !_frameClock.IsRunning);

    /// <summary>Publishes an activity event from any thread.</summary>
    /// <param name="islandEvent">Event to publish.</param>
    public bool Publish(in IslandEvent islandEvent) => _controller.Publish(islandEvent);

    /// <summary>Publishes a demo activity (developer runs/smoke tests).</summary>
    /// <param name="kind">Activity kind.</param>
    /// <param name="payload">Activity payload.</param>
    /// <param name="sequence">Per-key sequence.</param>
    /// <param name="source">Producer identity.</param>
    public bool PublishActivity(ActivityKind kind, ActivityPayload payload, long sequence, string source = "demo")
        => _controller.PublishActivity(kind, payload, sequence, source);

    /// <summary>Retracts a demo activity.</summary>
    /// <param name="kind">Activity kind.</param>
    /// <param name="sequence">Per-key sequence.</param>
    /// <param name="source">Producer identity.</param>
    public bool RetractActivity(ActivityKind kind, long sequence, string source = "demo")
        => _controller.RetractActivity(kind, sequence, source);

    /// <summary>Applies one Core reducer input.</summary>
    /// <param name="input">Reducer input.</param>
    public IslandTransition ApplyInput(in IslandInput input) => _controller.ApplyInput(input);

    /// <summary>Pumps the deadline timers synchronously (tests/smoke).</summary>
    public void PumpTimers()
    {
        _controller.PumpTimers();
        RenderNow();
        EnsureDeadlineTicker();
    }

    /// <summary>Simulates a primary click on the compact pill (smoke/tests).</summary>
    public void SimulatePrimaryClick()
    {
        var state = _controller.RenderState;
        var shell = IslandMetrics.ShellRect(state);
        _controller.HandlePointerPressed(shell.MidX, shell.MidY);

        // The click is committed on release (click-vs-drag discrimination), so a simulation must
        // deliver the matching release at the same point.
        _controller.HandlePointerReleased(shell.MidX, shell.MidY);
    }

    /// <summary>Simulates a click on split preview <paramref name="index"/> (smoke/tests).</summary>
    /// <returns>Whether the rendered state had that preview ball.</returns>
    public bool SimulateSplitIsletClick(int index)
    {
        var state = _controller.RenderState;
        var balls = IslandMetrics.SplitIsletRects(state);
        if (index < 0 || index >= balls.Length)
        {
            return false;
        }

        _controller.HandlePointerPressed(balls[index].MidX, balls[index].MidY);
        _controller.HandlePointerReleased(balls[index].MidX, balls[index].MidY);
        return true;
    }

    /// <summary>Simulates a click on the compact "+N" overflow chip (smoke/tests).</summary>
    /// <returns>Whether the rendered state had an overflow chip to click.</returns>
    public bool SimulateOverflowBadgeClick()
    {
        var state = _controller.RenderState;
        var badge = IslandMetrics.OverflowBadgeRect(state);
        if (badge.Width <= 0 || badge.Height <= 0)
        {
            return false;
        }

        _controller.HandlePointerPressed(badge.MidX, badge.MidY);
        _controller.HandlePointerReleased(badge.MidX, badge.MidY);
        return true;
    }

    /// <summary>Simulates Escape.</summary>
    public void SimulateEscape() => _controller.HandleEscape();

    /// <summary>Simulates the toggle hotkey.</summary>
    public void SimulateToggleHotkey() => _controller.HandleToggleHotkey();

    /// <summary>Simulates a page request (keyboard/gutter).</summary>
    /// <param name="page">Target page.</param>
    public void SimulatePageRequested(PageRef page) => _controller.HandlePageRequest(page);

    /// <summary>Simulates the pointer entering the compact pill.</summary>
    public void SimulatePointerEntered()
    {
        var shell = IslandMetrics.ShellRect(_controller.RenderState);
        _controller.HandlePointerMoved(shell.MidX, shell.MidY);
        EnsureDeadlineTicker();
    }

    /// <summary>Simulates the pointer leaving the island.</summary>
    public void SimulatePointerLeft()
    {
        _controller.HandlePointerLeft();
        EnsureDeadlineTicker();
    }

    /// <summary>Simulates one wheel step (smoke/visual-audit runs; no real input).</summary>
    /// <param name="deltaDip">Wheel delta in DIP (negative = down/forward).</param>
    public void SimulateWheel(double deltaDip)
    {
        _controller.HandleWheel(deltaDip, horizontal: false, shift: false);
        RenderNow();
        EnsureDeadlineTicker();
    }

    /// <summary>Opens the settings window (tray/hotkey seam; raises <see cref="SettingsRequested"/>).</summary>
    public void RequestSettings() => _controller.RequestSettings();

    /// <summary>Opens the marketplace (tray/hotkey seam; raises <see cref="MarketRequested"/>).</summary>
    public void RequestMarket() => _controller.RequestMarket();

    /// <summary>Renders any pending invalidation (or a full frame) immediately.</summary>
    /// <param name="forceFull">Whether to force a full-surface frame.</param>
    public void RenderNow(bool forceFull = false)
    {
        if (_disposed || _rendering)
        {
            return;
        }

        _rendering = true;
        _probeAdvanceAlloc = 0;
        _probeRenderAllocStart = _perfProbe is null ? 0 : SkiaHostPerfProbe.CurrentAllocatedBytes();
        try
        {
            RenderAllLocked(forceFull);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>Human-readable window/shell diagnostics (smoke runs, bug reports).</summary>
    public string DescribeWindows()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var entry in _windows)
        {
            var host = entry.Host;
            var placement = host.Placement;
            builder.AppendLine(
                $"hwnd=0x{host.Handle:X} monitor={entry.Monitor.Id} primary={entry.Monitor.IsPrimary} " +
                $"work={entry.Monitor.WorkArea} dpi={entry.Monitor.Dpi} scale={host.Scale:0.00} mode={host.Mode} " +
                $"surface={host.SurfaceSizePx.Width}x{host.SurfaceSizePx.Height}px visible={host.IsVisible} " +
                $"window={placement.WindowPixel} hitRects={host.HitRegion.InteractiveRectCount} hook={_mouseHook?.IsInstalled} " +
                $"frames={entry.Frames} dirty={entry.DirtyFrames}");
        }

        builder.AppendLine(_controller.Describe());
        var stats = Stats;
        var ticks = _frameClock is SkiaFrameClock skiaClock ? skiaClock.TickCount : -1;
        builder.AppendLine(
            $"frames={stats.Frames} dirty={stats.DirtyFrames} renderMs(last/mean)={stats.LastRenderMilliseconds:0.000}/{stats.MeanRenderMilliseconds:0.000} " +
            $"commits={stats.Commits} failures={stats.CommitFailures} uploadUs={stats.MeanUploadMicroseconds:0.0} idle={stats.Idle} clockTicks={ticks}");
        return builder.ToString();
    }

    /// <summary>Starts the host: monitors, layered windows, pipeline and the initial frame.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No monitor or the layered window could not be created.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _started = true;
        _logger.Information(LogCategories.Lifecycle, "island.skia.start",
            "Starting Skia island host (layered windows).");

        try
        {
            await EnsureMonitorsAsync(cancellationToken).ConfigureAwait(true);
            var settings = CurrentSettings();
            var plan = SkiaMonitorPlanner.Plan([], EnumerateCoreMonitors(_monitorService?.Current), settings.DisplayOn);
            if (plan.Targets.Length == 0)
            {
                throw new InvalidOperationException("No display monitors were enumerated for the layered island host.");
            }

            _primaryMonitorId = plan.Primary?.Id;
            foreach (var monitor in plan.Targets)
            {
                _windows.Add(CreateWindow(monitor, settings.IslandMode));
            }

            _controller.Start();
            ApplyPlacementAll();
            foreach (var entry in _windows)
            {
                entry.Host.Show();
            }

            UpdateHitRegions(force: true);
            RefreshAccessibility();
            RenderNow(forceFull: true);
            EnsureFollowMousePolling();
            EnsureDeadlineTicker();
            EnsureFrameLoop();

            if (_mouseHook is not null)
            {
                await _mouseHook.StartAsync(cancellationToken).ConfigureAwait(true);
            }

            _frameStats.SetEnabled(settings.Advanced.FrameStats);
            if (_perfProbe is not null)
            {
                _perfProbe.MarkHostStarted();
                _perfProbe.Dump();
                _perfTicker = new UiThreadTicker(_time);
                _perfTicker.Start(TimeSpan.FromSeconds(1), _perfProbe.Dump);
            }

            _logger.Information(LogCategories.Lifecycle, "island.skia.started", "Skia island host started.",
                data: new Dictionary<string, object?>
                {
                    ["mode"] = settings.IslandMode.ToString(),
                    ["monitors"] = _windows.Count,
                    ["anchor"] = settings.IslandAnchor.ToString(),
                    ["surface"] = _windows.Count > 0 ? _windows[0].Host.SurfaceSizePx.ToString() : "none",
                    ["fps"] = _options.ResolvedFrameRate,
                });
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.Information(LogCategories.Lifecycle, "island.skia.stop", "Stopping Skia island host.");
        _frameClock.Stop();
        CancelIdleTrim();
        RestoreLatencyMode();
        LowerTimerResolution();
        _deadlineTicker.Dispose();
        _perfTicker?.Dispose();
        _perfTicker = null;
        _followMouseTimer?.Dispose();
        _followMouseTimer = null;
        _pendingDrop = null;
        _options.Settings.Changed -= OnSettingsChanged;
        _controller.StateChanged -= OnStateChanged;
        _controller.PlacementRequested -= OnPlacementRequested;
        _controller.PlacementPersistRequested -= OnPlacementPersistRequested;
        _controller.FrameRequested -= EnsureFrameLoop;
        _uiaSource.Dispose();
        _controller.Dispose();

        foreach (var entry in _windows)
        {
            entry.Dispose();
        }

        _windows.Clear();
        _renderer.Dispose();
        _interop.Dispose();

        if (_monitorService is not null)
        {
            _monitorService.MonitorsChanged -= OnMonitorsChanged;
            _ = _monitorService.DisposeAsync();
            _monitorService = null;
        }

        if (_themeMonitor is not null)
        {
            _themeMonitor.SnapshotChanged -= OnSystemThemeChanged;
            _ = _themeMonitor.DisposeAsync();
            _themeMonitor = null;
        }

        if (_mouseHook is not null)
        {
            _mouseHook.Dispose();
            _mouseHook = null;
        }

        if (_mouseHookHost is not null)
        {
            _ = _mouseHookHost.DisposeAsync();
            _mouseHookHost = null;
        }

        if (_messageHost is not null)
        {
            _ = _messageHost.DisposeAsync();
            _messageHost = null;
        }
    }

    // ------------------------------------------------------------------ composition

    private void OnSystemThemeChanged(object? sender, SnapshotEventArgs<SystemThemeSnapshot> e)
    {
        try
        {
            // Refresh the cached probe the App's SystemAppearanceSnapshot.FromWindows() reads, then
            // re-map theme/accent/reduced-motion on the UI thread. Never throws into the monitor.
            SystemAppearanceProbe.Refresh(_platformLogger);
            _dispatcher.Post(() =>
            {
                try
                {
                    _controller.RefreshSystemAppearance();
                    EnsureFrameLoop();
                }
                catch (Exception ex)
                {
                    _logger.Warning(LogCategories.Settings, "island.appearance-apply-failed",
                        "A live system-appearance snapshot could not be applied.", ex);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Settings, "island.appearance-refresh-failed",
                "The live system-appearance change could not be processed.", ex);
        }
    }

    private AppSettings CurrentSettings() => _options.Settings.Current;

    private async Task EnsureMonitorsAsync(CancellationToken cancellationToken)
    {
        if (_monitorService is not null)
        {
            return;
        }

        _messageHost = new PlatformMessageHost(_platformLogger, _platformCounters);
        await _messageHost.StartAsync(cancellationToken).ConfigureAwait(true);
        _monitorService = new MonitorService(_messageHost, _platformLogger, _platformCounters, _time);
        await _monitorService.StartAsync(cancellationToken).ConfigureAwait(true);
        _monitorService.MonitorsChanged += OnMonitorsChanged;

        // Live follow-system appearance: WM_SETTINGCHANGE + registry watchers on the Personalize/DWM
        // keys. The monitor publishes change-only snapshots on the thread pool; applying them is
        // best-effort — a failure is logged and the current theme/motion stays in place.
        _themeMonitor = new SystemThemeMonitor(_messageHost, _platformLogger, _platformCounters, _time);
        _themeMonitor.SnapshotChanged += OnSystemThemeChanged;
        try
        {
            await _themeMonitor.StartAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Platform, "island.theme-monitor-failed",
                "The live system-theme monitor could not start; follow-system values stay at their startup snapshot.", ex);
        }

        // The mouse hook gets its own message thread: a WH_MOUSE_LL callback must return within the
        // Windows hook timeout, so it must never queue behind clipboard/display work on the shared
        // monitor host.
        _mouseHookHost = new PlatformMessageHost(_platformLogger, _platformCounters);
        await _mouseHookHost.StartAsync(cancellationToken).ConfigureAwait(true);
        _mouseHook = new IslandMouseHook(_mouseHookHost, _platformLogger, _platformCounters);
        _mouseHook.PointerPressed += OnHookPointerPressed;
        _mouseHook.PointerReleased += OnHookPointerReleased;
        _mouseHook.PointerMoved += OnHookPointerMoved;
        _mouseHook.PointerDragged += OnHookPointerDragged;
        _mouseHook.PointerLeft += OnHookPointerLeft;
        _mouseHook.Wheel += OnHookWheel;
        // Started at the end of StartAsync, after the controller has a render state and the first
        // frame is on screen: a hook that consumes clicks before that would swallow them into a
        // controller that cannot hit-test yet (the classic "first click after launch does nothing").
    }

    private static List<CoreMonitorInfo> EnumerateCoreMonitors(MonitorSet? set)
    {
        var monitors = new List<CoreMonitorInfo>(set?.Count ?? 0);
        if (set is null)
        {
            return monitors;
        }

        foreach (var monitor in set.Monitors)
        {
            monitors.Add(monitor.ToCore());
        }

        return monitors;
    }

    /// <summary>
    /// Applies a monitor snapshot on the UI thread: creates/disposes one layered host per target
    /// monitor, keeps the primary stable and first, refreshes retained monitor geometry and re-places.
    /// </summary>
    private void ApplyMonitorSet(MonitorSet? set)
    {
        if (_disposed || !_started || !_controller.IsStarted)
        {
            return;
        }

        if (set is null || set.Count == 0)
        {
            // EnumDisplayMonitors returning nothing is a transient failure in practice (a live
            // desktop always has at least one monitor); never tear down every window for it.
            _logger.Warning(LogCategories.Render, "island.skia.monitors-empty",
                "Empty monitor snapshot; keeping the existing layered island windows.");
            return;
        }

        var settings = CurrentSettings();
        var plan = SkiaMonitorPlanner.Plan(
            [.. _windows.Select(static entry => entry.Monitor)],
            EnumerateCoreMonitors(set),
            settings.DisplayOn,
            _primaryMonitorId,
            _cursorMonitorId);

        if (plan.RequiresWindowChanges)
        {
            foreach (var entry in _windows.Where(entry => plan.Removed.Any(
                removed => string.Equals(removed.Id, entry.Monitor.Id, StringComparison.Ordinal))).ToArray())
            {
                _windows.Remove(entry);
                entry.Dispose();
            }
        }

        var added = new List<WindowEntry>(plan.Added.Length);
        foreach (var monitor in plan.Added)
        {
            var entry = CreateWindow(monitor, settings.IslandMode);
            _windows.Add(entry);
            added.Add(entry);
        }

        // Retained windows adopt the fresh monitor snapshot (work area/DPI), then the list is
        // reordered so the stable primary stays first (PrimaryWindow identity is stable).
        var targetsById = plan.Targets.ToDictionary(static monitor => monitor.Id, StringComparer.Ordinal);
        foreach (var entry in _windows)
        {
            if (targetsById.TryGetValue(entry.Monitor.Id, out var monitor))
            {
                entry.Monitor = monitor;
            }
        }

        ReorderWindows(plan.Targets);
        _primaryMonitorId = plan.Primary?.Id;

        // NotifyDisplayChanged snaps the springs, re-places every window through Core and requests a
        // full frame. Added windows are shown afterwards so they never flash at a stale origin.
        _controller.NotifyDisplayChanged();
        foreach (var entry in added)
        {
            entry.Host.Show();
        }

        // NotifyDisplayChanged already rendered a full frame through PlacementRequested; only the
        // hit regions (shell squircle + pills) need an explicit refresh for the new windows.
        UpdateHitRegions(force: true);
        RefreshAccessibility();

        _logger.Information(LogCategories.Render, "island.skia.monitors-changed",
            "Applied a monitor-set change to the layered island windows.",
            data: new Dictionary<string, object?>
            {
                ["targets"] = plan.Targets.Length,
                ["added"] = plan.Added.Length,
                ["removed"] = plan.Removed.Length,
                ["retained"] = plan.Retained.Length,
                ["geometryChanged"] = plan.GeometryChanged,
                ["primary"] = plan.Primary?.Id ?? "none",
                ["displayOn"] = settings.DisplayOn.ToString(),
            });
    }

    private void ReorderWindows(IReadOnlyList<CoreMonitorInfo> targets)
    {
        if (_windows.Count <= 1 || targets.Count == 0)
        {
            return;
        }

        var ordered = new List<WindowEntry>(_windows.Count);
        foreach (var monitor in targets)
        {
            foreach (var entry in _windows)
            {
                if (!ordered.Contains(entry) && string.Equals(entry.Monitor.Id, monitor.Id, StringComparison.Ordinal))
                {
                    ordered.Add(entry);
                    break;
                }
            }
        }

        foreach (var entry in _windows)
        {
            if (!ordered.Contains(entry))
            {
                ordered.Add(entry);
            }
        }

        _windows.Clear();
        _windows.AddRange(ordered);
    }

    private WindowEntry CreateWindow(CoreMonitorInfo monitor, IslandMode mode)
    {
        var layeredMode = ToLayeredMode(mode);
        var host = new LayeredWindowHost(
            new LayeredHostOptions
            {
                Pacing = LayeredPacingMode.TargetFrameRate,
                TargetFramesPerSecond = _options.ResolvedFrameRate,
                MergeDirtyRects = true,
                WindowTitle = "FluidBar Island",
            },
            _interop,
            _platformLogger,
            _platformCounters,
            _time);

        host.Create(monitor.ToPlatform(), layeredMode);
        host.ConfigurePacing(LayeredPacingMode.TargetFrameRate, _options.ResolvedFrameRate);
        host.AttachAccessibility(_uiaSource, _uiaSource);

        var entry = new WindowEntry(host, monitor);
        host.PointerMoved += pointer =>
        {
            _controller.HandlePointerMoved(pointer.ClientXDip, pointer.ClientYDip);
            host.SetCursorKind(IsInteractive(pointer.ClientXDip, pointer.ClientYDip) ? LayeredCursorKind.Hand : LayeredCursorKind.Arrow);
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        };
        host.PointerLeft += () =>
        {
            // The window region ends where the shell/strip do, so moving into the transparent approach
            // band raises a window leave even though the pointer is still approaching the island. The
            // hook keeps forwarding moves there and already marked the band, so keep the state.
            if (_controller.IsPointerInHoverBand)
            {
                return;
            }

            _controller.HandlePointerLeft();
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        };
        host.PointerPressed += pointer =>
        {
            _controller.HandlePointerPressed(pointer.ClientXDip, pointer.ClientYDip);
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        };
        host.PointerReleased += pointer =>
        {
            _controller.HandlePointerReleased(pointer.ClientXDip, pointer.ClientYDip);
        };
        host.PointerWheel += wheel =>
        {
            // One Win32 notch (120 raw) is 70 DIP, i.e. half of a Core wheel notch: two notches make
            // the 140 DIP page step (prototype2's 100 px Chrome notch also falls short of its 140 px
            // wheelStep, so both need two notches per page). Wheel-down advances a page.
            var deltaDip = -(wheel.Delta / 120.0) * FluidBar.Core.Interaction.PagerConstants.WheelDipPerNotch;
            var shift = (wheel.Pointer.Modifiers & LayeredKeyModifiers.Shift) != 0;
            _controller.HandleWheel(deltaDip, wheel.IsHorizontal, shift);
            EnsureFrameLoop();
        };
        host.Dragged += drag =>
        {
            var x = drag.Pointer.ClientXDip;
            var y = drag.Pointer.ClientYDip;
            var timestamp = NowMs();
            switch (drag.Phase)
            {
                case LayeredDragPhase.Started:
                    entry.DragKind = _controller.HandleDragStarted(x, y, timestamp);
                    entry.DragActive = false;
                    entry.DragOriginPx = default;
                    entry.DragStartScreenXDip = drag.Pointer.ScreenX / SafeScale(entry.Host.Scale);
                    entry.DragStartScreenYDip = drag.Pointer.ScreenY / SafeScale(entry.Host.Scale);
                    break;
                case LayeredDragPhase.Moved:
                    if (entry.DragKind == SkiaDragKind.Pager)
                    {
                        _controller.HandleDragMoved(x, y, timestamp);
                    }
                    else if (entry.DragKind == SkiaDragKind.WindowCandidate)
                    {
                        MoveWindowForDrag(entry, drag);
                        UpdateHitRegions(force: true);
                    }

                    break;
                case LayeredDragPhase.Ended:
                case LayeredDragPhase.Cancelled:
                    if (entry.DragKind == SkiaDragKind.WindowCandidate && entry.DragActive)
                    {
                        EndWindowDrag(entry, drag.Phase == LayeredDragPhase.Cancelled);
                    }
                    else
                    {
                        _controller.HandleDragEnded(x, y, timestamp);
                    }

                    entry.DragKind = SkiaDragKind.None;
                    entry.DragActive = false;
                    break;
            }

            EnsureFrameLoop();
        };
        host.PointerCaptureLost += () =>
        {
            _controller.HandleDragEnded(0, 0, NowMs());
            EnsureFrameLoop();
        };
        host.LayeredDpiChanged += change =>
        {
            _logger.Debug(LogCategories.Render, "island.skia.dpi-changed",
                $"Island DPI changed {change.OldDpi} -> {change.NewDpi} ({change.SurfaceSizePx}).");
            entry.Dpi = change.NewDpi;

            // The monitor service debounces WM_DISPLAYCHANGE by 300 ms, so the cached snapshot still
            // has the old DPI. Update this entry immediately: Core layout, placement and the renderer
            // scale must all use the new value for this frame, not 300 ms later.
            entry.Monitor = entry.Monitor with { Dpi = change.NewDpi };

            // SetMonitor early-returns when the scale already matches, but still refreshes the
            // host's Monitor snapshot (region/topmost diagnostics use its DPI).
            entry.Host.SetMonitor(entry.Monitor.ToPlatform());

            // The host already rebuilt its DIB at ceil(DIP * scale). Drop the Skia wrapper so the next
            // frame re-wraps the new bits/stride; never dispose it under an in-flight frame.
            if (!_rendering)
            {
                entry.InvalidateSurface();
            }

            // Snap + re-place every window through Core (PlacementRequested) and request a full frame.
            _controller.NotifyDisplayChanged();
            UpdateHitRegions(force: true);
            RefreshAccessibility();
        };

        _logger.Information(LogCategories.Render, "island.skia.window-created", "Layered island window created.",
            data: new Dictionary<string, object?>
            {
                ["monitor"] = monitor.Id,
                ["mode"] = layeredMode.ToString(),
                ["surface"] = host.SurfaceSizePx.ToString(),
                ["dpi"] = host.Dpi,
            });
        return entry;
    }

    private bool IsInteractive(double xDip, double yDip)
    {
        if (!_controller.HasRenderState)
        {
            return false;
        }

        return SkiaIslandHitTest.Hit(_controller.RenderState, xDip, yDip).IsInteractive;
    }

    // ------------------------------------------------------------------ drag-to-move

    /// <summary>
    /// Follows the pointer with the dragged window. The threshold is measured in screen DIP and only
    /// crossed once per gesture; the window origin is captured at that moment and moved by the
    /// cumulative screen delta, clamped to the source monitor's work area. No animation, no
    /// per-frame work: one <c>SetWindowPos</c> per pointer move while dragging.
    /// </summary>
    private void MoveWindowForDrag(WindowEntry entry, in LayeredDragEvent drag)
    {
        var scale = SafeScale(entry.Host.Scale);
        if (!entry.DragActive)
        {
            if (!SkiaDragThreshold.Exceeds(
                    entry.DragStartScreenXDip,
                    entry.DragStartScreenYDip,
                    drag.Pointer.ScreenX / scale,
                    drag.Pointer.ScreenY / scale))
            {
                return;
            }

            if (!_controller.BeginWindowDrag())
            {
                // Dock mode or a state that cannot move: swallow the gesture, never expand on drop.
                entry.DragKind = SkiaDragKind.None;
                return;
            }

            entry.DragActive = true;
            entry.DragOriginPx = entry.Host.Placement.WindowPixel;
        }

        var mode = CurrentSettings().IslandMode;
        var width = entry.Host.Placement.WindowPixel.Width;
        var height = entry.Host.Placement.WindowPixel.Height;
        var work = entry.Monitor.WorkArea;
        var x = Math.Clamp(
            entry.DragOriginPx.X + drag.DeltaX,
            work.X,
            Math.Max(work.X, work.Right - width));
        var y = entry.DragOriginPx.Y;
        if (IslandPlacementDrag.AllowsVerticalDrag(mode))
        {
            y = Math.Clamp(
                entry.DragOriginPx.Y + drag.DeltaY,
                work.Y,
                Math.Max(work.Y, work.Bottom - height));
        }

        entry.Host.PlacePhysical(x, y);
    }

    /// <summary>
    /// Ends a window drag. On a normal drop the final host-window rectangle is resolved into a
    /// per-monitor anchor/offset <b>before</b> the controller applies
    /// <see cref="IslandInput.DragEnded"/> (whose <c>PersistPlacement</c> effect writes it). A
    /// cancelled drag restores the settings-driven placement and writes nothing.
    /// </summary>
    private void EndWindowDrag(WindowEntry entry, bool cancelled)
    {
        _pendingDrop = cancelled ? null : CapturePendingDrop(entry);
        if (!_controller.EndWindowDrag())
        {
            _pendingDrop = null;
        }

        if (cancelled)
        {
            ApplyPlacement(entry);
        }
    }

    /// <summary>Resolves the dropped host window into the anchor/offset persisted for its monitor.</summary>
    private PendingDrop? CapturePendingDrop(WindowEntry entry)
    {
        var settings = CurrentSettings();
        var mode = settings.IslandMode;
        if (!IslandPlacementDrag.AllowsDrag(mode))
        {
            return null;
        }

        var scale = SafeScale(entry.Host.Scale);
        var window = entry.Host.Placement.WindowPixel;
        var work = entry.Monitor.WorkArea;
        var windowDip = new VisualRect(
            (window.X - work.X) / scale,
            (window.Y - work.Y) / scale,
            entry.Host.SurfaceSizeDip.WidthDip,
            entry.Host.SurfaceSizeDip.HeightDip);
        var current = settings.ResolveMonitorPlacement(entry.Monitor.Id);
        var solved = IslandPlacementDrag.Solve(
            windowDip,
            entry.Monitor,
            mode,
            _controller.State.ContentKind,
            current.Anchor);
        return new PendingDrop(entry, entry.Monitor.Id, solved.Anchor, solved.OffsetX, solved.OffsetY);
    }

    /// <summary>
    /// Writes the pending drop through the settings controller (debounced, off the UI thread) and
    /// re-places the window once from the just-persisted placement. No-op for every other
    /// <c>PersistPlacement</c> effect (display/settings changes carry the effect too).
    /// </summary>
    private void OnPlacementPersistRequested()
    {
        var pending = _pendingDrop;
        _pendingDrop = null;
        if (_disposed || pending is null || !_windows.Contains(pending.Entry))
        {
            return;
        }

        try
        {
            _options.Settings.Update(settings => settings.WithMonitorPlacement(
                pending.MonitorId,
                pending.Anchor,
                pending.OffsetX,
                pending.OffsetY));
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        // One Core placement for the new anchor/offset; the window never animates its bounds.
        ApplyPlacement(pending.Entry);
        _logger.Information(LogCategories.Render, "island.skia.placement-persisted",
            "Persisted the dragged island placement for a monitor.",
            data: new Dictionary<string, object?>
            {
                ["monitor"] = pending.MonitorId,
                ["anchor"] = pending.Anchor.ToString(),
                ["offsetX"] = pending.OffsetX,
                ["offsetY"] = pending.OffsetY,
            });
    }

    // ------------------------------------------------------------------ follow-mouse

    /// <summary>
    /// Starts/stops the cursor-monitor poll for <see cref="DisplayTarget.FollowMouse"/>. The timer is
    /// created only for FollowMouse (zero work otherwise), ticks at 4 Hz off the UI thread and only
    /// posts when the monitor actually changed. Display changes refresh the cursor monitor
    /// immediately instead of waiting for the next tick.
    /// </summary>
    private void EnsureFollowMousePolling()
    {
        if (CurrentSettings().DisplayOn != DisplayTarget.FollowMouse)
        {
            _followMouseTimer?.Dispose();
            _followMouseTimer = null;
            _cursorMonitorId = null;
            return;
        }

        if (_followMouseTimer is not null || _disposed || !_started)
        {
            return;
        }

        _cursorMonitorId = _monitorService?.MonitorUnderCursor()?.Id ?? _cursorMonitorId;
        _followMouseTimer = _time.CreateTimer(
            static state => ((SkiaIslandHost)state!).PollCursorMonitor(),
            this,
            TimeSpan.FromMilliseconds(FollowMousePollMs),
            TimeSpan.FromMilliseconds(FollowMousePollMs));
    }

    /// <summary>Thread-pool cursor poll: cheap query, no UI work unless the monitor changed.</summary>
    private void PollCursorMonitor()
    {
        if (_disposed || !_started || _controller.IsWindowDragging)
        {
            return;
        }

        var monitor = _monitorService?.MonitorUnderCursor();
        var id = monitor?.Id;
        if (id is not { Length: > 0 } || string.Equals(id, _cursorMonitorId, StringComparison.Ordinal))
        {
            return;
        }

        _cursorMonitorId = id;
        _dispatcher.Post(() =>
        {
            if (_disposed || !_started)
            {
                return;
            }

            try
            {
                ApplyMonitorSet(_monitorService?.Current);
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Render, "island.skia.follow-mouse-failed",
                    "Applying a FollowMouse monitor change to the island windows failed.", ex);
            }
        });
    }

    private static double SafeScale(double scale) => double.IsFinite(scale) && scale > 0.1 ? scale : 1.0;

    // ------------------------------------------------------------------ placement

    private void OnPlacementRequested()
    {
        ApplyPlacementAll();
        RenderNow(forceFull: true);
    }

    private void ApplyPlacementAll()
    {
        foreach (var entry in _windows)
        {
            ApplyPlacement(entry);
        }
    }

    private void ApplyPlacement(WindowEntry entry)
    {
        var settings = CurrentSettings();
        var mode = settings.IslandMode;
        var layeredMode = ToLayeredMode(mode);
        if (entry.Host.Mode != layeredMode)
        {
            entry.Host.SetMode(layeredMode);
        }

        var platformMonitor = entry.Monitor.ToPlatform();
        var hostMonitor = entry.Host.Monitor;
        if (hostMonitor is null
            || !string.Equals(hostMonitor.Id, platformMonitor.Id, StringComparison.Ordinal)
            || hostMonitor.DpiX != platformMonitor.DpiX
            || hostMonitor.WorkArea != platformMonitor.WorkArea)
        {
            entry.Host.SetMonitor(platformMonitor);
        }

        // Per-monitor placement (spec 2.6): a drag writes the anchor/offset for the monitor it was
        // dropped on; monitors without an entry use the global anchor with zero offsets.
        var placement = settings.ResolveMonitorPlacement(entry.Monitor.Id);
        var request = IslandLayoutRequest.FromState(
            _controller.State,
            mode,
            placement.Anchor,
            placement.OffsetX,
            placement.OffsetY ?? double.NaN,
            stacking: settings.Stacking switch
            {
                StackingStrategySetting.Stacked => StackingStrategy.Stacked,
                StackingStrategySetting.Latest => StackingStrategy.LatestOnly,
                _ => StackingStrategy.Split,
            },
            hiddenHoverStrip: false,
            pagePosition: _controller.Pager.PagePosition,
            archipelagoVisible: _controller.Archipelago.IsVisible);
        var layout = IslandLayout.Calculate(request, entry.Monitor);

        // Core's WindowPlacement is the single placement product for float, flush and dock: the host
        // surface sizes match HostSurface exactly, so the window origin is WindowPixel verbatim. The
        // old dock PlacePhysical workaround is gone (see docs/rewrite/contracts/skia-host.md §5).
        entry.Host.PlaceForWindowPlacement(layout.Placement, layeredMode);
        entry.LastPlacement = layout.Placement;
        entry.Host.HitRegion.HasInteractiveContent = _controller.RenderState.IsVisible;
    }

    private static LayeredHostMode ToLayeredMode(IslandMode mode)
        => mode == IslandMode.Dock ? LayeredHostMode.Dock : LayeredHostMode.Float;

    // ------------------------------------------------------------------ frame loop

    private void EnsureFrameLoop()
    {
        if (_disposed || !_started)
        {
            return;
        }

        if (!_controller.IsAnimating)
        {
            if (_frameClock.IsRunning)
            {
                _frameClock.Stop();
            }

            RestoreLatencyMode();
            LowerTimerResolution();
            ScheduleIdleTrim();
            return;
        }

        CancelIdleTrim();
        EnterAnimationLatencyMode();
        RaiseTimerResolution();

        if (_frameClock is SkiaFrameClock skiaClock)
        {
            skiaClock.SetInterval(_controller.PreferredFrameInterval);
        }

        if (!_frameClock.IsRunning)
        {
            _frameClock.Start(OnFrame);
        }    }

    private SkiaIslandRenderChange _lastChange;
    private bool _lastRenderHappened;

    private void OnFrame(TimeSpan delta)
    {
        if (_disposed || _rendering)
        {
            return;
        }

        _rendering = true;
        var perfStart = _perfProbe is null ? 0 : SkiaHostPerfProbe.CurrentAllocatedBytes();
        try
        {
            _controller.PumpTimers();
            _controller.Advance(delta);
            _probeAdvanceAlloc = _perfProbe is null ? 0 : SkiaHostPerfProbe.CurrentAllocatedBytes() - perfStart;
            _probeRenderAllocStart = _perfProbe is null ? 0 : SkiaHostPerfProbe.CurrentAllocatedBytes();
            RenderAllLocked(forceFull: false);
            if (SkiaFrameTrace.Enabled && _windows.Count > 0)
            {
                SkiaFrameTrace.Frame(
                    _lastRenderHappened ? (_lastChange.FullFrame ? "full" : "dirty") : "none",
                    _controller.RenderState,
                    _controller.Motion,
                    false,
                    _lastChange);
            }

            if (_controller.IsAnimating)
            {
                // The preview rail springs move the pills a few DIP during a page turn; keep the
                // hook envelope on the painted geometry instead of the start-of-animation one.
                var state = _controller.RenderState;
                if (state.Archipelago.Visible)
                {
                    RebuildHookRects(state);
                }
            }
            else
            {
                _frameClock.Stop();
                UpdateHitRegions(force: false);
                RefreshAccessibility();
                ScheduleIdleTrim();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Render, "island.skia.frame-failed", "Skia island frame failed.", ex);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void OnDeadlineTick()
    {
        if (_disposed || _rendering)
        {
            return;
        }

        _rendering = true;
        _probeAdvanceAlloc = 0;
        _probeRenderAllocStart = _perfProbe is null ? 0 : SkiaHostPerfProbe.CurrentAllocatedBytes();
        try
        {
            _controller.PumpTimers();
            RenderAllLocked(forceFull: false);
            UpdateHitRegions(force: false);
            if (!_controller.IsAnimating)
            {
                RefreshAccessibility();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(LogCategories.Render, "island.skia.timer-failed", "Skia island timer failed.", ex);
        }
        finally
        {
            _rendering = false;
        }

        EnsureDeadlineTicker();
        EnsureFrameLoop();
    }

    private void EnsureDeadlineTicker()
    {
        if (_disposed)
        {
            return;
        }

        if (_controller.HasPendingDeadline)
        {
            if (!_deadlineTicker.IsRunning)
            {
                _deadlineTicker.Start(TimeSpan.FromMilliseconds(50), OnDeadlineTick);
            }
        }
        else if (_deadlineTicker.IsRunning)
        {
            _deadlineTicker.Stop();
        }
    }

    /// <summary>
    /// While frames are being produced, ask the GC for <see cref="GCLatencyMode.SustainedLowLatency"/>:
    /// full blocking collections are skipped in favour of gen0/gen1, which removes the 20–40 ms
    /// animation stalls a background gen2 caused even though the render path itself allocates nothing.
    /// The previous mode is restored the moment the island goes idle.
    /// </summary>
    private void EnterAnimationLatencyMode()
    {
        if (_savedLatencyMode is not null)
        {
            return;
        }

        try
        {
            _savedLatencyMode = GCSettings.LatencyMode;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch (Exception)
        {
            _savedLatencyMode = null;
        }

    }

    private void RestoreLatencyMode()
    {
        if (_savedLatencyMode is not { } saved)
        {
            return;
        }

        _savedLatencyMode = null;
        try
        {
            GCSettings.LatencyMode = saved;
        }
        catch (Exception)
        {
            // The runtime rejects invalid transitions; the default mode is restored on process exit.
        }
    }

    /// <summary>
    /// Windows' default timer granularity is 15.6 ms, so a requested 120 fps loop only gets every
    /// second tick and scheduling jitter pushes p95/p99 to 24–31 ms. Raise the resolution to 1 ms for
    /// the duration of the animation and lower it again as soon as the island is idle (the old
    /// behaviour only ever asked the threadpool timer, it never changed global resolution).
    /// </summary>
    private void RaiseTimerResolution()
    {
        if (_timerResolutionRaised)
        {
            return;
        }

        try
        {
            _timerResolutionRaised = timeBeginPeriod(1) == 0;
        }
        catch (Exception)
        {
            _timerResolutionRaised = false;
        }
    }

    private void LowerTimerResolution()
    {
        if (!_timerResolutionRaised)
        {
            return;
        }

        _timerResolutionRaised = false;
        try
        {
            timeEndPeriod(1);
        }
        catch (Exception)
        {
            // Restored on process exit by the OS regardless.
        }
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeEndPeriod(uint period);

    /// <summary>Delay before trimming caches/GC after the last frame (5 s of true idleness).</summary>
    private static readonly TimeSpan IdleTrimDelay = TimeSpan.FromSeconds(5);

    /// <summary>Dev/measurement override for the idle-trim delay (`FLUIDBAR_PERF_IDLE_TRIM_MS`).</summary>
    private static TimeSpan ResolveIdleTrimDelay()
    {
        var configured = Environment.GetEnvironmentVariable("FLUIDBAR_PERF_IDLE_TRIM_MS");
        return int.TryParse(configured, out var milliseconds) && milliseconds >= 100
            ? TimeSpan.FromMilliseconds(milliseconds)
            : IdleTrimDelay;
    }

    /// <summary>
    /// Schedules a one-shot memory trim once the frame loop has stopped. A tray island that sits idle
    /// for minutes should not keep the Skia glyph cache and a grown GC heap resident; purging them
    /// costs a single short pause and is invisible (nothing is animating, no frames are pending).
    /// </summary>
    private void ScheduleIdleTrim()
    {
        if (_disposed || _idleTrimTimer is not null)
        {
            return;
        }

        _idleTrimTimer = _time.CreateTimer(
            static state => ((SkiaIslandHost)state!).OnIdleTrimElapsed(), this, ResolveIdleTrimDelay(),
            Timeout.InfiniteTimeSpan);
    }

    private void CancelIdleTrim()
    {
        var timer = Interlocked.Exchange(ref _idleTrimTimer, null);
        timer?.Dispose();
    }

    private void OnIdleTrimElapsed()
    {
        CancelIdleTrim();
        _dispatcher.Post(TrimIdleMemory);
    }

    private void TrimIdleMemory()
    {
        if (_disposed || _rendering || _controller.IsAnimating)
        {
            return;
        }

        try
        {
            var heapBefore = GC.GetTotalMemory(forceFullCollection: false);
            using var processBefore = Process.GetCurrentProcess();
            var wsBefore = processBefore.WorkingSet64;

            // The raster glyph/typeface cache is the largest Skia-owned idle allocation.
            SKGraphics.PurgeFontCache();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: true);

            var heapAfter = GC.GetTotalMemory(forceFullCollection: false);
            using var processAfter = Process.GetCurrentProcess();
            Interlocked.CompareExchange(ref _idleTrimHeapBefore, heapBefore, 0);
            Interlocked.Exchange(ref _idleTrimHeapAfter, heapAfter);
            Interlocked.Increment(ref _idleTrimCount);
            _logger.Information(LogCategories.Render, "island.skia.idle-trim", "Idle memory trim completed.",
                data: new Dictionary<string, object?>
                {
                    ["managedHeapBeforeBytes"] = heapBefore,
                    ["managedHeapAfterBytes"] = heapAfter,
                    ["workingSetBeforeBytes"] = wsBefore,
                    ["workingSetAfterBytes"] = processAfter.WorkingSet64,
                });
        }
        catch (Exception ex)
        {
            _logger.Warning(LogCategories.Render, "island.skia.idle-trim-failed",
                "Idle memory trim failed.", ex);
        }
    }

    private void OnHookPointerPressed(float screenX, float screenY)
        => PostHookPointer(screenX, screenY, static (controller, x, y) => controller.HandlePointerPressed(x, y));

    private void OnHookPointerReleased(float screenX, float screenY)
    {
        if (!_hookDragActive)
        {
            PostHookPointer(screenX, screenY, static (controller, x, y) => controller.HandlePointerReleased(x, y));
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _hookDragActive = false;
            _controller.HandleDragEnded(_hookDragX, _hookDragY, NowMs());
            EnsureFrameLoop();
        });
    }

    private void OnHookPointerMoved(float screenX, float screenY)
        => PostHookPointer(screenX, screenY, static (controller, x, y) => controller.HandlePointerMoved(x, y));

    /// <summary>
    /// Rebuilds the expanded panel's pager drag from hook moves (the hook consumes the press, so the
    /// layered window never gets the capture it would need to report <c>Dragged</c> itself).
    /// </summary>
    private void OnHookPointerDragged(float screenX, float screenY)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed || !TryMapHookPoint(screenX, screenY, out var x, out var y))
            {
                return;
            }

            _hookDragX = x;
            _hookDragY = y;
            if (!_hookDragActive)
            {
                _hookDragActive = _controller.HandleDragStarted(x, y, NowMs()) == SkiaDragKind.Pager;
            }

            if (_hookDragActive)
            {
                _controller.HandleDragMoved(x, y, NowMs());
                EnsureFrameLoop();
            }
        });
    }

    private bool TryMapHookPoint(float screenX, float screenY, out float xDip, out float yDip)
    {
        foreach (var entry in _windows)
        {
            var placement = entry.Host.Placement.WindowPixel;
            var scale = entry.Host.Scale <= 0 ? 1 : entry.Host.Scale;
            if (screenX < placement.X || screenX >= placement.X + placement.Width
                || screenY < placement.Y || screenY >= placement.Y + placement.Height)
            {
                continue;
            }

            xDip = (float)((screenX - placement.X) / scale);
            yDip = (float)((screenY - placement.Y) / scale);
            return true;
        }

        xDip = 0;
        yDip = 0;
        return false;
    }

    private void OnHookWheel(int notches)
    {
        if (notches == 0)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _controller.HandleWheel(
                -notches * FluidBar.Core.Interaction.PagerConstants.WheelDipPerNotch,
                horizontal: false,
                shift: false);
            EnsureFrameLoop();
        });
    }

    private void PostHookPointer(float screenX, float screenY,
        Action<SkiaIslandController, float, float> action)
    {
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            foreach (var entry in _windows)
            {
                var placement = entry.Host.Placement.WindowPixel;
                var scale = entry.Host.Scale <= 0 ? 1 : entry.Host.Scale;
                if (screenX < placement.X || screenX >= placement.X + placement.Width
                    || screenY < placement.Y || screenY >= placement.Y + placement.Height)
                {
                    continue;
                }

                action(_controller, (float)((screenX - placement.X) / scale),
                    (float)((screenY - placement.Y) / scale));
                EnsureDeadlineTicker();
                EnsureFrameLoop();
                return;
            }
        });
    }

    private void OnHookPointerLeft()
    {
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _controller.HandlePointerLeft();
            EnsureDeadlineTicker();
            EnsureFrameLoop();
        });
    }

    private void OnStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        // Never snap the springs here: shell expand/collapse and the pager are spring-driven and the
        // frame loop below animates them. The snap was a workaround for the layered-update bug (see
        // LayeredWindowHost.TryUpdate) and turned every transition into an instant jump.
        UpdateHitRegions(force: false);
        RefreshAccessibility();
        EnsureDeadlineTicker();
        EnsureFrameLoop();
        RenderNow(forceFull: true);
    }

    /// <summary>
    /// Rebuilds the accessibility snapshot from the current render state and publishes it to every
    /// monitor's host. State/strip/focus driven; at most once when an animation settles, never per frame.
    /// </summary>
    private void RefreshAccessibility()
    {
        if (_disposed || _windows.Count == 0)
        {
            return;
        }

        try
        {
            var snapshot = _uiaSource.BuildSnapshot();
            foreach (var entry in _windows)
            {
                entry.Host.SetUiaSnapshot(snapshot);
            }
        }
        catch (Exception ex)
        {
            // Accessibility must never break the island: a provider failure leaves the last tree.
            _logger.Warning(LogCategories.Render, "island.skia.uia-refresh-failed",
                "Publishing the island accessibility snapshot failed.", ex);
        }
    }

    private void RenderAllLocked(bool forceFull)
    {
        _lastRenderHappened = false;
        if (_windows.Count == 0)
        {
            return;
        }

        SkiaIslandRenderChange change;
        if (forceFull)
        {
            _controller.TakeInvalidation(out _);
            change = SkiaIslandRenderChange.Full;
        }
        else if (!_controller.TakeInvalidation(out change))
        {
            return;
        }

        _lastChange = change;
        _lastRenderHappened = true;
        var state = _controller.RenderState;
        double totalRenderMs = 0;
        foreach (var entry in _windows)
        {
            totalRenderMs += RenderEntry(entry, state, change);
        }

        if (_perfProbe is not null)
        {
            var renderAlloc = SkiaHostPerfProbe.CurrentAllocatedBytes() - _probeRenderAllocStart;
            _perfProbe.RecordFrame(totalRenderMs, change.FullFrame, _probeAdvanceAlloc, renderAlloc);
            _probeAdvanceAlloc = 0;
        }
    }

    private double RenderEntry(WindowEntry entry, in IslandRenderState state, SkiaIslandRenderChange change)
    {
        var probeAllocStart = IslandFrameProbe.Enabled ? GC.GetAllocatedBytesForCurrentThread() : 0L;
        if (IslandVerifyDump.Enabled)
        {
            // Verification runs dump the *same* render state the layered surface gets, so the PNG is
            // exactly what a visible window would show (a locked desktop cannot be captured otherwise).
            IslandVerifyDump.Capture(_renderer, state, (float)entry.Host.SurfaceSizeDip.WidthDip is var w && w > 0
                ? Math.Max(1f, Math.Min(3f, w / 488f))
                : 1.5f);
        }

        var started = Stopwatch.GetTimestamp();
        var frame = entry.Host.BeginFrame();
        var surface = EnsureSurface(entry, frame);
        var scale = (float)frame.Scale;

        try
        {
            if (change.FullFrame)
            {
                _renderer.Render(surface.Canvas, state, scale);
                surface.Canvas.Flush();
                var result = entry.Host.CommitFrame();
                RecordCommit(entry, result, dirty: false);
            }
            else
            {
                var surfaceDip = entry.Host.SurfaceSizeDip;
                var damage = Intersect(
                    change.DamageDip,
                    new VisualRect(0, 0, surfaceDip.WidthDip, surfaceDip.HeightDip));
                if (damage.IsEmpty)
                {
                    entry.Host.DiscardFrame();
                    return 0;
                }

                _renderer.Render(
                    surface.Canvas,
                    state,
                    scale,
                    new SKRect((float)damage.X, (float)damage.Y, (float)damage.Right, (float)damage.Bottom));
                surface.Canvas.Flush();
                var pixelDamage = LayeredRect.FromDip(damage, frame.Scale);
                var result = entry.Host.CommitFrame(pixelDamage);
                RecordCommit(entry, result, dirty: true);
            }
        }
        catch
        {
            if (entry.Host.HasPendingFrame)
            {
                entry.Host.DiscardFrame();
            }

            throw;
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        if (IslandFrameProbe.Enabled)
        {
            IslandFrameProbe.Record(elapsedMs,
                GC.GetAllocatedBytesForCurrentThread() - probeAllocStart,
                change.FullFrame,
                change.FullFrame ? 0 : change.DamageDip.Width,
                change.FullFrame ? 0 : change.DamageDip.Height);
        }

        _frameStats.Record(elapsedMs);
        entry.Frames++;
        entry.LastRenderMs = elapsedMs;
        Interlocked.Increment(ref _frames);
        _lastRenderMs = elapsedMs;
        _totalRenderMs += elapsedMs;
        return elapsedMs;
    }

    /// <summary>Retained DIB bytes across every layered window (diagnostics for the perf probe).</summary>
    private long SurfaceBytes()
    {
        long total = 0;
        foreach (var entry in _windows)
        {
            var size = entry.Host.SurfaceSizePx;
            total += (long)size.Width * size.Height * 4;
        }

        return total;
    }

    private void RecordCommit(WindowEntry entry, LayeredCommitResult result, bool dirty)
    {
        if (!result.Updated)
        {
            Interlocked.Increment(ref _commitFailures);
            return;
        }

        Interlocked.Increment(ref _commits);
        _totalUploadMicroseconds += result.UpdateMicroseconds;
        if (dirty)
        {
            entry.DirtyFrames++;
            Interlocked.Increment(ref _dirtyFrames);
        }
    }

    private static SKSurface EnsureSurface(WindowEntry entry, in LayeredFrame frame)
    {
        if (entry.Surface is not null
            && entry.SurfaceBits == frame.Bits
            && entry.SurfaceWidth == frame.WidthPx
            && entry.SurfaceHeight == frame.HeightPx)
        {
            return entry.Surface;
        }

        entry.Surface?.Dispose();
        var info = new SKImageInfo(frame.WidthPx, frame.HeightPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        entry.Surface = SKSurface.Create(info, frame.Bits, frame.Stride)
            ?? throw new InvalidOperationException("Skia could not wrap the layered DIB surface.");
        entry.SurfaceBits = frame.Bits;
        entry.SurfaceWidth = frame.WidthPx;
        entry.SurfaceHeight = frame.HeightPx;
        return entry.Surface;
    }

    private void UpdateHitRegions(bool force)
    {
        if (!_controller.HasRenderState)
        {
            return;
        }

        var state = _controller.RenderState;
        var signature = new RegionSignature(state.Form, state.IsVisible, state.Archipelago.Visible, state.PageIndex, _controller.IsAnimating);
        if (!force && signature.Equals(_regionSignature))
        {
            return;
        }

        _regionSignature = signature;
        foreach (var entry in _windows)
        {
            ApplyHitRegion(entry.Host, state);
        }

        RebuildHookRects(state);
    }

    /// <summary>
    /// Rebuilds the screen-space rectangles that make the global mouse hook consume clicks for this
    /// island. Called from the hit-region update and, while the preview rail springs, once per frame:
    /// the pills travel up to a few DIP during a page turn, and the stale envelope would otherwise let
    /// a click on a moving neighbour glass through to the desktop.
    /// </summary>
    /// <param name="state">Current render state.</param>
    private void RebuildHookRects(in IslandRenderState state)
    {
        _hookRects.Clear();
        _watchRects.Clear();
        if (state.IsVisible)
        {
            const double slackDip = 2;
            var shell = IslandMetrics.ShellRect(state);
            var stripVisible = state.Archipelago.Visible && state.Archipelago.Opacity > 0.01;
            var strip = stripVisible ? SkiaIslandHitTest.MeasureStrip(state) : default;
            var hoverBand = ArchipelagoHoverBand.For(state);
            foreach (var entry in _windows)
            {
                var placement = entry.Host.Placement.WindowPixel;
                var scale = entry.Host.Scale <= 0 ? 1 : entry.Host.Scale;

                // The band is watch-only: the hook must see the pointer arrive there to reveal the rail,
                // but a click in the transparent area below the shell still belongs to the desktop.
                if (!hoverBand.IsEmpty)
                {
                    _watchRects.Add(new IslandMouseHook.Rect(
                        placement.X + (hoverBand.X * scale),
                        placement.Y + (hoverBand.Y * scale),
                        placement.X + (hoverBand.Right * scale),
                        placement.Y + (hoverBand.Bottom * scale)));
                }

                // Consume clicks only over the island shell (never the whole host surface): the
                // transparent bleed must stay click-through. A 2 DIP slack absorbs edge rounding so
                // the pill stays easy to hit at any DPI.
                _hookRects.Add(new IslandMouseHook.Rect(
                    placement.X + (shell.Left - slackDip) * scale,
                    placement.Y + (shell.Top - slackDip) * scale,
                    placement.X + (shell.Right + slackDip) * scale,
                    placement.Y + (shell.Bottom + slackDip) * scale));

                foreach (var ball in IslandMetrics.SplitIsletRects(state))
                {
                    if (ball.Width <= 0 || ball.Height <= 0)
                    {
                        continue;
                    }

                    _hookRects.Add(new IslandMouseHook.Rect(
                        placement.X + (ball.Left - slackDip) * scale,
                        placement.Y + (ball.Top - slackDip) * scale,
                        placement.X + (ball.Right + slackDip) * scale,
                        placement.Y + (ball.Bottom + slackDip) * scale));
                }

                // The preview rail sits below the shell, so without its own region the hook treats the
                // clicks as desktop and the pills/dots never receive input.
                if (!stripVisible)
                {
                    continue;
                }

                var minX = float.MaxValue;
                var minY = float.MaxValue;
                var maxX = float.MinValue;
                var maxY = float.MinValue;

                void Include(SKRect rect)
                {
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        return;
                    }

                    minX = Math.Min(minX, rect.Left);
                    minY = Math.Min(minY, rect.Top);
                    maxX = Math.Max(maxX, rect.Right);
                    maxY = Math.Max(maxY, rect.Bottom);
                }

                Include(strip.Previous);
                Include(strip.Current);
                Include(strip.Next);
                foreach (var dot in strip.DotsLeft)
                {
                    Include(dot);
                }

                foreach (var dot in strip.DotsRight)
                {
                    Include(dot);
                }

                if (maxX > minX && maxY > minY)
                {
                    _hookRects.Add(new IslandMouseHook.Rect(
                        placement.X + (minX - slackDip) * scale,
                        placement.Y + (minY - slackDip) * scale,
                        placement.X + (maxX + slackDip) * scale,
                        placement.Y + (maxY + slackDip) * scale));
                }
            }
        }

        _mouseHook?.SetRegions(_hookRects);
        _mouseHook?.SetWatchRegions(_watchRects);
    }

    private static void ApplyHitRegion(LayeredWindowHost host, in IslandRenderState state)
    {
        var region = host.HitRegion;
        if (!state.IsVisible)
        {
            region.HasInteractiveContent = false;
            region.Clear();
            return;
        }

        region.HasInteractiveContent = true;
        var shell = IslandMetrics.ShellRect(state);
        region.SetShell(new VisualRect(shell.Left, shell.Top, shell.Width, shell.Height), state.ShellRadii);

        Span<VisualRect> rects = stackalloc VisualRect[LayeredHitRegion.MaxInteractiveRects];
        var count = 0;
        // Split previews live outside the shell, so each ball gets its own interactive rect (clicks
        // between the balls stay transparent/click-through).
        foreach (var ball in IslandMetrics.SplitIsletRects(state))
        {
            AddRect(rects, ref count, ball);
        }

        if (!state.Archipelago.Visible || state.Archipelago.Opacity <= 0.01)
        {
            region.SetInteractiveRects(rects[..count]);
            return;
        }

        var strip = SkiaIslandHitTest.MeasureStrip(state);
        AddRect(rects, ref count, strip.Current);
        AddRect(rects, ref count, strip.Next);
        var dotHit = (float)IslandMetrics.DotHitSize / 2;
        foreach (var dot in strip.DotsLeft)
        {
            AddRect(rects, ref count, dot, dotHit);
        }

        foreach (var dot in strip.DotsRight)
        {
            AddRect(rects, ref count, dot, dotHit);
        }

        region.SetInteractiveRects(rects[..count]);
    }

    private static void AddRect(Span<VisualRect> rects, ref int count, SKRect rect, float inflate = 0)
    {
        if (count >= rects.Length || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        rects[count++] = new VisualRect(
            rect.Left - inflate,
            rect.Top - inflate,
            rect.Width + (2 * inflate),
            rect.Height + (2 * inflate));
    }

    private void OnMonitorsChanged(object? sender, SnapshotEventArgs<MonitorSet> args)
    {
        // The monitor service publishes from a thread-pool thread; every window operation must run on
        // the UI thread that created the hosts (the dispatcher runs inline when already there).
        _dispatcher.Post(() =>
        {
            if (_disposed || !_started)
            {
                return;
            }

            try
            {
                if (CurrentSettings().DisplayOn == DisplayTarget.FollowMouse)
                {
                    // A hot-plug can invalidate the cached cursor monitor; re-query before planning.
                    _cursorMonitorId = _monitorService?.MonitorUnderCursor()?.Id ?? _cursorMonitorId;
                }

                ApplyMonitorSet(args.Snapshot);
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Render, "island.skia.display-change-failed",
                    "Applying a display change to the island failed.", ex);
            }
        });
    }

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (_disposed || !_started)
        {
            return;
        }

        // Frame statistics is an opt-in render-path recorder (spec §9.3); toggling it takes effect
        // immediately instead of waiting for the next restart.
        _frameStats.SetEnabled(current.Advanced.FrameStats);
        if (previous.DisplayOn == current.DisplayOn)
        {
            return;
        }

        // DisplayOn changed (primary/all/follow-mouse): the controller already requested a re-place
        // for the existing windows; the host must also create/dispose the per-monitor windows and
        // start/stop the cursor poll.
        EnsureFollowMousePolling();
        _dispatcher.Post(() =>
        {
            if (_disposed || !_started)
            {
                return;
            }

            try
            {
                ApplyMonitorSet(_monitorService?.Current);
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Render, "island.skia.display-target-failed",
                    "Applying a DisplayOn change to the island windows failed.", ex);
            }
        });
    }

    private long NowMs() => (long)(_time.GetTimestamp() * 1000.0 / _time.TimestampFrequency);

    private static VisualRect Intersect(in VisualRect a, in VisualRect b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right <= left || bottom <= top ? VisualRect.Empty : new VisualRect(left, top, right - left, bottom - top);
    }

    /// <summary>Writes the primary surface to a PNG (evidence/smoke; not part of the frame path).</summary>
    /// <param name="path">Destination path.</param>
    public bool SavePrimaryFramePng(string path)
    {
        if (_windows.Count == 0 || _windows[0].Surface is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = _windows[0].Surface!.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return true;
    }

    private readonly record struct RegionSignature(
        ShellForm Form,
        bool Visible,
        bool ArchipelagoVisible,
        int Page,
        bool Animating);

    /// <summary>Drop placement waiting for Core's <c>PersistPlacement</c> effect.</summary>
    private sealed record PendingDrop(
        WindowEntry Entry,
        string MonitorId,
        FluidBar.Core.Layout.IslandAnchor Anchor,
        double OffsetX,
        double? OffsetY);

    private sealed class WindowEntry : IDisposable
    {
        public WindowEntry(LayeredWindowHost host, CoreMonitorInfo monitor)
        {
            Host = host;
            Monitor = monitor;
            Dpi = monitor.Dpi;
        }

        public LayeredWindowHost Host { get; }

        /// <summary>Fresh monitor snapshot for this window (work area/DPI updated on display changes).</summary>
        public CoreMonitorInfo Monitor { get; set; }

        public WindowPlacement LastPlacement { get; set; }

        /// <summary>What the active drag gesture means (pager, window-move candidate, none).</summary>
        public SkiaDragKind DragKind { get; set; }

        /// <summary>Whether the threshold was crossed and the window is following the pointer.</summary>
        public bool DragActive { get; set; }

        /// <summary>Window origin (physical) captured when the drag crossed the threshold.</summary>
        public LayeredRect DragOriginPx { get; set; }

        /// <summary>Press point in screen DIP (threshold space).</summary>
        public double DragStartScreenXDip { get; set; }

        /// <summary>Press point in screen DIP (threshold space).</summary>
        public double DragStartScreenYDip { get; set; }

        public uint Dpi { get; set; } = 96;

        public SKSurface? Surface { get; set; }

        public IntPtr SurfaceBits { get; set; }

        public int SurfaceWidth { get; set; }

        public int SurfaceHeight { get; set; }

        public long Frames { get; set; }

        public long DirtyFrames { get; set; }

        public double LastRenderMs { get; set; }

        /// <summary>
        /// Drops the Skia wrapper over the host DIB. Called after the host rebuilt its surface at a new
        /// DPI; <c>EnsureSurface</c> also re-wraps defensively when the bits/size change.
        /// </summary>
        public void InvalidateSurface()
        {
            Surface?.Dispose();
            Surface = null;
            SurfaceBits = IntPtr.Zero;
            SurfaceWidth = 0;
            SurfaceHeight = 0;
        }

        public void Dispose()
        {
            InvalidateSurface();
            Host.Dispose();
        }
    }
}

