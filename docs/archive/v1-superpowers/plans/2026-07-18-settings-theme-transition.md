# 设置面板主题渐变实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 为设置面板日夜主题切换增加 350ms 平滑颜色过渡。

**Architecture:** 保留启动时的即时主题初始化；用户切换时，原地动画化现有 `SolidColorBrush` 与渐变画刷的颜色，避免替换资源导致瞬切。快速连续切换时从当前呈现颜色继续过渡。

**Tech Stack:** C#、WPF、`ColorAnimation`、动态资源

---

### Task 1: 实现主题颜色动画

**Files:**
- Modify: `SettingsWindow.xaml.cs:458-605`

- [ ] 将 `ApplyTheme` 改为调用新的 `AnimateThemeVisuals`。
- [ ] 对主题 `SolidColorBrush` 的 `Color` 执行 350ms `CubicEase/EaseOut` 动画。
- [ ] 对 `BgGradient`、`SidebarGradient`、`TitleCardBg` 的 `GradientStop.Color` 执行相同动画。
- [ ] 保留 `UpdateThemeVisuals` 供首次加载即时应用。

### Task 2: 验证

**Files:**
- Test: `FluidBar.csproj`

- [ ] 运行 `dotnet build`。
- [ ] 确认 0 警告、0 错误。
