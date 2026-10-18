# 05 — Avalonia 12 Windows Spike (fixed-size transparent overlay)

**Date:** 2026-10-18 · **Spike:** `%TEMP%\fluidbar-spikes\av-win2` · **Verdict: GO** — no blocker found that forces the WPF fixed-size fallback.

**Time-boxed:** single-pass spike, 5 runs of the same binary; only the spike dir was written. `D:\build\GitLocal\FluidBar` untouched.

## Environment (as observed)

| Item | Value |
|---|---|
| OS | Windows 11 build 26200 (`Microsoft Windows NT 10.0.26200.0`) |
| .NET SDK | 10.0.301, runtime 10.0.9 |
| Avalonia | 12.1.4 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`), NuGet reachable; cold restore 3.1 min |
| GPU / display | 1 screen exposed to the session: 2560×1600, work area 0,0,2560,1600, scaling **1.5**; `Screens.All.Count = 1` — **no virtual displays were visible to this process**, so secondary-monitor placement could not be exercised |
| Window | 488×332 DIP = **732×498 px** at 1.5×, fixed HWND, top-center at (914,0) |
| Build | `net10.0-windows10.0.19041.0`, `WinExe`, `ApplicationManifest=app.manifest` (PerMonitorV2), **0 warnings / 0 errors**, incremental build ~1.0 s |

## Verdicts

| Requirement | Verdict | Headline evidence |
|---|---|---|
| Build (net10 + Avalonia 12.1.4, no templates/XAML) | **GO** | code-only `Application`/`Window`/`Control`; builds clean |
| Per-pixel transparency (corners show desktop) | **GO** | `TransparencyLevelHint=[Transparent]`, `ActualTransparencyLevel=Transparent`; PNG corner `#D3D3D3`, center `#08080A` |
| Fixed-window spring animation ≈120 fps potential | **GO** | HWND never resized; paint **p50 6.05 ms = 165.2 fps**, p99 8.11 ms, 489 paints / 3.003 s |
| Hit-test / click-through / no-activate / no-taskbar | **GO (with test caveat)** | `WM_NCHITTEST`: inside→1 (HTCLIENT), outside→−1 (HTTRANSPARENT), rounded-corner→−1; `WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW` verified; valid click run kept Chrome foreground |
| Idle cost after loop stop | **GO** | 0 renders during idle; CPU 0–15.6 ms / 3 s (≤0.52 % of one core); WS 157 MB |
| Acrylic (secondary) | **GO (secondary)** | second window: `ActualTransparencyLevel=AcrylicBlur`, no throw, visibly blurred desktop (`acrylic.png`) |
| Multi-monitor | **NO-GO (untested — environment)** | only 1 `Screen`; API path compiles/works, placement branch never ran |
| Clean shutdown | **GO** | windows closed, `exit=0`, log flushed |

**Overall: GO.** The measured 165 fps cadence gives 37 % headroom over 120 fps; p99 8.11 ms is still below the 8.33 ms 120 Hz budget. Nothing here justifies falling back to WPF for a fixed-size overlay.

## Requirement detail + exact numbers

### 1. Transparency — GO
- `TransparencyLevelHint = new[]{ WindowTransparencyLevel.Transparent }` set before `Show()`.
- After show: `ActualTransparencyLevel = Transparent`; `TransparencyBackgroundFallback = White` (i.e. if composition ever fails, the window turns **white**, not black — a useful failure signature).
- Screenshot `evidence.png` (732×498 physical px), captured with `Graphics.CopyFromScreen`:
  - `(2,2)` = `#D3D3D3`, `(0,0)` = `#D3D3D3`, bottom-right margin `#D3D3D3` — desktop wallpaper, **not** the `#08080A` island and **not** the white fallback.
  - window center = `#08080A` exactly (island fill).
  - adjacent desktop reference at (908,2) = `#F9FAFB` (wallpaper is a light gradient; corner value varies per run: `#818181`, `#D3D3D3`).
- Visually: rounded island floats on the desktop, corners fully transparent, 1 px light stroke, Fluent text crisp.

