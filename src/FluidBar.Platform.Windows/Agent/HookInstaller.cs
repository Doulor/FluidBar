using System.Text.Json;
using System.Text.Json.Nodes;
using FluidBar.Platform.Windows.Diagnostics;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>Options for the explicit agent-hook installer.</summary>
public sealed class HookInstallOptions
{
    /// <summary>Path of the agent's settings JSON (for example <c>%USERPROFILE%\.claude\settings.json</c>).</summary>
    public required string SettingsFilePath { get; init; }

    /// <summary>Hook command that writes events into the FluidBar inbox (absolute path preferred).</summary>
    public required string HookCommand { get; init; }

    /// <summary>Settings keys under <c>hooks</c> to register (defaults: Stop + Notification).</summary>
    public IReadOnlyList<string> HookEvents { get; init; } = ["Stop", "Notification"];

    /// <summary>Directory that receives timestamped backups before every write.</summary>
    public required string BackupDirectory { get; init; }
}

/// <summary>Result of an explicit install/uninstall operation.</summary>
public sealed record HookInstallResult(bool Success, bool Changed, string? BackupPath, string? FailureReason)
{
    public static HookInstallResult Failed(string reason) => new(false, false, null, reason);
}

/// <summary>
/// Pure merge/remove logic for the agent settings JSON. Unknown keys and unrelated hooks are preserved;
/// operations are idempotent; malformed JSON throws so the caller can abort without touching the file
/// (rewrite guardrail: never clobber a user's config).
/// </summary>
public static class AgentHookConfigMerger
{
    /// <summary>Adds the hook command under each event. Returns the new JSON text.</summary>
    public static string Merge(string? existingJson, string hookCommand, IReadOnlyList<string> hookEvents,
        out bool changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookCommand);
        ArgumentNullException.ThrowIfNull(hookEvents);
        changed = false;

