using System.Diagnostics;
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
/// <see cref="WindowPlacementConverter"/>. Idle means zero frames: the clock stops as soon as the
/// springs, pager, strip and dynamic content are at rest.
/// </para>
/// </summary>
public sealed class SkiaIslandHost : IDisposable
{
    private readonly SkiaIslandHostOptions _options;
    private readonly IAppLogger _logger;
    private readonly IPlatformLogger _platformLogger;
    private readonly PlatformCounters _platformCounters;
    private readonly TimeProvider _time;
    private readonly SkiaHostDispatcher _dispatcher;
    private readonly SkiaIslandController _controller;
    private readonly IslandRenderer _renderer;
    private readonly IslandWindowInterop _interop;
    private readonly IFrameClock _frameClock;
    private readonly UiThreadTicker _deadlineTicker;
    private readonly List<WindowEntry> _windows = [];

    private PlatformMessageHost? _messageHost;
    private MonitorService? _monitorService;
    private RegionSignature _regionSignature;
    private bool _started;
    private bool _disposed;
    private bool _rendering;
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
        _renderer = new IslandRenderer(IslandTheme.Dark);
        _frameClock = options.FrameClockFactory?.Invoke() ?? new SkiaFrameClock(_time);
        _deadlineTicker = new UiThreadTicker(_time);

