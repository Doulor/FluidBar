using System.Collections.Immutable;

namespace FluidBar.Sdk;

/// <summary>
/// Plugin-scoped localization: lookup, formatting and pluralization only. Culture is owned by the host
/// (plugins share the process and must never mutate <see cref="System.Globalization.CultureInfo"/>).
/// </summary>
public interface IPluginLocalization
{
    /// <summary>The locale the host is currently displaying this plugin in.</summary>
    string CurrentLocale { get; }

    /// <summary>The plugin's default (source) locale, normally <c>en-US</c>.</summary>
    string DefaultLocale { get; }

    /// <summary>Locales shipped in the plugin package.</summary>
    IReadOnlyList<string> AvailableLocales { get; }

    /// <summary>Resolves a message; falls back through the documented chain and finally returns the key.</summary>
    string Get(string key);

    /// <summary>Resolves a message without throwing.</summary>
    bool TryGet(string key, out string value);

    /// <summary>Resolves and formats an ICU-subset template.</summary>
    string Format(string key, IReadOnlyDictionary<string, object?> arguments);

    /// <summary>Resolves and formats a plural template for <paramref name="count"/>.</summary>
    string Plural(string key, long count);
}

/// <summary>Structured plugin logging. Implementations must never throw and must not block the caller.</summary>
public interface IPluginLog
{
    bool IsEnabled(PluginLogLevel level);

    void Log(
        PluginLogLevel level,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);
}

/// <summary>Log severities available to plugins.</summary>
public enum PluginLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>Convenience logging extensions.</summary>
public static class PluginLogExtensions
{
    public static void Trace(this IPluginLog log, string message, IReadOnlyDictionary<string, object?>? properties = null) =>
        log.Log(PluginLogLevel.Trace, message, exception: null, properties);

    public static void Debug(this IPluginLog log, string message, IReadOnlyDictionary<string, object?>? properties = null) =>
        log.Log(PluginLogLevel.Debug, message, exception: null, properties);

    public static void Info(this IPluginLog log, string message, IReadOnlyDictionary<string, object?>? properties = null) =>
        log.Log(PluginLogLevel.Information, message, exception: null, properties);

    public static void Warning(this IPluginLog log, string message, Exception? exception = null) =>
        log.Log(PluginLogLevel.Warning, message, exception);

    public static void Error(this IPluginLog log, string message, Exception? exception = null) =>
        log.Log(PluginLogLevel.Error, message, exception);
}

/// <summary>Per-plugin lease factory. Every subscription, timer or resource a plugin creates should be lease-owned.</summary>
public interface IPluginLeases
{
    bool IsDisposed { get; }

    /// <summary>Creates a lease whose timers are released when the lease (or the plugin context) is disposed.</summary>
    IPluginLease CreateLease(string name);
}

/// <summary>
/// A unit of plugin-owned lifetime. Disposing the lease cancels and awaits its timers; the host disposes all
/// leases before unloading the AssemblyLoadContext, which is the primary unload-leak mitigation.
/// </summary>
public interface IPluginLease : IDisposable
{
    string Name { get; }

    CancellationToken CancellationToken { get; }

    bool IsDisposed { get; }

    /// <summary>Schedules a single callback after <paramref name="dueTime"/>.</summary>
    IDisposable ScheduleOnce(TimeSpan dueTime, Func<CancellationToken, ValueTask> callback);

    /// <summary>Schedules a non-overlapping periodic callback. A slow callback skips its next tick.</summary>
    IDisposable SchedulePeriodic(TimeSpan period, Func<CancellationToken, ValueTask> callback, TimeSpan? initialDelay = null);
}

/// <summary>Binds handlers to manifest-declared commands. The host owns invocation and exception isolation.</summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Registers a handler for a command declared in <c>contributes.commands</c>. Throws
    /// <see cref="InvalidOperationException"/> for undeclared or duplicate commands; dispose the result to
    /// unregister.
    /// </summary>
    IDisposable Register(string commandId, Func<PluginCommandContext, CancellationToken, ValueTask> handler);

    bool IsRegistered(string commandId);
}

/// <summary>Where a command invocation originated.</summary>
public enum CommandInvocationSource
{
    Global = 0,
    Island = 1,
    ContextMenu = 2,
    CommandPalette = 3,
    Settings = 4,
    Hotkey = 5,
    Programmatic = 6,
}

/// <summary>Data passed to a plugin command handler.</summary>
public sealed record PluginCommandContext
{
    public required string PluginId { get; init; }

    public required string CommandId { get; init; }

    public CommandInvocationSource Source { get; init; } = CommandInvocationSource.Programmatic;

    public IReadOnlyDictionary<string, string?>? Arguments { get; init; }
}

/// <summary>Posts host-rendered notifications (never plugin UI).</summary>
public interface INotificationSink
{
    /// <summary>Posts a notification; returns <see langword="false"/> when dropped by host policy.</summary>
    bool TryPost(PluginNotification notification);
}

/// <summary>Notification data supplied by a plugin; the host renders it.</summary>
public sealed record PluginNotification
{
    public required string Title { get; init; }

    public string? Body { get; init; }

    public string? IconPath { get; init; }

    public PluginNotificationKind Kind { get; init; } = PluginNotificationKind.Info;

    /// <summary>Suggested display duration; the host may clamp it.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Optional manifest command invoked when the user clicks the notification.</summary>
    public string? CommandId { get; init; }
}

/// <summary>Notification severity.</summary>
public enum PluginNotificationKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>Host-owned, schema-validated per-plugin settings store.</summary>
public interface IPluginSettings
{
    IReadOnlyCollection<string> Keys { get; }

    bool TryGetBoolean(string key, out bool value);

    bool TryGetInt32(string key, out int value);

    bool TryGetDouble(string key, out double value);

    bool TryGetString(string key, out string? value);

    bool TryGetStringArray(string key, out IReadOnlyList<string>? value);

    bool GetBoolean(string key, bool fallback);

    int GetInt32(string key, int fallback);

    double GetDouble(string key, double fallback);

    string GetString(string key, string fallback);

    IReadOnlyList<string> GetStringArray(string key, IReadOnlyList<string>? fallback = null);

    void SetBoolean(string key, bool value);

    void SetInt32(string key, int value);

    void SetDouble(string key, double value);

    void SetString(string key, string? value);

    void SetStringArray(string key, IReadOnlyList<string> values);

    bool Remove(string key);

    /// <summary>Subscribes to changes; dispose the result (lease-owned) to unsubscribe.</summary>
    IDisposable Subscribe(Action<string> onChanged);

    /// <summary>Persists atomically through the host-owned settings file.</summary>
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-plugin file storage rooted at the plugin data directory. All relative paths are validated by the host;
/// absolute paths and <c>..</c> traversal are rejected.
/// </summary>
public interface IPluginStorage
{
    string DataDirectory { get; }

    /// <summary>Resolves a safe absolute path inside <see cref="DataDirectory"/>.</summary>
    string GetDataPath(string relativePath);

    bool Exists(string relativePath);

    Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default);

    Task WriteTextAsync(string relativePath, string content, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadBytesAsync(string relativePath, int maxBytes, CancellationToken cancellationToken = default);

    Task WriteBytesAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>Immutable empty settings map (useful for tests and null-object scenarios).</summary>
public static class PluginServiceDefaults
{
    /// <summary>An empty argument dictionary.</summary>
    public static IReadOnlyDictionary<string, object?> EmptyArguments { get; } =
        ImmutableDictionary<string, object?>.Empty;
}
