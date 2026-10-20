using System.Collections.Immutable;
using System.Text.Json;
using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Native, read-only agent providers: each parser is pinned against a real fixture shape so the island
/// can rely on local files only (no plugins, no hooks, no configuration).
/// </summary>
public sealed class DshNativeProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "dsh-native",
        Guid.NewGuid().ToString("N"));

    public DshNativeProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private DshSessionProvider CreateProvider()
        => new(new DshSessionOptions { ProjectionDirectory = _root, ActiveWindow = TimeSpan.FromHours(6) });

    private void WriteSession(string sessionId, bool running, int turn, int step, int lastTurn,
        string project = "FluidBar", string? pendingTool = null, string? question = null)
    {
        var openStep = running
            ? new { turn, step, startTime = Now.ToUnixTimeMilliseconds(), firstTokenTime = (long?)null }
            : null;
        var pending = pendingTool is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object> { [pendingTool] = new { callId = "call-1" } };
        var active = question is null ? Array.Empty<object>() : new object[] { new { id = "q1", question } };
        var document = new
        {
            version = 1,
            record = new
            {
                identity = new { cwd = "D:\\build\\" + project },
                rows = new
                {
                    workingDirectory = new { ver = 1, seq = 2, val = "D:\\build\\" + project },
                    sessionStats = new
                    {
                        ver = 1,
                        seq = 3,
                        val = new { turns = lastTurn, lastTurn, openStep, pendingCalls = pending },
                    },
                    turnBoundary = new
                    {
                        ver = 1,
                        seq = 4,
                        val = new { lastTurn, lastStepBoundary = new { kind = running ? "start" : "end", seq = 9 } },
                    },
                    userQuestions = new
                    {
                        ver = 1,
                        seq = 5,
                        val = new { questions = new { active, settled = Array.Empty<object>() } },
                    },
                },
            },
        };
        File.WriteAllText(Path.Combine(_root, sessionId + ".json"), JsonSerializer.Serialize(document));
    }

    [Fact]
    public void Poll_RunningSessionReportsTurnStepToolAndWaiting()
    {
        WriteSession("session-a", true, 3, 7, 3, pendingTool: "pwsh");
        var provider = CreateProvider();
        var events = provider.Poll(Now);

        var running = Assert.Single(events);
        Assert.Equal("running", running.Event);
        Assert.Contains("pwsh", running.Message, StringComparison.Ordinal);
        Assert.Equal("DeepSeek Harness · FluidBar", running.Title);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), running.StartedAt?.ToUnixTimeMilliseconds());

        WriteSession("session-b", true, 1, 2, 1, question: "Continue?");
        events = provider.Poll(Now);
        Assert.Contains(events, item => item.Event == "waiting" && item.Message.Contains("Continue?", StringComparison.Ordinal));
    }

    [Fact]
    public void Poll_ToolPhaseStaysRunning()
    {
        WriteSession("session-tool", false, 1, 1, 1);
        var path = Path.Combine(_root, "session-tool.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"kind\":\"end\"", "\"kind\":\"start\""));
        var events = CreateProvider().Poll(Now);

        var running = Assert.Single(events);
        Assert.Equal("running", running.Event);
        Assert.Equal("工具执行中", running.Message);
    }

    [Fact]
    public void Poll_RunningThenClosedBecomesCompletedWithDurationBase()
    {
        WriteSession("session-d", true, 1, 1, 1);
        var provider = CreateProvider();
        _ = provider.Poll(Now);

        WriteSession("session-d", false, 1, 1, 1);
        var events = provider.Poll(Now.AddSeconds(30));

        var completed = Assert.Single(events);
        Assert.Equal("completed", completed.Event);
        Assert.Equal(Now.ToUnixTimeMilliseconds(), completed.StartedAt?.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Poll_AdoptsExistingIdleSessionsSilently()
    {
        WriteSession("session-e", false, 4, 9, 4);
        Assert.Empty(CreateProvider().Poll(Now));
    }

    [Fact]
    public void Poll_HeadlessSessionThatClosesWhileWatchingBecomesCompleted()
    {
        var provider = CreateProvider();
        _ = provider.Poll(Now); // seeds known sessions (empty directory)

        var path = Path.Combine(_root, "session-headless.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            version = 1,
            record = new
            {
                identity = new { cwd = "C:\\temp\\headless" },
                rows = new
                {
                    workingDirectory = new { ver = 1, seq = 2, val = "C:\\temp\\headless" },
                    turnBoundary = new { ver = 1, seq = 4, val = new { lastTurn = 0 } },
                },
            },
        }));
        Assert.Empty(provider.Poll(Now));

        WriteSession("session-headless", false, 1, 1, 1, project: "headless");
        var completed = Assert.Single(provider.Poll(Now.AddSeconds(5)));
        Assert.Equal("completed", completed.Event);
        Assert.Contains("headless", completed.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Poll_MalformedProjectionIsIgnoredAndTwoSessionsStayApart()
    {
        File.WriteAllText(Path.Combine(_root, "broken.json"), "{ not json");
        WriteSession("session-g", true, 1, 1, 1, project: "alpha");
        WriteSession("session-h", true, 1, 1, 1, project: "beta");
        var events = CreateProvider().Poll(Now);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.Title.Contains("alpha", StringComparison.Ordinal));
        Assert.Contains(events, item => item.Title.Contains("beta", StringComparison.Ordinal));
    }
}

