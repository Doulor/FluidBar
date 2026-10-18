# FluidBar v2 — SkiaSharp island renderer contract

| Item | Value |
|---|---|
| Status | **GO (compact + expanded parity)** — 2-day spike, offscreen raster, 2026-10-18 |
| Owner | graphics spike (this document is the integration contract for the renderer lane) |
| Inputs | spec `2026-10-18-fluidbar-v2-design.md` §2/§3/§4/§5.6/§5.7, `contracts/core.md`, `contracts/ui.md`, `App/Island/**`, `tests/FluidBar.Ui.Tests/Evidence/*.png` |
| Spike | `tools/spikes/skia-island/` (outside `FluidBar.sln`) |
| Evidence | `build/reports/skia-island/*.png`, `build/reports/skia-island/measurements.{md,json}`, `docs/rewrite/research/07-skia-island-spike.md` |
| Decision | SkiaSharp 3.119.4 + Win32 layered host; **no Avalonia drawing** on the island surface |

This document is the contract for replacing the Avalonia island surface with a SkiaSharp renderer.
It covers the retained scene, text/icon caches, font fallback, shadow/AA, fractional DPI, dirty-rect
invalidation, input/hit regions, Core integration, accessibility, memory, risks and the go/no-go.

---

## 0. Verdict

**GO.** At 150 % DPI on the spike machine, a warm full expanded frame (732×498 px, 488×332 DIP host)
renders in **0.63–0.80 ms mean / 1.06–1.40 ms p99** with **≈0 managed allocations per frame**. The
120 fps budget is 8.33 ms, so the renderer has **≈5.9× headroom** before the host present path; the
expand morph (132×36 → 440×284, scaled shadow) measures **2.01 ms p99**, still 4.1× headroom.
The shell geometry is pixel-exact against the Avalonia evidence (mask IoU 1.0000, 440×284 DIP), and
the idle pill is 0.9986 IoU with a 3.1/255 mean RGB delta.

The remaining risk is **not drawing** — it is the Win32 layered present path (`UpdateLayeredWindow`
copy + compositor) and the UIA provider. Those must be measured in the host spike before the lane is
declared shippable at 120 fps. See §12.

---

## 1. Scope and host assumption

The spike renders to an offscreen `SKSurface` (`Rgba8888`, premultiplied, transparent) and writes
PNGs. Production hosts the same scene in one of two presenters:

| Presenter | Path | Cost model | Recommendation |
|---|---|---|---|
| **Layered** (default for parity) | `WS_EX_LAYERED` + `UpdateLayeredWindow` with a top-down BGRA DIB | CPU raster + one full-surface copy per present; no GPU sync | Use for M4 parity; measure at 60/120 fps |
| Composition (later) | `WS_EX_NOREDIRECTIONBITMAP` + D3D11/DComp or `Windows.UI.Composition` | GPU texture present; avoids the ULW copy | Preferred once the host lane owns D3D |

The renderer contract is presenter-agnostic: it produces a premultiplied BGRA/RGBA surface and a
damage rectangle. The presenter only uploads that rectangle. CPU raster numbers in this document are
therefore the **lower bound** for the layered path and a conservative upper bound for a GPU path.

Out of scope for this spike: HWND creation, `WM_NCHITTEST`, `SetWindowRgn`, UIA, IME, multi-monitor
hand-off. §8/§9 define the contracts those pieces must implement.

---

## 2. Coordinate and DPI contract

* All scene geometry is **DIP (double)**, origin at the host window's top-left. The shell sits at
  `(24,24)` inside the fixed host; compact pills are centred horizontally at `y=24`.
* One scale factor per monitor: `scale = dpi / 96`. The renderer applies `canvas.Scale(scale)` once;
  every node draws in DIP.
* **No layout rounding.** Positions are fractional. Pixel snapping is limited to:
  * hairline strokes: `1 DIP` (Skia AA keeps it visually 1 px at 150 %; do not snap the path),
  * shadow/static-layer bitmaps: built at device resolution,
  * hit regions: DIP geometry, not rounded (see §8).
* Geometry caches are bucketed at **0.5 DIP** (`round(v*2)/2`) so a spring morph does not rebuild a
  path every frame; text layout keys include the exact fractional size and the 0.5 DIP width bucket.
* On `WM_DPICHANGED`: snap springs, rebuild the static/shadow layers, clear the text layout cache
  (sizes/widths changed), re-render one frame. Never animate a DPI change.
