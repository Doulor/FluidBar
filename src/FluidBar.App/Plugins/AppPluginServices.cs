using Avalonia.Threading;
using FluidBar.Core.Diagnostics;
using FluidBar.Marketplace.Install;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;

namespace FluidBar.App.Plugins;

/// <summary>Bridges the plugin host log abstraction onto the App JSON-lines logger (never retains exceptions).</summary>
public sealed class AppPluginHostLogger : IPluginHostLogger
{
    private readonly IAppLogger _logger;

    /// <summary>Creates the bridge.</summary>
    public AppPluginHostLogger(IAppLogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public bool IsEnabled(PluginHostLogLevel level) => level >= PluginHostLogLevel.Information;

    /// <inheritdoc />
    public void Log(
        PluginHostLogLevel level,
        string pluginId,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        var data = new Dictionary<string, object?>(properties ?? new Dictionary<string, object?>())
        {
            ["plugin"] = pluginId,
        };

        switch (level)
        {
            case PluginHostLogLevel.Error:
            case PluginHostLogLevel.Critical:
                _logger.Error(LogCategories.Plugin, "plugin.host", message, exception, data);
                break;
            case PluginHostLogLevel.Warning:
                _logger.Warning(LogCategories.Plugin, "plugin.host", message, data: data);
                break;
            default:
                _logger.Information(LogCategories.Plugin, "plugin.host", message, data: data);
                break;
        }
    }
}

/// <summary>Bridges marketplace install logging onto the App logger.</summary>
public sealed class AppPluginInstallLogger : IPluginInstallLogger
{
    private readonly IAppLogger _logger;

    /// <summary>Creates the bridge.</summary>
    public AppPluginInstallLogger(IAppLogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public void Info(string message) => _logger.Information(LogCategories.Plugin, "plugin.install", message);

    /// <inheritdoc />
    public void Warn(string message) => _logger.Warning(LogCategories.Plugin, "plugin.install", message);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
        => _logger.Error(LogCategories.Plugin, "plugin.install", message, exception);
}

/// <summary>
/// Host-rendered notification sink placeholder. Plugin notifications are validated by the host wrapper and
/// recorded in the App log; a toast/HUD presenter is still a documented gap (no plugin UI is ever shown).
/// </summary>
public sealed class LoggingPluginNotificationSink : INotificationSink
{
    private readonly IAppLogger _logger;
    private readonly string _pluginId;

    /// <summary>Creates the sink for one plugin.</summary>
    public LoggingPluginNotificationSink(IAppLogger logger, string pluginId)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
    }

    /// <inheritdoc />
    public bool TryPost(PluginNotification notification)
    {
        if (notification is null)
        {
            return false;
        }

        _logger.Information(
            LogCategories.Plugin,
            "plugin.notification",
            notification.Title,
            data: new Dictionary<string, object?>
            {
                ["plugin"] = _pluginId,
                ["kind"] = notification.Kind.ToString(),
                ["body"] = notification.Body,
            });
        return true;
    }
}

/// <summary>Marshals plugin UI work onto the Avalonia UI thread (plugins never touch controls off-thread).</summary>
public sealed class AvaloniaHostDispatcher : IHostDispatcher
{
    /// <summary>Shared instance.</summary>
    public static AvaloniaHostDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    /// <inheritdoc />
    public bool TryPost(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return true;
        }

        Dispatcher.UIThread.Post(action);
        return true;
    }

    /// <inheritdoc />
    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Dispatcher.UIThread.InvokeAsync(action).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Dispatcher.UIThread.InvokeAsync(action).WaitAsync(cancellationToken);
    }
}
