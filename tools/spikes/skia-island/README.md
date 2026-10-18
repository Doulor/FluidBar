# Skia island spike (`tools/spikes/skia-island`)

2-day M0 spike for the FluidBar v2 island renderer: **can SkiaSharp render the island at high
fidelity and 60/120 fps?**

This project is deliberately **outside `FluidBar.sln`** (the Lead adds it if the spike is accepted).
It references `FluidBar.Core` for `ShellGeometry`/tokens but **no Avalonia, no App, no Platform**.

## Run

```powershell
dotnet run --project tools/spikes/skia-island/SkiaIsland.Spike.csproj -c Release
```

Requires .NET 10 SDK and Windows fonts (Segoe UI Variable, Microsoft YaHei UI, Segoe UI Emoji;
Cascadia Mono/Consolas optional). NuGet packages: SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3,
HarfBuzzSharp.NativeAssets.Win32 8.3.1.3. If nuget.org is slow, add
`https://repo.huaweicloud.com/repository/nuget/v3/index.json` as a source.

## Outputs

Written to `build/reports/skia-island/` (gitignored; regenerate on demand):

* `00-contact-sheet.png` — all scenes on one sheet
* `01a/01b-compact-idle-{en,zh}.png` — 132×36 pill + shadow bleed
* `02a/02b-compact-media-{en,zh}.png` — 226×40 media pill (art gradient + waveform)
* `03/03b-expanded-home-{en,zh}.png` — 440×284 shell, 732×498 px host
* `04/04b-expanded-media-{en,zh}.png` — media page + lyrics
* `05/05b-archipelago-{en,zh}.png` — 488×384 host: shell + detached 6 mini-islands
* `06-glyph-coverage.png` — EN/zh-Hans/emoji/kana/Hangul/accents
* `10/11/12-compare-*.png` — side-by-side with the Avalonia evidence PNGs
* `measurements.md` / `measurements.json` — render, text-cache, DPI, fidelity and memory numbers

## Headline numbers (150 %, Release)

| Case | mean | p99 | Alloc |
|---|---:|---:|---:|
| Expanded full frame (732×498) | 0.63–0.80 ms | 1.06–1.40 ms | 0.1 B/frame |
| Compact frame | 0.48–0.58 ms | 0.72–0.99 ms | 0.1 B/frame |
| Expanded dirty (376×114 DIP) | 0.16 ms | 0.23 ms | 0.1 B/frame |
| Archipelago (732×576) | 1.61 ms | 2.19 ms | 0.1 B/frame |
| Text layout warm | 0.11–0.16 µs/op | — | 0 B/op |
| Text layout cold | 74–134 µs/op | — | 23–41 KiB/op |
| Cold caches + first frame | 6.8 ms | — | — |

120 fps budget = 8.33 ms. The layered Win32 present path is **not** included; that is the next gate.

## Fidelity

* Idle: same 132×36 bounds, mask IoU **0.9986**, mean RGB Δ **3.1/255** vs Avalonia evidence.
* Expanded: shell bounds/mask **1.0000** at 440×284; content delta is the evidence's stale 48 DIP
  rail (A1 removed it).
* Emoji renders in color via HarfBuzz + Segoe UI Emoji.

## Source map

| File | Purpose |
|---|---|
| `Program.cs` | renders PNGs, comparisons, benchmarks, measurements |
| `IslandScenes.cs` | retained scene factory for compact/expanded/archipelago |
| `Scene.cs` | flat z-ordered node list + dirty-rect renderer |
| `TextEngine.cs` | font fallback, HarfBuzz emoji shaping, LRU layout/measure cache, trimming |
| `Icons.cs` | 24×24 path icons, cached stroke/fill paths |
| `ShadowCache.cs` | `SKImageFilter` drop shadow + baked static shell layer |
| `Geometry.cs` | squircle path (Core exponent), DIP helpers |
| `Theme.cs` | spec §3 tokens + EN/zh copy used by the spike |
| `Renderer.cs` | offscreen `SKSurface`, DPI transform, PNG encode |
| `Benchmarks.cs` | percentile/allocation/text/DPI benchmarks, JSON+Markdown |
| `Fidelity.cs` | mask IoU / fill / alpha comparison against evidence |

## Docs

* Architecture contract: [`docs/rewrite/contracts/skia-island.md`](../../../docs/rewrite/contracts/skia-island.md)
* Results + findings: [`docs/rewrite/research/07-skia-island-spike.md`](../../../docs/rewrite/research/07-skia-island-spike.md)

## Known findings to fix upstream

1. `ShellGeometry.SampleOutline` corner order cannot be connected into a path (drawing only; hit-test
   `Contains` is correct).
2. `ArchipelagoStrip` overlaps the active 72 DIP pill with the next slot and draws neighbours on top.
3. Evidence PNGs predate the A1 no-rail revision; expanded layouts are not pixel-comparable.
4. Expanded pages leave ~50 DIP unused at the bottom after rail removal; re-center the page body.
