# Animation Research (2026-10-21)

Goal: raise the **island body** animation quality without adding dependencies. This note records the
shallow-clone research and the exact values we mapped into `MotionTokens`.

## 1. Repositories and method

| Project | Clone | What we studied |
| --- | --- | --- |
| Telegram-iOS | `third_party/Telegram-iOS` — `git clone --depth 1 --filter=blob:none --sparse`, then `git show HEAD:submodules/...` | Display spring/transition infrastructure, interactive navigation transitions, list animation, context gesture activation, CoreList spring |
| MioIsland | `third_party/MioIsland` — `git clone --depth 1` | SwiftUI notch/island open-close, activity reveal, press/hover states, chat panel transitions |
| Hello-Dynamic-Island | `third_party/Hello-Dynamic-Island` — `git clone --depth 1` | SwiftUI Dynamic Island expand/collapse curve and content scale/opacity |
| iOS16-Live-Activities | `third_party/iOS16-Live-Activities` — `git clone --depth 1` | Live Activity layout/state vocabulary (no reusable animation curve found) |

All clones are shallow (`--depth 1`); Telegram is additionally blob-filtered and sparse. No history
or third-party animation library is imported into FluidBar.

## 2. Telegram-iOS findings

### 2.1 Spring and timing infrastructure

- `submodules/Display/Source/CAAnimationUtils.swift`
  - custom spring fallback: **mass 5.0, stiffness 900.0, damping 100.0**; duration is the spring's
    `settlingDuration`, linear timing function.
  - iOS 26 native spring (duration **0.3832 s**): mass 1.0, stiffness **555.027**, damping **47.118**.
  - arbitrary-duration spring fallback is the cubic Bézier **`(0.380, 0.700, 0.125, 1.000)`**.
- `submodules/Display/Source/ContainedViewLayoutTransition.swift`
  - every layout move is `.animated(duration, curve)`; `customSpring(mass: 5.0, stiffness: 900.0,
    damping: ...)`.
  - bounce helper: **duration × 1.25, damping 88, stiffness 750**.
  - navigation interactive completion: collapse **0.1 s easeInOut**; settled completion **0.5 s
    spring**; velocity handoff `max(0.05, min(0.2, distance / velocity))` — a short ease-out from
    the **current presentation value**, not a restart from zero.
- `submodules/Display/Source/InteractiveTransitionGestureRecognizer.swift`
  - edge validation width **16 DIP**; direction is validated before progress begins; progress can be
    reversed at any time.
- `submodules/Display/Source/ListViewAnimation.swift`
  - list spring is sampled from a **0.5 s** spring; per-item animations have an explicit `beginAt`
    so rows can be offset in time rather than all starting together.
- `submodules/Display/Source/ListViewItemNode.swift`
  - item springs carry explicit stiffness/damping and per-item start timestamps.
- `submodules/TelegramUI/Components/CoreList/CoreListDemo/Transition/CoreListSpringAnimation.swift`
  - classic CoreList spring: **mass 3.0 / stiffness 1000 / damping 500 / duration 0.5 s**.
  - iOS 26 CoreList spring: mass 1.0 / stiffness 555.027 / damping 47.118.
- `submodules/Display/Source/ContextControllerSourceNode.swift`
  - context activation begins after **0.12 s**; target scale = `max(0.7, (width − 15) / width)`
    interpolated by activation progress; this is a press-and-hold affordance, not an instant snap.

### 2.2 What we borrow

1. A short, under-damped press spring with a visible scale + brightness + inset shadow (Telegram's
   button/context activation), released back to identity with the same spring.
2. Layered entrance: container geometry first; content fade/slide delayed; chrome last. Telegram's
   list per-item `beginAt` and navigation progress choreography justify a **60 ms** content offset
   and another **60 ms** for chrome.
3. Interactive/velocity handoff: capture the current presentation value and retarget from there; never
   restart a page transition from page 0/identity. This is the model for rapid page switches.
4. Spring constants are authored as mass/stiffness/damping in Telegram; FluidBar uses the equivalent
   **period T + damping ratio ζ** form already used by Core, keeping all tokens in one place.

## 3. MioIsland findings

- `ClaudeIsland/Core/NotchViewModel.swift`: notch open/close uses `.easeOut(duration: 0.25)`;
  boot expands for **1.0 s** before collapsing.
- `ClaudeIsland/Core/NotchActivityCoordinator.swift`: showing/hiding the expanding activity uses
  `.smooth`.
- `ClaudeIsland/UI/Views/ChatView.swift`: waiting/approval transitions use
  `.spring(response: 0.35, dampingFraction: 0.85)`; message insertion uses asymmetric
  `.opacity + .move(edge: .bottom)`; processing/history use `.spring(response: 0.3, dampingFraction: 0.8)`.
- `ClaudeIsland/UI/Views/CompletionPanel/ClaudeStopVariantView.swift`:
  - press: **scale 0.94–0.96** in **spring(response: 0.2, dampingFraction: 0.6)**;
  - release: **spring(response: 0.3, dampingFraction: 0.6)**;
  - hover: scale 1.03–1.1, spring 0.25–0.3 / 0.65–0.7.
