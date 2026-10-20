# AI agent → FluidBar integration matrix

Generated: 2026-10-20 · branch v2/rebuild · author: island-audit (task-25)

This document records *how far each agent can be integrated*, what events are obtainable, and the
exact copy-paste snippet. Everything ultimately writes one schema-v2 event file
(`%APPDATA%\FluidBar\agent-events\*.json`); the island's AgentInboxWatcher displays it and merges
consecutive events with the same `sessionId` into one row.

## Contract

- Schema: `schemaVersion: 2`, fields `agent`, `event` (running/waiting/completed/failed), `title`,
  `message` (detail), `sessionId` (merge key), `startedAt` (elapsed label), `error` (folded into
  detail on failure), `timestamp`.
- Statuses: Running / WaitingForInput / Completed / Failed; unknown agents fall back to Generic.
- Kinds & display names: DeepSeek Harness, Claude Code, Codex, GitHub Copilot, Cursor, Windsurf,
  Aider, zcode, Qoder, WorkBuddy, Agent (generic).
- Duration: running rows show "已运行 X 分 Y 秒" from `startedAt`; finished rows "耗时 X 分 Y 秒".
- Same-session merging happens in `AgentEventHistory` (one row; earliest start wins), so running
  heartbeats never flood the page.
- Failure detail: the `error` field is appended to the detail line.

## Support matrix

| Agent | Detected on this machine | Mechanism | Events obtainable | Status | Snippet |
| --- | --- | --- | --- | --- | --- |
| **DeepSeek Harness (DSH)** | yes (`%USERPROFILE%\.dsh`, desktop app) | ① Cordis plugin on the official agent event bus ② projection-cache watch (zero install) ③ `dsh --profile headless --json` wrapper | created/running/step/tool/waiting/completed/failed | **已支持（真机验证，运行中+已完成截图）** | `docs/agent/snippets/dsh.md` |
| **Claude Code** | yes (`%USERPROFILE%\.claude\settings.json`) | hooks: UserPromptSubmit / PreToolUse / PostToolUse / Notification / Stop | running/tool/waiting/completed | 已支持（hooks；FluidBar 设置页内置安装器覆盖 Stop+Notification） | `docs/agent/snippets/claude-code.md` |
| **Codex** | yes (`%USERPROFILE%\.codex\config.toml`) | `notify` command (turn-ended) + managed lifecycle hooks on newer builds; generic wrapper for running state | completed/failed via notify; running via wrapper | 部分支持（notify 只有回合结束；running 需包装器） | `docs/agent/snippets/codex.md` |
| **GitHub Copilot** | yes (`gh` CLI) | documented agent hooks ("custom shell commands at key points during agent execution"); wrapper fallback | completed/failed (hooks), running (wrapper) | 部分支持（hooks 依版本；CLI 无统一逐步事件） | `docs/agent/snippets/copilot.md` |
| **Cursor** | no CLI/config detected | wrapper around `cursor-agent` CLI; IDE Agent mode has no documented hook/event stream | running/completed/failed (wrapper only) | 仅可包装（IDE 内部会话不可观测） | `docs/agent/snippets/cursor.md` |
| **Windsurf** | no | pure IDE (Cascade); no user shell hook or CLI event stream | none (wrapper only around a launched process) | 不可行（实时状态）；仅可包装 | `docs/agent/snippets/windsurf.md` |
| **Aider** | no | no documented lifecycle/notify option (aider.chat options page checked); CLI wrapper | running/completed/failed (wrapper only) | 仅可包装 | `docs/agent/snippets/aider.md` |
| **zcode** | no (CLI/install not found) | no public hook spec available in this environment; generic wrapper | running/completed/failed (wrapper only) | 待确认（需厂商 hook 文档） | `docs/agent/snippets/zcode.md` |
| **Qoder** | no | IDE-form agent; no public hook/CLI event stream | none documented | 待确认/仅可包装 | `docs/agent/snippets/qoder.md` |
| **WorkBuddy** | yes (`C:\Program Files\WorkBuddy` = Electron client, no CLI) | no public hook; wrapper only if a command-line entry exists | none documented | 不可行（实时状态）；原因见 snippet | `docs/agent/snippets/workbuddy.md` |

Notes:
- "Detected" is produced by `tools/agent-emit/detect.ps1` (read-only): config dirs + CLI commands.
- Public documentation checked on 2026-10-20: `code.claude.com/docs/en/hooks`,
  `docs.github.com/en/copilot/concepts/agents/hooks`, `aider.chat/docs/config/options.html`,
  `cursor.com/agent/cli` (web search was unavailable in this session, so zcode/qoder/workbuddy claims
  are limited to local installation evidence and their packaging form).
- Anything not observable without cooperation from the IDE is deliberately **not faked** (no polling
  of window titles / file changes presented as agent status).

## DSH: the primary integration (verified on this machine)

### Investigation

