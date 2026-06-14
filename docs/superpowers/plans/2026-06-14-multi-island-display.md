# Multi Island Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a configurable display strategy that can keep multiple non-clock Dynamic Island states visible side by side.

**Architecture:** Add testable pure logic for display policy and group layout, then wire that model into the existing WPF `MainWindow` and settings panel. The latest island keeps the existing full animation surface; older islands render as compact lightweight snapshot windows positioned from the same group layout.

**Tech Stack:** C# 12, WPF, existing no-framework console tests.

---

### Task 1: Strategy And Layout Model

**Files:**
- Modify: `Settings.cs`
- Modify: `IslandPresentation.cs`
- Test: `FluidBar.Tests/Program.cs`

- [ ] Add `IslandDisplayStrategy` setting with `LatestOnly` default.
- [ ] Add `IslandStackPolicy` and `IslandGroupLayout` pure logic.
- [ ] Test append, clock exclusion, max count, center/left/right anchor layout.

### Task 2: Settings Panel Control

**Files:**
- Modify: `SettingsWindow.xaml`
- Modify: `SettingsWindow.xaml.cs`

- [ ] Add a two-option display strategy selector in the behavior section.
- [ ] Persist strategy changes and call `_onSettingsChanged`.

### Task 3: Main Window Multi-Island UI

**Files:**
- Modify: `MainWindow.xaml`
- Modify: `MainWindow.xaml.cs`

- [ ] Keep the existing island as the latest active surface.
- [ ] Render old island snapshots to the left of the latest island with lightweight companion windows.
- [ ] Position and size the top-level window from `IslandGroupLayout`.
- [ ] Keep hover animation on the latest island render-synchronized.

### Task 4: Verification And Commit

**Files:**
- Test: `FluidBar.Tests/Program.cs`

- [ ] Run `dotnet run --project FluidBar.Tests\FluidBar.Tests.csproj`.
- [ ] Run `dotnet build -c CodexVerify`.
- [ ] Commit and push to `main`.
