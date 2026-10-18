using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Display;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Policies;
using MonitorInfo = FluidBar.Platform.Windows.Display.MonitorInfo;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Win32 per-pixel-alpha layered host for one island surface. It owns one
/// <c>WS_POPUP | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST</c> window per
/// monitor, a persistent 32bpp premultiplied-BGRA DIB section, and the input/hit-test policy; the
/// renderer draws into <see cref="LayeredFrame.Pixels"/> and commits with
/// <c>UpdateLayeredWindow</c>.
/// </summary>
/// <remarks>
/// <para><b>Why layered.</b> Canonical <c>UpdateLayeredWindow</c> stays transparent in environments
/// where DirectComposition/Avalonia surfaces flatten to opaque grey (see
/// <c>docs/rewrite/contracts/host-transparency.md</c>); a layered window also never activates and never
/// appears in the taskbar. The price is a CPU raster of the surface and a GDI upload per frame — which
/// this host makes cheap by reusing the DIB and uploading only dirty rectangles.</para>
/// <para><b>Thread affinity.</b> Every member except the events' subscription is thread-affine to the
/// thread that constructed the instance (the UI thread). The host never posts work to another thread
/// and never starts a timer: an idle island performs zero updates because <c>CommitFrame</c> is only
/// called by the frame loop.</para>
/// <para><b>Lifecycle.</b> <see cref="Create"/> → <see cref="Place(MonitorInfo, in VisualRect, LayeredHostMode)"/>
/// → <see cref="Show"/> → frames → <see cref="Hide"/>/<see cref="Show"/> → <see cref="Dispose"/>.
/// <see cref="Dispose"/> is idempotent and destroys the HWND on its owning thread.</para>
/// <para><b>DPI.</b> The host is Per-Monitor-V2 by contract: the process must be PMv2 DPI-aware. It
/// sizes the surface at <c>ceil(DIP * scale)</c> and rebuilds it on <see cref="LayeredDpiChanged"/> /
/// <see cref="SetMonitor"/>; it never scales a frame in software.</para>
/// <para><b>Hit-testing.</b> The host owns its window procedure, so it applies the
/// <c>HTCLIENT</c>/<c>HTTRANSPARENT</c> policy itself (<see cref="LayeredHitRegion"/>), including the
/// detached-island lane. Do <b>not</b> also call <c>IslandWindowInterop.RegisterHitTest</c> for this
/// HWND. An <see cref="IslandWindowInterop"/> may still be supplied for placement and topmost
/// re-assertion so the App keeps a single Win32 policy path.</para>
/// </remarks>
public sealed partial class LayeredWindowHost : IDisposable
{
    /// <summary>Window class name registered once per process.</summary>
    public const string WindowClassName = "FluidBar.LayeredIslandHost";

    private readonly LayeredHostOptions _options;
    private readonly IslandWindowInterop? _interop;
    private readonly IPlatformLogger _logger;
    private readonly PlatformCounters _counters;
    private readonly TimeProvider _timeProvider;
    private readonly int _ownerThreadId;
    private readonly LayeredHitRegion _hitRegion = new();
    private readonly GCHandle _selfHandle;

    private LayeredSurface? _surface;
    private LayeredFramePacer? _pacer;
    private IntPtr _hwnd;
    private IntPtr _screenDc;
    private LayeredHostMode _mode = LayeredHostMode.Float;
    private MonitorInfo? _monitor;
    private LayeredHostPlacement _placement;
    private double _scale = 1.0;
    private uint _dpi = 96;
    private bool _created;
    private bool _visible;
    private bool _frameOpen;
    private bool _needsFullFrame = true;
    private bool _geometryDirty = true;
    private bool _trackingLeave;
    private bool _dragging;
    private bool _pointerInside;
    private long _frameIndex;
    private long _lastTopmostTimestamp;
    private LayeredPointerEvent _lastPointer;
    private int _dragStartScreenX;
    private int _dragStartScreenY;
    private int _lastDragScreenX;
    private int _lastDragScreenY;
    private LayeredCursorKind _cursorKind;
    private IntPtr _cursorHandle;
    private int _disposed;

