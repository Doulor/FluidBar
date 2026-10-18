using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FluidBar.Core.Layout;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Hosting;
using MonitorInfo = FluidBar.Platform.Windows.Display.MonitorInfo;
using PixelRect = FluidBar.Platform.Windows.Display.PixelRect;

namespace FluidBar.Spikes.LayeredHost;

/// <summary>
/// FluidBar v2 layered-host spike. Proves a Win32 per-pixel-alpha layered island host: transparency,
/// click-through outside the shell, no focus steal, drag, and the cost of full-frame vs dirty-rect
/// <c>UpdateLayeredWindow</c> at 150% DPI.
/// </summary>
/// <remarks>
/// <para><c>--auto</c> (default) runs the whole evidence + measurement script and writes
/// <c>results.md</c>, <c>results.json</c> and <c>evidence/*.png</c> next to the project.</para>
/// <para><c>--manual</c> just shows the island on a checkerboard backdrop for 60 s (eyeball check).</para>
/// <para><c>--quick</c> shortens the measurement loops (smoke run).</para>
/// </remarks>
internal static class Program
{
    private const string ProjectFileName = "LayeredHostSpike.csproj";

    private static readonly StringBuilder Log = new();
    private static readonly List<string> Checks = new();
    private static readonly List<string> EvidenceFiles = new();
    private static readonly List<string> Measurements = new();
    private static string _projectRoot = "";
    private static string _evidenceDir = "";
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        TryAttachConsole();
        var manual = HasFlag(args, "--manual");
        var quick = HasFlag(args, "--quick");
        _projectRoot = FindProjectRoot();
        _evidenceDir = Path.Combine(_projectRoot, "evidence");
        Directory.CreateDirectory(_evidenceDir);

