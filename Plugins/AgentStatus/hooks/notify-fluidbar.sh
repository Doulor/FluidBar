#!/usr/bin/env bash
# FluidBar Agent Status hook script (Linux/macOS/WSL)
# Invoke from Claude Code Stop hook or Codex wrapper.
# Writes a completion JSON to the FluidBar agent-events inbox.

TOOL="${1:-claude-code}"
STATUS="${2:-completed}"
PROJECT="${3:-}"
SUMMARY="${4:-}"
BRANCH="${5:-}"
DURATION_MS="${6:-0}"
SESSION_ID="${7:-}"
ERROR_MSG="${8:-}"

# Resolve inbox path
if [[ "$OSTYPE" == "msys" || "$OSTYPE" == "cygwin" || "$OSTYPE" == "win32" ]]; then
    INBOX="${APPDATA}/FluidBar/agent-events/inbox"
else
    INBOX="${HOME}/.config/FluidBar/agent-events/inbox"
fi

mkdir -p "$INBOX"

TIMESTAMP=$(date +%Y%m%d-%H%M%S)
RANDOM_ID=$((RANDOM % 9000 + 1000))
FILENAME="hook-${TIMESTAMP}-${RANDOM_ID}.json"
FILEPATH="${INBOX}/${FILENAME}"

# Build JSON manually to avoid jq dependency
cat > "$FILEPATH" << JSONEOF
{
  "tool": "${TOOL}",
  "status": "${STATUS}",
  "project": "${PROJECT}",
  "summary": "${SUMMARY}",
  "branch": "${BRANCH}",
  "durationMs": ${DURATION_MS},
  "sessionId": "${SESSION_ID}",
  "error": "${ERROR_MSG}"
}
JSONEOF