        var root = ParseRoot(existingJson);
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        foreach (var eventName in hookEvents)
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                continue;
            }

            if (hooks[eventName] is not JsonArray entries)
            {
                entries = new JsonArray();
                hooks[eventName] = entries;
            }

            var alreadyPresent = entries
                .OfType<JsonObject>()
                .Any(entry => ContainsCommand(entry, hookCommand));
            if (alreadyPresent)
            {
                continue;
            }

            entries.Add(new JsonObject
            {
                ["matcher"] = string.Empty,
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = hookCommand,
                }),
            });
            changed = true;
        }

        return changed ? Serialize(root) : (existingJson ?? Serialize(root));
    }

    /// <summary>
    /// Adds several (event → command) hook pairs in one pass. Used by the agent integration page, where
    /// one agent needs a different adapter command per lifecycle event. Idempotent per command.
    /// </summary>
    public static string MergeCommands(string? existingJson,
        IReadOnlyList<(string Event, string Command)> hooks, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        changed = false;
        var root = ParseRoot(existingJson);
        if (root["hooks"] is not JsonObject hookObject)
        {
            hookObject = new JsonObject();
            root["hooks"] = hookObject;
        }

        foreach (var (eventName, command) in hooks)
        {
            if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            if (hookObject[eventName] is not JsonArray entries)
            {
                entries = new JsonArray();
                hookObject[eventName] = entries;
            }

            if (entries.OfType<JsonObject>().Any(entry => ContainsCommand(entry, command)))
            {
                continue;
            }

            entries.Add(new JsonObject
            {
                ["matcher"] = string.Empty,
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = command,
                }),
            });
            changed = true;
        }

        return changed ? Serialize(root) : (existingJson ?? Serialize(root));
    }

    /// <summary>Removes every hook entry that references one of the given commands.</summary>
    public static string RemoveCommands(string? existingJson, IReadOnlyList<string> commands, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(commands);
        changed = false;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return existingJson ?? "{}";
        }

        var root = ParseRoot(existingJson);
        if (root["hooks"] is not JsonObject hooks)
        {
            return existingJson;
        }

        foreach (var eventName in hooks.Select(pair => pair.Key).ToArray())
        {
            if (hooks[eventName] is not JsonArray entries)
            {
                continue;
            }

            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index] is JsonObject entry && commands.Any(command => ContainsCommand(entry, command)))
                {
                    entries.RemoveAt(index);
                    changed = true;
                }
            }

            if (entries.Count == 0)
            {
                hooks.Remove(eventName);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        return changed ? Serialize(root) : existingJson;
    }

    /// <summary>True when the settings already contain any of the given hook commands.</summary>
    public static bool ContainsAnyCommand(string? existingJson, IReadOnlyList<string> commands)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return false;
        }

        try
        {
            var root = ParseRoot(existingJson);
            return root["hooks"] is JsonObject hooks
                   && hooks.Any(pair => pair.Value is JsonArray entries
                                        && entries.OfType<JsonObject>()
                                            .Any(entry => commands.Any(command => ContainsCommand(entry, command))));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Removes only FluidBar-owned hook entries. Returns the new JSON text.</summary>
    public static string Remove(string? existingJson, string hookCommand, out bool changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookCommand);
        changed = false;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return existingJson ?? "{}";
        }

        var root = ParseRoot(existingJson);
        if (root["hooks"] is not JsonObject hooks)
        {
            return existingJson;
        }

        foreach (var eventName in hooks.Select(pair => pair.Key).ToArray())
        {
            if (hooks[eventName] is not JsonArray entries)
            {
                continue;
            }

            for (var index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index] is JsonObject entry && ContainsCommand(entry, hookCommand))
                {
                    entries.RemoveAt(index);
                    changed = true;
                }
            }

            if (entries.Count == 0)
            {
                hooks.Remove(eventName);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        return changed ? Serialize(root) : existingJson;
    }

    /// <summary>True when the settings already contain the given hook command.</summary>
    public static bool ContainsHook(string? existingJson, string hookCommand)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            return false;
        }

        try
        {
            var root = ParseRoot(existingJson);
            return root["hooks"] is JsonObject hooks
                   && hooks.Any(pair => pair.Value is JsonArray entries
                                        && entries.OfType<JsonObject>().Any(entry => ContainsCommand(entry, hookCommand)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsCommand(JsonObject entry, string hookCommand)
    {
        if (entry["hooks"] is JsonArray nested)
        {
            foreach (var hook in nested.OfType<JsonObject>())
            {
                var command = hook["command"]?.GetValue<string>();
                if (string.Equals(command, hookCommand, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static JsonObject ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        return node as JsonObject
               ?? throw new JsonException("Agent settings root must be a JSON object.");
    }

    private static string Serialize(JsonObject root) => root.ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = true,
    });
}

/// <summary>
/// Explicit hook install/repair for AI agent settings files (never runs on a timer, only from an
/// explicit user action). Backs up before writing, holds an exclusive lock, preserves unrelated
/// configuration, and restores from the backup on uninstall.
/// </summary>
public sealed class AgentHookInstaller : IAgentHookInstaller
{
    private readonly HookInstallOptions _options;
    private readonly IPlatformLogger _logger;

    public AgentHookInstaller(HookInstallOptions options, IPlatformLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SettingsFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BackupDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HookCommand);
        _logger = logger ?? NullPlatformLogger.Instance;
    }

    /// <summary>Installs/repairs the FluidBar hooks. Requires explicit user consent.</summary>
    public async Task<HookInstallResult> InstallAsync(bool userConfirmed, CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return HookInstallResult.Failed("Hook installation requires explicit user confirmation.");
        }

        return await MutateAsync(install: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the FluidBar hooks (restores the newest backup when it equals the current file).</summary>
    public async Task<HookInstallResult> UninstallAsync(bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed)
        {
            return HookInstallResult.Failed("Hook removal requires explicit user confirmation.");
        }

        return await MutateAsync(install: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>True when the configured settings file currently contains the FluidBar hook.</summary>
    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var path = _options.SettingsFilePath;
        return await Task.Run(() =>
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            return AgentHookConfigMerger.ContainsHook(json, _options.HookCommand);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HookInstallResult> MutateAsync(bool install, CancellationToken cancellationToken)
    {
        var settingsPath = Path.GetFullPath(_options.SettingsFilePath);
        var directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrEmpty(directory))
        {
            return HookInstallResult.Failed("Agent settings path has no parent directory.");
        }

        await Task.Run(() => Directory.CreateDirectory(directory), cancellationToken).ConfigureAwait(false);
        var lockPath = settingsPath + ".fluidbar.lock";
        FileStream? lockFile = null;
        try
        {
            lockFile = await AcquireLockAsync(lockPath, cancellationToken).ConfigureAwait(false);
            if (lockFile is null)
            {
                return HookInstallResult.Failed("Agent settings file is locked by another installer.");
            }

            return await Task.Run(() => MutateLocked(install, settingsPath, directory), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lockFile?.Dispose();
        }
    }

    private static async Task<FileStream?> AcquireLockAsync(string lockPath, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private HookInstallResult MutateLocked(bool install, string settingsPath, string directory)
    {
        try
        {
            var existing = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            string updated;
            bool changed;
            try
            {
                updated = install
                    ? AgentHookConfigMerger.Merge(existing, _options.HookCommand, _options.HookEvents, out changed)
                    : AgentHookConfigMerger.Remove(existing, _options.HookCommand, out changed);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(PlatformLogCategory.Agent, "agent.hook-invalid-settings",
                    "Agent settings file is not valid JSON; refusing to modify it.", ex);
                return HookInstallResult.Failed("Agent settings file is not valid JSON; nothing was changed.");
            }

            if (!changed)
            {
                return new HookInstallResult(true, false, null, null);
            }

            var backupPath = CreateBackup(settingsPath, directory);
            WriteAtomically(settingsPath, updated);
            _logger.LogInfo(PlatformLogCategory.Agent,
                install ? "agent.hooks-installed" : "agent.hooks-removed",
                install ? "Agent hooks installed (explicit user action)." : "Agent hooks removed.",
                new Dictionary<string, object?>
                {
                    ["settings"] = settingsPath,
                    ["backup"] = backupPath,
                });
            return new HookInstallResult(true, true, backupPath, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(PlatformLogCategory.Agent, "agent.hook-mutation-failed",
                "Agent hook install/uninstall failed.", ex);
            return HookInstallResult.Failed($"Hook operation failed: {ex.GetType().Name}.");
        }
    }
    private string? CreateBackup(string settingsPath, string directory)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        var backupDirectory = Path.GetFullPath(_options.BackupDirectory);
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory,
            $"{Path.GetFileName(settingsPath)}.{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.bak");
        File.Copy(settingsPath, backupPath, overwrite: false);
        return backupPath;
    }

    private static void WriteAtomically(string path, string content)
    {
        var tempPath = path + ".fluidbar.tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }
}