/// <summary>Claude Code transcript provider (JSONL under ~/.claude/projects).</summary>
public sealed class ClaudeTranscriptProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "claude-native",
        Guid.NewGuid().ToString("N"));

    public ClaudeTranscriptProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Append(string sessionId, object line)
        => File.AppendAllText(Path.Combine(_root, sessionId + ".jsonl"),
            JsonSerializer.Serialize(line) + Environment.NewLine);

    [Fact]
    public void Poll_AssistantWithoutStopReasonIsRunningWithToolName()
    {
        var path = Path.Combine(_root, "claude-session.jsonl");
        Append("claude-session", new
        {
            type = "assistant",
            timestamp = Now.AddSeconds(-5).ToString("o"),
            cwd = "D:\\build\\FluidBar",
            message = new
            {
                stop_reason = (string?)null,
                content = new object[] { new { type = "tool_use", name = "Bash" } },
            },
        });
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

        var events = new ClaudeTranscriptProvider(_root).Poll(Now);

        var running = Assert.Single(events);
        Assert.Equal("running", running.Event);
        Assert.Contains("Bash", running.Message, StringComparison.Ordinal);
        Assert.Equal("Claude Code · FluidBar", running.Title);
    }

    [Fact]
    public void Poll_AssistantWithStopReasonCompletesAfterRunning()
    {
        var provider = new ClaudeTranscriptProvider(_root);
        Append("claude-session", new
        {
            type = "assistant",
            timestamp = Now.AddSeconds(-5).ToString("o"),
            cwd = "D:\\build\\FluidBar",
            message = new { stop_reason = (string?)null, content = Array.Empty<object>() },
        });
        _ = provider.Poll(Now);

        Append("claude-session", new
        {
            type = "assistant",
            timestamp = Now.ToString("o"),
            cwd = "D:\\build\\FluidBar",
            message = new { stop_reason = "end_turn", content = Array.Empty<object>() },
        });
        var completed = Assert.Single(provider.Poll(Now.AddSeconds(1)));
        Assert.Equal("completed", completed.Event);
    }

    [Fact]
    public void Poll_ToolUseStopReasonStaysRunning()
    {
        Append("claude-tool", new
        {
            type = "assistant",
            timestamp = Now.AddSeconds(-5).ToString("o"),
            cwd = "D:\\build\\FluidBar",
            message = new
            {
                stop_reason = "tool_use",
                content = new object[] { new { type = "tool_use", name = "Bash" } },
            },
        });

        var running = Assert.Single(new ClaudeTranscriptProvider(_root).Poll(Now));
        Assert.Equal("running", running.Event);
        Assert.Contains("Bash", running.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Poll_MissingDirectoryReportsUnavailable()
    {
        var provider = new ClaudeTranscriptProvider(Path.Combine(_root, "missing"));
        Assert.False(provider.IsAvailable);
        Assert.Empty(provider.Poll(Now));
    }
}

/// <summary>Codex rollout provider (JSONL under ~/.codex/sessions).</summary>
public sealed class CodexSessionProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "codex-native",
        Guid.NewGuid().ToString("N"));

    public CodexSessionProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Append(object line)
        => File.AppendAllText(Path.Combine(_root, "rollout-test.jsonl"),
            JsonSerializer.Serialize(line) + Environment.NewLine);

    [Fact]
    public void Poll_TaskStartedIsRunningAndTaskCompleteIsCompleted()
    {
        Append(new { timestamp = Now.AddSeconds(-10).ToString("o"), type = "session_meta", payload = new { cwd = "D:\\build\\FluidBar" } });
        Append(new
        {
            timestamp = Now.AddSeconds(-8).ToString("o"),
            type = "event_msg",
            payload = new { type = "task_started", started_at = Now.ToUnixTimeSeconds() - 8 },
        });
        var provider = new CodexSessionProvider(_root);
        var running = Assert.Single(provider.Poll(Now));
        Assert.Equal("running", running.Event);
        Assert.Equal("Codex · FluidBar", running.Title);

        Append(new
        {
            timestamp = Now.ToString("o"),
            type = "event_msg",
            payload = new { type = "task_complete", completed_at = Now.ToUnixTimeSeconds() },
        });
        var completed = Assert.Single(provider.Poll(Now.AddSeconds(1)));
        Assert.Equal("completed", completed.Event);
    }
}