- Installation: `D:\Program Files\DeepSeek Harness` → `resources/app.asar` (Electron app) plus a
  Cordis-based runtime; the desktop profile lives at `%USERPROFILE%\.dsh\profiles\desktop`
  (`package.json` lists profile **bundles**, `cordis.patch.yml` is the user patch layer).
- Extension mechanism: external **Cordis plugins** (`dsh plugin --profile <name> add <package>`), and
  the Plugin API catalog inside `dsh-tool-cordis` documents the agent event bus
  (`agent/created`, `agent/status`, `agent/error`, `tools/result`, `user-questions/request`,
  `agent/disposed`).
- Live session data: `%USERPROFILE%\.dsh\storages\session_projcache\sessions\<sessionId>.json` is
  updated while the agent runs (`sessionStats.openStep`, `turnBoundary`, pending tool calls,
  `userQuestions`). This is the source for the zero-install watch bridge.
- No "hooks" key exists in DSH settings; the plugin/event-bus is the official hook surface. Nothing in
  DSH's program files was modified.

### Implemented paths

1. **Push plugin** — `tools/dsh-plugin-fluidbar-agent` (Cordis plugin; writes events from the bus).
2. **Watch bridge** — `tools/agent-emit/dsh-bridge.ps1 -Watch` (reads the projection cache; zero install).
3. **Headless wrapper** — `tools/agent-emit/dsh-bridge.ps1 -Run "<task>" [ -PatchPath <overlay> ]`
   (mirrors `dsh --profile headless --json` run events live).

### Verification (real machine, 2026-10-20)

- **Headless run through the wrapper**: session `session-fbd5097f…` emitted running (turn/step/tool)
  and then `completed`; the island merged both into one row.
  - `%TEMP%\fluidbar-evidence\fluidbar-50-agent-dsh-running.png` — 运行中, "第 1 轮 · 第 1 步",
    "已运行 0 分 1 秒".
  - `%TEMP%\fluidbar-evidence\fluidbar-51-agent-dsh-completed.png` — 已完成, "耗时 0 分 5 秒"
    (same row, no duplicate).
  - `%TEMP%\fluidbar-evidence\dsh-bridge-headless-events.jsonl` — the exact events the bridge wrote.
- **Desktop watch bridge**: a single `-Once` scan mirrored the live DSH desktop chat
  (`session-0f1d2183…`) as "运行中 第 8 轮 · 第 117 步 / 已运行 0 分 26 秒"
  (`fluidbar-52-agent-dsh-watch-running.png`, raw event `watch-once-event.json`). The bridge never
  writes into DSH; it reads the projection cache.
- **CLI wrapper**: `agent-emit-example.json` (schema-v2 event from `agent-emit.cmd`) and
  `detect-output.txt` (read-only agent detection) are in the same evidence folder.
- Evidence capture ran with FluidBar.exe (`FLUIDBAR_HOST=skia`) and an isolated inbox; the user's real
  inbox was backed up and restored afterwards.
- Full solution build + test suite after the changes: **all green** — 2083 tests across 9 test projects
  (Platform 598, Ui 343, Island 304, Core 567, Sdk 167, Integration 31, PluginHost 39, BuiltinPlugins 23,
  Architecture 11).

## Other agents: how to verify quickly

```powershell
# 1. what is installed?
pwsh -NoProfile -File tools\agent-emit\detect.ps1

# 2. emit a synthetic pair to check the island rendering (no agent needed)
pwsh -NoProfile -File tools\agent-emit\agent-emit.ps1 -Agent codex -Event running -Title Codex -SessionId demo -StartedAt (Get-Date).AddMinutes(-2).ToString('o') -Message 'using pwsh'
pwsh -NoProfile -File tools\agent-emit\agent-emit.ps1 -Agent codex -Event completed -Title Codex -SessionId demo -StartedAt (Get-Date).AddMinutes(-2).ToString('o') -Message done

# 3. real hook: follow docs/agent/snippets/<agent>.md then run one agent turn
```

## Status-quality changes behind this matrix (task-25)

- `AgentKind` extended: Generic, Claude, Codex, DeepSeek, Cursor, Windsurf, Aider, Copilot, Zcode,
  Qoder, WorkBuddy; each has a display name (brand names untranslated) and a stable kind token for
  compact/activity payloads.
- `AgentEventKind` refined: Running (new), WaitingForInput, Completed, Failed, Other (legacy).
- Schema v2 added `sessionId` / `startedAt` / `error`; v1 payloads keep working.
- Same-session merging in the bounded history (one row; earliest start retained; no eviction churn).
- Localized elapsed labels ("已运行 3 分 12 秒" / "Running 3m 12s"; "耗时 …" / "Took …").
- Failure text folded into the detail line; tones: running=Info, waiting=Warning, completed=Success,
  failed=Danger.
- Compact agent activity republishes on status transitions (running → completed) instead of being
  stuck on the first event of a session.
- Tests: `tests/FluidBar.Platform.Tests/AgentSessionStatusTests.cs`,
  `tests/FluidBar.Ui.Tests/AgentContentMapperTests.cs` (+ existing agent tests).