### 2. Fixed-window animation / frame pacing — GO
- Window is **never resized or moved during animation**. Only `IslandControl.IslandWidth/Height` change and `InvalidateVisual()` is called; the compositor repaints the whole fixed surface.
- `TopLevel.RequestAnimationFrame(Action<TimeSpan>)` is **one-shot**; re-arm at the end of each callback. It is a true per-compositor-frame hook here — RAF ticks ≈ paints (491 vs 489 in the 3 s window).
- 3.003 s recording, 489 painted frames:
  - **p50 = 6.05 ms → 165.2 fps** (matches a 165 Hz display; refresh is inferred, not queried from Avalonia)
  - p95 = 6.82 ms, **p99 = 8.11 ms**, max = 24.20 ms, min = 2.91 ms
  - mean = 6.13 ms → 163.2 fps over the window (≈2 % of frames missed; occasional JIT/scheduling outliers account for the max)
  - Across 4 successful runs: mean fps 160.8–164.1, p99 8.08–11.42 ms. Debug build, no warm-up beyond one show.
- Spring tokens implemented exactly: width `T=0.36, ζ=0.82`, height `T=0.39, ζ=0.86`, semi-implicit Euler at fixed **1/300 s** substeps, accumulator clamped to 250 ms.
  - Observed: `pW 0.455 @ 0.5 s`, `0.859 @ 1.0 s`, `0.992 @ 1.5 s`, `1.0105 @ 2.0 s` (+1.05 % overshoot), `1.0054 @ 2.5 s`; `pH` similar. So the island is still moving on every one of the 3 s frames (no static tail), visually settled ~1.5–2 s, numerically <0.2 % at ~3.3 s. That 0.2 % tail is the only tuning surprise; snap earlier if final-pixel exactness matters.
- Fallback `DispatcherTimer(1 ms, RenderPriority)` existed but **never activated** (`fallback=False`).

### 3. Hit-test / pass-through — GO
`SetWindowLongPtrW(hwnd, GWLP_WNDPROC, …)` subclass installed after show; old proc chained with `CallWindowProcW`. `WM_NCHITTEST` converts the signed 16-bit screen lParam with `PointToClient` → DIP, then does rounded-rect containment.
```
SendMessage(hwnd, WM_NCHITTEST, 0, MAKELPARAM(inside))  ->  1   (HTCLIENT)      inside (1280,249)
SendMessage(hwnd, WM_NCHITTEST, 0, MAKELPARAM(outside)) -> -1   (HTTRANSPARENT) outside (917,3)
SendMessage(... rounded-corner-outside (953,39))        -> -1   (HTTRANSPARENT)
```
The transparent margin therefore passes clicks through; inside the squircle the window is a normal client. `WM_MOUSEACTIVATE` handler returning `MA_NOACTIVATE` is also installed but **never fires** with `WS_EX_NOACTIVATE` (count 0) — keep it as belt-and-braces, not as the mechanism.

### 4. No-activate / no-taskbar — GO (test caveat)
- Ex-style before `0x00200008`, set/verify `0x08200088`: `WS_EX_NOACTIVATE=True`, `WS_EX_TOOLWINDOW=True`, `WS_EX_LAYERED` already present. `ShowInTaskbar=false`, `ShowActivated=false`, `Topmost=true`.
- Valid synthesized-click run: `WM_LBUTTONDOWN` delta = 1 (click reached the Avalonia HWND), `GetForegroundWindow` before = after = Chrome (`pid 9812`, our window `pid 15220`), so the click did **not** activate us.
- **Caveat:** in later runs another process in the session hijacked the cursor (`SetCursorPos(1280,249)` landed at `(791,941)`) and changed the foreground window independently; those runs are invalid, not a failure. `Window.IsActive` is unreliable — it reported `True` even while `GetForegroundWindow` was another process. Verify with the HWND's PID, not `IsActive`.