/// <summary>GitHub Copilot CLI provider (events.jsonl under ~/.copilot/session-state).</summary>
public sealed class CopilotSessionProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "copilot-native",
        Guid.NewGuid().ToString("N"));

    public CopilotSessionProviderTests() => Directory.CreateDirectory(Path.Combine(_root, "session-1"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string EventsPath => Path.Combine(_root, "session-1", "events.jsonl");

    private void Append(object line)
        => File.AppendAllText(EventsPath, JsonSerializer.Serialize(line) + Environment.NewLine);

    [Fact]
    public void Poll_SessionStartIsRunningAndSessionEndCompletes()
    {
        Append(new
        {
            type = "session.start",
            timestamp = Now.AddSeconds(-5).ToString("o"),
            data = new { sessionId = "session-1", context = new { cwd = "D:\\build\\FluidBar" } },
        });
        var provider = new CopilotSessionProvider(_root);
        var running = Assert.Single(provider.Poll(Now));
        Assert.Equal("running", running.Event);

        Append(new { type = "session.end", timestamp = Now.ToString("o"), data = new { } });
        var completed = Assert.Single(provider.Poll(Now.AddSeconds(1)));
        Assert.Equal("completed", completed.Event);
    }
}

/// <summary>Aider coarse activity provider (chat history file mtime).</summary>
public sealed class AiderSessionProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly string _path = Path.Combine(Path.GetTempPath(), "FluidBarTests", "aider-native",
        Guid.NewGuid().ToString("N") + ".md");

    public AiderSessionProviderTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "# aider chat history");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Poll_RecentWriteIsRunningAndOldWriteCompletes()
    {
        var provider = new AiderSessionProvider(_path, TimeSpan.FromSeconds(90));
        Assert.True(provider.IsAvailable);
        var running = Assert.Single(provider.Poll(Now));
        Assert.Equal("running", running.Event);

        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(-10));
        var completed = Assert.Single(provider.Poll(Now.AddMinutes(1)));
        Assert.Equal("completed", completed.Event);
    }
}

/// <summary>Fake provider + registry: events become the same bounded snapshot the island consumes.</summary>
public sealed class AgentProviderRegistryTests
{
    private sealed class FakeProvider : IAgentStatusProvider
    {
        public string ProviderId { get; init; } = "fake";

