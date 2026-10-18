using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Interop;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Devices;

/// <summary>
/// USB device arrival/removal monitor built on <c>RegisterDeviceNotification(DBT_DEVTYP_DEVICEINTERFACE)</c>
/// + <c>WM_DEVICECHANGE</c>. Event-driven: no WMI watchers, no polling (v1 used WMI with leaked
/// <c>ManagementObject</c>s).
/// </summary>
public sealed class UsbDeviceMonitor : PlatformMonitor<DeviceEventSnapshot>, IDeviceEventSource
{
    private static readonly Guid UsbDeviceInterfaceGuid = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private readonly PlatformMessageHost _host;
    private readonly DeviceEventHistory _history;
    private PlatformWindow? _window;
    private SafeDeviceNotificationHandle? _registration;
    private int _pendingEvents;

    public UsbDeviceMonitor(PlatformMessageHost host, IPlatformLogger logger, PlatformCounters counters,
        TimeProvider? timeProvider = null)
        : base("usb", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _history = new DeviceEventHistory();
    }

    /// <inheritdoc />
    public string SourceToken => "system:usb";

    protected override async ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        _window = await _host.CreateWindowAsync("usb", PlatformWindowKind.HiddenTopLevel, OnWindowMessage,
            cancellationToken).ConfigureAwait(false);

        var handle = _window.Handle;
        _registration = await _host.InvokeAsync(() =>
        {
            var filter = new NativeMethods.DevBroadcastDeviceInterface
            {
                cbSize = (uint)(Marshal.SizeOf<NativeMethods.DevBroadcastDeviceInterface>() + (sizeof(char) * 2)),
                dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_classguid = UsbDeviceInterfaceGuid,
            };
            return SafeDeviceNotificationHandle.Register(handle, in filter);
        }, cancellationToken).ConfigureAwait(false);

        Publish(_history.Snapshot());
        Logger.LogInfo(PlatformLogCategory.Monitor, "usb.started", "USB device notification monitor started.");
    }

    private IntPtr? OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message != NativeMethods.WM_DEVICECHANGE || lParam == IntPtr.Zero)
        {
            return null;
        }

        var change = (uint)wParam.ToInt64();
        if (change is not (NativeMethods.DBT_DEVICEARRIVAL or NativeMethods.DBT_DEVICEREMOVECOMPLETE))
        {
            return null;
        }

        // Keep the window procedure fast: parse + publish is cheap, but never do device I/O here.
        Interlocked.Increment(ref _pendingEvents);
        try
        {
            var headerSize = Marshal.SizeOf<NativeMethods.DevBroadcastDeviceInterface>();
            var header = Marshal.PtrToStructure<NativeMethods.DevBroadcastDeviceInterface>(lParam);
            if (header.dbcc_devicetype != NativeMethods.DBT_DEVTYP_DEVICEINTERFACE)
            {
                return null;
            }

            var namePointer = IntPtr.Add(lParam, headerSize);
            var devicePath = Marshal.PtrToStringUni(namePointer);
            var entry = UsbDevicePathParser.TryCreateEntry(devicePath,
                change == NativeMethods.DBT_DEVICEARRIVAL, TimeProvider.GetUtcNow());
            if (entry is null)
            {
                return null;
            }

            var snapshot = _history.Add(entry);
            Publish(snapshot);
            Counters.IncrementDeviceEvents();
            Logger.LogInfo(PlatformLogCategory.Monitor, "usb.device-event", "USB device change captured.",
                new Dictionary<string, object?>
                {
                    ["change"] = entry.Change.ToString(),
                    ["name"] = entry.Name,
                });
        }
        catch (Exception ex)
        {
            Counters.IncrementMonitorCallbackFailures();
            Logger.LogError(PlatformLogCategory.Monitor, "usb.event-failed",
                "Parsing a USB device change failed.", ex);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingEvents);
        }

        return IntPtr.Zero;
    }

    protected override async ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        var registration = _registration;
        _registration = null;
        var window = _window;
        _window = null;

        if (registration is not null)
        {
            try
            {
                await _host.InvokeAsync(() =>
                {
                    registration.Dispose();
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Monitor, "usb.unregister-failed",
                    "Failed to unregister USB device notifications.", ex);
            }
        }

        if (window is not null)
        {
            await window.DisposeAsync().ConfigureAwait(false);
        }

        _history.Clear();
        Logger.LogInfo(PlatformLogCategory.Monitor, "usb.stopped", "USB device notification monitor stopped.");
    }
}
