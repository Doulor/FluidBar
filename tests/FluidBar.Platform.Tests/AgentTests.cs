using System.Text.Json;
using FluidBar.Platform.Windows.Agent;

namespace FluidBar.Platform.Tests;

public sealed class AgentEventParserTests
{
    [Fact]
    public void TryParse_ValidClaudeCompleted()
    {
        const string json = """
        {"schemaVersion":1,"agent":"claude","event":"stop","title":"Task complete","message":"All tests pass",
         "timestamp":"2026-10-18T10:00:00Z"}
        """;
        Assert.True(AgentEventParser.TryParse(json, out var file, out var error), error);
        Assert.Equal("claude", file.Agent);
        Assert.Equal("stop", file.Event);
        Assert.Equal("Task complete", file.Title);
        Assert.NotNull(file.Timestamp);

        var entry = AgentEventParser.ToEntry(file, "/inbox/a.json", DateTimeOffset.UnixEpoch);
        Assert.Equal(AgentKind.Claude, entry.Agent);
        Assert.Equal(AgentEventKind.Completed, entry.Kind);
        Assert.StartsWith("claude:stop:", entry.Id, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"agent":"claude"}""")]
    [InlineData("""{"event":"stop"}""")]
    [InlineData("[]")]
    public void TryParse_InvalidPayloadsFail(string json)
    {
        Assert.False(AgentEventParser.TryParse(json, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("claude", AgentKind.Claude)]
    [InlineData("claude-code", AgentKind.Claude)]
    [InlineData("codex", AgentKind.Codex)]
    [InlineData("openai-codex", AgentKind.Codex)]
    [InlineData("other", AgentKind.Generic)]
    [InlineData(null, AgentKind.Generic)]
    public void MapAgent_MapsKnownIds(string? agent, AgentKind expected)
    {
        Assert.Equal(expected, AgentEventParser.MapAgent(agent));
    }

    [Theory]
    [InlineData("stop", AgentEventKind.Completed)]
    [InlineData("completed", AgentEventKind.Completed)]
    [InlineData("error", AgentEventKind.Failed)]
    [InlineData("waiting_for_input", AgentEventKind.WaitingForInput)]
    [InlineData("something", AgentEventKind.Other)]
    [InlineData(null, AgentEventKind.Other)]
    public void MapKind_MapsKnownEvents(string? eventName, AgentEventKind expected)
    {
        Assert.Equal(expected, AgentEventParser.MapKind(eventName));
    }

    [Fact]
    public void ToEntry_TruncatesTextAndFallsBackTitle()
    {
        var file = new AgentInboxFile(1, "codex", "failed", null, new string('x', 5000), null);
        var entry = AgentEventParser.ToEntry(file, "a.json", DateTimeOffset.UnixEpoch, maxTextCharacters: 10);
        Assert.Equal("failed", entry.Title);
        Assert.True(entry.Message.Length <= 21); // maxTextCharacters * 2 plus the ellipsis
        Assert.Equal(DateTimeOffset.UnixEpoch, entry.Timestamp);
    }
}

public sealed class AgentEventHistoryTests
{
    private static AgentEventEntry Entry(int index)
        => new($"id-{index}", AgentKind.Claude, AgentEventKind.Completed, "t", "m",
            DateTimeOffset.UnixEpoch.AddSeconds(index), $"f{index}.json");

    [Fact]
    public void Add_NewestFirstAndBounded()
    {
        var history = new AgentEventHistory(maxEntries: 3);
        for (var index = 1; index <= 5; index++)
        {
            history.Add(Entry(index));
        }

        var snapshot = history.Snapshot();
        Assert.Equal(3, snapshot.Recent.Length);
        Assert.Equal("id-5", snapshot.Latest!.Id);
        Assert.Equal(2, history.Evictions);
    }

    [Fact]
    public void Add_DuplicateIdIsIgnored()
    {
        var history = new AgentEventHistory();
        history.Add(Entry(1));
        history.Add(Entry(1));
        Assert.Equal(1, history.Count);
    }
}

public sealed class AgentHookConfigMergerTests
{
    private const string Command = @"C:\Apps\FluidBar\notify-fluidbar.ps1";

    [Fact]
    public void Merge_CreatesHooksInEmptySettings()
    {
        var result = AgentHookConfigMerger.Merge(null, Command, ["Stop", "Notification"], out var changed);
        Assert.True(changed);
        using var document = JsonDocument.Parse(result);
        var hooks = document.RootElement.GetProperty("hooks");
        Assert.True(hooks.TryGetProperty("Stop", out var stop));
        Assert.Equal(Command, stop[0].GetProperty("hooks")[0].GetProperty("command").GetString());
        Assert.True(hooks.TryGetProperty("Notification", out _));
    }

    [Fact]
    public void Merge_PreservesUnknownKeysAndUnrelatedHooks()
    {
        const string existing = """
        {"theme":"dark","permissions":{"allow":["Bash"]},
         "hooks":{"Stop":[{"hooks":[{"type":"command","command":"other.exe"}]}]}}
        """;
        var result = AgentHookConfigMerger.Merge(existing, Command, ["Stop"], out var changed);
        Assert.True(changed);
        using var document = JsonDocument.Parse(result);
        Assert.Equal("dark", document.RootElement.GetProperty("theme").GetString());
        Assert.True(document.RootElement.GetProperty("permissions").TryGetProperty("allow", out _));
        var stop = document.RootElement.GetProperty("hooks").GetProperty("Stop");
        Assert.Equal(2, stop.GetArrayLength()); // unrelated entry preserved + ours appended
    }

    [Fact]
    public void Merge_IsIdempotent()
    {
        var first = AgentHookConfigMerger.Merge(null, Command, ["Stop", "Notification"], out var changedFirst);
        var second = AgentHookConfigMerger.Merge(first, Command, ["Stop", "Notification"], out var changedSecond);
        Assert.True(changedFirst);
        Assert.False(changedSecond);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Remove_RemovesOnlyFluidBarEntries()
    {
        const string existing = """
        {"hooks":{"Stop":[{"hooks":[{"type":"command","command":"other.exe"}]},
                          {"hooks":[{"type":"command","command":"C:\\Apps\\FluidBar\\notify-fluidbar.ps1"}]}]}}
        """;
        var result = AgentHookConfigMerger.Remove(existing, Command, out var changed);
        Assert.True(changed);
        using var document = JsonDocument.Parse(result);
        var stop = document.RootElement.GetProperty("hooks").GetProperty("Stop");
        Assert.Equal(1, stop.GetArrayLength());
        Assert.Equal("other.exe", stop[0].GetProperty("hooks")[0].GetProperty("command").GetString());
    }

    [Fact]
    public void Remove_DropsEmptyHooksObject()
    {
        var merged = AgentHookConfigMerger.Merge(null, Command, ["Stop"], out _);
        var removed = AgentHookConfigMerger.Remove(merged, Command, out var changed);
        Assert.True(changed);
        using var document = JsonDocument.Parse(removed);
        Assert.False(document.RootElement.TryGetProperty("hooks", out _));
    }

    [Fact]
    public void Merge_InvalidJsonThrows()
    {
        Assert.ThrowsAny<JsonException>(() => AgentHookConfigMerger.Merge("{not json", Command, ["Stop"], out _));
    }

    [Fact]
    public void ContainsHook_DetectsInstalledState()
    {
        var merged = AgentHookConfigMerger.Merge(null, Command, ["Stop"], out _);
        Assert.True(AgentHookConfigMerger.ContainsHook(merged, Command));
        Assert.False(AgentHookConfigMerger.ContainsHook(merged, "other.exe"));
        Assert.False(AgentHookConfigMerger.ContainsHook(null, Command));
    }
}

public sealed class AgentHookInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "agent",
        Guid.NewGuid().ToString("N"));
    private const string Command = "powershell -File notify-fluidbar.ps1";

    private HookInstallOptions Options => new()
    {
        SettingsFilePath = Path.Combine(_root, ".claude", "settings.json"),
        BackupDirectory = Path.Combine(_root, "backups"),
        HookCommand = Command,
        HookEvents = ["Stop"],
    };

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

    [Fact]
    public async Task Install_RequiresExplicitConsent()
    {
        var installer = new AgentHookInstaller(Options, new CapturingLogger());
        var result = await installer.InstallAsync(userConfirmed: false);
        Assert.False(result.Success);
        Assert.False(Directory.Exists(Path.Combine(_root, ".claude")));
    }

    [Fact]
    public async Task Install_BacksUpMergesAndIsIdempotent()
    {
        var options = Options;
        Directory.CreateDirectory(Path.GetDirectoryName(options.SettingsFilePath)!);
        await File.WriteAllTextAsync(options.SettingsFilePath, """{"theme":"dark"}""");

        var installer = new AgentHookInstaller(options, new CapturingLogger());
        var first = await installer.InstallAsync(userConfirmed: true);
        Assert.True(first.Success);
        Assert.True(first.Changed);
        Assert.NotNull(first.BackupPath);
        Assert.True(File.Exists(first.BackupPath!));

        var settings = await File.ReadAllTextAsync(options.SettingsFilePath);
        Assert.Contains("dark", settings, StringComparison.Ordinal);
        Assert.True(AgentHookConfigMerger.ContainsHook(settings, Command));
        Assert.True(await installer.IsInstalledAsync());

        var second = await installer.InstallAsync(userConfirmed: true);
        Assert.True(second.Success);
        Assert.False(second.Changed);

        var uninstall = await installer.UninstallAsync(userConfirmed: true);
        Assert.True(uninstall.Success);
        Assert.True(uninstall.Changed);
        Assert.False(await installer.IsInstalledAsync());
        var restored = await File.ReadAllTextAsync(options.SettingsFilePath);
        Assert.Contains("dark", restored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_RefusesToModifyInvalidJson()
    {
        var options = Options;
        Directory.CreateDirectory(Path.GetDirectoryName(options.SettingsFilePath)!);
        const string invalid = "{ this is not json";
        await File.WriteAllTextAsync(options.SettingsFilePath, invalid);

        var installer = new AgentHookInstaller(options, new CapturingLogger());
        var result = await installer.InstallAsync(userConfirmed: true);
        Assert.False(result.Success);
        Assert.Equal(invalid, await File.ReadAllTextAsync(options.SettingsFilePath));
    }

    [Fact]
    public async Task Uninstall_WithoutInstallIsNoOp()
    {
        var installer = new AgentHookInstaller(Options, new CapturingLogger());
        var result = await installer.UninstallAsync(userConfirmed: true);
        Assert.True(result.Success);
        Assert.False(result.Changed);
    }
}

public sealed class AgentInboxWatcherTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FluidBarTests", "inbox",
        Guid.NewGuid().ToString("N"));

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
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

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Start_ReplaysValidFilesAndQuarantinesMalformed()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "valid.json"),
            """{"agent":"claude","event":"stop","title":"Done","message":"ok"}""");
        await File.WriteAllTextAsync(Path.Combine(_root, "invalid.json"), "{broken");

        await using var watcher = new AgentInboxWatcher(new AgentInboxOptions { InboxDirectory = _root },
            new CapturingLogger(), new FluidBar.Platform.Windows.Diagnostics.PlatformCounters());
        await watcher.StartAsync();

        // Wait for the full pipeline outcome (publish + file archival), not just the in-memory event:
        // the move to processed/quarantine happens after the snapshot is published.
        await TestWait.UntilAsync(() =>
            watcher.Current is { Recent.Length: 1 }
            && Directory.GetFiles(_root, "*.json").Length == 0
            && Directory.Exists(Path.Combine(_root, "quarantine"))
            && Directory.GetFiles(Path.Combine(_root, "quarantine"), "*.json").Length == 1);

        var latest = watcher.Current!.Latest!;
        Assert.Equal(AgentKind.Claude, latest.Agent);
        Assert.Equal(AgentEventKind.Completed, latest.Kind);
        Assert.Equal("Done", latest.Title);

        var quarantine = Path.Combine(_root, "quarantine");
        Assert.True(Directory.Exists(quarantine));
        Assert.Single(Directory.GetFiles(quarantine, "*.json"));
        Assert.Empty(Directory.GetFiles(_root, "*.json"));
        await watcher.StopAsync();
    }
}
