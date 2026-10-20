# Codex → FluidBar

Codex supports a **`notify` command** (top-level `notify = [exe, "turn-ended"]` in
`%USERPROFILE%\.codex\config.toml`) and, in recent builds, managed lifecycle hooks
(`allow_managed_hooks_only` / hook config layers in `requirements.toml`). Notify is the simplest
per-user integration.

Detected config on this machine: `%USERPROFILE%\.codex\config.toml` (already has a
`notify` entry for Codex computer-use — keep it and chain, or replace it).

## Notify adapter

`tools/agent-emit/hook-adapter.ps1` reads the notify payload from stdin and forwards the event.

```toml
# ~/.codex/config.toml
notify = [ "pwsh", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
           "D:\\build\\GitLocal\\FluidBar\\tools\\agent-emit\\hook-adapter.ps1",
           "-Agent", "codex", "-Event", "completed", "-Title", "Codex" ]
```

That gives one completed row per Codex turn. For a **running** row while Codex works there is no
per-step notify event; run Codex inside the generic wrapper below (it owns both states).

## Generic wrapper (both states)

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent codex -Title "Codex" -Command "codex"
```

`wrap.ps1` emits `running` on start, `completed` on exit 0 and `failed` (with the last stderr line)
otherwise. Use it when you want live state without editing Codex config. Chaining the existing
computer-use notify entry: call both binaries from a small .cmd shim and point `notify` at the shim.