    /// <summary>Creates the host object. No window exists until <see cref="Create"/> is called.</summary>
    /// <param name="options">Behaviour options (defaults to <see cref="LayeredHostOptions.Default"/>).</param>
    /// <param name="interop">
    /// Optional shared Win32 policy surface. When supplied, placement and topmost re-assertion are
    /// delegated to it so the App has one policy path; hit-testing stays inside the host.
    /// </param>
    /// <param name="logger">Optional platform logger.</param>
    /// <param name="counters">Optional platform counters (Win32 failures are counted here).</param>
    /// <param name="timeProvider">Optional clock (tests/diagnostics).</param>
    public LayeredWindowHost(LayeredHostOptions? options = null, IslandWindowInterop? interop = null,
        IPlatformLogger? logger = null, PlatformCounters? counters = null, TimeProvider? timeProvider = null)
    {
        _options = options ?? LayeredHostOptions.Default;
        _interop = interop;
        _logger = logger ?? NullPlatformLogger.Instance;
        _counters = counters ?? new PlatformCounters();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _cursorKind = _options.DefaultCursor;
        MergeDirtyRects = _options.MergeDirtyRects;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
    }

    /// <summary>The host HWND, or <see cref="IntPtr.Zero"/> before <see cref="Create"/> / after dispose.</summary>
    public IntPtr Handle => _hwnd;

    /// <summary>True after a successful <see cref="Create"/>.</summary>
    public bool IsCreated => _created;

    /// <summary>True while the window is shown.</summary>
    public bool IsVisible => _visible;

    /// <summary>True between <see cref="BeginFrame"/> and <see cref="CommitFrame()"/>.</summary>
    public bool HasPendingFrame => _frameOpen;

    /// <summary>Current presentation mode (selects the fixed surface size).</summary>
    public LayeredHostMode Mode => _mode;

    /// <summary>Fixed surface size of the current mode, in DIP.</summary>
    public LayeredSurfaceSize SurfaceSizeDip => LayeredSurfaceSize.ForMode(_mode);

    /// <summary>Current surface size in physical pixels.</summary>
    public LayeredPixelSize SurfaceSizePx => _surface?.Size ?? default;

    /// <summary>Current DIP scale (monitor DPI / 96).</summary>
    public double Scale => _scale;

    /// <summary>Current effective DPI (96 = 100%).</summary>
    public uint Dpi => _dpi;

    /// <summary>The monitor the host is currently placed on, if known.</summary>
    public MonitorInfo? Monitor => _monitor;

    /// <summary>The last placement produced by the host.</summary>
    public LayeredHostPlacement Placement => _placement;

    /// <summary>The persistent DIB (null before create). Exposed for diagnostics/benchmarks.</summary>
    public LayeredSurface? Surface => _surface;

    /// <summary>The frame pacer created with the host (null before create).</summary>
    public LayeredFramePacer? Pacer => _pacer;

    /// <summary>The live hit region; mutate it on layout settle, never per frame.</summary>
    public LayeredHitRegion HitRegion => _hitRegion;

    /// <summary>Behaviour options.</summary>
    public LayeredHostOptions Options => _options;

    /// <summary>
    /// Whether dirty rectangles are merged into one bounding box before uploading. Initialised from
    /// <see cref="LayeredHostOptions.MergeDirtyRects"/>; writable so a measurement harness (or a
    /// profile-driven tuning pass) can compare both strategies without recreating the window.
    /// </summary>
    public bool MergeDirtyRects { get; set; }

    // ------------------------------------------------------------------
    // Events (struct payloads, no per-event allocation)
    // ------------------------------------------------------------------

    /// <summary>Pointer moved over the window.</summary>
    public event Action<LayeredPointerEvent>? PointerMoved;

    /// <summary>Pointer left the window (no buttons held).</summary>
    public event Action? PointerLeft;

    /// <summary>Left button pressed on an interactive part of the island.</summary>
    public event Action<LayeredPointerEvent>? PointerPressed;

    /// <summary>Left button released.</summary>
    public event Action<LayeredPointerEvent>? PointerReleased;

    /// <summary>Left button double click.</summary>
    public event Action<LayeredPointerEvent>? PointerDoubleClicked;

    /// <summary>Vertical or horizontal wheel.</summary>
    public event Action<LayeredWheelEvent>? PointerWheel;

    /// <summary>Capture was lost to another window (alt-tab/UAC); the drag is cancelled.</summary>
    public event Action? PointerCaptureLost;

    /// <summary>Drag gesture (press, move, release, cancel) in screen pixels.</summary>
    public event Action<LayeredDragEvent>? Dragged;

    /// <summary>The host rebuilt its surface for a new DPI; a full frame is required next.</summary>
    public event Action<LayeredDpiChange>? LayeredDpiChanged;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    /// <summary>
    /// Creates the layered window and its DIB at the mode's fixed size for the monitor's DPI. The
    /// window is created hidden; call <see cref="Place(MonitorInfo, in VisualRect, LayeredHostMode)"/>
    /// and <see cref="Show"/> afterwards.
    /// </summary>
    /// <param name="monitor">Target monitor (its DPI selects the physical surface size).</param>
    /// <param name="mode">Presentation mode (selects 488x384 or 488x444 DIP).</param>
    /// <exception cref="Win32Exception">Window creation failed.</exception>
    public void Create(MonitorInfo monitor, LayeredHostMode mode = LayeredHostMode.Float)
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(monitor);
        if (_created)
        {
            throw new InvalidOperationException("The layered host is already created. Use SetMode/SetMonitor/Place or dispose it.");
        }