* Measured scaling (expanded home, warm, p99): 1.25× 1.24 ms, 1.5× 1.54 ms, 1.75× 2.13 ms,
  2.0× 2.38 ms. Fractional 1.5× is not a cliff; 2× is still 3.5× under the 120 fps budget.

---

## 3. Retained scene model

The scene is a **flat, ordered node list** (not a general UI tree). Pages are retained view objects
that own their data; the scene only holds draw closures and bounds. One frame = one `Render` call.

### 3.1 Root and layers

```
IslandScene (488×332 DIP float/flush, 488×384 with archipelago)
 ├─ 0   BackgroundLayer   cached shadow + squircle fill + 1 DIP hairline   (static SKImage)
 ├─ 10  Ghost pills       up to 2, behind the main pill (compact only)     (path draws)
 ├─ 20  Page content      per-component nodes (clock, cards, art, transport, lyrics, list rows…)
 ├─ 30  Chrome overlay    page title, collapse chevron, focus ring, pager dots
 └─ 40  Archipelago       detached mini-islands below the shell; active island drawn last
```

| Z | Node | Bounds (DIP) | Redraw policy |
|---:|---|---|---|
| 0 | `shell.background` | host rect | static image; rebuilt on size/radius/DPI/palette change |
| 10 | `shell.ghosts` | compact pill ± 12 | only when compact layer is visible |
| 20 | `compact.content` / `page.*` | component rect | dirty when the component's data changes |
| 30 | `overlay.title`, `overlay.collapse`, `overlay.focus` | chrome rect | dirty on page/locale/focus change |
| 40 | `archipelago` | strip rect + 8 DIP | dirty on active page/preview phase |

The active mini-island is a separate node drawn **after** the five inactive islands. This fixes an
App bug where the neighbour's preview painted over the grown active pill (see §11).

### 3.2 Node shape

```csharp
sealed class SceneNode {
    string Id; string Region; int Z;
    SKRect Bounds;                 // DIP, canvas-local
    Action<SKCanvas> Draw;         // cached closure; no per-frame allocation
    bool Visible;
}
```

`Region` is the invalidation key (`page.media.progress`, `page.media.lyrics`, `overlay`, …). A region
may contain several nodes; invalidating a region unions their bounds.

### 3.3 Rebuild vs property update

| Change | Action |
|---|---|
| Page switch | translate the page strip; no page root rebuild, no shell resize |
| Activity content replacement | update the retained view's fields; rebuild only affected nodes; keep scroll/focus/pager |
| Progress / waveform / lyric line | property write + invalidate one region; no text blob rebuild if the string is unchanged |
| Locale / palette / material | rebuild scene draw closures and text cache; keep geometry caches |
| Shell size / radius / DPI | rebuild squircle + static layer + shadow image; rebuild text layouts |
| Compact ↔ expanded | different scene; both retained, one is hidden; no layout in the frame loop |

The spike's `IslandScene` is the reference: `ExpandedHome`/`ExpandedMedia` build 6–7 nodes; a dirty
frame draws the background blit plus 2–3 intersecting nodes.

### 3.4 Static background and morph

The settled shell (shadow + fill + hairline) is baked into one full-host `SKImage` (`732×498` at
150 %). A dirty frame blits it clipped to the damage rect; a full frame blits it once. This is why
warm full-frame cost is ~0.7 ms rather than ~1.5 ms.

During a size morph the static image is **not** valid. The renderer then draws the live path:

1. blit the **scaled cached shadow** (one 440×284 image stretched to the current rect),
2. fill the squircle path (pooled `SKPath`, rebuilt per frame),
3. stroke the hairline,
4. draw content with opacity derived by Core `ContentOpacityPolicy`.

Measured morph (240 frames, 132×36 → 440×284, 732×498):

| Shadow strategy | mean | p99 | alloc/frame |
|---|---:|---:|---:|
| Scaled cached shadow | 0.86 ms | **2.01 ms** | 264 B (path objects) |
| Live `CreateDropShadowOnly` per frame | 2.25 ms | 5.01 ms | 555 B |
| No shadow during morph | 0.31 ms | 0.68 ms | 176 B |

Do **not** draw the live drop-shadow filter during morph: it is ~2.6× the cost and leaves only
1.7× headroom at 120 fps. Do **not** rebuild the full static layer per morph frame. Use the scaled
shadow (or a 9-slice shadow) and rebuild the sharp shadow once at settle. Pool the squircle path to
remove the remaining ~264 B/frame.

---

## 4. Text and icon caches

### 4.1 Font roles

