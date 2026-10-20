using System.Collections.Immutable;
using FluidBar.App.Island;
using FluidBar.App.Localization;
using FluidBar.App.State;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Agent page mapping: localized status labels, elapsed duration ("已运行 X 分 Y 秒"), failure details and
/// the display name per agent kind. The inbox parsing/merging itself is covered by Platform tests.
/// </summary>
public sealed class AgentContentMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 5, 0, TimeSpan.Zero);

    private static AgentEventSnapshot Snapshot(params AgentEventEntry[] entries)
        => new(entries.ToImmutableArray());

    private static AgentEventEntry Entry(
        AgentKind agent = AgentKind.DeepSeek,
        AgentEventKind kind = AgentEventKind.Running,
        string title = "DeepSeek Harness",
        string message = "",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? timestamp = null,
        string? sessionId = "session-x",
        string? sessionTitle = null,
        string? action = null,
        string? project = null)
        => new($"session:{sessionId}", agent, kind, title, message,
            timestamp ?? Now, "inbox.json", sessionId, startedAt, sessionTitle, action, project);

    [Fact]
    public async Task Map_Running_UsesTheLocalizedStatusAndElapsedDuration()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(startedAt: Now.AddMinutes(-3).AddSeconds(-12))), Now, localizer);
        });

        Assert.Single(rows);
        Assert.Equal("运行中", rows[0].StatusText);
        // prototype2 durations are numeric ("02:14"/"0:45"), not localized sentences.
        Assert.Equal("03:12", rows[0].TimeText);
        Assert.Equal(StatusTone.Info, rows[0].Tone);
        Assert.Equal(AgentKind.DeepSeek, rows[0].Kind);
        Assert.Equal("session-x", rows[0].SessionId);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_SubMinuteDurationUsesTheMssForm()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(startedAt: Now.AddSeconds(-45))), Now, localizer);
        });

        // Under a minute prototype2 still uses a clock form ("0:45"), never a bare "45s".
        Assert.Equal("0:45", rows[0].TimeText);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_Completed_UsesTheFinishedDuration()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(kind: AgentEventKind.Completed,
                    startedAt: Now.AddMinutes(-2).AddSeconds(-5), timestamp: Now)), Now, localizer);
        });

        Assert.Equal("已完成", rows[0].StatusText);
        Assert.Equal("02:05", rows[0].TimeText);
        Assert.Equal(StatusTone.Success, rows[0].Tone);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_Failed_WaitingAndUnknownStatesMapToTheRightTone()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(
                    Entry(kind: AgentEventKind.Failed, message: "MISSING_CREDENTIAL: no API key", startedAt: Now.AddMinutes(-1)),
                    Entry(kind: AgentEventKind.WaitingForInput, sessionId: "session-y"),
                    Entry(kind: AgentEventKind.Other, sessionId: "session-z")),
                Now, localizer);
        });

        Assert.Equal(3, rows.Length);
        Assert.Equal("失败", rows[0].StatusText);
        Assert.Equal(StatusTone.Danger, rows[0].Tone);
        Assert.Contains("MISSING_CREDENTIAL", rows[0].Detail, StringComparison.Ordinal);
        Assert.Equal("等待中", rows[1].StatusText);
        Assert.Equal(StatusTone.Warning, rows[1].Tone);
        Assert.Equal("运行中", rows[2].StatusText);
        Assert.Equal(StatusTone.Info, rows[2].Tone);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_WithoutAStartTime_FallsBackToTheClockTime()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(Snapshot(Entry(startedAt: null)), Now, localizer);
        });

        Assert.Equal(Now.ToLocalTime().ToString("HH:mm"), rows[0].TimeText);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_CompactText_PrefersTheActionOverTheSessionAndTheStatus()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(title: "DeepSeek Harness · FluidBar",
                    sessionTitle: "接手灵动岛项目重构与预览",
                    action: "读取 src/App.tsx", project: "FluidBar")),
                Now, localizer);
        });

        Assert.Equal("读取 src/App.tsx", rows[0].PrimaryText);
        Assert.Equal("DeepSeek Harness", rows[0].Name); // brand stays out of the capsule, on the row tool line
        Assert.Equal("FluidBar", rows[0].Project);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_CompactText_FallsBackToTheSessionNameWithoutATool()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(title: "Claude Code · fluidbar",
                    sessionTitle: "Rebuild the settings motion layer", project: "fluidbar")),
                Now, localizer);
        });

        Assert.Equal("Rebuild the settings motion layer", rows[0].PrimaryText);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Fact]
    public async Task Map_CompactText_FallsBackToTheStatusInsteadOfTheBrand()
    {
        var rows = await AvaloniaTest.Run(() =>
        {
            var localizer = TestFixtures.CreateLocalizer("zh-Hans");
            return AgentContentMapper.Map(
                Snapshot(Entry(title: "DeepSeek Harness · FluidBar", kind: AgentEventKind.Completed,
                    timestamp: Now)), Now, localizer);
        });

        Assert.Equal("已完成", rows[0].PrimaryText);
        Assert.DoesNotContain("DeepSeek", rows[0].PrimaryText, StringComparison.Ordinal);
        await AvaloniaTest.Run(() => TestFixtures.Localizer.SetCulture("en-US"));
    }

    [Theory]
    [InlineData(AgentKind.Claude, "Claude Code")]
    [InlineData(AgentKind.Codex, "Codex")]
    [InlineData(AgentKind.DeepSeek, "DeepSeek Harness")]
    [InlineData(AgentKind.Cursor, "Cursor")]
    [InlineData(AgentKind.Windsurf, "Windsurf")]
    [InlineData(AgentKind.Aider, "Aider")]
    [InlineData(AgentKind.Copilot, "GitHub Copilot")]
    [InlineData(AgentKind.Zcode, "zcode")]
    [InlineData(AgentKind.Qoder, "Qoder")]
    [InlineData(AgentKind.WorkBuddy, "WorkBuddy")]
    [InlineData(AgentKind.Generic, "Agent")]
    public void AgentDisplayName_CoversEveryKind(AgentKind kind, string expected)
    {
        Assert.Equal(expected, AgentContentMapper.AgentDisplayName(kind));
    }

    [Fact]
    public void Map_UsesTheDisplayNameWhenTheTitleIsEmpty()
    {
        var rows = AgentContentMapper.Map(Snapshot(Entry(agent: AgentKind.Cursor, title: "")), Now);

        Assert.Equal("Cursor", rows[0].Name);
    }

    [Fact]
    public void Map_EmptySnapshotAndRowLimit()
    {
        Assert.Empty(AgentContentMapper.Map(null, Now));
        var entries = Enumerable.Range(1, 12)
            .Select(i => Entry(sessionId: "session-" + i))
            .ToArray();
        Assert.Equal(AgentContentMapper.MaxRows, AgentContentMapper.Map(Snapshot(entries), Now).Length);
    }
}