        _monitor = monitor;
        _mode = mode;
        _dpi = monitor.DpiX == 0 ? 96u : monitor.DpiX;
        _scale = DisplayMetrics.ScaleFromDpi(_dpi);
        var surfaceSize = LayeredSurfaceSize.ForMode(mode);
        var sizePx = surfaceSize.ToPixels(_scale);

        EnsureClassRegistered();
        var instance = NativeMethods.Kernel32.GetModuleHandleW(null);
        var exStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TOPMOST;
        var hwnd = NativeMethods.User32.CreateWindowExW((uint)exStyle, WindowClassName, _options.WindowTitle,
            NativeMethods.WS_POPUP, 0, 0, sizePx.Width, sizePx.Height, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed for the layered island host.");
        }

        _hwnd = hwnd;
        NativeMethods.User32.SetWindowLongPtr(hwnd, NativeMethods.GWLP_USERDATA, GCHandle.ToIntPtr(_selfHandle));

        // WS_EX_TOPMOST is ignored by CreateWindowExW; assert it explicitly without activating.
        SetTopmostUnconditional();

        _surface = LayeredSurface.Create(sizePx.Width, sizePx.Height);
        _screenDc = NativeMethods.User32.GetDC(IntPtr.Zero);
        _pacer = new LayeredFramePacer(_options.Pacing, _options.TargetFramesPerSecond);
        _created = true;
        _needsFullFrame = true;
        _geometryDirty = true;
        _placement = new LayeredHostPlacement(
            new LayeredRect(0, 0, sizePx.Width, sizePx.Height), sizePx, surfaceSize, _scale,
            OffsetToPixels(LayeredSurfaceSize.ShellLocalOffset(mode), _scale));
        UpdateUiaGeometry();