| Role | Stack (first available) | Use |
|---|---|---|
| `Ui` | Segoe UI Variable Text → Segoe UI → Microsoft YaHei UI | captions, body |
| `UiSemiBold` | Segoe UI Variable Text 600 | titles, labels |
| `DisplaySemiBold` | Segoe UI Variable Display 600 | page titles |
| `DisplayBold` | Segoe UI Variable Display 700 | dashboard clock |
| `Mono` | Cascadia Mono → Consolas → Microsoft YaHei UI | times, positions |
| `MonoSemiBold` | Cascadia Mono 600 | compact clock, HUD values |

Variable weights are resolved through DirectWrite (`SKTypeface.FromFamilyName(family, weight, …)`);
the spike confirmed 600/700 instances are distinct and measurable.

### 4.2 Layout cache

Key: `(text, role, size, round(maxWidth*2))`. Value: shaped run blobs, per-run x offsets, width,
line height, ascent, glyph count, trim flag.

* LRU capacity **512** entries per locale (spike: 120 entries = 36 KiB EN / 67 KiB zh-Hans).
* Hit path: dictionary lookup + `LinkedListNode` move-to-front; **0 allocations**, ~0.1–0.2 µs/op.
* Miss path: segment → shape → trim → build blobs; **~74–134 µs and ~23–41 KiB transient** per
  unique string in the spike. Prewarm page text on locale/theme/DPI change, never on panel open.
* Character ellipsis: binary search on UTF-16 length (surrogate-safe), append `…`; 6–7 measurements
  per trimmed line. Cache the trimmed result, not the full string.
* Line metrics: `height = font.Spacing`, baseline centred with `ascent`; CJK uses the same primary
  line box (Avalonia-compatible). Explicit `1.24×` line height for multi-line CJK.
* Multi-run text: one `SKTextBlob` per fallback run, drawn at cumulative x with one paint. Runs are
  measured with `font.MeasureText` (Latin) or HarfBuzz advances (emoji).

### 4.3 i18n text cache

* Catalog → `Dictionary<string,string>` per locale, loaded once; fallback chain
  `requested → parent → en-US → key`.
* Formatted strings cached by `(key, locale, argument tuple)`; numbers use invariant formatting for
  HUD/percent (Core presenter already emits display text), dates use `CultureInfo`.
* Locale switch: clear layout cache, rebuild scene closures, keep font/geometry caches.
* Pseudo-locale (`qps-ploc`) increases length ~35 %; the layout cache key includes the final string,
  so pseudo-locale gets its own entries and trimming is exercised.
* Do not cache `FormattedText`-style mutable objects; cache immutable blobs.

### 4.4 Icons

* Path-based 24×24 grid, 1.6 grid stroke (≈1.25 px at 16 px), cached as stroke/fill `SKPath` per
  `IconKind`.
* Draw = translate + scale + set paint colour + two `DrawPath` calls. Tint via paint colour; no
  per-colour path copies.
* Icon sizes: 16 inline/transport, 18 nav, 14 chrome, 20 quick action. No emoji as functional icons.
* Directional icons (chevron/back) carry `MirrorInRtl`; mirror by negative x scale at draw time.

---

## 5. Font fallback, CJK and emoji

### 5.1 Segmentation

For each UTF-16 code point:

1. Emoji ranges (`1F000–1FAFF`, `2600–27BF`, `2B00–2BFF`, `FE00–FE0F`, `200D`, `1F1E6–1F1FF`)
   → Segoe UI Emoji run, **HarfBuzz-shaped**.
2. CJK ranges (`2E80–2FFF`, `3000–30FF`, `3400–4DBF`, `4E00–9FFF`, `F900–FAFF`, `FE30–FE4F`,
   `FF00–FFEF`, `20000–2FA1F`) → Microsoft YaHei UI (600 for semibold roles) run.
3. Otherwise primary typeface if `font.ContainsGlyph(cp)`.
4. Otherwise `SKFontManager.Default.MatchCharacter(cp)` (cached per code point); last resort primary.

Consecutive code points with the same typeface become one run. Baseline is shared; each run keeps
its own font metrics for width only.

### 5.2 Emoji / shaping

`SKTextBlob.Create(string, font)` maps a surrogate pair to two glyphs in the spike probe, so emoji
**must** be shaped. HarfBuzz (`HarfBuzzSharp 8.3.1.3`) loads `seguiemj.ttf`, shapes the cluster with
`SetScale(size*64)`, and builds a positioned `SKTextBlob` run. Color emoji (COLR/CPAL) rendered
correctly for 🎵 🎧 🌙 🚀 and ZWJ sequences in the glyph-coverage PNG.