### 5. Idle after settle — GO
Loop stopped (no re-arm, no timer), window still visible. `rendersDuringIdle = 0`.
- WorkingSet64 = **157.4 MB** (range 156–159 MB across runs), threads = 156 (Debug build; Skia/.NET pools).
- CPU `TotalProcessorTime` delta over 3 s = **0.00 ms** on the final run (earlier runs 15.62 ms/3 s = **0.52 % of one core**); the 15.6 ms quantum is `Process.TotalProcessorTime` granularity, so true idle is ≲0.5 % of one core.
- Hidden-window control: 15.62 ms / 2 s (0.78 % of one core) — i.e. noise/JIT, no compositor spin. One outlier run showed 125 ms/3 s (4.2 % of one core) during startup JIT; it did not reproduce.

### 6. Acrylic — GO (secondary)
Second window, same content, `TransparencyLevelHint=[AcrylicBlur]`, shown at (120,620). No exception; `ActualTransparencyLevel = AcrylicBlur`.
- `acrylic.png`: corner `(2,2)` = `#F1F2F3` (blurred light desktop), center `#295073` (blurred blue desktop), island `#08080A`; the image clearly shows a frosted/blurred backdrop vs. the crisp transparent window.
- Acrylic is therefore usable as an alternate mode, but note it changes the whole window's backdrop, not just the island; keep Transparent as the default for the product.

### 7. Multi-monitor — NO-GO (environment only)
- `Screens.All.Count = 1`; screen: bounds `0,0,2560,1600`, work area identical, `Scaling=1.5`, `IsPrimary=true`, **`DisplayName=""`** (platform quirk).
- Code path `Screens.All[1]` → set `Window.Position` → `TopLevel.RenderScaling` / `PointToScreen` was compiled but never exercised. No virtual displays were exposed to this session, so the secondary-monitor requirement remains unverified. This is the only requirement not proven, and it is an environment limitation rather than an Avalonia limitation.

## Avalonia 12 APIs — worked vs failed

**Worked, exactly as used:**
- `AppBuilder.Configure<SpikeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)`
- `Styles.Add(new FluentTheme())` (code-only; no XAML, no templates)
- `Window.WindowDecorations = Avalonia.Controls.WindowDecorations.None` — **Avalonia 12 rename**; `Window.SystemDecorations` is `[Obsolete]` (still works)
- `TopLevel.TransparencyLevelHint` (`IReadOnlyList<WindowTransparencyLevel>`) and `TopLevel.ActualTransparencyLevel`, `TransparencyBackgroundFallback`
- `TopLevel.RequestAnimationFrame(Action<TimeSpan>)` — per-frame, one-shot; the supported replacement for a compositor callback
- `Window.TryGetPlatformHandle()?.Handle` → HWND
- `TopLevel.ClientSize`, `TopLevel.RenderScaling`
- `VisualExtensions.PointToScreen(Point) -> PixelPoint`, `PointToClient(PixelPoint) -> Point`
- `Window.Screens.All / Primary / ScreenFromWindow(window)`, `Screen.WorkingArea / Bounds / Scaling / IsPrimary`, `Window.Position`
- `Window.ShowActivated`, `ShowInTaskbar`, `Topmost`, `CanResize`
- Win32: `SetWindowLongPtrW(GWLP_WNDPROC/GWL_EXSTYLE)`, `CallWindowProcW`, `SetWindowPos(SWP_FRAMECHANGED)`, `SendMessageW`, `GetWindowLongPtrW`, `GetForegroundWindow`, `SetCursorPos/mouse_event`

**Failed / absent / surprising:**
- `Avalonia.Rendering.Composition.CompositionTarget.Rendering` — **no public event** in the 12.1.4 XML docs (only `RequestRedraw`, `TryHitTest`). Do not plan on it; `RequestAnimationFrame` is the hook.
- `Screens.ScreenFromWindow(window)` is an **instance** method (called via `window.Screens`), not static — a static call gives CS0120.
- `Graphics.CopyFromScreen(..., CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt)` throws `InvalidEnumArgumentException` under .NET 10 / System.Drawing.Common 9.0.0. Use **`CopyPixelOperation.SourceCopy` alone** (works, captures DWM-composed transparent content).
- `Window.IsActive` can be `true` while the window is not foreground — never use it as no-activate proof.
- `Screen.DisplayName` returns `""`.
- `WM_MOUSEACTIVATE` is suppressed by `WS_EX_NOACTIVATE`; activation prevention is the ex-style's job.

