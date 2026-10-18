using System.Text.Json;
using FluidBar.PluginHost.Runtime;
using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// Runtime handle for one discovered/loaded plugin. The host owns handles; the App reads their state for the
/// plugins UI. After unload the handle deliberately contains only strings, timestamps, status and weak
/// references: never plugin types, assemblies, delegates or exception objects (they root the collectible ALC).
/// </summary>
public sealed class PluginHandle
{
    private long _consecutiveFailures;

    internal PluginHandle(PluginManifest manifest, string packageDirectory, string dataDirectory, string? entryAssemblyName)
    {
        Manifest = manifest;
        PackageDirectory = packageDirectory;
        DataDirectory = dataDirectory;
        EntryAssemblyName = entryAssemblyName;
    }

    public PluginManifest Manifest { get; }

    public string PluginId => Manifest.Id;

    public string DisplayName => Manifest.Name?.Resolve("en-US") ?? Manifest.Id;

    public string PackageDirectory { get; }

    public string DataDirectory { get; }

    /// <summary>Simple name of the entry assembly, used for the post-unload loaded-assembly check.</summary>
    public string? EntryAssemblyName { get; }

    public PluginLifecycleState State { get; internal set; } = PluginLifecycleState.Discovered;

    /// <summary>Formatted error text (never an exception object: exception objects can pin the plugin ALC).</summary>
    public string? LastError { get; internal set; }

    /// <summary>Last validation errors as strings, for the plugins UI.</summary>
    public IReadOnlyList<string> LastValidationErrors { get; internal set; } = [];

    public long ConsecutiveFailures => Interlocked.Read(ref _consecutiveFailures);

    public DateTimeOffset? LastLoadAttempt { get; internal set; }

    public DateTimeOffset? ActivatedAt { get; internal set; }

    /// <summary>Set when the plugin stays disabled / unload could not prove collection.</summary>
    public bool IsRestartRequired { get; internal set; }

    public bool IsDisabled => State == PluginLifecycleState.Disabled;

    /// <summary>
    /// Reads one host-owned setting value as JSON for the generic settings-page renderer. Returns
    /// <see langword="null"/> when the plugin is not loaded or the key has not been written.
    /// </summary>
    public JsonElement? GetSettingValue(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Runtime is { } runtime && runtime.Settings.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Writes one host-owned setting value (the plugin observes it through <c>IPluginSettings</c>).</summary>
    public bool SetSettingValue(string key, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Runtime is not { } runtime)
        {
            return false;
        }

        try
        {
            runtime.Settings.SetValue(key, value);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Flushes the host-owned settings file.</summary>
    public Task SaveSettingsAsync(CancellationToken cancellationToken = default)
        => Runtime is { } runtime ? runtime.Settings.SaveAsync(cancellationToken).AsTask() : Task.CompletedTask;

    /// <summary>Diagnostics-only weak view of the plugin instance. Never cached by the App.</summary>
    internal WeakReference? PluginInstanceReference { get; set; }

    /// <summary>Diagnostics-only weak view of the collectible AssemblyLoadContext.</summary>
    internal WeakReference? LoadContextReference { get; set; }

    internal object? PluginInstance { get; set; }

    internal PluginLoadContext? LoadContext { get; set; }

    internal PluginRuntimeRoot? Runtime { get; set; }

    internal void RecordFailure() => Interlocked.Increment(ref _consecutiveFailures);

    internal void ResetFailures() => Interlocked.Exchange(ref _consecutiveFailures, 0);

    internal void ClearSessionReferences()
    {
        PluginInstance = null;
        Runtime = null;
        LoadContext = null;
    }

    public override string ToString() => $"{PluginId} ({State})";
}