- `ClaudeIsland/UI/Views/CompletionPanel` / `ChatView`: content enter uses opacity + scale(0.95–0.98)
  + offset; removal is opacity only (asymmetric), avoiding a busy exit.

### What we borrow

- Press scale **0.96**, spring **T 0.18 s / ζ 0.65** (already our `MotionTokens.Press`).
- Content enter: scale **0.98–1.0**, short fade; reverse exit is quicker and has no extra bounce.
- Avoid animating every element symmetrically; only the newly inserted content gets the offset/scale.

## 4. Hello-Dynamic-Island findings

- `DynamicIsland/DynamicIslandView.swift`:
  - expanded content: `.opacity(expanded ? 1 : 0)` + `.scaleEffect(expanded ? 1 : 0.5, anchor: .top)`;
  - container: rounded rectangle radius morph **50 → 63**;
  - transition: `.interactiveSpring(response: 0.6, dampingFraction: 0.6, blendDuration: 0.6)`;
  - auto-collapse after **1.0 s**.

### What we borrow

- The island's morph is a **container scale/rounding morph + content scale from 0.98**, not a plain
  cross-fade. Our `MotionTokens.Expand` / `ExpandHeight` remain the authored shell curve; page and
  split content get a small in-place scale so the morph reads as one object.
- The tutorial's 0.6/0.6 is intentionally softer than our product UI; we keep our faster 0.36–0.46 s
  springs to match the existing FluidBar character.

## 5. Mapping into FluidBar

| Proposed token / constant | Value | Source / rationale | Applied to |
| --- | --- | --- | --- |
| `MotionTokens.Press` | T **0.18 s**, ζ **0.65** | existing + MioIsland press 0.2/0.6 | press spring |
| `MotionTokens.PressScale` | **0.96** | MioIsland press 0.94–0.96; Telegram button highlight | shell scale |
| `MotionTokens.PressBrightness` | **6%** white | iOS/Telegram highlight | shell brightness overlay |
| `MotionTokens.PressInnerShadow` | **18%** black top inset | Telegram context activation depth | shell inner shadow |
| `MotionTokens.ContentStaggerSeconds` | **0.060 s** | Telegram list `beginAt` + navigation layering | expanded content after container |
| `MotionTokens.ChromeStaggerSeconds` | **0.120 s** | container → content → chrome | title/collapse controls |
| `MotionTokens.PageScaleDrop` | **0.018** (1.8%) | Telegram interactive transition depth; kept subtle | page switch scale |
| `MotionTokens.PageFadeDrop` | **0.04** | asymmetric page enter/exit | page switch alpha |
| `MotionTokens.SplitStretch` | **8%** | Dynamic Island morph (shape, not pure translation) | split/fusion width |
| `MotionTokens.SplitSquash` | **6%** | same | split/fusion height |
| `MotionTokens.IdleBreathAmplitude` | **1.8%** | below the requested 2% ceiling | waveform amplitude / progress brightness |
| `MotionTokens.IdleBreathPeriodSeconds` | **4.0 s** | very slow idle motion | waveform/progress phase |
| `MotionTokens.Split` | T **0.46 s**, ζ **0.68** | existing split spring; retention | split/fusion spring |
| `MotionTokens.Expand` / `ExpandHeight` / `Collapse` | 0.36/0.82, 0.39/0.86, 0.30/1.00 | existing shell tokens | container expand/collapse |

### Concrete borrow list → FluidBar effects

1. **a) Press feedback** ← Telegram button/context highlight + MioIsland press/release:
   scale 0.96, 6% brightness, 18% inset shadow; press spring T .18/ζ .65; release same spring.
2. **b) Expand/collapse choreography** ← Telegram list `beginAt` + layered navigation:
   shell geometry starts immediately; content starts **60 ms** later; chrome **120 ms** later;
   collapse reverses (chrome/content disappear first, shell settles last).
3. **c) Page/activity switches** ← Telegram interactive transition presentation-value handoff:
   page track keeps its live `PagePosition`; every page gets 1.8% scale and 4% alpha dip at the
   halfway point; a second wheel/drag interrupts from the current position, never from zero.
4. **d) Split/fusion morph** ← Dynamic Island container morph:
   spring T .46/ζ .68 plus 8% stretch along travel and 6% squash across travel at mid-flight;
   returns to an exact circle at settle.
5. **e) Idle texture** ← MioIsland's slow ambient motion (and below the user's 2% cap):
   1.8% amplitude, 4 s period on the waveform amplitude and progress accent brightness.

## 6. Deliberate non-goals

- No third-party animation dependency, no CoreAnimation/SwiftUI runtime, no new DLL/NuGet.
- No full-history clones; only `--depth 1` (Telegram additionally blob-filtered/sparse).
- Settings panel is out of scope; this pass only touches island-body motion.
