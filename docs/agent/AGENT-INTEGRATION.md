# AI agent → FluidBar: native read-only support

Updated: 2026-10-20 · branch v2/rebuild · task-26: **no plugins, no hooks, no configuration**.
FluidBar observes agents the way it observes the media player: it reads the local files the agent
already writes. Nothing is installed and no third-party configuration is modified.

## Architecture

- `IAgentStatusProvider` (src/FluidBar.Platform.Windows/Agent/AgentStatusProvider.cs): one provider per
  agent, exposing `ProviderId`, `DisplayName`, `DataPath`, `IsAvailable` and `Poll(now)` returning
  provider-local events (running / waiting / completed / failed + detail + startedAt).
- `AgentProviderRegistry`: polls every available provider (2 s), converts events into the same bounded
  `AgentEventSnapshot` the hook inbox produces, merges by session id and publishes to the island.
- The existing hook inbox watcher stays as an **optional manual fallback** (tools/agent-emit); it is not
  required and the settings page does not offer to install anything.
- Native provider files are opened read-only; the registry never writes to them.

## Support matrix (detected on this machine)

| Agent | Native support | Source (read-only) | Parsed fields | State |
| --- | --- | --- | --- | --- |
| **DeepSeek Harness** | yes | `%USERPROFILE%\.dsh\storages\session_projcache\sessions\*.json` | turn/step, pending tool, active question, step boundary, timestamps | 原生支持 |
| **Claude Code** | yes | `%USERPROFILE%\.claude\projects\**\*.jsonl` | last assistant message, stop_reason (**only end_turn completes**; tool_use → running; long silence without end_turn → waiting), tool_use name, cwd, timestamps | 原生支持 |
| **Codex** | yes | `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl` | session_meta.cwd, task_started/task_complete, function_call name, timestamps | 原生支持 |
| **GitHub Copilot CLI** | yes | `%USERPROFILE%\.copilot\session-state\<id>\events.jsonl` | session.start/end, tool events, cwd, timestamps | 原生支持 |
| **Aider** | partial | `%USERPROFILE%\.aider.chat.history.md` | file mtime only → coarse running/completed, no tool detail | 未检测到（本机未安装） |
| **Cursor** | no (infeasible) | `state.vscdb` (SQLite) | no readable event stream | 不可行（原因见支持页） |
| **Windsurf** | no (infeasible) | — | pure IDE agent, no local event files | 不可行 |
| **Qoder** | no | — | checked dirs/shortcuts/registry/CLI/extensions: not installed | 未检测到 |
| **zcode** | no | — | checked install dirs/CLI/npm/extensions: not installed; no documented local state | 未检测到 |
| **WorkBuddy** | partial | `%USERPROFILE%\.workbuddy\workbuddy.db` (SQLite) | sessions table: title, status (running/pending/completed/error), cwd, activity time; queried from a read-only copy | 部分支持（已完成/失败可靠；运行中取决于 status/活动时间） |

The settings page (settings → 代理支持 / Agent support) renders this matrix: support level, data source
path, parsed fields; unsupported rows explain why. There are **no install buttons**.

## Evidence (real machine, 2026-10-20)

All screenshots were captured with `%APPDATA%\FluidBar\agent-events` temporarily isolated, so the only
rows came from the native providers. No plugin, hook, script or configuration change was active.

- `%TEMP%\fluidbar-evidence\fluidbar-60-dsh-builtin-running.png` — DSH native row:
  "DeepSeek Harness · … 运行中 工具执行中 已运行 0 分 18 秒" (no script, no plugin).
- `%TEMP%\fluidbar-evidence\fluidbar-61-dsh-builtin-completed.png` — the same live session plus a real
  `dsh --profile headless` session that completed while FluidBar watched ("已完成").
- `%TEMP%\fluidbar-evidence\fluidbar-64-native-agents.png` — four native rows at once from real recorded
  sessions: DSH running, GitHub Copilot / Codex / Claude Code completed (Claude Code project DNSHE-Panel).
