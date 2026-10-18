# FluidBar.Core — M6 soak/performance harness & recording contracts

| Item | Value |
|---|---|
| Project | `tests/FluidBar.Soak` (`net10.0` console, references **FluidBar.Core only**) |
| Harness entry | `dotnet run --project tests/FluidBar.Soak -c Release` |
| Report | JSON on **stdout**; human summary on **stderr**; optional `--report <path>` |
| Exit codes | `0` all budgets pass · `1` budget violation · `2` usage error |
| Budget constants | `tests/FluidBar.Soak/SoakBudgets.cs` (also linked into Core regression tests) |
| Recording format | `fluidbar.event-recording.v1` (`tests/FluidBar.Soak/Recording/EventRecording.cs`) |
| Fixtures | `tests/FluidBar.Soak/Fixtures/*.json` (embedded + copied to output) |
| Regression tests | `tests/FluidBar.Core.Tests/SoakBudgetRegressionTests.cs`, `RecordingReplayTests.cs` |
| Owner | core-owner (M6 groundwork), CI wiring by Lead |

The harness is deterministic (single seed), UI-free and Win32-free: it drives the real
`EventHub` → `ActivityScheduler` → `IslandStateMachine` → `IslandLayout` /
`ActivityPresenter` / `Spring` / `SettingsStore` pipeline with a `ManualDispatcher` and a
virtual clock derived from event offsets. No network, no file I/O outside `--report` (and the
settings scenario's `InMemorySettingsFileSystem`).

---

## 1. CLI

```powershell
# Full soak (default: A-F), JSON report to stdout, human summary to stderr
dotnet run --project tests/FluidBar.Soak -c Release

# Write the JSON report for CI artifacts
dotnet run --project tests/FluidBar.Soak -c Release -- --report artifacts/soak-report.json

# Run a subset / change seed / stop at first failure
dotnet run --project tests/FluidBar.Soak -c Release -- --scenarios A,B,D --seed 20261018 --fail-fast

# Discover scenario ids
dotnet run --project tests/FluidBar.Soak -c Release -- --list
```

Total runtime budget is **55 000 ms** (`SoakBudgets.TotalRuntimeBudgetMs`, enforced by scenario
`T`); reference machine runtime is ≈0.6 s, so CI has >90× headroom against the 60 s requirement.

---

## 2. Scenarios, workloads and budgets

Budgets are deliberately loose (≈5–60× measured headroom) so CI variance cannot cause false
failures; the *measured* numbers are printed in every run/report for trend tracking.

### A — Event storm (`SoakBudgets.EventStorm*`)

100,000 mixed `IslandEvent`s published through `EventHub` (manual dispatcher, drained every 128
events — a realistic UI-thread cadence), then `ActivityScheduler` + `IslandStateMachine`, with
`IslandLayout` computed 64 times. Mix: 40 % media incrementals (coalesced), 25 % notification
new/replacement, 13 % clipboard replacement, 10 % volume incrementals, 4 % battery, 4 %
lock-keys, 4 % retracts.

| Budget | Value | Measured (reference) |
|---|---|---|
| events | 100,000 | 100,000 |
| dropped | `== 0` | 0 |
| coalesced | `>= 1` | 47,780 |
| throughput | `>= 10,000 ev/s` | ≈625,000–719,000 ev/s |
| allocated/event | `<= 32 KiB` | ≈1,301 B |
| peak working set | `<= 512 MiB` | ≈49–51 MiB |
| max visible / background / retained | `<= 3 / 8 / 11` | 3 / 3 / 6 |
| layout violations (shell outside work area) | `== 0` | 0 |
| duration reference | — | ≈140–160 ms |

### B — Incremental media (`SoakBudgets.Incremental*`)

10,000 same-key `Incremental` media updates at a simulated 10 Hz (progress/lyrics/position),
one seeded `New` first.

| Budget | Value | Measured |
|---|---|---|
| primary changes | `== 0` | 0 |
| visible/background shape violations | `== 0` | 0 |
| state-kind violations (must stay `Compact`) | `== 0` | 0 |
| layout target changes | `== 0` | 0 |
| non-`ContentUpdated` scheduler outcomes | `== 0` | 0 |
| scheduler `SwitchedCount` delta | `== 0` | 0 |
| dropped | `== 0` | 0 |
| allocated/update | `<= 32 KiB` | ≈2,102 B |
| duration reference | — | ≈20 ms |

### C — Activity churn/expiry (`SoakBudgets.Churn*`)

10,000 deterministic New/Replacement/Incremental/Retract events across all activity kinds with
TTL ticks (virtual time 1 s/event, expiry tick every 2 events).

| Budget | Value | Measured |
|---|---|---|
| invariant violations (Visible ≤3, Background ≤8, retained ≤11, counts consistent) | `== 0` | 0 |
| sequence-map leaks (`LastSequences.Count > retained`) | `== 0` | 0 |
| TTL expiry ticks observed | `>= 1` | 41 |
| heap growth after warm-up (forced GC) | `<= 32 MiB` | ≈0 KB |
| final retained activities after retract-all + day-later tick | `== 0` | 0 |
| duration reference | — | ≈30 ms |

### D — Motion (`SoakBudgets.Motion*`)

All 9 tokens (`expand`, `expand.height`, `collapse`, `morph`, `hover`, `press`, `tab`, `nudge`,
`hide`) settled at 1/300 s frames for amplitudes 20/25/30 DIP; measured settle time compared with
`SettleTimePredictor` (independent RK4 2 % reference). Then 10,000 active substeps after 1,000
warm-up steps are measured for allocations, and `MotionLoop` is driven until it stops itself.

| Budget | Value | Measured |
|---|---|---|
| max settle deviation vs reference | `<= 20 %` | 3.68 % |
| allocations over 10k active substeps × 9 tokens | `<= 64 KiB` | **0 B** |
| `MotionLoop` self-stops at rest | `== 9 tokens` | 9/9 |
| frame stddev per token (deterministic) | reported | 0 ms |
| duration reference | — | ≈11 ms |

### E — Settings atomic saves (`SoakBudgets.Settings*`)

10,000 `SettingsStore.Save` calls through `InMemorySettingsFileSystem` (serialize → temp write →
replace → backup), alternating theme/font-scale; the test double's operation log is trimmed every
1,000 saves like a real rotation point.

| Budget | Value | Measured |
|---|---|---|
| failed saves | `== 0` | 0 |
| settings-saves counter | `== 10000` | 10,000 |
| save-failure counter | `== 0` | 0 |
| heap growth after warm-up (forced GC) | `<= 16 MiB` | ≈102 KB |
| final temp file absent | `== 1` | yes |
| backup present / final state reloads | `== 1` | yes |
| allocated/save | reported | ≈14 KiB |
| duration reference | — | ≈300 ms |

### F — Recording/replay fixtures (`SoakBudgets.FixtureCount`)

Replays the 3 deterministic recordings through the Core pipeline, validates every expected
presentation snapshot and replays twice to prove determinism.

| Budget | Value | Measured |
|---|---|---|
| fixtures | `== 3` | 3 |
| snapshot mismatches | `== 0` | 0 |
| non-deterministic replays | `== 0` | 0 |
| duration reference | — | ≈30 ms |

### T — Total runtime

| Budget | Value | Measured |
|---|---|---|
| total runtime | `<= 55,000 ms` | ≈600 ms |

Reference measurements: 2026-10-18, .NET 10.0.9, Windows 10.0.26200, 32 logical cores, Release.
Numbers will differ per machine; budgets must keep ≥5× headroom. Raise a budget only with a
committed soak-report artifact showing the regression is intentional.

---

## 3. JSON report

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-10-18T11:14:15+00:00",
  "seed": 20261018,
  "environment": {
    "framework": ".NET 10.0.9",
    "os": "Microsoft Windows 10.0.26200",
    "processorCount": 32,
    "machineName": "CI-RUNNER",
    "buildConfiguration": "Release"
  },
  "totalDurationMs": 604.5,
  "passed": true,
  "scenarios": [
    {
      "id": "A",
      "name": "event storm (100k mixed events)",
      "passed": true,
      "durationMs": 160.0,
      "metrics": { "throughputEventsPerSecond": 624998, "eventsDropped": 0, "...": 0 },
      "budgets": [
        { "name": "eventsDropped", "comparison": "==", "budget": 0, "actual": 0, "passed": true }
      ],
      "failures": []
    }
  ]
}
```

* `passed` is the AND of all scenarios and the total-runtime budget.
* Every budget breach produces a precise `failures[]` entry
  (`name: measured X, budget <= Y`) and the human summary repeats it on stderr.
* Metrics are doubles; boolean-ish values are encoded as `0/1` where useful for dashboards.

CI recipe (Lead wires the workflow):

```yaml
- name: Core soak (M6 budgets)
  run: dotnet run --project tests/FluidBar.Soak -c Release -- --report artifacts/soak-report.json
  timeout-minutes: 3           # harness itself budgets 55 s; 3 min covers build/JIT on slow agents