        _controller.SettingsRequested += () => SettingsRequested?.Invoke();
        _controller.MarketRequested += () => MarketRequested?.Invoke();
        _controller.MediaCommandRequested += command => MediaCommandRequested?.Invoke(command);
        _controller.StateChanged += OnStateChanged;
        _controller.PlacementRequested += OnPlacementRequested;
        _controller.FrameRequested += EnsureFrameLoop;
    }

    /// <summary>Raised when the settings window should open.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the marketplace should open.</summary>
    public event Action? MarketRequested;

    /// <summary>Raised when the user asks to execute a media transport command.</summary>
    public event Action<MediaTransportCommand>? MediaCommandRequested;

    /// <summary>Current island state snapshot.</summary>
    public IslandState State => _controller.State;

    /// <summary>Current activity scheduler snapshot.</summary>
    public ActivitySchedulerState SchedulerState => _controller.SchedulerState;

    /// <summary>Live layered hosts (one per target monitor).</summary>
    public IReadOnlyList<LayeredWindowHost> Windows => _windows.Select(static entry => entry.Host).ToArray();

    /// <summary>Primary layered host (diagnostics/evidence), or null before start.</summary>
    public LayeredWindowHost? PrimaryWindow => _windows.Count > 0 ? _windows[0].Host : null;

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
    }

    /// <summary>Simulates Escape.</summary>
    public void SimulateEscape() => _controller.HandleEscape();

    /// <summary>Simulates the toggle hotkey.</summary>
    public void SimulateToggleHotkey() => _controller.HandleToggleHotkey();

    /// <summary>Simulates a page request (keyboard/gutter).</summary>
    /// <param name="page">Target page.</param>
    public void SimulatePageRequested(IslandPage page) => _controller.HandlePageRequest(page);

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
                $"hwnd=0x{host.Handle:X} monitor={entry.Monitor.Id} scale={host.Scale:0.00} mode={host.Mode} " +
                $"surface={host.SurfaceSizePx.Width}x{host.SurfaceSizePx.Height}px visible={host.IsVisible} " +
                $"window={placement.WindowPixel} hitRects={host.HitRegion.InteractiveRectCount} " +
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
            var monitors = ResolveTargetMonitors(settings);
            if (monitors.Count == 0)
            {
                throw new InvalidOperationException("No display monitors were enumerated for the layered island host.");
            }

            foreach (var monitor in monitors)
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
            RenderNow(forceFull: true);
            EnsureDeadlineTicker();
            EnsureFrameLoop();

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
        _deadlineTicker.Dispose();
        _controller.StateChanged -= OnStateChanged;
        _controller.PlacementRequested -= OnPlacementRequested;
        _controller.FrameRequested -= EnsureFrameLoop;
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

        if (_messageHost is not null)
        {
            _ = _messageHost.DisposeAsync();
            _messageHost = null;
        }
    }

    // ------------------------------------------------------------------ composition

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
    }

    private List<CoreMonitorInfo> ResolveTargetMonitors(AppSettings settings)
    {
        var set = _monitorService?.Current;
        var monitors = new List<CoreMonitorInfo>();
        if (set is not null)
        {
            foreach (var monitor in set.Monitors)
            {
                monitors.Add(monitor.ToCore());
            }
        }

        if (monitors.Count == 0)
        {
            return monitors;
        }

        var primary = monitors.Find(static m => m.IsPrimary) ?? monitors[0];
        return settings.DisplayOn switch
        {
            // TODO(skia-mvp): FollowMouse needs a pointer query seam; the primary monitor is used.
            DisplayTarget.All => monitors,
            _ => [primary],
        };
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
        host.PointerReleased += pointer => _controller.HandlePointerReleased(pointer.ClientXDip, pointer.ClientYDip);
        host.PointerWheel += wheel =>
        {
            // One Win32 notch (120) equals one 140 DIP Core wheel notch; wheel-down advances a page.
            var deltaDip = -(wheel.Delta / 120.0) * FluidBar.Core.Interaction.PagerConstants.WheelNotchDip;
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
                    _controller.HandleDragStarted(x, y, timestamp);
                    break;
                case LayeredDragPhase.Moved:
                    _controller.HandleDragMoved(x, y, timestamp);
                    break;
                case LayeredDragPhase.Ended:
                case LayeredDragPhase.Cancelled:
                    _controller.HandleDragEnded(x, y, timestamp);
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
            _controller.NotifyDisplayChanged();
            ApplyPlacement(entry);
            RenderNow(forceFull: true);
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
        if (entry.Host.Monitor is null || !string.Equals(entry.Host.Monitor.Id, platformMonitor.Id, StringComparison.Ordinal)
            || entry.Host.Dpi != platformMonitor.DpiX)
        {
            entry.Host.SetMonitor(platformMonitor);
        }

        var request = IslandLayoutRequest.FromState(
            _controller.State,
            mode,
            settings.IslandAnchor,
            stacking: settings.Stacking == StackingStrategySetting.Stacked ? StackingStrategy.Stacked : StackingStrategy.LatestOnly,
            hiddenHoverStrip: false,
            pagePosition: _controller.Pager.PagePosition,
            archipelagoVisible: _controller.Archipelago.IsVisible);
        var layout = IslandLayout.Calculate(request, entry.Monitor);
        ApplyLayeredPlacement(entry.Host, layout.Placement, layeredMode);
        entry.LastPlacement = layout.Placement;
        entry.Host.HitRegion.HasInteractiveContent = _controller.RenderState.IsVisible;
    }

    private static void ApplyLayeredPlacement(LayeredWindowHost host, in WindowPlacement placement, LayeredHostMode mode)
    {
        if (mode == LayeredHostMode.Dock)
        {
            // Core's dock composition is panel 24..308 + strip 316..368 + archipelago band below,
            // while LayeredSurfaceSize.ShellLocalOffset assumes a 52 DIP lane above the panel. Use the
            // authoritative Core window origin so the renderer's dock geometry lands on screen.
            host.PlacePhysical(placement.WindowPixel.X, placement.WindowPixel.Y);
            return;
        }

        host.PlaceForWindowPlacement(placement, mode);
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

            return;
        }

        if (_frameClock is SkiaFrameClock skiaClock)
        {
            skiaClock.SetInterval(_controller.PreferredFrameInterval);
        }

        if (!_frameClock.IsRunning)
        {
            _frameClock.Start(OnFrame);
        }    }

    private void OnFrame(TimeSpan delta)
    {
        if (_disposed || _rendering)
        {
            return;
        }

        _rendering = true;
        try
        {
            _controller.PumpTimers();
            _controller.Advance(delta);
            RenderAllLocked(forceFull: false);
            if (!_controller.IsAnimating)
            {
                _frameClock.Stop();
                UpdateHitRegions(force: false);
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
        try
        {
            _controller.PumpTimers();
            RenderAllLocked(forceFull: false);
            UpdateHitRegions(force: false);
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

    private void OnStateChanged()
    {
        if (_disposed)
        {
            return;
        }

        UpdateHitRegions(force: false);
        EnsureDeadlineTicker();
        EnsureFrameLoop();
        RenderNow();
    }

    private void RenderAllLocked(bool forceFull)
    {
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

        var state = _controller.RenderState;
        foreach (var entry in _windows)
        {
            RenderEntry(entry, state, change);
        }
    }

    private void RenderEntry(WindowEntry entry, in IslandRenderState state, SkiaIslandRenderChange change)
    {
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
                    return;
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
        entry.Frames++;
        entry.LastRenderMs = elapsedMs;
        Interlocked.Increment(ref _frames);
        _lastRenderMs = elapsedMs;
        _totalRenderMs += elapsedMs;
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

        if (!state.Archipelago.Visible || state.Archipelago.Opacity <= 0.01)
        {
            region.SetInteractiveRects(default);
            return;
        }

        var strip = SkiaIslandHitTest.MeasureStrip(state);
        Span<VisualRect> rects = stackalloc VisualRect[LayeredHitRegion.MaxInteractiveRects];
        var count = 0;
        AddRect(rects, ref count, strip.Previous);
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
        _ = args;
        _dispatcher.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _controller.NotifyDisplayChanged();
                ApplyPlacementAll();
                UpdateHitRegions(force: true);
                RenderNow(forceFull: true);
            }
            catch (Exception ex)
            {
                _logger.Warning(LogCategories.Render, "island.skia.display-change-failed",
                    "Applying a display change to the island failed.", ex);
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

    private sealed class WindowEntry : IDisposable
    {
        public WindowEntry(LayeredWindowHost host, CoreMonitorInfo monitor)
        {
            Host = host;
            Monitor = monitor;
        }

        public LayeredWindowHost Host { get; }

        public CoreMonitorInfo Monitor { get; }

        public WindowPlacement LastPlacement { get; set; }

        public uint Dpi { get; set; }

        public SKSurface? Surface { get; set; }

        public IntPtr SurfaceBits { get; set; }

        public int SurfaceWidth { get; set; }

        public int SurfaceHeight { get; set; }

        public long Frames { get; set; }

        public long DirtyFrames { get; set; }

        public double LastRenderMs { get; set; }

        public void Dispose()
        {
            Surface?.Dispose();
            Surface = null;
            Host.Dispose();
        }
    }
}