HarfBuzz is not used for Latin: the variable-font instance needed for SemiBold is resolved by
DirectWrite, and HarfBuzzSharp 8.3.1.3 has no `SetVariations` on `Font`. Latin shaping (kerning,
ligatures) is therefore a known fidelity gap of ~0–1 px per short label; acceptable for the island.
If exact Latin kerning is required later, load the static `segoeuib.ttf` for HarfBuzz and keep the
variable typeface for rendering, or upgrade HarfBuzzSharp for variations.

### 5.3 CJK / fallback notes

* zh-Hans is fully covered by Microsoft YaHei UI; the measured zh layout is ~0.6× the cold cost of
  EN because fewer fallback decisions and shorter strings.
* Kana is covered by YaHei; Hangul falls through to `MatchCharacter` (Malgun Gothic on this machine).
* Missing-glyph fallback is cached per code point; cap the cache (e.g. 512) and clear on font change.
* Font availability is probed once at startup; a missing YaHei falls back to `Microsoft YaHei`,
  `Noto Sans SC`, then `SKTypeface.Default`, and the UI logs a visible degradation (no silent tofu).

---

## 6. Shadow, AA and materials

* Shadow: `SKImageFilter.CreateDropShadowOnly(0, dy, sigma, sigma, color, null)` with
  `dy = 8 DIP`, `sigma = blur/2 = 12 DIP`, `color = #000 @35 %` (dock 4/6/28 %).
  The filter is applied **once** into an `SKImage` at device resolution, then blitted.
* Cache key: `(width, height, radius, scale, sigma, dy, alpha)` in 0.5 DIP buckets. One image per
  settled size; ~1.7 MB at 440×284 @150 %. Dispose on DPI/mode change.
* Ghost stack: two squircle paths at scale .96/.92, y −6/−12, opacity .55/.35; shadow only on the
  outermost pill.
* AA: `SKFontEdging.Antialias`, `Subpixel=false`; transparent surfaces cannot use ClearType. All
  geometry uses `IsAntialias=true`. Hairline stays 1 DIP (not 1 px) so 150/200 % remain crisp.
* Fill: `#08080A @0.96` (min 0.85 for text); high contrast forces 1.0 and Tier 0. Simulated glass
  (Tier 1) is a 2-stop white gradient + 1 px top highlight, no blur. Whole-window acrylic remains
  Tier 2/diagnostic only.
* No inner rim light, no gradient stroke, no purple wash (spec §3.3).

---

## 7. Dirty-rect invalidation

### 7.1 Regions

`compact.shell`, `compact.content`, `page.home.clock`, `page.home.cards`, `page.home.quick`,
`page.media.art`, `page.media.meta`, `page.media.progress`, `page.media.transport`,
`page.media.lyrics`, `page.list.rows`, `overlay`, `archipelago`, `shell.background`.

`Invalidate(region)` unions the region's node bounds, inflates by 2 DIP for AA, and coalesces with
any pending damage. At most one render per frame.

### 7.2 Frame algorithm

```csharp
void Render(IslandScene scene, SKRect? damage) {
    canvas.Save();
    if (damage is { } d) canvas.ClipRect(d);
    // replace pixels, not blend: transparent layered surface
    clearPaint.BlendMode = SKBlendMode.Src;
    canvas.DrawRect(damage ?? full, clearPaint);       // transparent
    canvas.DrawImage(scene.Background.Image, full);   // clipped blit of static layer
    foreach (var node in scene.Nodes)                 // z-ordered
        if (node.Visible && (damage is null || node.Bounds.Intersects(damage)))
            node.Draw(canvas);
    canvas.Restore();
}
```

* The background blit is clipped by the damage rect; a 376×114 DIP dirty frame costs **0.16 ms**
  (p99 0.23 ms) vs 0.63–0.80 ms full frame.
* Content-only dirty (waveform 26×22 DIP) costs **0.014 ms**.
* For a **morph**, damage is the union of old/new shell bounds → full frame; do not attempt
  per-region damage while the shell size changes.
* For an **opaque** presenter, replace the transparent clear with the backdrop fill; never re-blend
  alpha over existing pixels (double-darkening).
* ULW path: pass the damage rect to the presenter; `UpdateLayeredWindow` still uploads the full
  surface in the common API. If that copy exceeds budget, use `UpdateLayeredWindowIndirect` with a
  dirty sub-rect or switch to a D3D swap chain.
* Full-frame fallback triggers: DPI change, palette/material change, first frame after show,
  page switch (strip translation), focus ring appearance.

### 7.3 Measured dirty/full ratios