        public string DisplayName => "Fake";

        public string DataPath => "fake";

        public bool IsAvailable { get; set; } = true;

        public DateTimeOffset? LastActivityUtc { get; private set; }

        public List<AgentProviderEvent> Pending { get; } = [];

        public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now)
        {
            if (Pending.Count > 0)
            {
                LastActivityUtc = now;
            }

            var events = Pending.ToArray();
            Pending.Clear();
            return events;
        }
    }

    private static async Task<AgentEventSnapshot> WaitForAsync(AgentProviderRegistry registry,
        Func<AgentEventSnapshot, bool>? predicate = null)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (registry.Current is { } snapshot && (predicate is null || predicate(snapshot)))
            {
                return snapshot;
            }

            await Task.Delay(5);
        }

        Assert.Fail("Registry did not publish a snapshot.");
        return null!;
    }

    [Fact]
    public async Task Refresh_MergesProviderEventsIntoRows()
    {
        var provider = new FakeProvider { ProviderId = "claude-code" };
        provider.Pending.Add(new AgentProviderEvent("s1", "running", "Claude Code · X", "正在使用 Bash",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(-5)));
        await using var registry = new AgentProviderRegistry([provider], null, NullPlatformLogger.Instance,
            new PlatformCounters(), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        await registry.RefreshAsync();
        var snapshot = await WaitForAsync(registry);

        var row = Assert.Single(snapshot.Recent);
        Assert.Equal(AgentKind.Claude, row.Agent);
        Assert.Equal(AgentEventKind.Running, row.Kind);
        Assert.Equal("claude-code:s1", row.SessionId);
    }

    [Fact]
    public async Task Refresh_SkipsUnavailableProviders()
    {
        var provider = new FakeProvider { IsAvailable = false };
        provider.Pending.Add(new AgentProviderEvent("s1", "running", "Fake", "x", DateTimeOffset.UnixEpoch));
        await using var registry = new AgentProviderRegistry([provider], null, NullPlatformLogger.Instance,
            new PlatformCounters(), new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        await registry.RefreshAsync();
        await Task.Delay(80);

        Assert.True(registry.Current is null || registry.Current.Recent.IsEmpty);
    }
}

/// <summary>Support catalog: native providers are reported, unreadable agents explain why.</summary>
public sealed class AgentSupportCatalogTests
{
    private sealed class StubProvider : IAgentStatusProvider
    {
        public string ProviderId { get; init; } = "dsh";

        public string DisplayName { get; init; } = "DeepSeek Harness";

        public string DataPath => "C:\\Users\\x\\.dsh";

        public bool IsAvailable { get; init; } = true;

        public DateTimeOffset? LastActivityUtc => null;

        public IReadOnlyList<AgentProviderEvent> Poll(DateTimeOffset now) => [];
    }

    [Fact]
    public void Create_ReportsNativeNotDetectedAndInfeasibleRows()
    {
        var statuses = AgentSupportCatalog.Create(
        [
            new StubProvider(),
            new StubProvider { ProviderId = "claude-code", DisplayName = "Claude Code", IsAvailable = false },
            new StubProvider { ProviderId = "aider", DisplayName = "Aider" },
        ]);

        Assert.Equal(AgentSupportKind.Native, statuses.Single(status => status.Id == "dsh").Kind);
        Assert.Equal(AgentSupportKind.NotDetected, statuses.Single(status => status.Id == "claude-code").Kind);
        Assert.Equal(AgentSupportKind.Partial, statuses.Single(status => status.Id == "aider").Kind);
        Assert.Equal(AgentSupportKind.Infeasible,
            statuses.Single(status => status.Id == "workbuddy").Kind);
        Assert.All(statuses.Where(status => status.Kind is AgentSupportKind.Infeasible
            or AgentSupportKind.NotDetected), status => Assert.NotNull(status.NoteCode));
    }
}