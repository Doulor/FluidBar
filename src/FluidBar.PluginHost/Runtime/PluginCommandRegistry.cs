using System.Collections.Immutable;
using FluidBar.PluginHost.Logging;
using FluidBar.Sdk;
using FluidBar.Sdk.Manifest;

namespace FluidBar.PluginHost.Runtime;

/// <summary>
/// Per-plugin command registry. Only commands declared in <c>contributes.commands</c> can be registered; a
/// missing handler leaves the command greyed out in the UI instead of crashing. Handlers are dropped on unload.
/// </summary>
internal sealed class PluginCommandRegistry : ICommandRegistry, IDisposable
{
    private readonly string _pluginId;
    private readonly IPluginHostLogger _logger;
    private readonly ImmutableHashSet<string> _declaredCommands;
    private readonly Dictionary<string, Func<PluginCommandContext, CancellationToken, ValueTask>> _handlers =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    internal PluginCommandRegistry(string pluginId, PluginManifest manifest, IPluginHostLogger logger)
    {
        _pluginId = pluginId;
        _logger = logger;
        _declaredCommands = (manifest.Contributes?.Commands ?? [])
            .Select(static command => command.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    public IDisposable Register(string commandId, Func<PluginCommandContext, CancellationToken, ValueTask> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_declaredCommands.Contains(commandId))
            {
                throw new InvalidOperationException(
                    $"Command '{commandId}' is not declared in the manifest contributes.commands block for plugin '{_pluginId}'.");
            }

            if (!_handlers.TryAdd(commandId, handler))
            {
                throw new InvalidOperationException($"Command '{commandId}' already has a registered handler.");
            }
        }

        return new Registration(this, commandId);
    }

    public bool IsRegistered(string commandId)
    {
        lock (_gate)
        {
            return _handlers.ContainsKey(commandId);
        }
    }

    internal bool TryGetHandler(string commandId, out Func<PluginCommandContext, CancellationToken, ValueTask>? handler)
    {
        lock (_gate)
        {
            return _handlers.TryGetValue(commandId, out handler);
        }
    }

    internal IReadOnlySet<string> DeclaredCommands => _declaredCommands;

    private void Unregister(string commandId)
    {
        lock (_gate)
        {
            _handlers.Remove(commandId);
        }

        _logger.Debug(_pluginId, $"Command '{commandId}' handler unregistered.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handlers.Clear();
        }
    }

    private sealed class Registration(PluginCommandRegistry owner, string commandId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.Unregister(commandId);
        }
    }
}
