namespace FluidBar.App.Plugins;

/// <summary>
/// Filesystem locations of the plugin lane. Production uses <c>%APPDATA%\FluidBar\plugins</c>; tests and
/// portable/dev runs override the root with <c>FLUIDBAR_PLUGINS_DIR</c> (never a hard-coded absolute path).
/// </summary>
public static class PluginRuntimePaths
{
    /// <summary>Environment variable that overrides the plugins root (tests, portable installs).</summary>
    public const string RootOverrideVariable = "FLUIDBAR_PLUGINS_DIR";

    /// <summary>Default plugins root: <c>%APPDATA%\FluidBar\plugins</c>.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "plugins");

    /// <summary>Resolves the plugins root, honoring <see cref="RootOverrideVariable"/> when it is an absolute path.</summary>
    public static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(RootOverrideVariable);
        return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? Path.GetFullPath(configured)
            : DefaultRoot;
    }

    /// <summary>Directory of one plugin (<c>&lt;root&gt;/&lt;id&gt;</c>).</summary>
    public static string PluginDirectory(string root, string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return Path.Combine(root, pluginId);
    }

    /// <summary>Host-owned settings file for a plugin.</summary>
    public static string SettingsFile(string root, string pluginId)
        => Path.Combine(PluginDirectory(root, pluginId), "settings.json");

    /// <summary>Plugin data directory (<c>$PLUGIN_DATA</c>).</summary>
    public static string DataDirectory(string root, string pluginId)
        => Path.Combine(PluginDirectory(root, pluginId), "data");
}
