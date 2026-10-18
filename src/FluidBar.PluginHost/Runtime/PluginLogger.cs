using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;

namespace FluidBar.PluginHost.Runtime;

/// <summary>Bridges the plugin-facing logger to the host logging abstraction with the plugin id stamped in.</summary>
internal sealed class PluginLogger : IPluginLog
{
    private readonly string _pluginId;
    private readonly IPluginHostLogger _hostLogger;

    internal PluginLogger(string pluginId, IPluginHostLogger hostLogger)
    {
        _pluginId = pluginId;
        _hostLogger = hostLogger;
    }

    public bool IsEnabled(PluginLogLevel level) => _hostLogger.IsEnabled(Map(level));

    public void Log(
        PluginLogLevel level,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null) =>
        _hostLogger.Log(Map(level), _pluginId, message, exception, properties);

    private static PluginHostLogLevel Map(PluginLogLevel level) => level switch
    {
        PluginLogLevel.Trace => PluginHostLogLevel.Trace,
        PluginLogLevel.Debug => PluginHostLogLevel.Debug,
        PluginLogLevel.Information => PluginHostLogLevel.Information,
        PluginLogLevel.Warning => PluginHostLogLevel.Warning,
        PluginLogLevel.Error => PluginHostLogLevel.Error,
        PluginLogLevel.Critical => PluginHostLogLevel.Critical,
        _ => PluginHostLogLevel.Information,
    };
}
