# Skia layered host — acceptance checklist

> Last updated: 2026-10-18 23:30 (Asia/Shanghai)
> Scope: the production `LayeredWindowHost` + `IslandRenderer` path (`FLUIDBAR_HOST=skia`).
> Run `tools/smoke/skia-host-smoke.ps1` for the automated part; the rest is manual QA.

## 1. Automated smoke

- [ ] `pwsh tools/smoke/skia-host-smoke.ps1` exits 0 and writes `build/reports/skia-host-smoke/report.json`
- [ ] `desktop.png` shows the island with **no grey rectangle**; corner sample differs from panel sample
- [ ] process alive after 6 s; working set reasonable (report values); no crash log in `%TEMP%\fluidbar-smoke.log`
- [ ] idle 30 s: CPU near 0 %, no frame uploads (renderer stats / host counters)
- [ ] click compact → expands; Esc / click outside → collapses; focus never leaves the foreground window
- [ ] click on a transparent corner/gap falls through to the window below
- [ ] drag the island moves it by the exact pointer delta; release re-anchors

## 2. Visual / interaction

- [ ] compact idle 132×36, media 226×40, HUD 208×40, notification 248×36, clipboard 264×36, agent 236×36, timer 190×36, battery 208×36
- [ ] expanded shell 440×284, content 400×252 at 20/12 insets, **no black top/bottom bands**
- [ ] pager: drag follow 0.85, snap T 0.38 / ζ 0.94, wheel 140 px / 180 ms, flick ≤ 1 page, long drag multi-page, rubber band 0.35
- [ ] clipboard/scrollable content owns the wheel until the boundary; 40 px overscroll + 120 ms dwell then page; reverse cancels
- [ ] transient mini-islands: 3 (26/26 + 64×28) below the panel, hidden at rest, fade 900 ms after activity; far-page dots ≤ 3 per side, clickable
- [ ] settings/market windows open from tray/console; appearance changes apply live
- [ ] zh-Hans / en-US switch without restart; pseudo-locale `qps-ploc` shows no clipped text
- [ ] 150 % and 200 % DPI: crisp text, correct physical sizes, no blurry scaling

## 3. Performance

- [ ] 60 fps sustained; 120 fps attempted; p99 frame interval ≤ budget
- [ ] full-frame ULW ≤ 0.5 ms p99; dirty ≤ 0.2 ms p99 (spike reference: 0.06–0.16 / 0.08–0.10 ms)
- [ ] steady-state managed allocation ≤ 1 KB/frame; idle 0 uploads
- [ ] memory flat over 30 min (no growth from text/icon/shadow caches; bounded capacities)
- [ ] no UI-thread I/O; no `Window` bounds animation; no per-frame layout

## 4. Robustness

- [ ] monitor hot-plug / DPI change re-places the host once, keeps anchor
- [ ] dock mode: 488×444 host, dock strip + expanded panel + detached islands stay on-screen
- [ ] sleep/resume, session lock/unlock, fullscreen app (topmost policy) behave
- [ ] kill/restart: settings persist, no orphan tray icon / hotkey
- [ ] crash-free 2 h soak with activities toggling

## 5. Sign-off

- [ ] `dotnet build FluidBar.sln` 0 errors; `dotnet test` all suites green
- [ ] `docs/rewrite/STATUS.md` + `CHANGELOG.md` updated with the acceptance date and evidence paths