- `settings-support-zh-Hans.png` / `settings-support-en-US.png` — the bilingual support page.

Notes on live capture:
- The DSH running row comes from the live desktop session's projection cache (tool phase = step boundary
  "start"); the completed row comes from a genuine `dsh headless` run observed end-to-end.
- Claude/Codex/Copilot rows were produced from **existing real session records** (a Claude Code JSONL
  transcript, a Codex rollout, a Copilot events file) copied into the watched directories with a fresh
  mtime; the providers parsed the real transcript content (tool names, completion state, project cwd).
  A live running capture needs a live agent process; the parser is pinned by unit tests including Claude's
  `tool_use` stop reason.

## What changed in the app

- `src/FluidBar.Platform.Windows/Agent/`:
  - `AgentStatusProvider.cs` — provider contract + registry poller.
  - `NativeAgentProviders.cs` — DSH / Claude Code / Codex / Copilot / Aider providers.
  - `AgentSupportCatalog.cs` — support-status rows (native/partial/not detected/infeasible + notes).
  - `DshSessionProvider.cs` — DSH parser as an `IAgentStatusProvider`.
- `src/FluidBar.App/Hosting/Skia/SkiaPlatformActions.cs` — builds the registry from
  `NativeAgentProviders.CreateDefault()`, merges it with the optional hook inbox, starts/stops it.
- Settings: new **Agent support** section (8th nav item) built from the shared `SettingsControls` layer;
  bilingual catalogs (`Settings.Support.*`). The previous one-click hook page and
  `AgentIntegrationService` were removed, so FluidBar never modifies another agent's configuration.
- Path seams for tests/diagnostics: `FLUIDBAR_DSH_PROJECTION`, `FLUIDBAR_CLAUDE_PROJECTS`,
  `FLUIDBAR_CODEX_SESSIONS`, `FLUIDBAR_COPILOT_STATE`, `FLUIDBAR_AIDER_HISTORY`.

## Optional fallback tools (not part of the support page)

`tools/agent-emit/` and `tools/dsh-plugin-fluidbar-agent/` remain for users who explicitly want
push-style hooks/plugins; nothing installs them and they are not needed for any native-supported agent.
The old hook installer was deleted so FluidBar cannot modify another agent's configuration.

## Tests

- `tests/FluidBar.Platform.Tests/NativeAgentProviderTests.cs`: DSH parser (running/waiting/tool phase,
  completion transition, headless created-during-watch, malformed files), Claude transcript parser
  (running, tool_use, completed), Codex rollout parser, Copilot events parser, Aider mtime parser,
  registry merge/unavailable-provider handling, support-catalog mapping.
- `tests/FluidBar.Ui.Tests/AgentSupportSectionTests.cs`: the support page renders every agent, has no
  Install/Remove buttons, is bilingual and explains infeasible agents; the control-layer guard runs for
  the new section too.
- Full solution build + tests are green (see the task report for exact counts).

## WorkBuddy native provider (task-27)

WorkBuddy is installed as an Electron client at C:\Program Files\WorkBuddy (no CLI). Paths checked:

- %APPDATA%\WorkBuddy — exists but empty; %LOCALAPPDATA%\WorkBuddy and %LOCALAPPDATA%\Programs\WorkBuddy absent.
- %APPDATA%\WorkBuddyManager\launcher.conf — launcher preferences only.
- %USERPROFILE%\.codebuddy\logs — VS Code-fork logs, no session state.
- C:\Program Files\WorkBuddy\resources\app.asar — Electron bundle, no command line interface.
- %USERPROFILE%\.workbuddy\ — the real data directory: workbuddy.db (SQLite), sessions/, tasks/, logs/, traces/, workspace/sessions/.

