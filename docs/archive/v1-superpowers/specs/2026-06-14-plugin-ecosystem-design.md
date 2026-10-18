# FluidBar Plugin Ecosystem Design

## Goal

Build a source-based official plugin area for FluidBar and ship the first official plugin set: clipboard, media, agent task status, and Windows notifications. Contributors should be able to fork the repository, add a plugin under `Plugins/`, submit a PR, and have the plugin reviewed before it ships in the main app.

## Scope

This design intentionally does not load arbitrary third-party DLLs at runtime. Plugins are source code in this repository and are compiled with the application after review. This keeps the open-source contribution path simple while avoiding untrusted runtime code execution.

## Plugin Layout

`Plugins/` becomes the official plugin workspace:

- `Plugins/catalog.json` lists plugin metadata used by the settings panel and documentation.
- `Plugins/README.md` explains how to fork, create a plugin, test it, and submit a PR.
- `Plugins/Template/` contains a contributor-facing sample manifest and implementation notes.
- `Plugins/Clipboard/` contains the existing clipboard plugin code.
- `Plugins/Media/`, `Plugins/AgentStatus/`, and `Plugins/Notifications/` contain the new official plugins.

The runtime still uses `IIslandPlugin`; the manager registers built-in source plugins. The catalog is for UI/documentation/discovery, not for loading unreviewed code.

## Event Model

`IslandEvent` gains optional structured metadata through a new `IslandEventPayload` record. Existing call sites that only pass source/title/content/icon continue to work.

Payload fields cover the new richer islands:

- `Kind`: text, status, progress, media, notification, agent, or scrolling text.
- `Subtitle`, `Badge`, `SourceName`: secondary display text.
- `ProgressPercent`, `IsActive`, `ShowsAudioWave`.
- `AlbumArtPath`, `AppIconPath`, `LyricLine`, `SecondaryLyricLine`.
- `DetailLines`: richer hover-card lines.

`IslandPresentation` maps payload metadata into compact and hover-card presentations. This keeps plugin code focused on data collection and lets the main island UI own layout.

## Official Plugins

### Clipboard

Move the existing clipboard plugin into `Plugins/Clipboard`. Keep its settings and current behavior. It continues to use the left-start marquee and hover-card multiline display.

### Media

The media plugin listens through Windows `GlobalSystemMediaTransportControlsSessionManager`. It displays:

- media source name and source icon when available,
- title, artist, album,
- play/pause state,
- progress percent when timeline data is available,
- animated audio wave bars while playing,
- lyric line if a lyric provider can resolve one.

Lyrics are best-effort. GSMTC does not guarantee lyric data, so lyrics are implemented through provider interfaces. The first version supports local `.lrc` matching and plugin-provided text snapshots. If a platform such as Kugou does not expose lyrics through a supported channel, the island falls back to media metadata without breaking.

### Agent Status

The agent plugin provides a local hook bridge for Claude Code and Codex. It accepts short JSON events from local hooks and shows a completion/error island. Claude Code is expected to use its documented hooks, especially `Stop` and notification-style hooks. Codex support uses the same local JSON contract so a future Codex hook or wrapper can post completion events.

The first implementation uses a local directory inbox under `%APPDATA%\FluidBar\agent-events`. Hooks can write a JSON file there without needing network permissions. The plugin polls this folder, consumes valid events, and ignores malformed files after moving them to a failed folder.

### Notifications

The notification plugin uses Windows `UserNotificationListener` where available. It requests user permission from the settings panel, then reads toast notifications and emits notification islands with app name, title, body, and app icon when available. If permission is denied or unsupported, the settings detail panel shows the current status and a request-permission action.

## UI Behavior

All new plugins must work in compact and hover-card modes:

- Media compact: source icon, title/artist, wave bars, progress. Hover-card: larger media layout with source badge, title, artist, progress bar, lyric line, and play state.
- Agent compact: tool name and task status. Hover-card: project/session, status, short summary, duration when present.
- Notification compact: app/source and message title. Hover-card: app, title, body, timestamp, and grouped detail lines.

The current dynamic island animation system remains the single animation owner. Plugins do not animate WPF controls directly.

## Tests

Add presentation tests for:

- catalog metadata validation,
- event payload backward compatibility,
- media payload projection with audio wave and lyric line,
- agent hook JSON parsing,
- notification payload projection,
- hover-card sizing for media, agent, and notification views.

Build verification remains:

- `dotnet run --project FluidBar.Tests\FluidBar.Tests.csproj`
- `dotnet build -c CodexVerify`

