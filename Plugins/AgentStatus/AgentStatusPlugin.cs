using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace FluidBar;

public sealed class AgentStatusPlugin : IIslandPlugin
{
    private static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluidBar",
        "agent-events");

    private static readonly string[] RequiredHookEvents =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop"];

    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _guardTimer;

    public string Id => "agent-status";
    public string Name => "Agent 状态";
    public string Description => "监听 Claude Code / Codex 本地 hook 事件，任务完成或失败时显示提醒";
    public string Icon => "\uE8F2";
    public bool Enabled { get; set; } = true;
    public IPluginConfig? Config => null;
    public event Action<IslandEvent>? EventTriggered;

    // Hooks guardian settings
    public bool HooksGuardEnabled { get; set; } = true;
    public int HooksGuardIntervalMs { get; set; } = 30_000;

    public AgentStatusPlugin()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => PollInbox();

        _guardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30_000) };
        _guardTimer.Tick += (_, _) => RunHooksGuard();
    }

    public void Initialize()
    {
        Directory.CreateDirectory(InboxDir);
        Directory.CreateDirectory(ProcessedDir);
        Directory.CreateDirectory(FailedDir);
    }

    public void Start()
    {
        Initialize();
        if (!_timer.IsEnabled)
            _timer.Start();
        StartGuard();
    }

    public void Stop()
    {
        _timer.Stop();
        _guardTimer.Stop();
    }

    public void Dispose() => Stop();

    public void StartGuard()
    {
        _guardTimer.Stop();
        if (!HooksGuardEnabled) return;
        var interval = Math.Clamp(HooksGuardIntervalMs, 5_000, 60_000);
        _guardTimer.Interval = TimeSpan.FromMilliseconds(interval);
        _guardTimer.Start();
    }

    public void StopGuard() => _guardTimer.Stop();

    // --- Inbox polling ---

    private static string InboxDir => Path.Combine(BaseDir, "inbox");
    private static string ProcessedDir => Path.Combine(BaseDir, "processed");
    private static string FailedDir => Path.Combine(BaseDir, "failed");

    private void PollInbox()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(InboxDir, "*.json").OrderBy(File.GetCreationTimeUtc))
                ConsumeFile(path);
        }
        catch
        {
        }
    }

    private void ConsumeFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var hook = AgentHookEvent.Parse(json);
            EventTriggered?.Invoke(AgentStatusIslandEventFactory.FromHook(hook));
            MoveTo(path, ProcessedDir);
        }
        catch
        {
            MoveTo(path, FailedDir);
        }
    }

    private static void MoveTo(string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json");
        try
        {
            File.Move(path, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(path); }
            catch { }
        }
    }

    // --- Hooks guardian ---

    private static string HookScriptPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Plugins", "AgentStatus", "hooks", "claude-code-hook.ps1");

    private static string ClaudeSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "settings.json");

    private static string HookCommand => $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{HookScriptPath}\"";

    private void RunHooksGuard()
    {
        try
        {
            var status = CheckHooks();
            if (!status.Installed)
            {
                RepairHooks(status);
            }
        }
        catch
        {
            // Silent — guardian should never crash the app
        }
    }

    /// <summary>
    /// Checks if all required hook events are registered in Claude Code's settings.json.
    /// </summary>
    public HooksCheckStatus CheckHooks()
    {
        if (!File.Exists(ClaudeSettingsPath))
            return new HooksCheckStatus(false, false, 0, RequiredHookEvents.Length, RequiredHookEvents);

        try
        {
            var json = File.ReadAllText(ClaudeSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("hooks", out var hooks) || hooks.ValueKind != JsonValueKind.Object)
                return new HooksCheckStatus(false, true, 0, RequiredHookEvents.Length, RequiredHookEvents);

            var command = HookCommand;
            var missing = new List<string>();

            foreach (var evt in RequiredHookEvents)
            {
                if (!hooks.TryGetProperty(evt, out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    missing.Add(evt);
                    continue;
                }

                var found = false;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("hooks", out var hookArr) && hookArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var h in hookArr.EnumerateArray())
                        {
                            if (h.TryGetProperty("command", out var cmd) &&
                                string.Equals(cmd.GetString(), command, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (found) break;
                }

                if (!found)
                    missing.Add(evt);
            }

            return new HooksCheckStatus(
                missing.Count == 0,
                true,
                RequiredHookEvents.Length - missing.Count,
                RequiredHookEvents.Length,
                missing.ToArray());
        }
        catch
        {
            return new HooksCheckStatus(false, true, 0, RequiredHookEvents.Length, RequiredHookEvents);
        }
    }

    /// <summary>
    /// Installs or repairs missing hook entries in Claude Code's settings.json.
    /// </summary>
    public bool RepairHooks(HooksCheckStatus? currentStatus = null)
    {
        currentStatus ??= CheckHooks();
        if (currentStatus.Installed) return true;

        try
        {
            // Load or create settings
            Dictionary<string, object> settings;
            if (File.Exists(ClaudeSettingsPath))
            {
                var json = File.ReadAllText(ClaudeSettingsPath);
                settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ClaudeSettingsPath)!);
                settings = [];
            }

            // Get or create hooks object
            Dictionary<string, object> hooks;
            if (settings.TryGetValue("hooks", out var hooksObj) &&
                hooksObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                hooks = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            else
            {
                hooks = [];
            }

            var command = HookCommand;

            // Upsert each missing event
            foreach (var evt in currentStatus.MissingEvents)
            {
                var entry = new Dictionary<string, object>
                {
                    ["matcher"] = "*",
                    ["hooks"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["type"] = "command",
                            ["command"] = command
                        }
                    }
                };

                hooks[evt] = new[] { entry };
            }

            settings["hooks"] = hooks;

            // Write back
            var options = new JsonSerializerOptions { WriteIndented = true };
            var output = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(ClaudeSettingsPath, output);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes all FluidBar hook entries from Claude Code's settings.json.
    /// </summary>
    public bool RemoveHooks()
    {
        try
        {
            if (!File.Exists(ClaudeSettingsPath)) return true;

            var json = File.ReadAllText(ClaudeSettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("hooks", out var hooksProp) || hooksProp.ValueKind != JsonValueKind.Object)
                return true;

            var hooks = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(hooksProp.GetRawText()) ?? [];
            var command = HookCommand;
            var changed = false;

            foreach (var evt in RequiredHookEvents)
            {
                if (!hooks.TryGetValue(evt, out var entries) || entries.ValueKind != JsonValueKind.Array)
                    continue;

                var remaining = new List<object>();
                foreach (var entry in entries.EnumerateArray())
                {
                    var hasFluidBar = false;
                    if (entry.TryGetProperty("hooks", out var hookArr) && hookArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var h in hookArr.EnumerateArray())
                        {
                            if (h.TryGetProperty("command", out var cmd) &&
                                string.Equals(cmd.GetString(), command, StringComparison.OrdinalIgnoreCase))
                            {
                                hasFluidBar = true;
                                break;
                            }
                        }
                    }
                    if (!hasFluidBar)
                        remaining.Add(JsonSerializer.Deserialize<object>(entry.GetRawText())!);
                }

                if (remaining.Count == 0)
                {
                    hooks.Remove(evt);
                    changed = true;
                }
                else if (remaining.Count < entries.GetArrayLength())
                {
                    hooks[evt] = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(remaining));
                    changed = true;
                }
            }

            if (!changed) return true;

            // Rebuild settings with updated hooks
            var settingsJson = File.ReadAllText(ClaudeSettingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            settings["hooks"] = hooks;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ClaudeSettingsPath, JsonSerializer.Serialize(settings, options));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record HooksCheckStatus(
    bool Installed,
    bool ConfigExists,
    int HookCount,
    int RequiredCount,
    IReadOnlyList<string> MissingEvents);

