# SkiaSharp island spike — results (2026-10-18)

| Item | Value |
|---|---|
| Scope | 2-day spike: can SkiaSharp render the FluidBar v2 island at high fidelity and 60/120 fps? |
| Branch | `v2/rebuild` (no commit) |
| Project | `tools/spikes/skia-island/` (outside `FluidBar.sln`, references `FluidBar.Core`) |
| Renderer | SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3, .NET 10, offscreen raster |
| DPI | 150 % (1.5×), host 488×332 DIP → **732×498 px**; archipelago host 488×384 → 732×576 px |
| Verdict | **GO** for compact + expanded parity; host present + UIA remain conditional |
| Contract | [`docs/rewrite/contracts/skia-island.md`](../contracts/skia-island.md) |

---

## 1. What was built

A console spike (`FluidBar.SkiaIslandSpike`) that:

* builds a **retained scene** (background static layer + z-ordered content nodes),
* renders five surfaces at 150 % DPI to an offscreen `SKSurface` and saves transparent PNGs:
  compact idle, compact media, expanded home, expanded media with lyrics, detached archipelago;
* renders EN and zh-Hans variants, plus a glyph-coverage page (emoji/kana/Hangul/accents),
* compares against the Avalonia evidence PNGs (shell mask IoU, fill/alpha),
* measures full-frame, dirty-rect, text-cache and DPI-scaling cost, allocations/frame and cold start.

Run:

```powershell
dotnet run --project tools/spikes/skia-island/SkiaIsland.Spike.csproj -c Release
```

Outputs (regenerated, `build/reports/` is gitignored):

| File | Pixels | Content |
|---|---:|---|
| `build/reports/skia-island/00-contact-sheet.png` | 1540×1500 | all scenes composited for review |
| `build/reports/skia-island/01a-compact-idle-en.png` | 318×174 | 132×36 pill + 40 DIP shadow bleed (EN) |
| `build/reports/skia-island/01b-compact-idle-zh.png` | 318×174 | zh-Hans idle |
| `build/reports/skia-island/02a-compact-media-en.png` | 459×180 | 226×40 media pill, art gradient + waveform |
| `build/reports/skia-island/02b-compact-media-zh.png` | 459×180 | zh-Hans media |
| `build/reports/skia-island/03-expanded-home-en.png` | 732×498 | 440×284 shell, home page (EN) |
| `build/reports/skia-island/03b-expanded-home-zh.png` | 732×498 | home (zh-Hans) |
| `build/reports/skia-island/04-expanded-media-en.png` | 732×498 | media page + lyric lines (EN) |
| `build/reports/skia-island/04b-expanded-media-zh.png` | 732×498 | media (zh-Hans) |
| `build/reports/skia-island/05-archipelago-en.png` | 732×576 | shell + detached strip, 6 mini-islands (EN) |
| `build/reports/skia-island/05b-archipelago-zh.png` | 732×576 | archipelago (zh-Hans) |
| `build/reports/skia-island/06-glyph-coverage.png` | 732×498 | EN/zh/emoji/kana/Hangul/accents |
| `build/reports/skia-island/10-compare-home.png` | 1464×540 | Avalonia evidence vs spike (home) |
| `build/reports/skia-island/11-compare-media.png` | 1464×540 | Avalonia evidence vs spike (media) |
| `build/reports/skia-island/12-compare-idle.png` | 1464×540 | Avalonia evidence vs spike (idle) |
| `build/reports/skia-island/measurements.md` / `.json` | — | raw measured tables (auto-generated) |

The PNGs are transparent; the contact sheet/comparisons composite them on `#E9EBEF` / white.

---

## 2. Measured results (Release, 150 %, 732×498)

Environment: .NET 10.0.9, Windows 10.0.26200, SkiaSharp 3.119.0.0 (package 3.119.4),
HarfBuzzSharp 1.0.0.0 (package 8.3.1.3), laptop `TX-LAPTOP`. Raster CPU surface; no GPU context.
Run-to-run p99 varies by roughly ±20 % on this laptop (thermal/background load); the numbers below
are one Release run, and repeated runs stayed inside the stated ranges.

### 2.1 Render time

| Case | mean ms | p50 | p95 | p99 | max | alloc B/frame |
|---|---:|---:|---:|---:|---:|---:|
| Compact idle (host frame) | 0.455 | 0.439 | 0.539 | 0.675 | 0.958 | 0.1 |
| Compact media (host frame) | 0.486 | 0.466 | 0.593 | 0.717 | 0.886 | 0.1 |
| **Expanded home (full frame)** | **0.605** | 0.584 | 0.715 | **0.848** | 1.032 | 0.1 |
| **Expanded media (full frame)** | **0.546** | 0.524 | 0.686 | **0.846** | 0.885 | 0.1 |
| Expanded media dirty (376×114 DIP) | 0.157 | 0.153 | 0.171 | 0.245 | 0.491 | 0.1 |
| Expanded home, live background (no static layer) | 1.063 | 1.014 | 1.310 | 1.443 | 1.788 | 0.1 |
| Expanded + archipelago (732×576) | 1.145 | 1.053 | 1.611 | 1.933 | 2.362 | 0.1 |
| Compact waveform dirty (26×22 DIP) | 0.010 | 0.009 | 0.015 | 0.022 | 0.039 | 0.1 |
| Morph 132×36→440×284, scaled shadow | 0.856 | 0.804 | 1.621 | 2.014 | 2.318 | 264 |
| Morph, live `CreateDropShadowOnly` per frame | 2.254 | 2.047 | 4.432 | 5.014 | 5.317 | 555 |
| Morph, no shadow | 0.313 | 0.282 | 0.566 | 0.679 | 0.794 | 176 |