- uses: actions/upload-artifact@v4
  if: always()
  with:
    name: soak-report
    path: artifacts/soak-report.json
```

Do **not** parallelise the scenarios on one machine: the allocation/heap metrics are
process-global and would cross-contaminate. Split by scenario (`--scenarios A`) only when each
split gets its own job/process.

---

## 4. Event recording/replay format (`fluidbar.event-recording.v1`)

The fixtures are the deterministic bridge between Core and UI tests: a UI test replays a real
event sequence through the Core pipeline and snapshots the resulting `IslandViewModel` /
`ActivityViewModel` without any Platform or UI setup.

### 4.1 Schema

```json
{
  "format": "fluidbar.event-recording.v1",
  "name": "media-session",
  "description": "…",
  "seed": 1101,
  "tickExpiryBeforeEachEvent": true,
  "events": [
    {
      "offsetMs": 0,
      "kind": "New",                    // New | Replacement | Incremental | Retract
      "activityKind": "Media",          // ActivityKind enum name
      "source": "media:gsmtc",
      "sequence": 1,
      "payload": {
        "title": "Midnight City", "subtitle": "M83",
        "isActive": true, "showsAudioWave": true,
        "positionTicks": 0, "endTicks": 3000000000,
        "progressPercent": 10, "lyricLine": "…", "level": 62,
        "ttlMs": 5000, "isPinned": false, "preferredPage": "Media"
      }
    }
  ],
  "expected": [
    {
      "atEventIndex": 0,
      "stateKind": "Compact",           // IslandStateKind
      "page": "Media",                  // IslandPage
      "primaryKind": "Media",           // null = idle surface
      "title": "Midnight City",
      "ghostCount": 0, "badgeCount": 0,
      "progressPercent": 0, "showsWave": true,
      "hudLabelKey": "Hud.Volume"
    }
  ]
}
```

Rules:

* `offsetMs` is the only clock; replay constructs `<origin> + offsetMs` with the fixed origin
  `2026-10-18T12:00:00Z`. **No ambient time is read.**
* When `tickExpiryBeforeEachEvent` is true (default), `ActivityScheduler.Tick(now)` runs before
  each event, so TTL fixtures (clipboard 4 s, notification 5 s, lock-keys 1.6 s, …) behave exactly
  as in the app.
* `expected[].atEventIndex` refers to the zero-based index in `events`; a fixture may assert any
  subset of indices.
* Presentation snapshots are compared field-by-field; absent expected fields mean “don't care”
  for nullable fields (`title`, `progressPercent`, `hudLabelKey`) and default values otherwise.

### 4.2 Bundled fixtures

| Fixture | Events | Assertions | Covers |
|---|---|---|---|
| `media-session` | 6 | 6 | GSMTC start → progress/lyrics increments → track replacement → retract |
| `notification-burst` | 6 | 6 | same-rank replacement, clipboard preemption of a transient notification, media ghost, retract to idle |
| `clipboard-agent-mix` | 7 | 7 | ghost stacking, TTL expiry of transient primaries, HUD surface, retract |

### 4.3 Replay helper API (`FluidBar.Soak.Recording`)

```csharp
var recording = EventRecordingCatalog.Load("media-session"); // embedded fixtures
var steps = EventRecordingReplay.Replay(recording);          // one ReplayStep per event
foreach (var step in steps)
{
    var view = step.View;               // IslandViewModel (primary/ghosts/overflow/HUD)
    var snapshot = EventRecordingReplay.Snapshot(step); // ExpectedSnapshot shape (actually observed)
    // UI tests: render/snapshot against snapshot + step.SchedulerState + step.Transition
}

