using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Threading;
using FluidBar.Sdk;
using FluidBar.Sdk.Contributions;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// First-party devices plugin: USB and Bluetooth device changes become transient SDK activities. The
/// stable Core source tokens (<c>system:usb</c>/<c>system:bluetooth</c>) are carried in the event fields so
/// the App adapter can map them to the right activity source while the SDK source stays the plugin id.
/// </summary>
public sealed class DevicesPlugin : BuiltinPluginBase
{
    private readonly IDeviceEventSource? _usb;
    private readonly IDeviceEventSource? _bluetooth;
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _lastByToken = new(StringComparer.Ordinal);

    public DevicesPlugin(IDeviceEventSource? usb, IDeviceEventSource? bluetooth)
    {
        _usb = usb;
        _bluetooth = bluetooth;
        if (_usb is null && _bluetooth is null)
        {
            throw new ArgumentException("At least one device event source is required.", nameof(usb));
        }
    }

    public override string PluginId => BuiltinIds.DevicesPlugin;

    public static PluginContributions Contributions => BuiltinContributions.ForDevices();

    protected override void OnStarted()
    {
        if (_usb is not null)
        {
            var source = _usb;
            TrackSubscription(() => source.SnapshotChanged += OnUsbChanged,
                () => source.SnapshotChanged -= OnUsbChanged);
            PublishSourceSnapshot(source);
        }

        if (_bluetooth is not null)
        {
            var source = _bluetooth;
            TrackSubscription(() => source.SnapshotChanged += OnBluetoothChanged,
                () => source.SnapshotChanged -= OnBluetoothChanged);
            PublishSourceSnapshot(source);
        }

        RegisterCommand(BuiltinIds.DevicesRetract, (_, _) =>
        {
            Retract();
            return ValueTask.CompletedTask;
        });
    }

    private void OnUsbChanged(object? sender, SnapshotEventArgs<DeviceEventSnapshot> args)
    {
        if (IsStarted)
        {
            PublishDeviceSnapshot(_usb!, args.Snapshot);
        }
    }

    private void OnBluetoothChanged(object? sender, SnapshotEventArgs<DeviceEventSnapshot> args)
    {
        if (IsStarted)
        {
            PublishDeviceSnapshot(_bluetooth!, args.Snapshot);
        }
    }

    private void PublishSourceSnapshot(IDeviceEventSource source)
    {
        var snapshot = source.Current;
        if (snapshot is not null && snapshot.Latest is not null)
        {
            // A cold start must not replay the latest historical event; only live changes are activities.
            lock (_stateGate)
            {
                _lastByToken[source.SourceToken] = BuildSignature(source.SourceToken, snapshot.Latest);
            }
        }
    }

    internal void PublishDeviceSnapshot(IDeviceEventSource source, DeviceEventSnapshot? snapshot)
    {
        var latest = snapshot?.Latest;
        if (latest is null)
        {
            return;
        }

        var signature = BuildSignature(source.SourceToken, latest);
        lock (_stateGate)
        {
            if (_lastByToken.TryGetValue(source.SourceToken, out var previous)
                && string.Equals(previous, signature, StringComparison.Ordinal))
            {
                return;
            }

            _lastByToken[source.SourceToken] = signature;
        }

        var connected = latest.Change == DeviceChangeKind.Connected;
        var changeToken = connected ? "connected" : "disconnected";
        var icon = source.SourceToken == "system:bluetooth" ? "bluetooth" : "usb";
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["icon"] = icon,
            ["title"] = latest.Name,
            ["source"] = source.SourceToken,
            ["badge"] = latest.Kind.ToString(),
            ["device"] = latest.Name,
        };

        Publish(new IslandEvent
        {
            Source = PluginId,
            Title = latest.Name,
            Content = $"{source.SourceToken}:{changeToken}",
            IconKind = icon,
            Kind = IslandEventKind.Status,
            LayoutId = BuiltinIds.DevicesTransientLayout,
            Payload = new IslandEventPayload
            {
                Subtitle = source.SourceToken,
                Badge = latest.Kind.ToString(),
                IsActive = connected,
                SourceName = latest.Kind.ToString(),
                DetailLines = [$"{source.SourceToken}:{changeToken}"],
                Fields = fields,
            },
        });
    }

    private static string BuildSignature(string sourceToken, DeviceEventEntry entry)
        => string.Join('|', sourceToken, entry.DeviceId, entry.Change.ToString());

    protected override void OnStopped()
    {
        lock (_stateGate)
        {
            _lastByToken.Clear();
        }
    }
}