Budget check: 120 fps = 8.33 ms, 60 fps = 16.67 ms.

* Expanded full frame p99 **0.85 ms** → **≈9.8× headroom at 120 fps** (conservative range across
  repeated runs: 0.85–1.40 ms).
* Morph p99 **2.01 ms** with a scaled cached shadow → **4.1× headroom at 120 fps**. The live
  drop-shadow filter costs 5.01 ms p99 — do not use it per morph frame.
* Even with the static-layer optimisation disabled (live shadow + path), p99 is **1.44 ms**.
* Dirty content is **4–5× cheaper** than a full frame.
* Steady-state allocations are **0.1 B/frame** (measurement noise; effectively zero). Morph
  allocates ~176–555 B/frame from per-frame `SKPath`/filter objects; pool the path to get back to
  ~0.
* `UpdateLayeredWindow` copy/compositor is **not** included; it is the next gate.

### 2.2 Text layout / measure cache

| Case | ops | µs/op | alloc B/op | hits | misses | cache |
|---|---:|---:|---:|---:|---:|---:|
| UI 13.5 / 160 DIP (EN) cold | 120 | 103.0 | 38,667 | 0 | 120 | 120 / 35.9 KiB |
| UI 13.5 / 160 DIP (EN) warm | 240 | 0.111 | 0.3 | 240 | 0 | 120 / 35.9 KiB |
| UI 13.5 / 160 DIP (zh-Hans) cold | 120 | 60.8 | 23,434 | 0 | 120 | 120 / 67.0 KiB |
| UI 13.5 / 160 DIP (zh-Hans) warm | 240 | 0.077 | 0.3 | 240 | 0 | 120 / 67.0 KiB |
| Trim 13.5 / 120 DIP (EN long) cold | 120 | 99.4 | 40,862 | 0 | 120 | 120 / 34.5 KiB |
| Trim 13.5 / 120 DIP (EN long) warm | 240 | 0.091 | 0.3 | 240 | 0 | 120 / 34.5 KiB |
| Page title 15 / 340 DIP cold | 120 | 21.5 | 8,543 | 0 | 120 | 120 / 35.3 KiB |
| Page title 15 / 340 DIP warm | 240 | 0.063 | 0.3 | 240 | 0 | 120 / 35.3 KiB |

Cold cost includes segmentation, fallback, shaping and binary-search trimming; 38–41 KiB/op is
transient garbage, not retained. Warm hits are allocation-free. **Prewarm page text** on
locale/DPI/theme change, not on panel open (30 strings ≈ 4 ms worst case cold).

### 2.3 DPI scaling (expanded home, warm)

| Scale | Surface | mean ms | p99 ms | fps at p99 |
|---:|---:|---:|---:|---:|
| 1.25× | 610×415 | 0.524 | 0.931 | 1074 |
| 1.50× | 732×498 | 0.702 | 1.020 | 980 |
| 1.75× | 854×581 | 0.964 | 1.402 | 713 |
| 2.00× | 976×664 | 1.339 | 1.927 | 519 |

Fractional 1.5× is not a cliff. 2× is still 3.5× under the 120 fps budget.

### 2.4 Cold start and memory