        try
        {
            // The manifest already declares PerMonitorV2; the API call keeps a manual `dotnet run` honest.
            SpikeNative.SetProcessDpiAwarenessContext(new IntPtr(-4));
            var exitCode = manual ? RunManual() : RunAuto(quick);
            WriteLogFile();
            return exitCode;
        }
        catch (Exception exception)
        {
            LogLine("FATAL: " + exception);
            WriteLogFile();
            return 2;
        }
    }

    // ------------------------------------------------------------------
    // Auto run: evidence + measurements
    // ------------------------------------------------------------------

    private static int RunAuto(bool quick)
    {
        var monitor = QueryPrimaryMonitor();
        var scale = monitor.Scale;
        LogLine($"monitor work={monitor.WorkArea} bounds={monitor.Bounds} dpi={monitor.DpiX} scale={scale:0.###}");

        // Deterministic checkerboard behind the island (transparent pixels must show it).
        const int backdropWidth = 900;
        const int backdropHeight = 760;
        var backdropX = monitor.WorkArea.X + Math.Max(0, (monitor.WorkArea.Width - backdropWidth) / 2);
        var backdropY = monitor.WorkArea.Y + Math.Max(0, (monitor.WorkArea.Height - backdropHeight) / 2);
        BackdropWindow.Trace = LogLine;
        using var backdrop = BackdropWindow.Create(backdropX, backdropY, backdropWidth, backdropHeight);
        var backdropRectCheck = GetWindowRect(backdrop.Handle);
        LogLine($"backdrop hwnd=0x{backdrop.Handle:X} at ({backdropX},{backdropY}) {backdropWidth}x{backdropHeight} " +
            $"visible={SpikeNative.IsWindowVisible(backdrop.Handle)} actual=({backdropRectCheck.Left},{backdropRectCheck.Top}," +
            $"{backdropRectCheck.Width}x{backdropRectCheck.Height})");
        var backdropProbe = Evidence.Capture(backdropX + 12, backdropY + 12, 8, 8);
        var backdropProbePixel = backdropProbe.Pixel(0, 0);
        LogLine($"backdrop probe rgb=({backdropProbePixel.R},{backdropProbePixel.G},{backdropProbePixel.B})");
        var atProbe = SpikeNative.WindowFromPoint(new SpikeNative.POINT(backdropX + 12, backdropY + 12));
        LogLine($"window at probe: hwnd=0x{atProbe:X} class={ClassNameOf(atProbe)} " +
            $"backdropExStyle=0x{SpikeNative.GetWindowLongPtrW(backdrop.Handle, -20).ToInt64():X} " +
            $"prevAbove=0x{SpikeNative.GetWindow(backdrop.Handle, 3):X}");

        // Baseline: what the desktop shows before the island exists. Transparent probes must be
        // unchanged from this frame, which proves per-pixel alpha without depending on z-order.
        PumpFor(120);
        var baseline = Evidence.Capture(backdropX, backdropY, backdropWidth, backdropHeight);

        using var renderer = new IslandRenderer();
        var logger = new DelegatingPlatformLogger(
            entry => LogLine($"  [platform:{entry.Level}] {entry.EventId} {entry.Message}" +
                (entry.Exception is null ? string.Empty : $" EXCEPTION: {entry.Exception}")),
            PlatformLogLevel.Info);
        var counters = new PlatformCounters();
        using var host = new LayeredWindowHost(new LayeredHostOptions
        {
            Pacing = LayeredPacingMode.None,
            MergeDirtyRects = true,
            ReassertTopmost = true,
            UseExplicitUpdateGeometry = false,
        }, logger: logger, counters: counters);

        var drag = DragSupport.Attach(host, LogLine);
        drag.Verbose = true;

        host.Create(monitor, LayeredHostMode.Float);
        var floatShell = ShellDipFor(monitor, backdropX + 40, backdropY + 40, LayeredHostMode.Float);
        var placement = host.Place(monitor, floatShell, LayeredHostMode.Float);
        ApplyHitRegion(host, renderer, LayeredHostMode.Float);

        var firstFrame = host.BeginFrame();
        renderer.RenderFull(firstFrame, LayeredHostMode.Float, 0);
        var firstCommit = host.CommitFrame();
        host.Show();
        PumpFor(200);
        RaiseBackdropAndHost(backdrop, host);

        LogLine($"host hwnd=0x{host.Handle:X} window={placement.WindowPixel} surface={placement.SurfaceSizePx} " +
            $"dip={placement.SurfaceSizeDip} dpi={host.Dpi} scale={host.Scale:0.###} " +
            $"firstCommit ok={firstCommit.Updated} us={firstCommit.UpdateMicroseconds:0.0} fallback={firstCommit.UsedFallback}");
        Check("host created at 150% DPI physical size",
            placement.SurfaceSizePx.Width == (int)Math.Ceiling(488 * scale)
                && placement.SurfaceSizePx.Height == (int)Math.Ceiling(384 * scale),
            $"surface={placement.SurfaceSizePx} expected={(int)Math.Ceiling(488 * scale)}x{(int)Math.Ceiling(384 * scale)}");

        // ---------------- visual evidence ----------------
        var full = Evidence.Capture(backdropX, backdropY, backdropWidth, backdropHeight);
        SaveEvidence(full, "01-float-full.png");
        SaveEvidence(Evidence.Capture(placement.WindowPixel.X - 20, placement.WindowPixel.Y - 20, 280, 280),
            "02-float-corner-zoom.png");
        SaveEvidence(Evidence.Capture(
            placement.WindowPixel.X - 10,
            placement.WindowPixel.Y + (int)Math.Round(296 * scale),
            (int)Math.Round(508 * scale),
            (int)Math.Round(96 * scale)),
            "03-float-detached-lane.png");

        // ---------------- pixel probes ----------------
        // Host-local DIP probes (host origin = window top-left).
        Probe("corner is transparent", full, baseline, host, 4, 4, expectTransparent: true);
        Probe("panel centre is opaque", full, baseline, host, 244, 166, expectTransparent: false);
        Probe("lane between pills is transparent", full, baseline, host, 169, 363, expectTransparent: true);
        Probe("detached pill is opaque", full, baseline, host, 244, 363, expectTransparent: false);
        Probe("bottom bleed is transparent", full, baseline, host, 244, 380, expectTransparent: true);
        Probe("right bleed is transparent", full, baseline, host, 480, 200, expectTransparent: true);

        // A bright island pixel proves opacity even on a dark desktop (where "dark == desktop" would be
        // ambiguous): the artwork tile is a saturated gradient that cannot be wallpaper.
        var logoX = ScreenX(host, 90);
        var logoY = ScreenY(host, 150);
        var (logoR, logoG, logoB) = full.PixelAtScreen(logoX, logoY);
        Check("pixel: artwork tile is opaque and bright", logoR + logoG + logoB > 250,
            $"screen=({logoX},{logoY}) rgb=({logoR},{logoG},{logoB})");

        // ---------------- click-through (real WindowFromPoint) ----------------
        var cornerHit = WindowAt(host, 4, 4);
        var panelHit = WindowAt(host, 244, 166);
        var gapHit = WindowAt(host, 169, 363);
        var pillHit = WindowAt(host, 244, 363);
        var bleedHit = WindowAt(host, 244, 380);
        Check("click-through: transparent corner does not hit the host", cornerHit != host.Handle && cornerHit != IntPtr.Zero,
            $"WindowFromPoint(corner)=0x{cornerHit:X} ({ClassNameOf(cornerHit)}) backdrop=0x{backdrop.Handle:X} host=0x{host.Handle:X}");
        Check("click-through: panel receives the click", panelHit == host.Handle, $"WindowFromPoint(panel)=0x{panelHit:X}");
        Check("click-through: gap between detached pills does not hit the host", gapHit != host.Handle,
            $"WindowFromPoint(gap)=0x{gapHit:X} ({ClassNameOf(gapHit)})");
        Check("click-through: detached pill receives the click", pillHit == host.Handle,
            $"WindowFromPoint(pill)=0x{pillHit:X}");
        Check("click-through: bottom bleed does not hit the host", bleedHit != host.Handle,
            $"WindowFromPoint(bleed)=0x{bleedHit:X} ({ClassNameOf(bleedHit)})");

        var hitTestCorner = SendHitTest(host, 4, 4);
        var hitTestPanel = SendHitTest(host, 244, 166);
        Check("WM_NCHITTEST policy", hitTestCorner == -1 && hitTestPanel == 1,
            $"HT(corner)={hitTestCorner} HT(panel)={hitTestPanel} (HTTRANSPARENT=-1, HTCLIENT=1)");

        // ---------------- focus + drag (synthetic input) ----------------
        var foregroundBefore = SpikeNative.GetForegroundWindow();
        var panelCentreX = ScreenX(host, 244);
        var panelCentreY = ScreenY(host, 166);
        var originalCursor = new SpikeNative.POINT();
        SpikeNative.GetCursorPos(out originalCursor);

        // Stay clear of the double-click interval so the click test and the drag test are two
        // independent single clicks.
        PumpFor(600);
        MoveCursor(panelCentreX, panelCentreY);
        PumpFor(80);
        SpikeNative.GetCursorPos(out var cursorNow);
        LogLine($"cursor=({cursorNow.X},{cursorNow.Y}) target=({panelCentreX},{panelCentreY}) " +
            $"sendInputDown={ClickLeft()} pointerMoves={drag.Moves} pointerPresses={drag.Presses}");
        PumpFor(200);
        var foregroundAfterClick = SpikeNative.GetForegroundWindow();
        Check("click does not activate the island", foregroundAfterClick != host.Handle,
            $"foreground before=0x{foregroundBefore:X} after=0x{foregroundAfterClick:X} host=0x{host.Handle:X}");
        Check("click leaves the foreground window unchanged", foregroundAfterClick == foregroundBefore,
            $"foreground before=0x{foregroundBefore:X} after=0x{foregroundAfterClick:X}");

        // Drag: the cursor is the ground truth. A live desktop (or a remote-desktop cursor) can move
        // the pointer mid-gesture, so the gesture is retried and compared against the measured cursor
        // delta rather than the nominal one.
        var draggedX = 0;
        var draggedY = 0;
        var cursorDeltaX = 0;
        var cursorDeltaY = 0;
        var dragAttempts = 0;
        var dragStartedBefore = drag.Started;
        for (; dragAttempts < 3; dragAttempts++)
        {
            var rectBefore = GetWindowRect(host.Handle);
            MoveCursor(panelCentreX, panelCentreY);
            PumpFor(120);
            LeftDown();
            PumpFor(60);
            SpikeNative.GetCursorPos(out var cursorAtPress);
            for (var i = 1; i <= 10; i++)
            {
                MoveCursor(panelCentreX - (i * 12), panelCentreY - (i * 8));
                PumpFor(25);
            }

            SpikeNative.GetCursorPos(out var cursorAtRelease);
            LeftUp();
            PumpFor(200);
            var rectAfter = GetWindowRect(host.Handle);
            draggedX = rectAfter.Left - rectBefore.Left;
            draggedY = rectAfter.Top - rectBefore.Top;
            cursorDeltaX = cursorAtRelease.X - cursorAtPress.X;
            cursorDeltaY = cursorAtRelease.Y - cursorAtPress.Y;
            if (draggedX == cursorDeltaX && draggedY == cursorDeltaY
                && Math.Abs(cursorDeltaX + 120) <= 2 && Math.Abs(cursorDeltaY + 80) <= 2)
            {
                break;
            }

            host.Place(monitor, floatShell, LayeredHostMode.Float);
            PumpFor(150);
        }

        dragAttempts++;
        Check("drag moves the host by the exact pointer delta", draggedX == cursorDeltaX && draggedY == cursorDeltaY,
            $"window moved ({draggedX},{draggedY}) cursor moved ({cursorDeltaX},{cursorDeltaY}) in {dragAttempts} attempt(s); " +
            $"dragStarted={drag.Started - dragStartedBefore} applied={drag.Applied} cancelled={drag.Cancelled} " +
            $"moves={drag.Moves} presses={drag.Presses} lastButtons={drag.LastButtons}");
        Check("drag reached the requested distance", Math.Abs(cursorDeltaX + 120) <= 2 && Math.Abs(cursorDeltaY + 80) <= 2,
            $"cursor delta ({cursorDeltaX},{cursorDeltaY}) vs requested (-120,-80) in {dragAttempts} attempt(s)");
        Check("drag does not activate the island", SpikeNative.GetForegroundWindow() != host.Handle,
            $"foreground=0x{SpikeNative.GetForegroundWindow():X} host=0x{host.Handle:X}");
        SaveEvidence(Evidence.Capture(backdropX, backdropY, backdropWidth, backdropHeight), "04-after-drag.png");

        // Restore and restore the cursor so the rest of the run is not affected.
        host.Place(monitor, floatShell, LayeredHostMode.Float);
        MoveCursor(originalCursor.X, originalCursor.Y);
        PumpFor(120);

        // ---------------- measurements ----------------
        var frameCount = quick ? 120 : 300;
        host.ConfigurePacing(LayeredPacingMode.None);
        var fullLoop = SpikeBenchmarks.RunFullFrameLoop(host, renderer, LayeredHostMode.Float, frameCount);
        Record(fullLoop.Upload);
        Record(fullLoop.Frame);
        Measurements.Add($"| full frame (488x384 DIP) | {fullLoop.Upload.P50Us:0.0} | {fullLoop.Upload.P95Us:0.0} | " +
            $"{fullLoop.Upload.P99Us:0.0} | {fullLoop.Upload.MaxUs:0.0} | {fullLoop.Frame.P50Us:0.0} | " +
            $"{fullLoop.AllocatedBytesPerFrame:0} B | {fullLoop.CpuPercentOfOneCore:0.0}% |");
        Check("full-frame updates all succeeded", fullLoop.AllUpdatesSucceeded,
            $"fallbacks={fullLoop.FallbackUpdates} lastError={fullLoop.LastWin32Error}");
        Check("zero per-frame managed allocation (full frame)", fullLoop.AllocatedBytesPerFrame < 1024,
            $"{fullLoop.AllocatedBytesPerFrame:0} B/frame");
        LogLine(fullLoop.Upload.ToRow());
        LogLine(fullLoop.Frame.ToRow());
        LogLine($"full loop: alloc={fullLoop.AllocatedBytesPerFrame:0} B/frame cpu={fullLoop.CpuPercentOfOneCore:0.0}% " +
            $"fps={fullLoop.FramesPerSecond:0.0} fallbacks={fullLoop.FallbackUpdates}");

        // Shadow cost: the drop-shadow image filter is the dominant full-frame cost, so it is
        // measured separately (production should bake the shadow into a bitmap once).
        renderer.ShadowEnabled = false;
        var fullNoShadow = SpikeBenchmarks.RunFullFrameLoop(host, renderer, LayeredHostMode.Float, frameCount);
        renderer.ShadowEnabled = true;
        Record(fullNoShadow.Upload);
        Measurements.Add($"| full frame, shadow off | {fullNoShadow.Upload.P50Us:0.0} | {fullNoShadow.Upload.P95Us:0.0} | " +
            $"{fullNoShadow.Upload.P99Us:0.0} | {fullNoShadow.Upload.MaxUs:0.0} | {fullNoShadow.Frame.P50Us:0.0} | " +
            $"{fullNoShadow.AllocatedBytesPerFrame:0} B | {fullNoShadow.CpuPercentOfOneCore:0.0}% |");
        LogLine(fullNoShadow.Frame.ToRow());

        host.MergeDirtyRects = true;
        var dirtyMerged = SpikeBenchmarks.RunDirtyLoop(host, renderer, LayeredHostMode.Float, frameCount);
        Record(dirtyMerged.Upload);
        Measurements.Add($"| dirty rects, merged (2 -> 1 box) | {dirtyMerged.Upload.P50Us:0.0} | {dirtyMerged.Upload.P95Us:0.0} | " +
            $"{dirtyMerged.Upload.P99Us:0.0} | {dirtyMerged.Upload.MaxUs:0.0} | {dirtyMerged.Frame.P50Us:0.0} | " +
            $"{dirtyMerged.AllocatedBytesPerFrame:0} B | {dirtyMerged.CpuPercentOfOneCore:0.0}% |");
        Check("dirty-rect updates all succeeded", dirtyMerged.AllUpdatesSucceeded,
            $"fallbacks={dirtyMerged.FallbackUpdates} lastError={dirtyMerged.LastWin32Error}");
        Check("zero per-frame managed allocation (dirty)", dirtyMerged.AllocatedBytesPerFrame < 1024,
            $"{dirtyMerged.AllocatedBytesPerFrame:0} B/frame");
        LogLine(dirtyMerged.Upload.ToRow());

        host.MergeDirtyRects = false;
        var dirtyMulti = SpikeBenchmarks.RunDirtyLoop(host, renderer, LayeredHostMode.Float, frameCount);
        Record(dirtyMulti.Upload);
        Measurements.Add($"| dirty rects, 2 uploads | {dirtyMulti.Upload.P50Us:0.0} | {dirtyMulti.Upload.P95Us:0.0} | " +
            $"{dirtyMulti.Upload.P99Us:0.0} | {dirtyMulti.Upload.MaxUs:0.0} | {dirtyMulti.Frame.P50Us:0.0} | " +
            $"{dirtyMulti.AllocatedBytesPerFrame:0} B | {dirtyMulti.CpuPercentOfOneCore:0.0}% |");
        LogLine(dirtyMulti.Upload.ToRow());
        host.MergeDirtyRects = true;

        var pacing60 = SpikeBenchmarks.RunPacedLoop(host, renderer, LayeredHostMode.Float,
            LayeredPacingMode.TargetFrameRate, 60, quick ? 90 : 180);
        var pacing120 = SpikeBenchmarks.RunPacedLoop(host, renderer, LayeredHostMode.Float,
            LayeredPacingMode.TargetFrameRate, 120, quick ? 180 : 360);
        var pacingComposition = SpikeBenchmarks.RunPacedLoop(host, renderer, LayeredHostMode.Float,
            LayeredPacingMode.Composition, 0, quick ? 60 : 120);
        Measurements.Add($"| paced 60 fps | {pacing60.EffectiveFps:0.0} fps | p99 interval {pacing60.P99IntervalMs:0.00} ms | " +
            $"max {pacing60.MaxIntervalMs:0.00} ms | late {pacing60.LateFrames} | upload {pacing60.UploadMeanUs:0.0} us |");
        Measurements.Add($"| paced 120 fps | {pacing120.EffectiveFps:0.0} fps | p99 interval {pacing120.P99IntervalMs:0.00} ms | " +
            $"max {pacing120.MaxIntervalMs:0.00} ms | late {pacing120.LateFrames} | upload {pacing120.UploadMeanUs:0.0} us |");
        Measurements.Add($"| composition (DwmFlush) | {pacingComposition.EffectiveFps:0.0} fps | " +
            $"p99 interval {pacingComposition.P99IntervalMs:0.00} ms | max {pacingComposition.MaxIntervalMs:0.00} ms | " +
            $"late {pacingComposition.LateFrames} | upload {pacingComposition.UploadMeanUs:0.0} us |");
        LogLine($"pacing 60: {pacing60.EffectiveFps:0.0} fps p99={pacing60.P99IntervalMs:0.00}ms late={pacing60.LateFrames}");
        LogLine($"pacing 120: {pacing120.EffectiveFps:0.0} fps p99={pacing120.P99IntervalMs:0.00}ms late={pacing120.LateFrames}");
        LogLine($"pacing composition: {pacingComposition.EffectiveFps:0.0} fps p99={pacingComposition.P99IntervalMs:0.00}ms");
        Check("60 fps pacing holds the target", pacing60.EffectiveFps >= 57 && pacing60.P99IntervalMs <= 20,
            $"effective={pacing60.EffectiveFps:0.0} fps p99={pacing60.P99IntervalMs:0.00} ms");
        Check("120 fps attempt is recorded (target 8.33 ms)", pacing120.EffectiveFps >= 100,
            $"effective={pacing120.EffectiveFps:0.0} fps p99={pacing120.P99IntervalMs:0.00} ms");

        // Idle: no CommitFrame calls at all, only the message pump.
        host.ConfigurePacing(LayeredPacingMode.None);
        var idleCpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var idleStarted = Stopwatch.GetTimestamp();
        // 6 s: Process.TotalProcessorTime has ~15.6 ms granularity, so a 2 s window is dominated by
        // timer quantization. The host itself performs zero uploads in this window.
        PumpFor(6000, idleSleepMilliseconds: 20);
        var idleWallMs = (Stopwatch.GetTimestamp() - idleStarted) * 1000.0 / Stopwatch.Frequency;
        var idleCpu = Process.GetCurrentProcess().TotalProcessorTime - idleCpuBefore;
        var idlePercent = idleWallMs <= 0 ? 0 : idleCpu.TotalMilliseconds / idleWallMs * 100.0;
        Measurements.Add($"| idle (6 s, no commits) | - | - | - | - | - | - | {idlePercent:0.00}% |");
        Check("idle performs no uploads and stays near zero CPU", idlePercent < 1.0,
            $"{idlePercent:0.00}% of one core over {idleWallMs:0} ms with 0 CommitFrame calls");
        LogLine($"idle: {idlePercent:0.00}% of one core over {idleWallMs:0}ms, 0 uploads");

        // ---------------- dock mode (488x444 DIP) ----------------
        host.SetMode(LayeredHostMode.Dock);
        // Surface-local Y of the dock strip bottom is (height - padding), so the host origin is that
        // many pixels above the wanted strip bottom.
        var dockHostY = backdropY + backdropHeight - 40
            - (int)Math.Round((LayeredSurfaceSize.DockHeight - LayeredSurfaceSize.Padding) * scale);
        var dockShell = ShellDipFor(monitor, backdropX + 40, dockHostY, LayeredHostMode.Dock);
        var dockPlacement = host.Place(monitor, dockShell, LayeredHostMode.Dock);
        ApplyHitRegion(host, renderer, LayeredHostMode.Dock);
        var dockFrame = host.BeginFrame();
        renderer.RenderFull(dockFrame, LayeredHostMode.Dock, 0.4);
        var dockCommit = host.CommitFrame();
        PumpFor(200);
        RaiseBackdropAndHost(backdrop, host);
        Check("dock surface is 488x444 DIP", dockPlacement.SurfaceSizePx.Width == (int)Math.Ceiling(488 * scale)
            && dockPlacement.SurfaceSizePx.Height == (int)Math.Ceiling(444 * scale),
            $"surface={dockPlacement.SurfaceSizePx}");
        SaveEvidence(Evidence.Capture(backdropX, backdropY, backdropWidth, backdropHeight), "05-dock-full.png");
        var dockProbeCorner = WindowAt(host, 4, 4);
        Check("dock: corner still click-through", dockProbeCorner != host.Handle,
            $"WindowFromPoint(corner)=0x{dockProbeCorner:X} ({ClassNameOf(dockProbeCorner)})");

        var dockLoop = SpikeBenchmarks.RunFullFrameLoop(host, renderer, LayeredHostMode.Dock, quick ? 60 : 120);
        Record(dockLoop.Upload);
        Measurements.Add($"| dock full frame (488x444 DIP) | {dockLoop.Upload.P50Us:0.0} | {dockLoop.Upload.P95Us:0.0} | " +
            $"{dockLoop.Upload.P99Us:0.0} | {dockLoop.Upload.MaxUs:0.0} | {dockLoop.Frame.P50Us:0.0} | " +
            $"{dockLoop.AllocatedBytesPerFrame:0} B | {dockLoop.CpuPercentOfOneCore:0.0}% |");
        LogLine(dockLoop.Upload.ToRow());
        LogLine($"dock commit ok={dockCommit.Updated} us={dockCommit.UpdateMicroseconds:0.0}");

        host.Hide();
        PumpFor(100);

        var snapshot = counters.Snapshot();
        Check("no Win32 failures were counted", snapshot.Win32Failures == 0, $"win32Failures={snapshot.Win32Failures}");

        WriteResults(monitor, fullLoop, dirtyMerged, dirtyMulti, pacing60, pacing120, pacingComposition,
            idlePercent, dockLoop, snapshot.Win32Failures);
        LogLine($"DONE failures={_failures} evidence={EvidenceFiles.Count} results={Path.Combine(_projectRoot, "results.md")}");
        return _failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // Manual run
    // ------------------------------------------------------------------

    private static int RunManual()
    {
        var monitor = QueryPrimaryMonitor();
        var scale = monitor.Scale;
        const int backdropWidth = 900;
        const int backdropHeight = 760;
        var backdropX = monitor.WorkArea.X + Math.Max(0, (monitor.WorkArea.Width - backdropWidth) / 2);
        var backdropY = monitor.WorkArea.Y + Math.Max(0, (monitor.WorkArea.Height - backdropHeight) / 2);
        BackdropWindow.Trace = LogLine;
        using var backdrop = BackdropWindow.Create(backdropX, backdropY, backdropWidth, backdropHeight);
        using var renderer = new IslandRenderer();
        using var host = new LayeredWindowHost(new LayeredHostOptions { Pacing = LayeredPacingMode.None });
        var drag = DragSupport.Attach(host, LogLine);
        drag.Verbose = true;

        host.Create(monitor, LayeredHostMode.Float);
        var shell = ShellDipFor(monitor, backdropX + 40, backdropY + 40, LayeredHostMode.Float);
        host.Place(monitor, shell, LayeredHostMode.Float);
        ApplyHitRegion(host, renderer, LayeredHostMode.Float);
        var frame = host.BeginFrame();
        renderer.RenderFull(frame, LayeredHostMode.Float, 0);
        host.CommitFrame();
        host.Show();
        LogLine($"manual: island shown at 150% ({scale:0.###}); window={host.Placement.WindowPixel}; " +
            "exiting in 60 s");
        for (var i = 0; i < 12; i++)
        {
            PumpFor(5000);
            LogLine($"manual: still running ({(i + 1) * 5}s)");
        }

        return 0;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static MonitorInfo QueryPrimaryMonitor()
    {
        var point = new SpikeNative.POINT(0, 0);
        var monitorHandle = SpikeNative.MonitorFromPoint(point, SpikeNative.MONITOR_DEFAULTTOPRIMARY);
        var info = new SpikeNative.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<SpikeNative.MONITORINFO>(),
        };
        if (!SpikeNative.GetMonitorInfoW(monitorHandle, ref info))
        {
            throw new InvalidOperationException("GetMonitorInfoW failed.");
        }

        SpikeNative.GetDpiForMonitor(monitorHandle, SpikeNative.MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY);
        return new MonitorInfo(
            "primary",
            0,
            new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Width, info.rcMonitor.Height),
            new PixelRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Width, info.rcWork.Height),
            dpiX == 0 ? 96 : dpiX,
            dpiY == 0 ? dpiX : dpiY,
            true);
    }

    /// <summary>Work-area DIP rectangle of the mode's anchor shell that puts the host at (hostX, hostY) px.</summary>
    private static VisualRect ShellDipFor(MonitorInfo monitor, int hostX, int hostY, LayeredHostMode mode)
    {
        var scale = monitor.Scale;
        // The spike always anchors the compact form: float uses the panel top, dock uses the strip top.
        var offset = LayeredSurfaceSize.ShellLocalOffset(mode, ShellForm.Compact);
        var x = ((hostX - monitor.WorkArea.X) / scale) + offset.X;
        var y = ((hostY - monitor.WorkArea.Y) / scale) + offset.Y;
        var size = mode == LayeredHostMode.Dock
            ? new VisualRect(x, y, LayeredSurfaceSize.PanelWidth, LayeredSurfaceSize.DockStripHeight)
            : new VisualRect(x, y, LayeredSurfaceSize.PanelWidth, LayeredSurfaceSize.PanelHeight);
        return size;
    }

    /// <summary>
    /// Puts the backdrop in the topmost band and then the host above it. Other applications can be
    /// topmost too (the DSH browser window is), so evidence captures must re-assert the order.
    /// </summary>
    private static void RaiseBackdropAndHost(BackdropWindow backdrop, LayeredWindowHost host)
    {
        SpikeNative.SetWindowPos(backdrop.Handle, SpikeNative.HwndTopmost, 0, 0, 0, 0,
            SpikeNative.SwpNoMove | SpikeNative.SwpNoSize | SpikeNative.SwpNoActivate | SpikeNative.SwpShowWindow);
        SpikeNative.SetWindowPos(host.Handle, SpikeNative.HwndTopmost, 0, 0, 0, 0,
            SpikeNative.SwpNoMove | SpikeNative.SwpNoSize | SpikeNative.SwpNoActivate);
        PumpFor(150);
    }

    private static void ApplyHitRegion(LayeredWindowHost host, IslandRenderer renderer, LayeredHostMode mode)
    {
        host.HitRegion.SetShell(IslandRenderer.ShellRect(mode), IslandRenderer.ShellRadii(mode));
        host.HitRegion.SetInteractiveRects(renderer.InteractiveRects(mode));
        host.HitRegion.HasInteractiveContent = true;
    }

    private static int ScreenX(LayeredWindowHost host, double hostDipX)
        => host.Placement.WindowPixel.X + (int)Math.Round(hostDipX * host.Scale);

    private static int ScreenY(LayeredWindowHost host, double hostDipY)
        => host.Placement.WindowPixel.Y + (int)Math.Round(hostDipY * host.Scale);

    private static IntPtr WindowAt(LayeredWindowHost host, double hostDipX, double hostDipY)
        => SpikeNative.WindowFromPoint(new SpikeNative.POINT(ScreenX(host, hostDipX), ScreenY(host, hostDipY)));

    private static int SendHitTest(LayeredWindowHost host, double hostDipX, double hostDipY)
    {
        var x = ScreenX(host, hostDipX);
        var y = ScreenY(host, hostDipY);
        var packed = (IntPtr)((y << 16) | (x & 0xFFFF));
        var result = SpikeNative.SendMessageW(host.Handle, SpikeNative.WM_NCHITTEST, IntPtr.Zero, packed);
        return result.ToInt32();
    }

    private static void Probe(string name, CapturedFrame frame, CapturedFrame baseline, LayeredWindowHost host,
        double hostDipX, double hostDipY, bool expectTransparent)
    {
        var screenX = ScreenX(host, hostDipX);
        var screenY = ScreenY(host, hostDipY);
        var (r, g, b) = frame.PixelAtScreen(screenX, screenY);
        var (br, bg, bb) = baseline.PixelAtScreen(screenX, screenY);
        var unchanged = Math.Abs(r - br) <= 6 && Math.Abs(g - bg) <= 6 && Math.Abs(b - bb) <= 6;
        var checker = frame.IsBackdrop(screenX, screenY, 12);
        var island = frame.IsDarkIsland(screenX, screenY);
        var ok = expectTransparent ? unchanged || checker : island;
        Check($"pixel: {name}", ok,
            $"host-dip=({hostDipX:0},{hostDipY:0}) screen=({screenX},{screenY}) rgb=({r},{g},{b}) " +
            $"baseline=({br},{bg},{bb}) unchanged={unchanged} checker={checker} island={island}");
    }

    private static void SaveEvidence(CapturedFrame frame, string fileName)
    {
        var path = Path.Combine(_evidenceDir, fileName);
        Evidence.SavePng(frame, path);
        EvidenceFiles.Add(fileName);
        LogLine($"evidence: {path} ({frame.Width}x{frame.Height})");
    }

    private static void Record(TimingStats stats)
    {
        Measurements.Add($"| {stats.Name} | {stats.P50Us:0.0} | {stats.P95Us:0.0} | {stats.P99Us:0.0} | " +
            $"{stats.MaxUs:0.0} | - | - | - |");
    }

    private static void Check(string name, bool condition, string detail)
    {
        Checks.Add($"| {(condition ? "PASS" : "FAIL")} | {name} | {detail} |");
        LogLine($"{(condition ? "PASS" : "FAIL")} {name}: {detail}");
        if (!condition)
        {
            _failures++;
        }
    }

    private static void LogLine(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Log.AppendLine(line);
        Console.WriteLine(line);
    }

    private static void WriteLogFile()
    {
        try
        {
            File.WriteAllText(Path.Combine(_projectRoot, "spike.log"), Log.ToString());
        }
        catch (IOException)
        {
            // Logging must never take the spike down.
        }
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ProjectFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static bool HasFlag(string[] args, string flag)
        => args.Any(argument => string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase));

    private static void TryAttachConsole()
    {
        try
        {
            if (!SpikeNative.AttachConsole(unchecked((uint)-1)) && !SpikeNative.AllocConsole())
            {
                return;
            }

            var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(output);
            var error = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(error);
        }
        catch (IOException)
        {
            // No console available (redirected/daemon launch): the log file still records everything.
        }
    }

    private static void PumpFor(int milliseconds, int idleSleepMilliseconds = 1)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(milliseconds / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            while (SpikeNative.PeekMessageW(out var message, IntPtr.Zero, 0, 0, SpikeNative.PM_REMOVE))
            {
                if (message.message == SpikeNative.WM_QUIT)
                {
                    return;
                }

                SpikeNative.TranslateMessage(ref message);
                SpikeNative.DispatchMessageW(ref message);
            }

            Thread.Sleep(idleSleepMilliseconds);
        }
    }

    private static void MoveCursor(int x, int y) => SpikeNative.SetCursorPos(x, y);

    private static uint ClickLeft()
    {
        var down = LeftDown();
        Thread.Sleep(30);
        var up = LeftUp();
        return down + up;
    }

    private static uint LeftDown()
    {
        var input = new SpikeNative.INPUT
        {
            type = SpikeNative.INPUT_MOUSE,
            u = new SpikeNative.INPUTUNION
            {
                mi = new SpikeNative.MOUSEINPUT { dwFlags = SpikeNative.MOUSEEVENTF_LEFTDOWN },
            },
        };
        return SpikeNative.SendInput(1, ref input, Marshal.SizeOf<SpikeNative.INPUT>());
    }

    private static uint LeftUp()
    {
        var input = new SpikeNative.INPUT
        {
            type = SpikeNative.INPUT_MOUSE,
            u = new SpikeNative.INPUTUNION
            {
                mi = new SpikeNative.MOUSEINPUT { dwFlags = SpikeNative.MOUSEEVENTF_LEFTUP },
            },
        };
        return SpikeNative.SendInput(1, ref input, Marshal.SizeOf<SpikeNative.INPUT>());
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "<null>";
        }

        var buffer = new char[256];
        var length = SpikeNative.GetClassNameW(hwnd, buffer, buffer.Length);
        return length <= 0 ? "<unknown>" : new string(buffer, 0, Math.Min(length, buffer.Length));
    }

    private static SpikeNative.RECT GetWindowRect(IntPtr hwnd)
    {
        SpikeNative.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    private static void WriteResults(MonitorInfo monitor, FrameLoopResult full, FrameLoopResult dirtyMerged,
        FrameLoopResult dirtyMulti, PacingResult pacing60, PacingResult pacing120, PacingResult pacingComposition,
        double idlePercent, FrameLoopResult dock, long win32Failures)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# Layered host spike — results");
        markdown.AppendLine();
        markdown.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  ");
        markdown.AppendLine($"Machine: {Environment.MachineName}, .NET {Environment.Version}, Windows {Environment.OSVersion.Version}  ");
        markdown.AppendLine($"Monitor: work={monitor.WorkArea} dpi={monitor.DpiX} scale={monitor.Scale:0.###}  ");
        markdown.AppendLine($"Host surfaces: float 488x384 DIP = {(int)Math.Ceiling(488 * monitor.Scale)}x" +
            $"{(int)Math.Ceiling(384 * monitor.Scale)} px, dock 488x444 DIP = " +
            $"{(int)Math.Ceiling(488 * monitor.Scale)}x{(int)Math.Ceiling(444 * monitor.Scale)} px");
        markdown.AppendLine();
        markdown.AppendLine("## Verdicts");
        markdown.AppendLine();
        markdown.AppendLine("| Result | Check | Detail |");
        markdown.AppendLine("|---|---|---|");
        foreach (var check in Checks)
        {
            markdown.AppendLine(check);
        }

        markdown.AppendLine();
        markdown.AppendLine("## Frame costs (microseconds per frame)");
        markdown.AppendLine();
        markdown.AppendLine("| Path | p50 | p95 | p99 | max | frame total p50 | alloc/frame | CPU (1 core) |");
        markdown.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var measurement in Measurements)
        {
            markdown.AppendLine(measurement);
        }

        markdown.AppendLine();
        markdown.AppendLine("## Raw notes");
        markdown.AppendLine();
        markdown.AppendLine($"- Win32 failures counted by `PlatformCounters`: {win32Failures}");
        markdown.AppendLine($"- Idle CPU: {idlePercent:0.00}% of one core over 6 s with zero commits");
        markdown.AppendLine($"- Full-frame loop: {full.FramesPerSecond:0.0} fps unpaced, {full.AllocatedBytesPerFrame:0} B/frame allocated");
        markdown.AppendLine($"- Dirty loop (merged): {dirtyMerged.Upload.P50Us:0.0} us p50 vs full {full.Upload.P50Us:0.0} us p50");
        markdown.AppendLine($"- Dirty loop (2 uploads): {dirtyMulti.Upload.P50Us:0.0} us p50, " +
            $"{dirtyMulti.FramesPerSecond:0.0} fps unpaced");
        markdown.AppendLine($"- Dock full frame: {dock.Upload.P50Us:0.0} us p50 ({dock.Upload.P99Us:0.0} us p99)");
        markdown.AppendLine();
        markdown.AppendLine("## Evidence");
        markdown.AppendLine();
        foreach (var file in EvidenceFiles)
        {
            markdown.AppendLine($"- `evidence/{file}`");
        }

        markdown.AppendLine();
        markdown.AppendLine("## Files");
        markdown.AppendLine();
        markdown.AppendLine("- `LayeredHostSpike.csproj`, `Program.cs`, `IslandRenderer.cs`, `Evidence.cs`, " +
            "`SpikeBenchmarks.cs`, `BackdropWindow.cs`, `SpikeNative.cs`");
        markdown.AppendLine("- `spike.log` (full console transcript)");

        File.WriteAllText(Path.Combine(_projectRoot, "results.md"), markdown.ToString());

        var json = JsonSerializer.Serialize(new
        {
            generated = DateTimeOffset.Now,
            machine = Environment.MachineName,
            monitor = new
            {
                workArea = monitor.WorkArea.ToString(),
                dpi = monitor.DpiX,
                scale = monitor.Scale,
                floatSurfacePx = new
                {
                    width = (int)Math.Ceiling(488 * monitor.Scale),
                    height = (int)Math.Ceiling(384 * monitor.Scale),
                },
                dockSurfacePx = new
                {
                    width = (int)Math.Ceiling(488 * monitor.Scale),
                    height = (int)Math.Ceiling(444 * monitor.Scale),
                },
            },
            checks = Checks.Select(line => line.Trim('|').Split('|').Select(part => part.Trim()).ToArray()),
            frameCostsUs = new
            {
                full = new { p50 = full.Upload.P50Us, p95 = full.Upload.P95Us, p99 = full.Upload.P99Us, max = full.Upload.MaxUs },
                dirtyMerged = new { p50 = dirtyMerged.Upload.P50Us, p95 = dirtyMerged.Upload.P95Us, p99 = dirtyMerged.Upload.P99Us, max = dirtyMerged.Upload.MaxUs },
                dirtyMulti = new { p50 = dirtyMulti.Upload.P50Us, p95 = dirtyMulti.Upload.P95Us, p99 = dirtyMulti.Upload.P99Us, max = dirtyMulti.Upload.MaxUs },
                dockFull = new { p50 = dock.Upload.P50Us, p95 = dock.Upload.P95Us, p99 = dock.Upload.P99Us, max = dock.Upload.MaxUs },
            },
            allocationBytesPerFrame = new
            {
                full = full.AllocatedBytesPerFrame,
                dirty = dirtyMerged.AllocatedBytesPerFrame,
            },
            cpuPercentOfOneCore = new
            {
                full = full.CpuPercentOfOneCore,
                dirty = dirtyMerged.CpuPercentOfOneCore,
                idle = idlePercent,
            },
            pacing = new
            {
                fps60 = new { effective = pacing60.EffectiveFps, p99Ms = pacing60.P99IntervalMs, late = pacing60.LateFrames },
                fps120 = new { effective = pacing120.EffectiveFps, p99Ms = pacing120.P99IntervalMs, late = pacing120.LateFrames },
                composition = new { effective = pacingComposition.EffectiveFps, p99Ms = pacingComposition.P99IntervalMs },
            },
            win32Failures,
            failures = _failures,
            evidence = EvidenceFiles,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_projectRoot, "results.json"), json);
    }
}
