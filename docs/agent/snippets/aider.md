# Aider → FluidBar

Aider is a terminal pair-programmer. Its documented options (aider.chat/docs/config/options.html,
checked 2026-10-20) expose git-commit options but **no user lifecycle/notify hook**, so the integration
is a CLI wrapper.

## Wrapper

```powershell
pwsh -NoProfile -File tools\agent-emit\wrap.ps1 -Agent aider -Title "Aider" -Command "aider"
```

That yields one running → completed row per aider session. Tool-level detail is unavailable (aider
does not report its edits over a hook); if you need per-file detail, add `/run` scripts that call
`agent-emit.ps1 -Agent aider -Event running -Message "<file>"` from inside the session.
