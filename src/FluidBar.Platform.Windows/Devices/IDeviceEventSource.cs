using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Devices;

/// <summary>Device event contract consumed by first-party plugins. Implemented by the USB/Bluetooth monitors.</summary>
public interface IDeviceEventSource
{
    DeviceEventSnapshot? Current { get; }

    event EventHandler<SnapshotEventArgs<DeviceEventSnapshot>>? SnapshotChanged;

    /// <summary>Stable Core source token (<c>system:usb</c>/<c>system:bluetooth</c>) this source feeds.</summary>
    string SourceToken { get; }
}
