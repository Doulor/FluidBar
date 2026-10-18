# Layered host spike (`tools/spikes/layered-host`)

Proves the Win32 per-pixel-alpha layered host for the FluidBar v2 island (see
[`docs/rewrite/contracts/layered-host.md`](../../../docs/rewrite/contracts/layered-host.md)).

## Run

```powershell
dotnet build tools/spikes/layered-host/LayeredHostSpike.csproj

# Full evidence + measurement script (~45 s; moves the pointer and restores it):
tools/spikes/layered-host/bin/Debug/net10.0-windows10.0.19041.0/LayeredHostSpike.exe --auto

# Eyeball check: shows the island on a checkerboard for 60 s:
tools/spikes/layered-host/bin/Debug/net10.0-windows10.0.19041.0/LayeredHostSpike.exe --manual

# Short loops (smoke):
tools/spikes/layered-host/bin/Debug/net10.0-windows10.0.19041.0/LayeredHostSpike.exe --auto --quick
```

`--auto` exits `0` only when every check passes. Outputs, written next to this file:

| Output | Contents |
|---|---|
| `results.md` | verdict table, per-path frame costs (µs), pacing, alloc/CPU, raw notes |
| `results.json` | the same numbers as JSON (for tooling/CI) |
| `evidence/*.png` | screen captures: full float frame, corner zoom, detached-island lane, after drag, dock |
| `spike.log` | full console transcript (including platform log lines) |

The spike draws a 440×284 squircle panel (title, media card, chips) plus **6 detached mini-islands**
in the lane below it at 150 % DPI, over a deterministic checkerboard backdrop. It measures full-frame
vs dirty-rect `UpdateLayeredWindow`, frame pacing (60/120 fps deadline + `DwmFlush`), managed
allocation per frame, CPU, and idle cost, and it asserts transparency, click-through
(`WindowFromPoint`), no focus steal and drag.

## Notes

- SkiaSharp 3.119.4 is a **spike-only** package: `FluidBar.Platform.Windows` stays framework-free and
  package-free. `NuGet.config` here lists nuget.org plus the Huawei Cloud mirror as a fallback.
- The process is Per-Monitor-V2 (`app.manifest`); without it the surface would stay at 100 % size.
- `evidence/` and `results.*` are generated; the committed source is the `.cs` files and this README.
