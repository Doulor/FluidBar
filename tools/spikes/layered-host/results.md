# Layered host spike — results

Generated: 2026-10-18 21:35:40 +08:00  
Machine: TX-LAPTOP, .NET 10.0.9, Windows 10.0.26200.0  
Monitor: work=(0,0 2560x1600) dpi=144 scale=1.5  
Host surfaces: float 488x384 DIP = 732x576 px, dock 488x444 DIP = 732x666 px

## Verdicts

| Result | Check | Detail |
|---|---|---|
| PASS | host created at 150% DPI physical size | surface=732x576px expected=732x576 |
| PASS | pixel: corner is transparent | host-dip=(4,4) screen=(876,466) rgb=(225,29,72) baseline=(225,29,72) unchanged=True checker=True island=False |
| PASS | pixel: panel centre is opaque | host-dip=(244,166) screen=(1236,709) rgb=(26,29,38) baseline=(14,165,233) unchanged=False checker=False island=True |
| PASS | pixel: lane between pills is transparent | host-dip=(169,363) screen=(1124,1004) rgb=(14,165,233) baseline=(14,165,233) unchanged=True checker=True island=False |
| PASS | pixel: detached pill is opaque | host-dip=(244,363) screen=(1236,1004) rgb=(31,19,29) baseline=(225,29,72) unchanged=False checker=False island=True |
| PASS | pixel: bottom bleed is transparent | host-dip=(244,380) screen=(1236,1030) rgb=(14,165,233) baseline=(14,165,233) unchanged=True checker=True island=False |
| PASS | pixel: right bleed is transparent | host-dip=(480,200) screen=(1590,760) rgb=(13,157,222) baseline=(14,165,233) unchanged=False checker=True island=False |
| PASS | pixel: artwork tile is opaque and bright | screen=(1005,685) rgb=(236,72,153) |
| PASS | click-through: transparent corner does not hit the host | WindowFromPoint(corner)=0x72047A (FluidBar.Spike.CheckerBackdrop) backdrop=0x72047A host=0x1CC0744 |
| PASS | click-through: panel receives the click | WindowFromPoint(panel)=0x1CC0744 |
| PASS | click-through: gap between detached pills does not hit the host | WindowFromPoint(gap)=0x72047A (FluidBar.Spike.CheckerBackdrop) |
| PASS | click-through: detached pill receives the click | WindowFromPoint(pill)=0x1CC0744 |
| PASS | click-through: bottom bleed does not hit the host | WindowFromPoint(bleed)=0x72047A (FluidBar.Spike.CheckerBackdrop) |
| PASS | WM_NCHITTEST policy | HT(corner)=-1 HT(panel)=1 (HTTRANSPARENT=-1, HTCLIENT=1) |
| PASS | click does not activate the island | foreground before=0x1D1518 after=0x1D1518 host=0x1CC0744 |
| PASS | click leaves the foreground window unchanged | foreground before=0x1D1518 after=0x1D1518 |
| PASS | drag moves the host by the exact pointer delta | window moved (-120,-80) cursor moved (-120,-80) in 2 attempt(s); dragStarted=2 applied=26 cancelled=0 moves=60 presses=3 lastButtons=None |
| PASS | drag reached the requested distance | cursor delta (-120,-80) vs requested (-120,-80) in 2 attempt(s) |
| PASS | drag does not activate the island | foreground=0x1D1518 host=0x1CC0744 |
| PASS | full-frame updates all succeeded | fallbacks=0 lastError=0 |
| PASS | zero per-frame managed allocation (full frame) | 5 B/frame |
| PASS | dirty-rect updates all succeeded | fallbacks=0 lastError=0 |
| PASS | zero per-frame managed allocation (dirty) | 1 B/frame |
| PASS | 60 fps pacing holds the target | effective=60.0 fps p99=18.03 ms |
| PASS | 120 fps attempt is recorded (target 8.33 ms) | effective=120.0 fps p99=20.06 ms |
| PASS | idle performs no uploads and stays near zero CPU | 0.00% of one core over 6026 ms with 0 CommitFrame calls |
| PASS | dock surface is 488x444 DIP | surface=732x666px |
| PASS | dock: corner still click-through | WindowFromPoint(corner)=0x72047A (FluidBar.Spike.CheckerBackdrop) |
| PASS | no Win32 failures were counted | win32Failures=0 |

## Frame costs (microseconds per frame)

| Path | p50 | p95 | p99 | max | frame total p50 | alloc/frame | CPU (1 core) |
|---|---|---|---|---|---|---|---|
| ULW full frame | 171.6 | 325.3 | 537.2 | 745.7 | - | - | - |
| frame total (full) | 6588.6 | 7896.9 | 8616.3 | 8880.7 | - | - | - |
| full frame (488x384 DIP) | 171.6 | 325.3 | 537.2 | 745.7 | 6588.6 | 5 B | 101.6% |
| ULW full frame | 131.4 | 883.7 | 1444.7 | 3697.0 | - | - | - |
| full frame, shadow off | 131.4 | 883.7 | 1444.7 | 3697.0 | 2217.1 | 1 B | 87.6% |
| ULW dirty rects | 145.3 | 308.6 | 473.7 | 628.5 | - | - | - |
| dirty rects, merged (2 -> 1 box) | 145.3 | 308.6 | 473.7 | 628.5 | 236.8 | 1 B | 59.7% |
| ULW dirty rects | 330.6 | 655.4 | 830.3 | 1377.5 | - | - | - |
| dirty rects, 2 uploads | 330.6 | 655.4 | 830.3 | 1377.5 | 444.7 | 3 B | 53.3% |
| paced 60 fps | 60.0 fps | p99 interval 18.03 ms | max 18.06 ms | late 0 | upload 309.8 us |
| paced 120 fps | 120.0 fps | p99 interval 20.06 ms | max 34.84 ms | late 21 | upload 466.2 us |
| composition (DwmFlush) | 159.4 fps | p99 interval 12.21 ms | max 12.55 ms | late 0 | upload 492.1 us |
| idle (6 s, no commits) | - | - | - | - | - | - | 0.00% |
| ULW full frame | 221.6 | 1592.7 | 4747.8 | 6202.5 | - | - | - |
| dock full frame (488x444 DIP) | 221.6 | 1592.7 | 4747.8 | 6202.5 | 8131.5 | 7 B | 97.7% |

## Raw notes

- Win32 failures counted by `PlatformCounters`: 0
- Idle CPU: 0.00% of one core over 6 s with zero commits
- Full-frame loop: 150.1 fps unpaced, 5 B/frame allocated
- Dirty loop (merged): 145.3 us p50 vs full 171.6 us p50
- Dirty loop (2 uploads): 330.6 us p50, 2048.0 fps unpaced
- Dock full frame: 221.6 us p50 (4747.8 us p99)

## Evidence

- `evidence/01-float-full.png`
- `evidence/02-float-corner-zoom.png`
- `evidence/03-float-detached-lane.png`
- `evidence/04-after-drag.png`
- `evidence/05-dock-full.png`

## Files

- `LayeredHostSpike.csproj`, `Program.cs`, `IslandRenderer.cs`, `Evidence.cs`, `SpikeBenchmarks.cs`, `BackdropWindow.cs`, `SpikeNative.cs`
- `spike.log` (full console transcript)