* Cold caches + scene build + first expanded frame: **6.0 ms** (first render **1.0 ms**).
* Managed heap 0.6 MiB; process working set **99.6 MB** (includes the .NET runtime, Skia, HarfBuzz and
  the benchmark's multi-DPI caches).
* Text cache 54 KiB; shadow images 10.0 MiB / 7; static layers 11.4 MiB / 9 (the spike deliberately
  keeps four DPI scales and standalone compact layers; production keeps one per monitor).
* Island-specific budget: static 1.46 MB + shadow 1.70 MB + text 0.15–0.3 MB + surface 1.46 MB
  ≈ **5–6 MB per monitor at 150 %**.

---

## 3. Fidelity vs the Avalonia evidence

Quantitative (transparent PNGs, opaque shell mask aligned by bounding box):

| Case | Evidence bounds | Spike bounds | Mask IoU | Mean RGB Δ | Fill (evidence / spike) |
|---|---|---|---:|---:|---|
| Compact idle | 267,36 198×54 px (132×36 DIP) | 267,36 198×54 px | **0.9986** | **3.13/255** | `#070709@246` / `#08080A@246` |
| Expanded home | 36,36 660×426 px (440×284 DIP) | 36,36 660×426 px | **1.0000** | 13.38 | `#070709@247` / `#08080A@246` |
| Expanded media | 36,36 660×426 px (440×284 DIP) | 36,36 660×426 px | **1.0000** | 13.92 | `#070709@247` / `#08080A@246` |

The expanded mean RGB delta is dominated by the **layout difference** (evidence still has the
rejected 48 DIP rail; the spike uses the A1 no-rail 400×252 page). The shell bounds, squircle mask,
hairline and fill match exactly. Idle is the only scene with an identical layout, and it is
effectively pixel-identical: same 132×36 bounds, 0.9986 mask IoU, 3.1/255 mean colour delta.

### 3.1 Differences noted

| Difference | Assessment |
|---|---|
| Evidence has a left rail; spike uses the A1 horizontal pager (no rail) | Expected; evidence predates A1. Not a renderer difference. |
| Evidence title row is ~14 DIP lower; content/quick actions use a different vertical rhythm | Layout decision after rail removal; spike follows current `HomePage`/`MediaPage` coordinates. See finding 4. |
| Shadow falloff: Avalonia draws 4 scaled shells; Skia uses one Gaussian (`sigma=12 DIP`) | Skia is smoother/wider. Tune `sigma`/alpha if the compact shadow is judged too soft. |
| Text rasterization: DirectWrite variable instances in both; Skia uses grayscale AA | Very close; slight advance/kerning differences (~1 px on short labels). |
| Icons: same 24-grid path set, Skia stroke 1.6 grid units | Visually equivalent; Skia AA is slightly cleaner at 16 px. |
| Media page: evidence has a `3 / 12` queue badge; spike has the current collapse chevron | Evidence is stale; current App uses the chevron. |
| Archipelago: App overlaps the active 72 DIP pill with the next 48 DIP slot and paints neighbours on top; spike uses a flow layout | Spike fixes a real App bug; see finding 2. |
| Emoji: Skia renders color Segoe UI Emoji via HarfBuzz | Works; App currently has no emoji in the island. |

---

## 4. Upstream findings (for the parent/Lead)

1. **`FluidBar.Core.Layout.ShellGeometry.SampleOutline` is not directly connectable.**
   The top-right corner is emitted right-edge → top-edge (reverse=false) while the surrounding
   traversal expects top-edge → right-edge. Joining the returned points in order cuts across the
   shell. `Contains`/hit-testing is unaffected. Fix Core's corner order (or document that the
   outline is only for sampling) before anyone uses it to build a path.
2. **`ArchipelagoStrip.IslandRect` overlaps the active island and the draw order is wrong.**
   Inactive slots use a 56 DIP pitch; the active island is 72 DIP wide at the slot x, so it overlaps
   the next 48 DIP island by 24 DIP. The `for i=0..5` draw loop paints later neighbours over the
   active pill (visible red notification dot on top of “Now playing”). The spike uses widths
   `48 + 24·clamp(1−|i−p|,0,1)`, 8 DIP gaps (total stays 352) and draws the active island last.
3. **Evidence PNGs predate the no-rail A1 revision.** Do not use them for pixel diffing expanded
   content; use them for shell/colour/shadow/text-style reference only.
4. **Expanded pages are top-anchored and waste ~50 DIP at the bottom** after the rail was removed
   (media lyrics end at viewport y≈202 of 252; home quick actions end at 222). The evidence fills
   more of the height. Recommend re-centering the page body and/or moving quick actions down ~20 DIP
   before parity sign-off.
5. **Emoji must be shaped.** `SKTextBlob.Create(string, font)` mapped a surrogate pair to two glyphs;
   HarfBuzz shaping produces one positioned glyph and color COLR rendering works.
6. **`PagerSnapMotion` (T 0.44 / ζ 0.97) supersedes `motion.tab` (T 0.34 / ζ 0.92)** for the page
   strip; the spec/contract should be updated or Core reverted. The spike follows Core.

---

## 5. Verdict and effort

**GO.** SkiaSharp renders the island at high fidelity (idle IoU 0.9986, expanded shell IoU 1.0000)
and with large headroom (expanded p99 0.85–1.40 ms across runs, morph p99 2.01 ms vs 8.33 ms at
120 fps, zero steady-state allocations). The renderer is not the risk; the Win32 layered present
path and UIA are.

Effort estimate (one graphics engineer, parity with current App): **46–60 eng-days** (~9–12 weeks).
A narrower MVP (compact + expanded home/media + pager + strip, no UIA) is **20–25 eng-days**.
Full breakdown and WPF fallback criteria: [`contracts/skia-island.md`](../contracts/skia-island.md) §12–13.

### Conditions before shipping

1. Host spike proves the layered present path at 120 fps (or documents a 90 fps downgrade).
2. UIA fragment provider for pager/transport/slider/list controls lands and is tested with Narrator.
3. Archipelago flow layout and page vertical rhythm fixes land (findings 2 and 4).
4. A 30 min soak with the real activity stream keeps working set < 120 MB and 0 frame callbacks idle.
