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
| **Claude Code** | yes | `%USERPROFILE%\.claude\projects\**\*.jsonl` | last assistant message, stop_reason (tool_use stays running), tool_use name, cwd, timestamps | 原生支持 |
| **Codex** | yes | `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl` | session_meta.cwd, task_started/task_complete, function_call name, timestamps | 原生支持 |
| **GitHub Copilot CLI** | yes | `%USERPROFILE%\.copilot\session-state\<id>\events.jsonl` | session.start/end, tool events, cwd, timestamps | 原生支持 |
| **Aider** | partial | `%USERPROFILE%\.aider.chat.history.md` | file mtime only → coarse running/completed, no tool detail | 未检测到（本机未安装） |
| **Cursor** | no (infeasible) | `state.vscdb` (SQLite) | no readable event stream | 不可行（原因见支持页） |
| **Windsurf / Qoder** | no (infeasible) | — | pure IDE agents, no local event files | 不可行 |
| **zcode** | no (infeasible) | — | no documented local state format | 不可行 |
| **WorkBuddy** | no (infeasible) | — | Electron client, no CLI/event files | 不可行 |

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