        _logger.LogInfo(PlatformLogCategory.Platform, "layered-host.created",
            "Layered island host created.",
            new Dictionary<string, object?>
            {
                ["hwnd"] = $"0x{hwnd:X}",
                ["mode"] = mode.ToString(),
                ["surfacePx"] = sizePx.ToString(),
                ["dpi"] = _dpi,
                ["scale"] = _scale,
            });
    }

    /// <summary>Shows the window without activating it and asserts topmost once.</summary>
    public void Show()
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfNotCreated();
        if (_visible)
        {
            return;
        }

        NativeMethods.User32.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);

        // The first ShowWindow call of a process is overridden by STARTUPINFO (a console-launched
        // WinExe can start hidden), so force visibility with SWP_SHOWWINDOW as well.
        if (!NativeMethods.User32.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.show-failed",
                "SetWindowPos(SWP_SHOWWINDOW) failed. " + Win32Error.Format(Marshal.GetLastWin32Error()));
        }

        _visible = true;
        UpdateUiaGeometry();
        SetTopmostUnconditional();
        _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.shown", "Layered island host shown.");
    }

    /// <summary>Hides the window (the HWND and DIB stay alive for the next show).</summary>
    public void Hide()
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_created || !_visible)
        {
            return;
        }

        if (_dragging)
        {
            EndDrag(LayeredDragPhase.Cancelled, _lastPointer);
        }

        NativeMethods.User32.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        _visible = false;
        _pointerInside = false;
        UpdateUiaGeometry();
        _logger.LogDebug(PlatformLogCategory.Platform, "layered-host.hidden", "Layered island host hidden.");
    }

    /// <summary>
    /// Places the host for a shell rectangle given in work-area-relative DIP (Core layout output) and
    /// the mode/form that rectangle belongs to. Exactly one <c>SetWindowPos</c>; never called per
    /// frame. Prefer <see cref="PlaceForWindowPlacement"/> when a Core
    /// <see cref="WindowPlacement"/> is available: it is the authoritative seam.
    /// </summary>
    /// <param name="monitor">Target monitor (work-area origin and DPI).</param>
    /// <param name="shellDip">Anchor shell rect: the panel for float/flush, the dock strip for dock.</param>
    /// <param name="mode">Presentation mode of <paramref name="shellDip"/>.</param>
    /// <param name="form">
    /// Visual form the anchor rect belongs to; selects the dock compact (strip, y=316) versus expanded
    /// (panel, y=24) local offset. Float/flush use the panel top for both.
    /// </param>
    /// <returns>The placement that was applied.</returns>
    public LayeredHostPlacement Place(MonitorInfo monitor, in VisualRect shellDip, LayeredHostMode mode,
        FluidBar.Core.Layout.ShellForm form = FluidBar.Core.Layout.ShellForm.Compact)
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfNotCreated();
        ArgumentNullException.ThrowIfNull(monitor);

        var scale = DisplayMetrics.ScaleFromDpi(monitor.DpiX == 0 ? 96u : monitor.DpiX);
        var surfaceSize = LayeredSurfaceSize.ForMode(mode);
        var offset = LayeredSurfaceSize.ShellLocalOffset(mode, form);
        var sizePx = surfaceSize.ToPixels(scale);
        var originX = monitor.WorkArea.X + WindowPlacementConverter.Round((shellDip.X - offset.X) * scale);
        var originY = monitor.WorkArea.Y + WindowPlacementConverter.Round((shellDip.Y - offset.Y) * scale);
        return ApplyPlacement(monitor, mode, monitor.DpiX == 0 ? 96u : monitor.DpiX, scale,
            new LayeredRect(originX, originY, sizePx.Width, sizePx.Height), surfaceSize, offset, sizePx);
    }

    /// <summary>
    /// Places the host using Core's single visual→window conversion
    /// (<see cref="WindowPlacementConverter.PlaceWindow"/>). Core's <see cref="WindowPlacement"/>
    /// owns the whole host-surface rectangle for both modes (488x384 float/flush, 488x444 dock), so
    /// the window origin is exactly <see cref="WindowPlacement.WindowPixel"/>; only the DIB size is
    /// re-derived from the mode (ceil rule) to defend against a stale/mismatched placement.
    /// </summary>
    /// <param name="placement">Core placement product.</param>
    /// <param name="mode">Presentation mode the placement was computed for.</param>
    public LayeredHostPlacement PlaceForWindowPlacement(in WindowPlacement placement, LayeredHostMode mode)
    {
        ArgumentNullException.ThrowIfNull(placement);
        EnsureOwningThread();
        ThrowIfNotCreated();
        var computed = ComputePlacement(placement, mode);
        var dpi = (uint)Math.Round(computed.Scale * 96);
        return ApplyPlacement(_monitor ?? CreateSyntheticMonitor(placement, computed.Scale), mode, dpi,
            computed.Scale, computed.WindowPixel, computed.SurfaceSizeDip, placement.ShellLocalOffset,
            computed.SurfaceSizePx);
    }

    /// <summary>
    /// Pure Core-placement → host-placement math (no HWND, no thread affinity). The window origin is
    /// Core's <see cref="WindowPlacement.WindowPixel"/> verbatim; the size is the mode's fixed surface
    /// at <c>ceil(DIP × scale)</c>. Used by <see cref="PlaceForWindowPlacement"/> and pinned by tests.
    /// </summary>
    /// <param name="placement">Core placement product.</param>
    /// <param name="mode">Presentation mode the placement was computed for.</param>
    public static LayeredHostPlacement ComputePlacement(in WindowPlacement placement, LayeredHostMode mode)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var scale = double.IsFinite(placement.Scale) && placement.Scale > 0 ? placement.Scale : 1.0;
        var surfaceSize = LayeredSurfaceSize.ForMode(mode);
        var sizePx = surfaceSize.ToPixels(scale);
        var window = new LayeredRect(placement.WindowPixel.X, placement.WindowPixel.Y, sizePx.Width, sizePx.Height);
        return new LayeredHostPlacement(window, sizePx, surfaceSize, scale,
            OffsetToPixels(placement.ShellLocalOffset, scale));
    }

    /// <summary>Moves the window to an absolute physical origin, keeping the current surface size.</summary>
    public LayeredHostPlacement PlacePhysical(int xPx, int yPx)
    {
        EnsureOwningThread();
        ThrowIfNotCreated();
        var rect = new LayeredRect(xPx, yPx, _placement.WindowPixel.Width, _placement.WindowPixel.Height);
        ApplyWindowRect(rect);
        _geometryDirty = true;
        _placement = _placement with { WindowPixel = rect };
        UpdateUiaGeometry();
        return _placement;
    }

    /// <summary>
    /// Switches presentation mode, rebuilding the DIB at the new fixed size (one <c>SetWindowPos</c>,
    /// same origin). The caller must re-<see cref="Place"/> afterwards because the shell anchor moved.
    /// </summary>
    public LayeredHostPlacement SetMode(LayeredHostMode mode)
    {
        EnsureOwningThread();
        ThrowIfNotCreated();
        if (mode == _mode)
        {
            return _placement;
        }

        _mode = mode;
        var surfaceSize = LayeredSurfaceSize.ForMode(mode);
        var sizePx = surfaceSize.ToPixels(_scale);
        RebuildSurface(sizePx);
        var rect = new LayeredRect(_placement.WindowPixel.X, _placement.WindowPixel.Y, sizePx.Width, sizePx.Height);
        ApplyWindowRect(rect);
        _placement = new LayeredHostPlacement(rect, sizePx, surfaceSize, _scale,
            OffsetToPixels(LayeredSurfaceSize.ShellLocalOffset(mode), _scale));
        _needsFullFrame = true;
        _geometryDirty = true;
        _pacer?.Reset();
        UpdateUiaGeometry();
        return _placement;
    }

    /// <summary>
    /// Updates the monitor (and therefore the DPI scale) without moving the window origin. Rebuilds
    /// the surface when the scale changed. The caller re-places for the new monitor's work area.
    /// </summary>
    public LayeredHostPlacement SetMonitor(MonitorInfo monitor)
    {
        EnsureOwningThread();
        ThrowIfNotCreated();
        ArgumentNullException.ThrowIfNull(monitor);
        _monitor = monitor;
        var dpi = monitor.DpiX == 0 ? 96u : monitor.DpiX;
        var scale = DisplayMetrics.ScaleFromDpi(dpi);
        if (Math.Abs(scale - _scale) < 0.0001 && dpi == _dpi)
        {
            return _placement;
        }

        _dpi = dpi;
        _scale = scale;
        var sizePx = LayeredSurfaceSize.ForMode(_mode).ToPixels(scale);
        RebuildSurface(sizePx);
        var rect = new LayeredRect(_placement.WindowPixel.X, _placement.WindowPixel.Y, sizePx.Width, sizePx.Height);
        ApplyWindowRect(rect);
        _placement = new LayeredHostPlacement(rect, sizePx, LayeredSurfaceSize.ForMode(_mode), scale,
            OffsetToPixels(LayeredSurfaceSize.ShellLocalOffset(_mode), scale));
        _needsFullFrame = true;
        _geometryDirty = true;
        _pacer?.Reset();
        UpdateUiaGeometry();
        return _placement;
    }

    /// <summary>Sets the cursor shown over interactive content (hover state from the renderer).</summary>
    public void SetCursorKind(LayeredCursorKind kind)
    {
        if (kind == _cursorKind)
        {
            return;
        }

        _cursorKind = kind;
        _cursorHandle = IntPtr.Zero;
        if (_pointerInside && _created)
        {
            NativeMethods.User32.SetCursor(CursorHandle(kind));
        }
    }

    /// <summary>
    /// Applies a rounded window region from the settled shell geometry (delegates to
    /// <see cref="IslandWindowInterop.UpdateWindowRegionOnSettle"/> when an interop instance was
    /// supplied). Optional: per-pixel alpha already clips the visual and
    /// <see cref="LayeredHitRegion"/> already clips the clicks. Settle-only — never per frame.
    /// </summary>
    public void ApplyWindowRegionOnSettle(ShellGeometry geometry)
    {
        if (_interop is null || _monitor is null || !_created)
        {
            return;
        }

        _interop.UpdateWindowRegionOnSettle(_hwnd, geometry, _monitor);
    }

    // ------------------------------------------------------------------
    // Frames
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts a frame and returns a view over the persistent DIB. The renderer draws into
    /// <see cref="LayeredFrame.Pixels"/> (premultiplied BGRA, top-down) and must call one of the
    /// <c>CommitFrame</c> overloads.
    /// </summary>
    /// <exception cref="InvalidOperationException">A frame is already open, or the host is not created.</exception>
    public LayeredFrame BeginFrame()
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfNotCreated();
        if (_frameOpen)
        {
            throw new InvalidOperationException("BeginFrame() was already called; commit or discard the open frame first.");
        }

        _frameOpen = true;
        _frameIndex++;
        return new LayeredFrame(_surface!, _scale, _frameIndex);
    }

    /// <summary>Uploads the whole surface (<c>UpdateLayeredWindow</c>, one call).</summary>
    public LayeredCommitResult CommitFrame() => CommitCore(default, fullFrame: true);

    /// <summary>Uploads only the given dirty rectangles (surface pixels).</summary>
    /// <param name="dirtyRects">Dirty rectangles; empty means "nothing changed" and uploads nothing.</param>
    public LayeredCommitResult CommitFrame(ReadOnlySpan<LayeredRect> dirtyRects)
        => CommitCore(dirtyRects, fullFrame: false);

    /// <summary>Uploads a single dirty rectangle (surface pixels).</summary>
    public LayeredCommitResult CommitFrame(in LayeredRect dirtyRect) => CommitCore([dirtyRect], fullFrame: false);

    /// <summary>Discards an open frame without uploading (e.g. the renderer decided nothing changed).</summary>
    public void DiscardFrame()
    {
        EnsureOwningThread();
        _frameOpen = false;
    }

    /// <summary>Waits for the next paced frame (no-op for <see cref="LayeredPacingMode.None"/>).</summary>
    public void WaitForNextFrame() => _pacer?.WaitForNextFrame();

    /// <summary>
    /// Replaces the pacing strategy at runtime (settings change / display refresh change). The
    /// previous pacer is disposed; the new one starts with a fresh deadline clock.
    /// </summary>
    /// <param name="mode">Pacing strategy.</param>
    /// <param name="targetFramesPerSecond">Target rate for <see cref="LayeredPacingMode.TargetFrameRate"/>.</param>
    public void ConfigurePacing(LayeredPacingMode mode, double targetFramesPerSecond = 60)
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _pacer?.Dispose();
        _pacer = new LayeredFramePacer(mode, targetFramesPerSecond);
    }

    /// <summary>Pacing counters for the current measurement window.</summary>
    public LayeredPacingStats GetPacingStats() => _pacer?.GetStats() ?? default;

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private unsafe LayeredCommitResult CommitCore(ReadOnlySpan<LayeredRect> dirtyRects, bool fullFrame)
    {
        EnsureOwningThread();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ThrowIfNotCreated();
        if (!_frameOpen)
        {
            throw new InvalidOperationException("BeginFrame() must be called before CommitFrame().");
        }

        _frameOpen = false;
        var surface = _surface!;
        var useDirty = !fullFrame && !_needsFullFrame && dirtyRects.Length > 0;

        var started = Stopwatch.GetTimestamp();
        var uploaded = 0;
        var ok = true;
        var usedFallback = false;
        var error = 0;

        if (useDirty)
        {
            var max = Math.Max(1, _options.MaxDirtyRects);
            if (MergeDirtyRects)
            {
                var union = LayeredRect.Empty;
                foreach (var raw in dirtyRects)
                {
                    var rect = raw.ClampTo(surface.WidthPx, surface.HeightPx);
                    if (rect.IsEmpty || rect.Area < _options.MinDirtyAreaPixels)
                    {
                        continue;
                    }

                    union = union.Union(rect);
                    if (++uploaded >= max)
                    {
                        break;
                    }
                }

                if (union.IsEmpty)
                {
                    return LayeredCommitResult.None;
                }

                (ok, error, usedFallback) = TryUpdate(union);
                uploaded = 1;
            }
            else
            {
                foreach (var raw in dirtyRects)
                {
                    var rect = raw.ClampTo(surface.WidthPx, surface.HeightPx);
                    if (rect.IsEmpty || rect.Area < _options.MinDirtyAreaPixels)
                    {
                        continue;
                    }

                    var (rectOk, rectError, rectFallback) = TryUpdate(rect);
                    ok &= rectOk;
                    usedFallback |= rectFallback;
                    if (!rectOk)
                    {
                        error = rectError;
                    }

                    uploaded++;
                    if (uploaded >= max)
                    {
                        break;
                    }
                }

                if (uploaded == 0)
                {
                    return LayeredCommitResult.None;
                }
            }
        }
        else
        {
            (ok, error, usedFallback) = TryUpdate(null);
            uploaded = 1;
        }

        var elapsed = Stopwatch.GetTimestamp() - started;
        var microseconds = elapsed * 1_000_000.0 / Stopwatch.Frequency;

        if (ok)
        {
            _needsFullFrame = false;
            _geometryDirty = false;
            ReassertTopmostIfDue();
            if (_options.LogFrames)
            {
                _logger.LogTrace(PlatformLogCategory.Platform, "layered-host.frame",
                    "Layered frame committed.",
                    new Dictionary<string, object?>
                    {
                        ["frame"] = _frameIndex,
                        ["dirty"] = uploaded,
                        ["full"] = !useDirty,
                        ["us"] = Math.Round(microseconds, 1),
                    });
            }
        }
        else
        {
            _needsFullFrame = true;
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.update-failed",
                "UpdateLayeredWindow failed. " + Win32Error.Format(error));
        }

        return new LayeredCommitResult(ok, !useDirty, uploaded, microseconds, error, usedFallback);
    }

    private unsafe (bool Ok, int Error, bool UsedFallback) TryUpdate(LayeredRect? dirty)
    {
        var surface = _surface!;
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = NativeMethods.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = NativeMethods.AC_SRC_ALPHA,
        };
        var source = new NativeMethods.POINT(0, 0);
        var destination = default(NativeMethods.POINT);
        var size = default(NativeMethods.SIZE);
        var dirtyRect = default(NativeMethods.RECT);

        var info = new NativeMethods.UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(NativeMethods.UPDATELAYEREDWINDOWINFO),
            hdcDst = _screenDc,
            hdcSrc = surface.Hdc,
            pptSrc = (IntPtr)(&source),
            pblend = (IntPtr)(&blend),
            dwFlags = NativeMethods.ULW_ALPHA,
        };

        if (_options.UseExplicitUpdateGeometry || _geometryDirty)
        {
            if (!NativeMethods.User32.GetWindowRect(_hwnd, out var windowRect))
            {
                return (false, Marshal.GetLastWin32Error(), false);
            }

            destination = new NativeMethods.POINT(windowRect.Left, windowRect.Top);
            size = new NativeMethods.SIZE(windowRect.Width, windowRect.Height);
            info.pptDst = (IntPtr)(&destination);
            info.psize = (IntPtr)(&size);
        }

        if (dirty is { } rect)
        {
            dirtyRect = new NativeMethods.RECT
            {
                Left = rect.X,
                Top = rect.Y,
                Right = rect.Right,
                Bottom = rect.Bottom,
            };
            info.prcDirty = (IntPtr)(&dirtyRect);
        }

        if (NativeMethods.User32.UpdateLayeredWindowIndirect(_hwnd, ref info))
        {
            _geometryDirty = false;
            return (true, 0, false);
        }

        var error = Marshal.GetLastWin32Error();

        // Canonical fallback: explicit position/size, the exact call the known-good layered probe uses.
        if (!NativeMethods.User32.GetWindowRect(_hwnd, out var fallbackRect))
        {
            return (false, error, false);
        }

        var fallbackDestination = new NativeMethods.POINT(fallbackRect.Left, fallbackRect.Top);
        var fallbackSize = new NativeMethods.SIZE(fallbackRect.Width, fallbackRect.Height);
        if (NativeMethods.User32.UpdateLayeredWindow(_hwnd, _screenDc, ref fallbackDestination, ref fallbackSize,
                surface.Hdc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA))
        {
            _geometryDirty = false;
            return (true, 0, true);
        }

        return (false, Marshal.GetLastWin32Error(), false);
    }

    private LayeredHostPlacement ApplyPlacement(MonitorInfo monitor, LayeredHostMode mode, uint dpi, double scale,
        LayeredRect windowRect, LayeredSurfaceSize surfaceSize, VisualPoint offsetDip, LayeredPixelSize sizePx)
    {
        var scaleChanged = Math.Abs(scale - _scale) > 0.0001;
        var modeChanged = mode != _mode;
        if (modeChanged || scaleChanged)
        {
            _mode = mode;
            _dpi = dpi;
            _scale = scale;
            RebuildSurface(sizePx);
            _pacer?.Reset();
        }
        else
        {
            _dpi = dpi;
        }

        _monitor = monitor;
        ApplyWindowRect(windowRect);
        _needsFullFrame = true;
        _geometryDirty = true;
        _placement = new LayeredHostPlacement(windowRect, sizePx, surfaceSize, scale,
            OffsetToPixels(offsetDip, scale));
        UpdateUiaGeometry();
        return _placement;
    }

    private void ApplyWindowRect(in LayeredRect rect)
    {
        if (_interop is not null)
        {
            _interop.PlacePhysical(_hwnd, rect.X, rect.Y, rect.Width, rect.Height);
            return;
        }

        if (!NativeMethods.User32.SetWindowPos(_hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.place-failed",
                "SetWindowPos(place) failed. " + Win32Error.Format(Marshal.GetLastWin32Error()));
        }
    }

    private void RebuildSurface(LayeredPixelSize sizePx)
    {
        if (_surface is null)
        {
            _surface = LayeredSurface.Create(sizePx.Width, sizePx.Height);
            return;
        }

        _surface.Recreate(sizePx.Width, sizePx.Height);
    }

    private void SetTopmostUnconditional()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (_interop is not null)
        {
            _interop.SetTopmost(_hwnd);
            _lastTopmostTimestamp = _timeProvider.GetTimestamp();
            return;
        }

        if (!NativeMethods.User32.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.topmost-failed",
                "SetWindowPos(HWND_TOPMOST) failed. " + Win32Error.Format(Marshal.GetLastWin32Error()));
            return;
        }

        _lastTopmostTimestamp = _timeProvider.GetTimestamp();
    }

    /// <summary>
    /// Policy-gated topmost re-assert: throttled to the configured cadence, never above the Windows
    /// shell foreground window (Start menu/taskbar/task view/tooltips), never while a full-screen app
    /// owns the session.
    /// </summary>
    private void ReassertTopmostIfDue()
    {
        if (!_options.ReassertTopmost || !_visible)
        {
            return;
        }

        var sinceLast = _lastTopmostTimestamp == 0
            ? TimeSpan.MaxValue
            : _timeProvider.GetElapsedTime(_lastTopmostTimestamp);
        if (sinceLast < _options.TopmostReassertInterval)
        {
            return;
        }

        if (_interop is not null)
        {
            _interop.TryReassertTopmost(_hwnd);
            _lastTopmostTimestamp = _timeProvider.GetTimestamp();
            return;
        }

        var context = new TopmostContext(IslandWindowInterop.GetForegroundWindowClass(), _visible,
            IslandWindowInterop.QueryUserNotificationState(), sinceLast);
        if (TopmostPolicy.Evaluate(context) != TopmostDecision.Reassert)
        {
            _lastTopmostTimestamp = _timeProvider.GetTimestamp();
            return;
        }

        if (!NativeMethods.User32.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE))
        {
            _counters.IncrementWin32Failures();
            _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.topmost-failed",
                "SetWindowPos(HWND_TOPMOST) failed. " + Win32Error.Format(Marshal.GetLastWin32Error()));
            return;
        }

        _lastTopmostTimestamp = _timeProvider.GetTimestamp();
    }

    private IntPtr CursorHandle(LayeredCursorKind kind)
    {
        if (_cursorHandle != IntPtr.Zero)
        {
            return _cursorHandle;
        }

        var id = kind switch
        {
            LayeredCursorKind.Hand => NativeMethods.IDC_HAND,
            LayeredCursorKind.IBeam => NativeMethods.IDC_IBEAM,
            LayeredCursorKind.SizeAll => NativeMethods.IDC_SIZEALL,
            LayeredCursorKind.SizeWE => NativeMethods.IDC_SIZEWE,
            LayeredCursorKind.SizeNS => NativeMethods.IDC_SIZENS,
            _ => NativeMethods.IDC_ARROW,
        };
        _cursorHandle = NativeMethods.User32.LoadCursorW(IntPtr.Zero, id);
        return _cursorHandle;
    }

    private static LayeredPixelSize OffsetToPixels(VisualPoint offsetDip, double scale)
        => new(
            (int)Math.Round(offsetDip.X * scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(offsetDip.Y * scale, MidpointRounding.AwayFromZero));

    private static MonitorInfo CreateSyntheticMonitor(in WindowPlacement placement, double scale)
    {
        var dpi = (uint)Math.Round(scale * 96);
        var width = (int)Math.Ceiling(placement.WindowVisual.Width * scale);
        var height = (int)Math.Ceiling(placement.WindowVisual.Height * scale);
        return new MonitorInfo("placement", 0, new Display.PixelRect(0, 0, width, height),
            new Display.PixelRect(0, 0, width, height), dpi, dpi, false);
    }

    private void EnsureOwningThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "LayeredWindowHost is thread-affine: every call must happen on the thread that constructed it.");
        }
    }

    private void ThrowIfNotCreated()
    {
        if (!_created)
        {
            throw new InvalidOperationException("LayeredWindowHost.Create() must be called first.");
        }
    }

    /// <summary>Destroys the window and releases the DIB. Idempotent; must run on the owning thread.</summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (_created && Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "LayeredWindowHost.Dispose() must run on the thread that created the window.");
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            NativeMethods.User32.ReleaseCapture();
        }

        DisposeAccessibility();

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.User32.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_USERDATA, IntPtr.Zero);
            if (!NativeMethods.User32.DestroyWindow(_hwnd))
            {
                _logger.LogWarning(PlatformLogCategory.Platform, "layered-host.destroy-failed",
                    "DestroyWindow failed. " + Win32Error.Format(Marshal.GetLastWin32Error()));
            }

            _hwnd = IntPtr.Zero;
        }

        if (_screenDc != IntPtr.Zero)
        {
            _ = NativeMethods.User32.ReleaseDC(IntPtr.Zero, _screenDc);
            _screenDc = IntPtr.Zero;
        }

        _surface?.Dispose();
        _surface = null;
        _pacer?.Dispose();
        _pacer = null;
        _created = false;
        _visible = false;

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _logger.LogInfo(PlatformLogCategory.Platform, "layered-host.disposed", "Layered island host disposed.");
    }
}