| Frame | Mean | p99 | Area px |
|---|---:|---:|---:|
| Expanded media full | 0.63 ms | 1.06 ms | 364,536 |
| Expanded media dirty (progress + lyrics) | 0.16 ms | 0.23 ms | 96,444 |
| Compact waveform dirty | 0.018 ms | 0.055 ms | 858 |

---

## 8. Input and hit-region model

### 8.1 One geometry for draw, clip and hit

`Core.Layout.ShellGeometry` remains the single source of truth for the squircle. The renderer builds
its `SKPath` from the same exponent (the Core `SampleOutline` ordering is not directly connectable —
see §11). Hit-testing calls `ShellGeometry.Contains` on the same `(w,h,radii)`.

`WM_NCHITTEST`:

1. If hidden/opacity ≤ 0.01 → `HTTRANSPARENT`.
2. If the point is inside the shell squircle → `HTCLIENT` (or the control-specific region).
3. If the settled expanded layer owns the detached archipelago and the point is inside a mini-island
   (or its 10/6 DIP hover inflation) → `HTCLIENT`.
4. Otherwise `HTTRANSPARENT`.

`SetWindowRgn` is updated **only at settle** (bounding box + radius), never per frame. During morphs
the per-point hit test plus alpha does the work.

### 8.2 Regions and actions

| Region | Hit | Action |
|---|---|---|
| Compact shell (idle/media/HUD…) | entire pill | click → expand; double click → source app |
| Compact media transport | 24×24 (peek 3×24) | play/pause, previous, next; do not expand |
| Compact hover | pill + 10 DIP | 90 ms intent → Peek (+10 w / +6 h, `motion.hoverPop`); leave 120 ms debounce |
| Expanded shell | 440×284 squircle | background click → no-op; outside → collapse (focus-loss path) |
| Pager viewport | 400×252 at (20,16) | drag / wheel / click; >6 DIP cancels page click |
| Chrome | title 340×20, collapse 20×20 at (400,14) | collapse chevron; title not interactive |
| Page controls | 32 min / 36 transport | buttons, sliders, list rows; slider owns wheel/pointer |
| Archipelago strip | 352×34 at (44,294) + 10/6 inflation | click mini-island → page; drag → scrub; hover reveals |
| Mini-island | 48×26 / active 72×30 | click; active label; drag threshold 6 DIP |
| Hidden hover strip | 10 DIP at top | reveal compact (setting-gated) |

### 8.3 Compact gestures

* Click: `CompactAction` from the surface's own hit test (`Expand`, `TogglePlay`, `Previous`,
  `Next`). The shell never expands on a transport click.
* Hover: 90 ms intent, `motion.hoverPop`; peek grows the compact layer, not the window.
* Drag: press + move > 4 DIP enters island drag; rubber-band against the work area; release snaps to
  the nearest anchor (24 DIP threshold) or keeps the free offset. **Window position changes only on
  release/placement**, never per frame.
* Double click: open the source app; 500 ms debounce against re-expand.

### 8.4 Pager: drag, wheel, scroll chain

All decisions are Core policy calls; the renderer only consumes offsets.

```csharp
// press
drag = PagePagerPolicy.Drag(startPage, accumulatedDragDip);   // damping .80, edge .35
renderOffset = drag.DisplayOffsetDip;

// release
var decision = PagePagerPolicy.Release(startPage, dragDip, velocityDipPerMs);
snap.SnapToOffset(decision.TargetOffsetDip);                  // PagerSnapMotion T .44 ζ .97
```

* Drag sign: positive = towards the next page; the host feeds `-pointerDeltaX`.
* `|drag| < 24 DIP` → snap back; short drag/flick → at most ±1 page; long drag → `round(dx/400)`.
* `PagerSnapMotion` (T 0.44/ζ 0.97, pager v2) supersedes `motion.tab` for the page strip; keep
  `motion.tab` for the dot indicator if dots are reintroduced.
* A new drag calls `Interrupt()` and starts from the visual offset.
* Wheel: `PagerScrollChainPolicy` first. Scrollable content with room → `ScrollContent`; at a
  boundary accumulate ≥40 DIP **and** ≥120 ms dwell → one page step; reversal cancels; a running
  snap freezes the chain; horizontal wheel/Shift bypasses content. When the content is not
  scrollable, `PagerWheelPolicy` (notch 140 DIP, 180 ms cooldown) steps one page.
* Page controls that own the gesture (slider, list, text field) set `controlOwnsGesture=true`; the
  pager must not page.
* Keyboard: `1–6`, `←/→`, `Home/End`, `Esc` (host consumes).

### 8.5 Archipelago strip

