# DeepSeek Harness (DSH) → FluidBar

DSH is the user's primary agent, so it has three integration levels. All of them emit the same
schema-v2 event file, so the island shows one merged row per session.

## 1. Desktop watch bridge (zero install, recommended)

DSH desktop writes a live projection for every session under
`%USERPROFILE%\.dsh\storages\session_projcache\sessions\<sessionId>.json`. The bridge polls that
folder (read-only) and mirrors each active desktop session:

```powershell
pwsh -NoProfile -File tools\agent-emit\dsh-bridge.ps1 -Watch
```

- Running: `sessionStats.openStep` is non-null → row + step number or pending tool name.
- Waiting: active `userQuestions` → "等待输入：<question>".
- Completed: the step closes and the turn ends.
- Heartbeat every 10 s keeps "已运行 X 分 Y 秒" ticking (same sessionId, no duplicate rows).

Options: `-LoopSeconds 2`, `-HeartbeatSeconds 10`, `-ActiveMinutes 30`, `-ProjectionDir <path>`,
`-InboxDir <path>`, `-Once` (single scan). Stop with Ctrl+C.

## 2. Headless wrapper (one-shot / automation)

`dsh --profile headless --json "<task>"` streams `session` / `status` / `tool_call` /
`tool_result` / `turn_end` events. The wrapper mirrors them in real time:

```powershell
pwsh -NoProfile -File tools\agent-emit\dsh-bridge.ps1 -Run "run the tests and summarise"
```

The headless profile needs a model provider. Copy `dsh-headless-patch.example.yml`, fill in your
provider (or point at an existing provider overlay), and pass `-PatchPath <file>`; credentials are
resolved by DSH's credentials service (same store the desktop Models page writes).

## 3. Cordis plugin (push events, official extension point)

DSH desktop is a Cordis plugin host. The official hook is the agent event bus documented inside
`dsh-tool-cordis` (Plugin API catalog):

| Event | Payload | Use |
| --- | --- | --- |
| `agent/created` | `{ agent, source }` | session started |
| `agent/status` | `{ agent, status: 'idle'\|'running' }` | running/finished transitions |
| `agent/error` | `{ agent, turn, step, error }` | failure detail |
| `tools/result` | `(exec, result)` | tool name/detail |
| `user-questions/request` | waterfall `(request, next)` | waiting for input |
| `agent/disposed` | `{ agent }` | cleanup |

`tools/dsh-plugin-fluidbar-agent` is a ready Cordis plugin. Install (user action, then restart DSH):

```powershell
dsh plugin --profile desktop add file:D:\build\GitLocal\FluidBar\tools\dsh-plugin-fluidbar-agent
# then add "dsh-plugin-fluidbar-agent" to dsh.profile.bundles in
#   %USERPROFILE%\.dsh\profiles\desktop\package.json
# remember to also keep it in the profile's patch layer (see the package README)
```

Restart DeepSeek Harness. The plugin writes the same inbox files, so the island rows are identical
to the watch bridge — but they are push-driven instead of polled.

## Why not modify DSH internals

The plugin API above is the officially supported extension surface; nothing in DSH's program files is
patched. The desktop watch bridge reads the projection cache only. Both approaches survive DSH
updates.
