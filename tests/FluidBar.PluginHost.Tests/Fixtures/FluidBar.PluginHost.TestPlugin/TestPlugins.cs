using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using FluidBar.Sdk;

namespace FluidBar.PluginHost.TestPlugin;

/// <summary>Happy-path fixture: publishes an event, owns a long-period lease timer, registers disposal + a command.</summary>
public sealed class HappyPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private IPluginLease? _lease;
    private IDisposable? _commandRegistration;
    private IDisposable? _resource;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        context.Log.Info("HappyPlugin initialized.");
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        _lease = context.Leases.CreateLease("happy.timer");
        _lease.SchedulePeriodic(TimeSpan.FromHours(1), _ => ValueTask.CompletedTask);
        _resource = context.RegisterForDisposal(() => context.Log.Info("HappyPlugin registered resource released."));

        try
        {
            _commandRegistration = context.Commands.Register("test-happy.ping", (_, _) =>
            {
                context.Log.Info("command executed");
                return ValueTask.CompletedTask;
            });
        }
        catch (InvalidOperationException)
        {
            // The fixture manifest may not declare the command in simpler tests; that is fine.
        }

        context.Ipublish("happy-started");
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _commandRegistration?.Dispose();
        _commandRegistration = null;
        _resource?.Dispose();
        _resource = null;
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}

internal static class PluginContextTestExtensions
{
    internal static void Ipublish(this IPluginContext context, string title)
    {
        context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId,
            Title = title,
            Content = context.Localization.Get("fixture.body"),
        });
    }
}

/// <summary>Throws in the constructor (activation failure).</summary>
public sealed class ThrowingConstructorPlugin : IFluidBarPlugin
{
    public ThrowingConstructorPlugin() => throw new InvalidOperationException("fixture: constructor failed");

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Throws during InitializeAsync.</summary>
public sealed class ThrowingInitializePlugin : IFluidBarPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("fixture: initialize failed");

    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Throws during StartAsync.</summary>
public sealed class ThrowingStartPlugin : IFluidBarPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StartAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException("fixture: start failed");

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Never completes StartAsync within the host timeout.</summary>
public sealed class HangingStartPlugin : IFluidBarPlugin
{
    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public async ValueTask StartAsync(CancellationToken cancellationToken) =>
        await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Deliberately leaks an external GC root, so the loader cannot collect the ALC. Used to prove the unload
/// verification actually detects a leak (regression evidence for the loader contract).
/// </summary>
public sealed class LeakyGcHandlePlugin : IFluidBarPlugin
{
    private GCHandle _selfRoot;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _selfRoot = GCHandle.Alloc(this);
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Reports the load contexts it sees so tests can prove single SDK type identity.</summary>
public sealed class IdentityPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        var pluginContextName = AssemblyLoadContext.GetLoadContext(typeof(IdentityPlugin).Assembly)?.Name ?? "<none>";
        var sdkContextName = AssemblyLoadContext.GetLoadContext(typeof(IFluidBarPlugin).Assembly)?.Name ?? "<none>";
        var sdkLocation = typeof(IFluidBarPlugin).Assembly.Location;
        context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId,
            Title = $"plugin-alc={pluginContextName}|sdk-alc={sdkContextName}|sdk-location={sdkLocation}",
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Reads and writes settings and exposes them through a published event.</summary>
public sealed class SettingsPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        var counter = context.Settings.GetInt32("counter", 0);
        context.Settings.SetInt32("counter", counter + 1);
        context.Settings.SaveAsync(cancellationToken).GetAwaiter().GetResult();
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        context.Islands.TryPublish(new IslandEvent
        {
            Source = context.PluginId,
            Title = $"counter={context.Settings.GetInt32("counter", -1)}",
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Publishes on a fast lease timer so tests can prove timers stop on StopAsync/unload.</summary>
public sealed class TimedPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private IPluginLease? _lease;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        _lease = context.Leases.CreateLease("timed");
        _lease.SchedulePeriodic(TimeSpan.FromMilliseconds(25), _ =>
        {
            context.Islands.TryPublish(new IslandEvent { Source = context.PluginId, Title = "tick" });
            return ValueTask.CompletedTask;
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _lease?.Dispose();
        _lease = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);
}

/// <summary>Exercises context services and records observable markers through island events.</summary>
public sealed class ProbePlugin : IFluidBarPlugin
{
    private IPluginContext? _context;
    private readonly List<IDisposable> _registrations = [];

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        Mark(context, "localized=" + context.Localization.Get("fixture.body"));
        Mark(context, "missing=" + context.Localization.Get("missing.key"));
        Mark(context, "plural=" + context.Localization.Plural("items.count", 2));

        try
        {
            _ = context.Storage.GetDataPath("../escape.txt");
            Mark(context, "storage-allowed");
        }
        catch (ArgumentException)
        {
            Mark(context, "storage-rejected");
        }

        context.Storage.WriteTextAsync("cache/probe.txt", "probe").GetAwaiter().GetResult();
        Mark(context, "storage-write=" + context.Storage.Exists("cache/probe.txt"));

        var badNotification = context.Notifications.TryPost(new PluginNotification { Title = new string('x', 300) });
        Mark(context, "notification-rejected=" + (!badNotification));
        context.Notifications.TryPost(new PluginNotification { Title = "probe" });

        var badEvent = context.Islands.TryPublish(new IslandEvent { Source = "not-this-plugin", Title = "bad" });
        Mark(context, "source-rejected=" + (!badEvent));

        var grant = context.GrantedCapabilities.Contains(FluidBar.Sdk.Capabilities.PluginCapability.Ui);
        Mark(context, "granted-ui=" + grant);

        try
        {
            _registrations.Add(context.Commands.Register("test-probe.ping", (_, _) =>
            {
                Mark(context, "command-executed");
                return ValueTask.CompletedTask;
            }));
            _registrations.Add(context.Commands.Register("test-probe.fail", (_, _) =>
                throw new InvalidOperationException("fixture: command failed")));
        }
        catch (InvalidOperationException)
        {
            Mark(context, "command-declaration-missing");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

    private static void Mark(IPluginContext context, string marker) =>
        context.Islands.TryPublish(new IslandEvent { Source = context.PluginId, Title = marker });
}

/// <summary>A type that exists in the plugin assembly but does not implement IFluidBarPlugin.</summary>
public sealed class NotAPlugin
{
    public string Name => "not a plugin";
}

/// <summary>Registers a resource for loader disposal and deliberately does not dispose it in StopAsync.</summary>
public sealed class DisposalPlugin : IFluidBarPlugin
{
    private IPluginContext? _context;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        _context = context;
        return ValueTask.CompletedTask;
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Not initialized.");
        context.RegisterForDisposal(() => context.Log.Info("disposal plugin resource released"));
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