* Reveal on bottom-band hover (300 ms) or drag/wheel; auto-hide after 1200 ms; `ArchipelagoMotion` drives
  opacity/translateY/scale from Core springs.
* Click an island → `SelectPage(index)`; drag the strip → `PagePagerPolicy.Drag` with
  `StripScrubFactor=5`; release without >6 DIP movement → tap the pressed island.
* Active position is the smoothed pager page position; the strip layout is a **flow** of widths
  `48 + 24·clamp(1−|i−p|,0,1)` with 8 DIP gaps. This keeps `TotalWidth=352` and prevents the active
  72 DIP pill from overlapping its neighbour (App bug, §11).
* Wheel over the strip always pages (never scrolls content).

### 8.6 Core motion / frame-clock integration

* One `SpringSet` + one `MotionLoop` per host window. `IFrameClock.Start(onFrame)` → step springs →
  derive `ShellFrame` → `Invalidate` once → `Render`.
* Springs: `motion.expand` (width), `motion.expand.height` (height), `motion.collapse`,
  `motion.hoverPop`, `motion.press`, `motion.nudge`, `motion.hide`; pager snap via
  `PagerSnapMotion`; archipelago via `ArchipelagoMotion`.
* `MotionLoop.Step` stops the clock when `AllAtRest`; idle = **0 frame callbacks**. Any dynamic
  content (waveform, agent pulse, lyric progress) must register a dirty flag and keep the loop
  alive only while visible.
* Settle: `|x−target| < 0.04 DIP`, `|v| < 0.35 DIP/s` for 2 frames → snap + stop.
* Retarget: keep 100 % velocity same direction, ×0.35 reversed, clamp `|v| ≤ 0.2·|Δ|/dt`.
* Reduced motion: ζ→1, T×0.5, nudge 0, no bloom/spin; `MotionProfile` already implements this.
* **Never** animate window bounds; the host moves the window only on anchor/DPI/mode changes.

---

## 9. Accessibility / UIA plan

The island is a custom-drawn surface, so it must expose a UIA **fragment tree**; there are no child
HWNDs.

| Element | Control type | Patterns / properties |
|---|---|---|
| Island root | `Pane` | Name = current activity/page; `IsContentElement`, `IsControlElement` |
| Page pager | `Tab`/`TabItem` per page | `SelectionItem`; Name = localized page title; `Selection` = current |
| Collapse chevron | `Button` | `Invoke`; Name = localized “Collapse” |
| Transport | `Button` ×3 | `Invoke`; Names = Play/Pause/Previous/Next; state via `Toggle`/`Value` |
| Progress / seek | `Slider` | `RangeValue` (0–100), `Value`; `SetValue`; keyboard ±2 %, Home/End |
| List rows (notifications/clipboard/agent) | `ListItem` | `Invoke` (open/copy), `Toggle` (pin/done); Name = title + body |
| HUD track | `Slider` | `RangeValue`; muted state via `HelpText` |
| Mini-islands | `Button`/`TabItem` | `Invoke`/`SelectionItem`; Name = page title |
| Focus ring | visual only | 2 DIP `--info` + 2 DIP offset; never animated |

* Provider: `IRawElementProviderSimple` + `IInvokeProvider`/`IRangeValueProvider`/
  `ISelectionItemProvider`, raised from `WM_GETOBJECT`; child ids stable per page/control.
* Focus: keyboard focus enters the current page on expand (T4), `Tab` cycles controls, `Esc`
  collapses; focus is retained across content replacement (spec §2.7).
* Names are localized through the same catalog; never hard-code English.
* Announce activity + value on change (e.g. “Now playing, Midnight City, M83, 1:52 of 4:03”).
* High contrast: Tier 0 opaque fill, 1 DIP system-color stroke, focus ring `--info`; no shadow-only
  affordances. Reduced motion honored from `SPI_GETCLIENTAREAANIMATION` + settings.
* IME: only lookup/search needs text input; use a separate small window, never the island surface.
* Test with Inspect/Narrator: tree depth ≤ 3, every actionable element reachable, no “pane” with 50
  unnamed children. UIA is **not** in the spike; budget it separately (§12).

---

## 10. Memory budget

