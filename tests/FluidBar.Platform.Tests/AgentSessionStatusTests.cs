using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Agent schema v2: session-scoped payloads (sessionId/startedAt/error), running status refinement and
/// same-session merging. v1 payloads keep working through the optional record fields.
/// </summary>
public sealed class AgentSessionStatusTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryParse_SchemaV2_ReadsSessionStartedAtAndError()
    {
        const string json = """
        {"schemaVersion":2,"agent":"dsh","event":"running","title":"DeepSeek Harness",
         "message":"正在使用 pwsh","sessionId":"session-abc","startedAt":"2026-10-20T10:00:00Z",
         "timestamp":"2026-10-20T10:02:30Z"}
        """;

        Assert.True(AgentEventParser.TryParse(json, out var file, out var error), error);
        Assert.Equal("session-abc", file.SessionId);
        Assert.Equal(Start, file.StartedAt);
        Assert.Equal(AgentEventKind.Running, AgentEventParser.MapKind(file.Event));
    }

    [Fact]
    public void ToEntry_UsesOneRowIdPerSession()
    {
        var running = AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "running", "DeepSeek Harness", "step 1", Start, "session-abc", Start),
            "/inbox/a.json", Start);
        var completed = AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "completed", "DeepSeek Harness", "done", Start.AddMinutes(2),
                "session-abc", Start),
            "/inbox/b.json", Start);

        Assert.Equal("session:session-abc", running.Id);
        Assert.Equal(running.Id, completed.Id);
        Assert.Equal(AgentKind.DeepSeek, running.Agent);
        Assert.Equal(AgentEventKind.Running, running.Kind);
        Assert.Equal(AgentEventKind.Completed, completed.Kind);
        Assert.Equal(Start, running.StartedAt);
    }

    [Fact]
    public void ToEntry_FailedFoldsTheErrorIntoTheDetail()
    {
        var entry = AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "failed", null, "turn 3", Start, "session-x", Start,
                Error: "MISSING_CREDENTIAL: no API key"),
            "/inbox/c.json", Start);

        Assert.Equal(AgentEventKind.Failed, entry.Kind);
        Assert.Contains("MISSING_CREDENTIAL", entry.Message, StringComparison.Ordinal);
        Assert.Contains("turn 3", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void History_MergesRunningIntoOneRowAndKeepsTheOriginalStart()
    {
        var history = new AgentEventHistory();
        history.Add(AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "running", "DeepSeek Harness", "step 1", Start, "session-abc", Start),
            "a.json", Start));
        history.Add(AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "running", "DeepSeek Harness", "step 2", Start.AddSeconds(10),
                "session-abc", null),
            "b.json", Start));
        history.Add(AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "completed", "DeepSeek Harness", "done", Start.AddMinutes(2),
                "session-abc", null),
            "c.json", Start));

        var snapshot = history.Snapshot();
        Assert.Single(snapshot.Recent);
        var row = snapshot.Recent[0];
        Assert.Equal(AgentEventKind.Completed, row.Kind);
        Assert.Equal(Start, row.StartedAt);
        Assert.Equal(Start.AddMinutes(2), row.Timestamp);
        Assert.Equal("done", row.Message);
        Assert.Equal(0, history.Evictions);
    }

    [Fact]
    public void History_KeepsDifferentSessionsApart()
    {
        var history = new AgentEventHistory();
        history.Add(AgentEventParser.ToEntry(
            new AgentInboxFile(2, "dsh", "running", "DeepSeek Harness", null, Start, "session-a", Start), "a.json", Start));
        history.Add(AgentEventParser.ToEntry(
            new AgentInboxFile(2, "claude-code", "running", "Claude Code", null, Start, "session-b", Start), "b.json", Start));

        Assert.Equal(2, history.Snapshot().Recent.Length);
    }

    [Theory]
    [InlineData("cursor", AgentKind.Cursor)]
    [InlineData("cursor-agent", AgentKind.Cursor)]
    [InlineData("windsurf", AgentKind.Windsurf)]
    [InlineData("codeium", AgentKind.Windsurf)]
    [InlineData("aider", AgentKind.Aider)]
    [InlineData("copilot", AgentKind.Copilot)]
    [InlineData("github-copilot", AgentKind.Copilot)]
    [InlineData("zcode", AgentKind.Zcode)]
    [InlineData("qoder", AgentKind.Qoder)]
    [InlineData("workbuddy", AgentKind.WorkBuddy)]
    [InlineData("work-buddy", AgentKind.WorkBuddy)]
    public void MapAgent_KnowsTheNewAgents(string agent, AgentKind expected)
    {
        Assert.Equal(expected, AgentEventParser.MapAgent(agent));
    }

    [Theory]
    [InlineData("running", AgentEventKind.Running)]
    [InlineData("started", AgentEventKind.Running)]
    [InlineData("tool_call", AgentEventKind.Running)]
    [InlineData("progress", AgentEventKind.Running)]
    [InlineData("idle", AgentEventKind.Completed)]
    [InlineData("finished", AgentEventKind.Completed)]
    public void MapKind_RefinesRunningAndCompletion(string eventName, AgentEventKind expected)
    {
        Assert.Equal(expected, AgentEventParser.MapKind(eventName));
    }

    [Fact]
    public void LegacyV1PayloadsKeepTheirPerFileRowId()
    {
        var entry = AgentEventParser.ToEntry(
            new AgentInboxFile(1, "claude", "stop", "Task", "ok", Start),
            "/inbox/legacy.json", Start);

        Assert.Null(entry.SessionId);
        Assert.StartsWith("claude:stop:", entry.Id, StringComparison.Ordinal);
        Assert.Equal(Start, entry.StartedAt);
    }
}