var failures = EventRecordingReplay.Validate(recording);      // empty when fixture is correct
```

`ReplayStep` exposes `Event`, `SchedulerState`, `Update`, `Transition` and `View`, so UI tests can
assert both presentation and effects (e.g. nudge/animation retargets) per event.

### 4.4 How UI tests consume the fixtures

The format + replay helper and the JSON fixtures are authored once in `FluidBar.Soak`; the Core
regression suite links them (see `tests/FluidBar.Core.Tests/FluidBar.Core.Tests.csproj`):

```xml
<ItemGroup>
  <Compile Include="..\FluidBar.Soak\Recording\**\*.cs"
           Link="Soak\Recording\%(RecursiveDir)%(Filename)%(Extension)" />
  <Compile Include="..\FluidBar.Soak\SoakBudgets.cs" Link="Soak\SoakBudgets.cs" />
  <EmbeddedResource Include="..\FluidBar.Soak\Fixtures\*.json"
                    Link="Fixtures\%(Filename)%(Extension)" />
</ItemGroup>
```

`FluidBar.Ui.Tests` can use either:

1. **Project reference** to `tests/FluidBar.Soak` (it is an Exe but builds a normal library too;
   `EventRecordingCatalog` reads the embedded fixtures from that assembly), or
2. the same `<Compile Include…/><EmbeddedResource Include…/>` link block, or
3. plain JSON loading if the UI test only needs the contract (`format` field must be validated).

UI tests should treat the fixtures as read-only and pin assertions to `format:
"fluidbar.event-recording.v1"`; a new format version requires a new fixture directory/version
field, never in-place mutation.

---

## 5. In-process regression budgets (Core.Tests)

`SoakBudgetRegressionTests` enforces the same constants on smaller deterministic workloads so a
PR that breaks a budget fails `dotnet test` even before the soak job runs:

| Test | Guarantees |
|---|---|
| `EventHub_QueueCapacityIsBoundedUnderFlood` | capacity 64 + 500 events ⇒ `QueueDepth == 64`, `Dropped == 436`, exactly 64 delivered |
| `EventHub_CoalescesIncrementalFloodIntoOnePendingEvent` | 10,000 same-key incrementals ⇒ 1 pending, 9,999 coalesced, 0 dropped, latest sequence delivered |
| `Scheduler_ChurnStaysWithinRetainedStateBudgets` | 2,000 mixed events ⇒ Visible ≤3, Background ≤8, retained ≤11, consistent counts, no sequence leak, empty after retract+expiry |
| `Motion_SteppingAfterWarmupAllocatesNothing` | 10,000 active substeps after warm-up allocate ≤4 KiB (measured 0) |
| `SettingsStore_RepeatedSavesStayBoundedAndCorrect` | 1,000 atomic saves ⇒ heap growth ≤16 MiB, 1,000 save counter, temp absent, backup present, reload matches final state |

`RecordingReplayTests` additionally asserts: catalog has exactly 3 fixtures, every fixture
matches its expected snapshots, replay is deterministic (twice ⇒ identical snapshots), and the
three fixtures exercise the documented behaviours (media title/progress sequence, transient
preemption drop, HUD/expiry surfaces).

---

## 6. Known limitations / flakiness guards

1. **Release is the reference configuration.** Debug numbers are valid (budgets still enforced)
   but have different allocation/JIT behaviour; CI should run `-c Release`.
2. **Allocation metrics are process-global.** Scenarios must run sequentially in one process, as
   the harness does. `GC.GetAllocatedBytesForCurrentThread` is used for per-step/per-update
   numbers; heap-growth numbers force a full blocking GC before/after warm-up.
3. **Throughput is machine-dependent.** The floor is 10,000 ev/s (~60× headroom); it is a
   regression tripwire, not a performance target. The real M6 performance targets are measured by
   the UI/soak workstream (120 fps p99, CPU/working-set budgets).
4. **Peak working set** covers the whole test process (including the runtime and xUnit-free
   harness); the 512 MiB ceiling catches runaway growth only.
5. **Settings scenario uses the in-memory file system** (no disk I/O, deterministic). Its
   operation log is trimmed every 1,000 saves because a real file system does not retain history;
   the production atomic-write protocol (temp → replace → backup) is still exercised exactly.
6. **`InMemorySettingsFileSystem`** is a test double; disk-full/permission failures are covered by
   unit tests, not the soak harness.
7. **Motion scenario seeds:** `seed` is accepted for interface symmetry, but motion is fully
   deterministic and does not consume randomness.
8. **Fixtures are deterministic snapshots** of the confirmed Core semantics; when Core behaviour
   intentionally changes, update `expected` in the fixture **and** the reason in the PR (the soak
   F scenario and `RecordingReplayTests` fail otherwise).
9. **No UI/Platform coverage here.** Click-through, DPI placement, render frame budget and
   1 h handle/memory soak belong to the UI/Platform soak workstream; this harness covers the
   Core domain pipeline only.