| Asset | 150 % size | Bytes | Budget |
|---|---:|---:|---:|
| Static shell layer | 732×498×4 | 1.46 MB | 1 per monitor |
| Shadow image (440×284 + 40 margin) | 780×546×4 | 1.70 MB | 1 per settled size |
| Compact shadow (156×60 + margin) | ~354×210×4 | 0.30 MB | 1 per compact size |
| Text layout cache | 512 entries | ~150–300 KiB | bounded LRU |
| Fonts/typefaces | 9 faces | < 2 MB native | process-wide |
| Icon paths | 27 icons | < 100 KiB | process-wide |
| Scene/nodes | 20 nodes | < 20 KiB | per monitor |
| Surface | 732×498×4 | 1.46 MB | per monitor |
| **Island subtotal** | | **~5–6 MB** | **< 12 MB** |

Measured process working set after warm-up: **90–100 MB** (includes the .NET runtime, Skia, HarfBuzz
and the spike's benchmark buffers). The spike's multi-DPI/standalone cache (7 shadows + 9 static
layers) is ~22 MB; production keeps one DPI per monitor. Evict text layouts on locale change, shadows/static
layers on DPI/mode change; never keep an unbounded `Dictionary` (spec §5.8 rule 6).

---

## 11. Risks and upstream findings

### 11.1 Findings from the spike

1. **Core `ShellGeometry.SampleOutline` ordering is not connectable.** The top-right corner is
   emitted right-edge → top-edge while the surrounding order expects top-edge → right-edge, so a
   path built by joining the points cuts across the shell. `Contains` is unaffected (hit-testing is
   correct); only drawing is affected. Fix Core's corner order or keep the spike's `Squircle.Build`.
2. **App `ArchipelagoStrip` overlap/z-order.** `IslandRect` gives the active island 72 DIP at the
   slot x while neighbours stay on a 56 DIP pitch, so the active pill overlaps the next slot by 24
   DIP; the draw loop paints later neighbours over it. The spike uses a flow layout
   (`48+24·influence`, 8 DIP gaps) and draws the active last; `TotalWidth` stays 352.
3. **Evidence PNGs predate the A1 no-rail revision.** They show the 48 DIP rail and different
   vertical rhythm. Shell shape/colour/fill are comparable (IoU 1.0); content positions are not.
4. **Expanded pages are top-anchored and leave ~50 DIP unused at the bottom** after the rail was
   removed (media lyrics end at viewport y≈202 of 252). The evidence fills more of the height.
   Recommend re-centering the page body / moving quick actions down before parity sign-off.
5. **Emoji must be shaped.** `SKTextBlob.Create(string)` mapped a surrogate pair to two glyphs;
   HarfBuzz produces one positioned glyph and color COLR rendering works.
6. **Shadow falloff differs from Avalonia.** Avalonia draws 4 scaled shells; Skia uses one Gaussian
   (`sigma=12 DIP`). Skia is smoother and slightly wider. Match the reference by reducing `sigma` to
   ~8 DIP / alpha .30 for compact if the difference is judged visible.

### 11.2 Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| ULW full-surface copy blows the 8.33 ms budget at 120 fps | Medium | High | Measure in host spike; dirty sub-rect upload; fall back to 90 fps or D3D present |
| Multi-monitor / per-monitor DPI artifacts | Medium | High | One surface per monitor; snap on `WM_DPICHANGED`; 125/150/175/200 test matrix |
| Text quality vs Avalonia (no ClearType, Latin kerning) | Low | Medium | Grayscale AA is spec-compliant; add HarfBuzz Latin shaping if needed |
| Color emoji/ZWJ sequences | Low | Low | HarfBuzz shaping + Segoe UI Emoji; fallback to monochrome |
| UIA provider effort underestimated | High | Medium | Budget 5–7 days; test with Inspect/Narrator early |
| Font licensing / availability (Segoe UI Variable, YaHei) | Low | Medium | System fonts only; explicit fallback chain; startup probe |
| Dirty-rect correctness (alpha double-blend) | Medium | Medium | Use `Src` clear, opaque backdrop for opaque presenters; golden-image tests |
| Static layer stale after palette/material change | Low | Medium | Invalidate on settings signature; one full frame |
| Memory growth from per-size shadow cache | Medium | Low | 0.5 DIP buckets + LRU; clear on settle/DPI change |

---

## 12. Go/no-go and effort

### 12.1 Gate results (spike machine, Release, 150 %)

Run-to-run p99 varies by ~±20 % on this laptop; repeated Release runs stayed inside the ranges
below (the best run was 0.60–0.65 ms mean / 1.12–1.29 ms p99 for the expanded frame).

| Gate | Target | Measured | Verdict |
|---|---|---|---|
| Expanded full frame @120 fps | p99 < 8.33 ms | **1.06–1.40 ms p99**, 0.63–0.80 ms mean | GO (5.9× headroom) |
| Morph 132×36→440×284 @120 fps | p99 < 8.33 ms | **2.01 ms p99** (scaled shadow) | GO (4.1× headroom) |
| Compact frame | p99 < 8.33 ms | **0.72–0.99 ms p99** | GO |
| Dirty content | < 2 ms | **0.16 ms mean / 0.23 ms p99** | GO |
| Archipelago (732×576) | p99 < 8.33 ms | **2.19 ms p99** | GO |
| Steady-state allocations | ~0 B/frame | **0.1 B/frame** | GO |
| Text warm hit | < 1 µs | **0.09–0.17 µs** | GO |
| Text cold layout | < 200 µs | **74–134 µs** | GO (prewarm) |
| Shell fidelity | pixel-exact geometry | **IoU 1.0000; 440×284** | GO |
| Idle fidelity | ΔE small | **IoU 0.9986; RGB Δ 3.1/255** | GO |
| 2× DPI | p99 < 8.33 ms | **2.38 ms p99** | GO |
| Host present + UIA | not measured | — | **Conditional** |

**Verdict: GO for compact + expanded parity on SkiaSharp.** Conditions:

1. Host spike proves the layered present path at 120 fps (or 90 fps with a documented downgrade).
2. UIA provider lands with the page/transport/slider/list controls in §9.
3. The page-layout issues in §11.1 (items 2 and 4) are fixed before visual sign-off.

### 12.2 Effort estimate (one graphics engineer, parity with current App)

| Workstream | Days | Notes |
|---|---:|---|
| Renderer core (scene, static layer, shadow, dirty rect, DPI) | 8–10 | spike code is the seed; add damage coalescing and tests |
| Text/i18n (fallback, shaping, cache, trimming, prewarm) | 4–5 | HarfBuzz emoji + zh already proven |
| Input/hit regions (compact, pager, wheel chain, strip, controls) | 5–7 | Core policies exist; focus on capture/cancel/hit geometry |
| Page parity (home/media/notifications/clipboard/agent/widgets) | 10–12 | six pages + empty states + list interactions |
| Compact parity (idle/media/HUD + remaining activities) | 4–6 | mostly token-driven |
| UIA / keyboard / focus / high contrast | 5–7 | fragment provider + tests |
| Host integration (ULW, per-monitor, settle region, topmost) | 5–8 | separate host spike; not in this spike |
| Performance/soak/regression harness | 5 | 1 h soak, 60/120 fps capture, golden images |
| **Total** | **46–60 eng-days** | ~9–12 weeks single engineer; compact+expanded MVP ~6 weeks |

If the Lead wants a narrower first milestone: **compact + expanded home/media + pager + strip**
(no UIA, no remaining compact surfaces) is ~20–25 eng-days and proves the architecture end-to-end.

---

## 13. WPF fallback criteria

Switch to the WPF fixed-size layered window (same `FrameClock`/`SpringSet`/`Layout`/`HitTester`,
`FrameworkElement.OnRender`, 60 fps cap, 9-slice baked shadows) if **any** of these is measured in
the host spike:

1. Warm expanded p99 > **8.33 ms** at 120 fps after the ULW/dirty-rect work, or > **16.67 ms** at 60.
2. Island working set > **120 MB** or a rising trend over a 30 min soak.
3. Per-monitor DPI/placement cannot meet the 108-case layout matrix (F-04-class regressions).
4. UIA fragment provider cannot expose the page/transport/slider controls within the M4 budget.
5. Text quality on transparent surfaces is judged unacceptable (grayscale AA is expected; ClearType
   is not available on layered surfaces in either stack).
6. The host cannot keep click-through/corner hit-testing correct during morphs.

Do **not** switch for cosmetic differences (shadow falloff, 1 px kerning, emoji style). Those are
tuning items, not architecture failures.

---

## 14. Open questions

1. Presenter: `UpdateLayeredWindow` (parity, simple) vs `WS_EX_NOREDIRECTIONBITMAP` + D3D/DComp
   (faster, more code). Decide in the host spike with a 120 fps capture.
2. Render thread: UI thread vs a dedicated render thread. Core springs are cheap; text layout is
   not. A render thread needs a double-buffered scene snapshot.
3. Latin shaping: accept Skia advances or add HarfBuzz with static Segoe UI faces?
4. Pager spring: Core `PagerSnapMotion` T 0.44/ζ 0.97 vs spec `motion.tab` T 0.34/ζ 0.92. The spike
   follows Core; update the spec/contract or revert Core.
5. Archipelago flow layout vs the current App overlap behaviour (recommend flow).
6. Page vertical rhythm after the rail removal (recommend re-centering; see §11.1 item 4).


