namespace FluidBar.PluginHost.Logging;

/// <summary>Log severities used by the plugin host and the per-plugin log bridge.</summary>
public enum PluginHostLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>
/// Host logging abstraction. The host never writes to the console directly: the App injects the same
/// JSON-lines file sink used by the rest of FluidBar, and tests inject a recording logger so failure
/// isolation is deterministic and assertable.
/// </summary>
public interface IPluginHostLogger
{
    bool IsEnabled(PluginHostLogLevel level);

    void Log(
        PluginHostLogLevel level,
        string pluginId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);
}

/// <summary>Discards everything; the default for hosts that do not configure logging yet.</summary>
public sealed class NullPluginHostLogger : IPluginHostLogger
{
    public static NullPluginHostLogger Instance { get; } = new();

    private NullPluginHostLogger()
    {
    }

    public bool IsEnabled(PluginHostLogLevel level) => false;

    public void Log(
        PluginHostLogLevel level,
        string pluginId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        // Intentionally empty: the null sink exists so call sites never need null checks.
    }
}

/// <summary>Convenience extensions that keep host log call sites short.</summary>
public static class PluginHostLoggerExtensions
{
    public static void Info(this IPluginHostLogger logger, string pluginId, string message) =>
        logger.Log(PluginHostLogLevel.Information, pluginId, message);

    public static void Warn(this IPluginHostLogger logger, string pluginId, string message, Exception? exception = null) =>
        logger.Log(PluginHostLogLevel.Warning, pluginId, message, exception);

    public static void Error(this IPluginHostLogger logger, string pluginId, string message, Exception? exception = null) =>
        logger.Log(PluginHostLogLevel.Error, pluginId, message, exception);

    public static void Debug(this IPluginHostLogger logger, string pluginId, string message) =>
        logger.Log(PluginHostLogLevel.Debug, pluginId, message);
}
