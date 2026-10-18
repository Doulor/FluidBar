using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace FluidBar.Platform.Windows.Devices;

/// <summary>
/// Bluetooth device connection monitor built on the WinRT <see cref="DeviceWatcher"/> for paired
/// devices plus <see cref="BluetoothDevice.ConnectionStatus"/> checks. Event-driven: no WMI polling.
/// </summary>
public sealed class BluetoothDeviceMonitor : PlatformMonitor<DeviceEventSnapshot>, IDeviceEventSource
{
    private readonly DeviceEventHistory _history;
    private readonly Dictionary<string, DeviceChangeKind> _lastState = new(StringComparer.Ordinal);
    private readonly object _stateGate = new();
    private DeviceWatcher? _watcher;

    public BluetoothDeviceMonitor(IPlatformLogger logger, PlatformCounters counters, TimeProvider? timeProvider = null)
        : base("bluetooth", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _history = new DeviceEventHistory();
    }

    /// <inheritdoc />
    public string SourceToken => "system:bluetooth";

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken)
                .ConfigureAwait(false);
            lock (_stateGate)
            {
                _lastState.Clear();
                foreach (var device in devices)
                {
                    // Prime without publishing: a cold start must not replay every paired device.
                    _lastState[device.Id] = DeviceChangeKind.Disconnected;
                }
            }

            var watcher = DeviceInformation.CreateWatcher(selector);
            watcher.Added += OnDeviceAdded;
            watcher.Removed += OnDeviceRemoved;
            watcher.Updated += OnDeviceUpdated;
            watcher.EnumerationCompleted += OnEnumerationCompleted;
            watcher.Stopped += OnWatcherStopped;
            watcher.Start();
            _watcher = watcher;
            Publish(_history.Snapshot());
            Logger.LogInfo(PlatformLogCategory.Monitor, "bluetooth.started",
                "Bluetooth device watcher started.", new Dictionary<string, object?> { ["paired"] = devices.Count });
        }
        catch (Exception ex)
        {
            // No Bluetooth radio / unsupported session: degrade silently and keep the service object usable.
            Counters.IncrementDeviceEvents();
            Logger.LogWarning(PlatformLogCategory.Monitor, "bluetooth.unavailable",
                "Bluetooth device monitoring is unavailable on this system.", ex);
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        PublishDeviceEvent(info.Id, info.Name, DeviceChangeKind.Connected);
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        PublishDeviceEvent(update.Id, null, DeviceChangeKind.Disconnected);
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (State != PlatformMonitorState.Running)
        {
            return;
        }

        _ = RefreshConnectionStatusSafeAsync(update.Id);
    }

    private async Task RefreshConnectionStatusSafeAsync(string deviceId)
    {
        try
        {
            var device = await BluetoothDevice.FromIdAsync(deviceId).AsTask().ConfigureAwait(false);
            if (device is null)
            {
                return;
            }

            var change = device.ConnectionStatus == BluetoothConnectionStatus.Connected
                ? DeviceChangeKind.Connected
                : DeviceChangeKind.Disconnected;
            PublishDeviceEvent(deviceId, device.Name, change);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(PlatformLogCategory.Monitor, "bluetooth.status-failed",
                $"Could not read Bluetooth connection status: {ex.GetType().Name}.");
        }
    }

    private void PublishDeviceEvent(string deviceId, string? name, DeviceChangeKind change)
    {
        lock (_stateGate)
        {
            if (_lastState.TryGetValue(deviceId, out var previous) && previous == change)
            {
                return;
            }

            _lastState[deviceId] = change;
        }

        var entry = new DeviceEventEntry(deviceId, DeviceKind.Bluetooth, change,
            string.IsNullOrWhiteSpace(name) ? "Bluetooth device" : name, TimeProvider.GetUtcNow());
        var snapshot = _history.Add(entry);
        Publish(snapshot);
        Counters.IncrementDeviceEvents();
        Logger.LogInfo(PlatformLogCategory.Monitor, "bluetooth.device-event", "Bluetooth device change captured.",
            new Dictionary<string, object?>
            {
                ["change"] = change.ToString(),
                ["name"] = entry.Name,
            });
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
        => Logger.LogDebug(PlatformLogCategory.Monitor, "bluetooth.enumeration-complete",
            "Bluetooth device enumeration completed.");

    private void OnWatcherStopped(DeviceWatcher sender, object args)
        => Logger.LogDebug(PlatformLogCategory.Monitor, "bluetooth.watcher-stopped",
            "Bluetooth device watcher stopped.");

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            try
            {
                watcher.Added -= OnDeviceAdded;
                watcher.Removed -= OnDeviceRemoved;
                watcher.Updated -= OnDeviceUpdated;
                watcher.EnumerationCompleted -= OnEnumerationCompleted;
                watcher.Stopped -= OnWatcherStopped;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "bluetooth.stop-failed",
                    "Stopping the Bluetooth device watcher failed.", ex);
            }
        }

        lock (_stateGate)
        {
            _lastState.Clear();
        }

        _history.Clear();
        return ValueTask.CompletedTask;
    }
}
