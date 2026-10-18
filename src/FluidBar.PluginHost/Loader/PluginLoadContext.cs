using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace FluidBar.PluginHost.Loader;

/// <summary>
/// The frozen set of assemblies that are always resolved from the host (the single type-identity rule):
/// framework/TPA assemblies already loaded by the process plus <c>FluidBar.Sdk</c>. Computed once at host
/// construction; never grows, so it cannot leak plugin assemblies into the shared set.
/// </summary>
public sealed class HostAssemblyCatalog
{
    private static readonly ImmutableHashSet<string> ForbiddenAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // WPF
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Xaml",
        "System.Windows.Forms",
        "System.Windows.Controls.Ribbon",
        "WindowsFormsIntegration",
        "UIAutomationClient",
        "UIAutomationTypes",
        "System.Printing",
        "ReachFramework",
        "DirectWriteForwarder",
        // App / host / other FluidBar projects: a plugin depends on FluidBar.Sdk only.
        "FluidBar.App",
        "FluidBar.Host",
        "FluidBar.PluginHost",
        "FluidBar.Marketplace",
        "FluidBar.Platform.Windows",
        "FluidBar.Core",
    }.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly ImmutableArray<string> ForbiddenAssemblyPrefixes = ["Avalonia", "Microsoft.Web.WebView2"];

    private readonly ImmutableHashSet<string> _sharedAssemblyNames;

    public HostAssemblyCatalog(IEnumerable<string>? additionalSharedAssemblies = null)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);

        // The plugin contract is always shared from the host/default ALC: one type identity.
        builder.Add("FluidBar.Sdk");

        // Trusted Platform Assemblies = framework + host app assemblies already resolved for the process.
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(name))
                {
                    builder.Add(name);
                }
            }
        }

        // Anything already loaded by the host is shared too (e.g. a WinRT projection the App loaded).
        foreach (var assembly in AssemblyLoadContext.Default.Assemblies)
        {
            if (assembly.GetName().Name is { Length: > 0 } name)
            {
                builder.Add(name);
            }
        }

        if (additionalSharedAssemblies is not null)
        {
            foreach (var name in additionalSharedAssemblies)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    builder.Add(name);
                }
            }
        }

        _sharedAssemblyNames = builder.ToImmutable();
    }

    /// <summary>The frozen snapshot of shared assembly simple names.</summary>
    public IReadOnlyCollection<string> SharedAssemblyNames => _sharedAssemblyNames;

    /// <summary>True when the assembly must be resolved from the default ALC (host copy).</summary>
    public bool ShouldShareFromHost(string assemblyName) => _sharedAssemblyNames.Contains(assemblyName);

    /// <summary>True for assemblies a plugin may never reference (UI frameworks, app/host assemblies).</summary>
    public static bool IsForbidden(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
        {
            return false;
        }

        if (ForbiddenAssemblyNames.Contains(assemblyName))
        {
            return true;
        }

        foreach (var prefix in ForbiddenAssemblyPrefixes)
        {
            if (assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The forbidden simple names (for docs/diagnostics).</summary>
    public static IReadOnlyCollection<string> ForbiddenAssemblySimpleNames => ForbiddenAssemblyNames;
}

/// <summary>
/// One collectible AssemblyLoadContext per plugin, with an <see cref="AssemblyDependencyResolver"/> pointed at
/// the entry assembly and its <c>.deps.json</c> (<c>EnableDynamicLoading</c>).
/// </summary>
/// <remarks>
/// The shared-contract check happens <b>before</b> the dependency resolver: a package that illegally ships
/// <c>FluidBar.Sdk.dll</c> must still resolve the host SDK, otherwise a second type universe is created and
/// every plugin fails <c>IsAssignableFrom</c>. ALC names are unique per load so reloads are distinguishable in
/// diagnostics; exactly one ALC is active per plugin id at the application level.
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private static int _liveContextCount;

    private readonly AssemblyDependencyResolver _resolver;
    private readonly HostAssemblyCatalog _hostAssemblies;

    public PluginLoadContext(string entryAssemblyPath, string pluginId, HostAssemblyCatalog hostAssemblies)
        : base($"FluidBar.Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}:{Guid.NewGuid():N}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _hostAssemblies = hostAssemblies ?? throw new ArgumentNullException(nameof(hostAssemblies));
        PluginId = pluginId;
        Interlocked.Increment(ref _liveContextCount);
        Unloading += static _ => Interlocked.Decrement(ref _liveContextCount);
    }

    public string PluginId { get; }

    /// <summary>Diagnostics counter: collectible contexts created but not yet unloading.</summary>
    public static int LiveContextCount => Volatile.Read(ref _liveContextCount);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;

        // Shared contract/framework/host assemblies: return null so the default ALC resolves them.
        if (_hostAssemblies.ShouldShareFromHost(name))
        {
            return null;
        }

        if (HostAssemblyCatalog.IsForbidden(name))
        {
            throw new FileLoadException(
                $"Plugin '{PluginId}' attempted to load forbidden assembly '{name}'. Plugins must not reference UI frameworks or FluidBar host assemblies.");
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        // Native libraries cannot be unloaded by a collectible ALC; a package shipping natives is effectively
        // restart-to-update. This is documented and flagged by the marketplace policy checks.
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