## Pitfalls for the real implementation

1. **Keep the HWND fixed.** Never set `Width/Height/Position` per frame; animate geometry inside `Render`. The whole 3 s test ran with a constant 488×332 DIP window.
2. **Transparency:** set the hint before `Show()`. On Win11 the default `WinUIComposition` path handles per-pixel alpha; `TransparencyBackgroundFallback=White` means a white window is your "transparency broke" signal.
3. **RAF is one-shot** — re-arm from inside the callback and call `InvalidateVisual()` each tick; that is what drives the compositor. If it ever produces nothing, keep the 1 ms Render-priority `DispatcherTimer` watchdog (it did not trigger here).
4. **Physics from `Stopwatch`, not the `TimeSpan` argument**; accumulate and consume fixed `1/300 s` substeps, clamp frame dt to 250 ms, cap substeps to avoid a spiral of death.
5. **WndProc delegate must be rooted** (static field) or it can be GC'd → crash. Chain to the old proc for everything else. Convert `WM_NCHITTEST` lParam with signed 16-bit extraction, then `PointToClient` to DIP before rounded-rect math.
6. **`SetWindowLongPtr` ex-styles need `SetWindowPos(..., SWP_FRAMECHANGED)`** and must be applied after `Show()` when the HWND exists.
7. **Screenshot after show** with `PointToScreen(new Point(0,0))` + `ClientSize * RenderScaling`; `CopyFromScreen` only accepts `SourceCopy`.
8. **CPU measurement:** `TotalProcessorTime` has ~15.6 ms granularity — sample ≥3 s; a 0.00 ms delta is "below timer resolution", not literally zero.
9. **Debug build** inflates WorkingSet/threads and startup JIT; re-measure Release before setting a memory budget.
10. **Multi-monitor:** not testable in this session; test on the target multi-display/165 Hz machine before shipping.

## Compact reusable skeleton

`Spike.csproj` (exact, worked):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.4" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.4" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.1.4" />
    <PackageReference Include="System.Drawing.Common" Version="9.0.0" />
  </ItemGroup>
</Project>
```
`app.manifest`: `<dpiAwareness>PerMonitorV2</dpiAwareness>` (plus `dpiAware=true/pm`, `longPathAware`, Win10 supportedOS GUID).

```csharp
// Program.cs — code-only app, no XAML
using Avalonia; using Avalonia.Controls; using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media; using Avalonia.Threading; using System.Diagnostics; using System.Runtime.InteropServices;

static class Program {
    [STAThread] static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

sealed class App : Application {
    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) {
            var w = new IslandWindow(); d.MainWindow = w; w.Show();
            _ = new Runner(w, d).RunAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

sealed class IslandWindow : Window {
    public readonly Island Island = new();
    public IslandWindow() {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None; // Avalonia 12 (SystemDecorations obsolete)
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; CanResize = false;
        Width = 488; Height = 332; WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Colors.Transparent);
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Content = Island;
    }
}

sealed class Island : Control {
    static readonly IBrush Fill = new SolidColorBrush(Color.Parse("#08080A"));
    static readonly IPen Stroke = new Pen(new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)), 1);
    static readonly Typeface Face = new("Segoe UI");
    public double W { get; set; } = 132, H { get; set; } = 36;
    public override void Render(DrawingContext c) {
        var r = new Rect(24, 24, W, H);
        c.DrawRectangle(Fill, Stroke, new RoundedRect(r, Math.Min(26, H / 2)));
        var t = new FormattedText("FluidBar", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Face, 12.5, Brushes.White);
        c.DrawText(t, new Point(r.Center.X - t.Width / 2, r.Center.Y - t.Height / 2));
    }
}

sealed class Runner {
    const double T_W = 0.36, Z_W = 0.82, T_H = 0.39, Z_H = 0.86, H = 1.0 / 300.0;
    readonly IslandWindow _w; readonly IClassicDesktopStyleApplicationLifetime _life;
    double _pW, _vW, _pH, _vH, _acc, _last; bool _loop;

