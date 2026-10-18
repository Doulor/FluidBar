using FluidBar.Core.Diagnostics;

namespace FluidBar.Platform.Windows.Diagnostics;

/// <summary>
/// Bridges the platform logger abstraction to the host-owned Core <see cref="IAppLogger"/>.
/// The App wires this once: <c>new AppLoggerPlatformAdapter(coreLogger)</c> and refines before
/// construction, or plugs the platform abstraction directly into an <see cref="IPlatformLogger"/>
/// chain.
/// </summary>
/// <remarks>
/// Category mapping keeps the Core closed set stable: platform monitor/media/clipboard/shell events
/// are mapped onto <see cref="LogCategories.Platform"/>; lifecycle/plugin/market/settings map to their
/// own Core categories so the diagnostics dashboard slices stay meaningful.
/// </remarks>
public sealed class AppLoggerPlatformAdapter : IPlatformLogger
{
    private readonly IAppLogger _coreLogger;
    private readonly Dictionary<PlatformLogCategory, string> _categoryMap;

    public AppLoggerPlatformAdapter(IAppLogger coreLogger)
    {
        _coreLogger = coreLogger ?? throw new ArgumentNullException(nameof(coreLogger));
        _categoryMap = BuildCategoryMap();
    }

    public bool IsEnabled(PlatformLogLevel level) => _coreLogger.IsEnabled(MapLevel(level));

    public void Log(in PlatformLogEntry entry)
    {
        try
        {
            var category = _categoryMap.TryGetValue(entry.Category, out var mapped)
                ? mapped
                : LogCategories.Platform;
            _coreLogger.Log(MapLevel(entry.Level), category, entry.EventId, entry.Message,
                exception: null, data: entry.Data);
        }
        catch (Exception)
        {
            // Logging adapters must never take a producer down.
        }
    }

    /// <summary>Maps a platform level onto the Core level enum.</summary>
    public static LogLevel MapLevel(PlatformLogLevel level) => level switch
    {
        PlatformLogLevel.Trace => LogLevel.Trace,
        PlatformLogLevel.Debug => LogLevel.Debug,
        PlatformLogLevel.Info => LogLevel.Information,
        PlatformLogLevel.Warning => LogLevel.Warning,
        PlatformLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };

    private static Dictionary<PlatformLogCategory, string> BuildCategoryMap() => new()
    {
        [PlatformLogCategory.Lifecycle] = LogCategories.Lifecycle,
        [PlatformLogCategory.Activity] = LogCategories.Activity,
        [PlatformLogCategory.Motion] = LogCategories.Motion,
        [PlatformLogCategory.Render] = LogCategories.Render,
        [PlatformLogCategory.Plugin] = LogCategories.Plugin,
        [PlatformLogCategory.Market] = LogCategories.Market,
        [PlatformLogCategory.Settings] = LogCategories.Settings,
        [PlatformLogCategory.Platform] = LogCategories.Platform,
        [PlatformLogCategory.Monitor] = LogCategories.Platform,
        [PlatformLogCategory.Media] = LogCategories.Platform,
        [PlatformLogCategory.Clipboard] = LogCategories.Platform,
        [PlatformLogCategory.Notifications] = LogCategories.Platform,
        [PlatformLogCategory.Shell] = LogCategories.Platform,
        [PlatformLogCategory.Agent] = LogCategories.Platform,
        [PlatformLogCategory.Update] = LogCategories.Platform,
        [PlatformLogCategory.Diagnostics] = LogCategories.Platform,
    };
}