SQLite schema (inspected from a copy of workbuddy.db + -wal, opened read-only):
sessions(id, status, title, custom_title, cwd, created_at, updated_at, last_activity_at, deleted_at, agent_dirty, agent_last_synced, ...),
session_usage, automation_runtime_state(running, ...), automations, workspaces, buddy_snapshots, plus tasks/<id>/*.json (status pending/completed).
Observed while the app was closed: 32 sessions (25 completed, 7 error), no running row; agent_dirty/agent_last_synced null; automation runtime table empty.

Implementation (src/FluidBar.Platform.Windows/Agent/WorkBuddySessionProvider.cs):
copies workbuddy.db (+ -wal) to %TEMP%\FluidBar\workbuddy-db, opens the COPY read-write via winsqlite3.dll
(the original is never opened), and maps sessions.status: running/in_progress/pending or non-terminal-with-recent-activity -> running;
completed -> completed; error -> failed. Results are cached by DB/WAL timestamp so the 2 s poll does not recopy the file.

Limitation: the WorkBuddy app was not running during this session (and must not be disturbed), so a live running transition could not be observed.
Evidence (fluidbar-65/66) uses a copy of the real sessions DB with one real session row set to running and then completed by a script;
the provider read that copy end-to-end (zero plugins, zero configuration).

## qoder / zcode investigation (task-27)

qoder — checked C:\Program Files\Qoder, C:\Program Files (x86)\Qoder, %LOCALAPPDATA%\Programs\Qoder, %APPDATA%\Qoder, %LOCALAPPDATA%\Qoder,
Start Menu shortcuts, App Paths registry, VS Code extensions, npm global modules and 'where qoder'. None exist: not installed, nothing to parse.

zcode — checked C:\Program Files\zcode*, %LOCALAPPDATA%\Programs\zcode, %APPDATA%\zcode, %LOCALAPPDATA%\zcode, Start Menu shortcuts, registry,
npm global modules and 'where zcode'. None exist: not installed, and there is no documented local state format to parse defensively.

## Claude Code status semantics (task-27)

- Only the last assistant message with stop_reason=end_turn marks the session completed.
- A recent tool_use / no-end-turn transcript is running; after 120 s without new lines and without end_turn it becomes waiting (等待输入),
  so a blocked Claude session can no longer stay running forever.
- Long imported transcript histories use the observation time as the elapsed base (no week-long durations).
- Unit tests cover end_turn completion, tool_use running, quiet->waiting, waiting->end_turn completion and stale-at-startup adoption.

## Evidence added in task-27

- fluidbar-65-workbuddy-running.png — WorkBuddy 运行中 (real DB copy, 已运行 0 分 6 秒)
- fluidbar-66-workbuddy-completed.png — WorkBuddy 已完成 (0 分 8 秒)
- fluidbar-67-claude-waiting.png — Claude Code 等待中「等待输入」(real transcript, tool_use, stale)
- fluidbar-68-claude-completed.png — Claude Code 已完成 after end_turn (0 分 10 秒)
- settings-support-zh-Hans.png / settings-support-en-US.png — updated support page: WorkBuddy 部分支持, qoder/zcode 未检测到, Cursor/Windsurf 不可行


### Evidence scope note (WorkBuddy)

**The WorkBuddy evidence is a read-path diagnostic, not a live observation**: the screenshots were produced
from a copy of the real `~/.workbuddy/workbuddy.db` with one existing session row changed to `running` and
then `completed` by a script; the WorkBuddy app itself was not running and no real running transition was
observed. The provider read that copy end-to-end (copy + winsqlite3 query + status mapping).

### Tunable parameters and wording

- Claude Code active threshold: default **120 s** (`ClaudeTranscriptProvider(activeThreshold: TimeSpan)`);
  a transcript quiet for longer than that without `end_turn` becomes `waiting`, not `running`.
- UI wording: status chip **等待中 / Waiting** (orange warning tone) + detail **等待输入 / Waiting for input**,
  deliberately distinct from **运行中 / Running** (blue); a waiting row is not shown as stuck/running.
- WorkBuddy reader: if `winsqlite3.dll` is missing or the copied DB cannot be opened, the provider degrades to
  an empty result per poll (no exception on the monitor thread); covered by a unit test.