    public Runner(IslandWindow w, IClassicDesktopStyleApplicationLifetime life) { _w = w; _life = life; }
    public async Task RunAsync() {
        await Task.Delay(600);
        var hwnd = _w.TryGetPlatformHandle()!.Handle;   // Win32 HWND
        InstallHook(hwnd);
        ApplyExStyles(hwnd);                            // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW + SWP_FRAMECHANGED
        _loop = true; _last = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _w.RequestAnimationFrame(Tick);
        await Task.Delay(3000); _loop = false;          // ...run test/screenshot here...
        await Task.Delay(200); _w.Close(); _life.Shutdown();
    }

    void Tick(TimeSpan _) {
        if (!_loop) return;
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _acc += Math.Min(0.25, _last > 0 ? now - _last : 0); _last = now;
        while (_acc >= H) { Step(ref _pW, ref _vW, 1, T_W, Z_W); Step(ref _pH, ref _vH, 1, T_H, Z_H); _acc -= H; }
        _w.Island.W = 132 + 308 * _pW; _w.Island.H = 36 + 248 * _pH;
        _w.Island.InvalidateVisual();
        _w.RequestAnimationFrame(Tick);                 // one-shot: re-arm
    }
    static void Step(ref double p, ref double v, double target, double T, double z) {
        v += (1.0 / (T * T) * (target - p) - 2.0 * z / T * v) * H;
        p += v * H;
        if (Math.Abs(target - p) < 0.0005 && Math.Abs(v) < 0.002) { p = target; v = 0; }
    }

    // --- Win32 subclass ---
    const int GWLP_WNDPROC = -4, GWL_EXSTYLE = -20;
    const long WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    const uint WM_NCHITTEST = 0x84;
    delegate IntPtr WndProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    static WndProc? _hook; static IntPtr _old; static IslandWindow? _win;
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProcW(IntPtr p, IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);

    void InstallHook(IntPtr hwnd) {
        _win = _w; _hook = Proc;
        _old = SetWindowLongPtrW(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_hook));
    }
    static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l) {
        if (msg == WM_NCHITTEST && _win != null) {
            int x = unchecked((short)(long)l), y = unchecked((short)((long)l >> 16));
            var p = _win.PointToClient(new PixelPoint(x, y));
            return Inside(p, _win.Island.W, _win.Island.H) ? (IntPtr)1 : (IntPtr)(-1); // HTCLIENT / HTTRANSPARENT
        }
        return CallWindowProcW(_old, h, msg, w, l);
    }
    static bool Inside(Point p, double w, double h) {
        var r = new Rect(24, 24, w, h); if (!r.Contains(p)) return false;
        double rad = Math.Min(26, h / 2);
        if (p.X >= r.Left + rad && p.X <= r.Right - rad) return true;
        if (p.Y >= r.Top + rad && p.Y <= r.Bottom - rad) return true;
        double cx = p.X < r.Left + rad ? r.Left + rad : r.Right - rad;
        double cy = p.Y < r.Top + rad ? r.Top + rad : r.Bottom - rad;
        return (p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy) <= rad * rad;
    }
    static void ApplyExStyles(IntPtr h) {
        long ex = GetWindowLongPtrW(h, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtrW(h, GWL_EXSTYLE, (IntPtr)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0004 | 0x0020); // NOMOVE|NOSIZE|NOZORDER|FRAMECHANGED
    }
}
```

Hit-test proof:
```csharp
long lp = ((long)(y & 0xFFFF) << 16) | (uint)(x & 0xFFFF);
var r = SendMessageW(hwnd, 0x84, IntPtr.Zero, (IntPtr)lp); // 1 = HTCLIENT, -1 = HTTRANSPARENT
```

## Artifacts

- `C:\Users\Doulor\AppData\Local\Temp\fluidbar-spikes\av-win2\evidence.png` — transparent window, corners show desktop, island `#08080A`
- `...\acrylic.png` — AcrylicBlur window, visibly frosted corners
- `...\results.log` — full instrumented run (45 lines)
- `...\Program.cs`, `...\Spike.csproj`, `...\app.manifest`, `...\bin\…\Spike.exe`
