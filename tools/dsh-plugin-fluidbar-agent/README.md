# dsh-plugin-fluidbar-agent

DeepSeek Harness (DSH) → FluidBar agent-status plugin. It subscribes to DSH's documented Cordis agent
event bus and writes FluidBar schema-v2 agent events, so the island shows one merged
**running → completed/failed** row per DSH session.

## Why a plugin

DSH desktop is a Cordis plugin host; the officially supported extension point is the agent event bus
documented in `dsh-tool-cordis` (`agent/created`, `agent/status`, `agent/error`, `tools/result`,
`user-questions/request`, `agent/disposed`). The plugin uses that surface only — DSH's program files
are never patched, so it survives DSH updates.

## Install

```powershell
# 1. install the package into the desktop profile (pnpm under the hood)
dsh plugin --profile desktop add file:D:\build\GitLocal\FluidBar\tools\dsh-plugin-fluidbar-agent

# 2. add it to the profile bundles so the patch layer is composed
#    %USERPROFILE%\.dsh\profiles\desktop\package.json
#      "dsh": { "profile": { "bundles": [ ..., "dsh-plugin-fluidbar-agent" ] } }

# 3. restart DeepSeek Harness
```

If `dsh plugin add` is unavailable in your DSH build, copy this folder into
`%USERPROFILE%\.dsh\profiles\desktop\node_modules\dsh-plugin-fluidbar-agent` and add the same
bundle name; the loader resolves the package by name.

## Verify

Start a DSH task, then watch the inbox:

```powershell
Get-ChildItem "$env:APPDATA\FluidBar\agent-events" -Filter *.json |
  ForEach-Object { Get-Content $_.FullName -Raw } | Select-String '"agent":"dsh"'
```

The island agent page shows "DeepSeek Harness 运行中" while the agent works and "已完成" afterwards.
`已运行 X 分 Y 秒` comes from the plugin's `startedAt` on the first running event.

## Config

| Option | Default | Meaning |
| --- | --- | --- |
| `inboxDir` | `%APPDATA%\FluidBar\agent-events` | where event files are written |
| `title` | `DeepSeek Harness` | row title |

Set them through the profile's `cordis.patch.yml` entry:

```yaml
- id: fluidbar-agent-hook
  name: dsh-plugin-fluidbar-agent
  config:
    title: DeepSeek Harness
    inboxDir: C:\\Users\\me\\AppData\\Roaming\\FluidBar\\agent-events
```

## Limitations

- Installing/removing requires a DSH restart (the profile is composed at boot).
- `tools/result` carries the tool name only; command arguments are not exposed on the bus.
- Waiting-for-input uses the `user-questions/request` waterfall and therefore needs a DSH build with the
  ask-user tool enabled.
- If a DSH build changes event names, the zero-install alternative `tools/agent-emit/dsh-bridge.ps1 -Watch`
  keeps working (it reads the projection cache instead of the bus).
