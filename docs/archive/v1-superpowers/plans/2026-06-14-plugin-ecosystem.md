# Plugin Ecosystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a source-based official plugin marketplace area and ship clipboard, media, agent status, and Windows notification plugins.

**Architecture:** Keep plugins compiled into the WPF app through `IIslandPlugin`, add a catalog for discovery/contribution, and upgrade `IslandEvent` with optional structured metadata. Windows-facing plugins collect data; `IslandPresentation` and `MainWindow` own visual projection and animations.

**Tech Stack:** C# 13, .NET 10 WPF, Windows WinRT APIs for media and notifications, JSON catalog/settings files.

---

### Task 1: Event Payload And Presentation Contract

**Files:**
- Modify: `EventSystem.cs`
- Modify: `IslandPresentation.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Write failing tests for backward-compatible `IslandEvent` construction and rich media/agent/notification payload projection.
- [ ] Run `dotnet run --project FluidBar.Tests\FluidBar.Tests.csproj` and confirm the new symbols fail.
- [ ] Add `IslandEventPayload`, `IslandEventKind`, and payload-aware presentation mapping.
- [ ] Run the tests and keep existing presentation behavior green.

### Task 2: Source Plugin Marketplace Files

**Files:**
- Create: `Plugins/catalog.json`
- Create: `Plugins/README.md`
- Create: `Plugins/Template/README.md`
- Create: `PluginCatalog.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Write failing tests that validate required catalog ids: `clipboard`, `media`, `agent-status`, `notifications`.
- [ ] Implement catalog records and loader.
- [ ] Add contributor documentation and template instructions.
- [ ] Run tests.

### Task 3: Move Clipboard Plugin Into Plugins Area

**Files:**
- Move: `ClipboardPlugin.cs` to `Plugins/Clipboard/ClipboardPlugin.cs`
- Move: `ClipboardPluginSettings.cs` to `Plugins/Clipboard/ClipboardPluginSettings.cs`
- Move: `ClipboardItem.cs` to `Plugins/Clipboard/ClipboardItem.cs`
- Modify: `App.xaml.cs`
- Modify: `SettingsWindow.xaml.cs`

- [ ] Move files without changing behavior.
- [ ] Build to verify SDK-style compile includes the new paths.
- [ ] Keep settings panel clipboard controls wired to the moved config type.

### Task 4: Media Plugin

**Files:**
- Create: `Plugins/Media/MediaPlugin.cs`
- Create: `Plugins/Media/MediaModels.cs`
- Create: `Plugins/Media/LyricsProvider.cs`
- Modify: `FluidBar.csproj`
- Modify: `App.xaml.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Write failing tests for media snapshot to island event conversion, including title, artist, source, playing wave, progress, and lyric line.
- [ ] Add Windows SDK target framework version so WinRT media APIs compile.
- [ ] Implement a polling GSMTC plugin with safe fallback when API calls fail.
- [ ] Register the plugin and add settings rows for hover card, lyric display, and polling interval.

### Task 5: Agent Status Plugin

**Files:**
- Create: `Plugins/AgentStatus/AgentStatusPlugin.cs`
- Create: `Plugins/AgentStatus/AgentStatusModels.cs`
- Create: `Plugins/AgentStatus/README.md`
- Modify: `App.xaml.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Write failing tests for hook JSON parsing and invalid-file handling.
- [ ] Implement `%APPDATA%\FluidBar\agent-events` polling.
- [ ] Emit agent completion/error events with rich hover-card lines.
- [ ] Document Claude Code hook JSON examples and Codex-compatible event examples.

### Task 6: Notifications Plugin

**Files:**
- Create: `Plugins/Notifications/NotificationsPlugin.cs`
- Create: `Plugins/Notifications/NotificationModels.cs`
- Modify: `FluidBar.csproj`
- Modify: `App.xaml.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Write failing tests for notification snapshot projection.
- [ ] Implement `UserNotificationListener` permission/status handling behind safe try/catch.
- [ ] Poll recent toast notifications and de-duplicate by id.
- [ ] Add settings detail rows for permission status and request action.

### Task 7: Main Island UI Projection

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`
- Modify: `IslandSnapshotWindow.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Add icon mappings for media, agent, and notifications.
- [ ] Add payload-aware hover-card body/subtitle/badge/progress/wave display.
- [ ] Ensure media/notification/agent views have stable compact and hover-card dimensions.
- [ ] Keep multi-island snapshots showing concise title/content without extra WPF dependencies.

### Task 8: Verification And Commit

**Files:**
- Modify only files changed by previous tasks.

- [ ] Run `dotnet run --project FluidBar.Tests\FluidBar.Tests.csproj`.
- [ ] Run `dotnet build -c CodexVerify`.
- [ ] Run `git status --short --branch`.
- [ ] Commit with message `Add official plugin ecosystem`.
- [ ] Push `main` if verification passes.

