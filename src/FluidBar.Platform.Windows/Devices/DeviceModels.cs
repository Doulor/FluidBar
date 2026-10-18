using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FluidBar.Platform.Windows.Devices;

/// <summary>Device class shown in the island activity.</summary>
public enum DeviceKind
{
    Unknown,
    Usb,
    Bluetooth,
}

/// <summary>What happened to the device.</summary>
public enum DeviceChangeKind
{
    Connected,
    Disconnected,
}

/// <summary>Immutable device event.</summary>
public sealed record DeviceEventEntry(
    string DeviceId,
    DeviceKind Kind,
    DeviceChangeKind Change,
    string Name,
    DateTimeOffset Timestamp);

/// <summary>Immutable bounded snapshot of recent device events (newest first).</summary>
public sealed record DeviceEventSnapshot(ImmutableArray<DeviceEventEntry> Recent)
{
    public static readonly DeviceEventSnapshot Empty = new(ImmutableArray<DeviceEventEntry>.Empty);

    public DeviceEventEntry? Latest => Recent.IsEmpty ? null : Recent[0];
}

/// <summary>Pure bounded device-event history (consecutive duplicate suppression).</summary>
public sealed class DeviceEventHistory
{
    private readonly object _gate = new();
    private readonly LinkedList<DeviceEventEntry> _entries = new();

    public DeviceEventHistory(int maxEntries = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        MaxEntries = maxEntries;
    }

    public int MaxEntries { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public long Evictions { get; private set; }

    /// <summary>Adds an entry unless it repeats the newest one (same id + change).</summary>
    public DeviceEventSnapshot Add(DeviceEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            var latest = _entries.First?.Value;
            if (latest is not null
                && string.Equals(latest.DeviceId, entry.DeviceId, StringComparison.Ordinal)
                && latest.Change == entry.Change)
            {
                return BuildSnapshotLocked();
            }

            _entries.AddFirst(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
                Evictions++;
            }

            return BuildSnapshotLocked();
        }
    }

    public DeviceEventSnapshot Snapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotLocked();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private DeviceEventSnapshot BuildSnapshotLocked()
    {
        if (_entries.Count == 0)
        {
            return DeviceEventSnapshot.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<DeviceEventEntry>(_entries.Count);
        foreach (var entry in _entries)
        {
            builder.Add(entry);
        }

        return new DeviceEventSnapshot(builder.MoveToImmutable());
    }
}

/// <summary>
/// Pure parser for the device-interface path returned by <c>WM_DEVICECHANGE</c>
/// (<c>\\?\USB#VID_046D&amp;PID_C52B#serial#{guid}</c>). Extracts vendor/product/serial and a compact
/// display name without touching the device (no WMI, no handle opens).
/// </summary>
public static class UsbDevicePathParser
{
    private static readonly Regex UsbPattern = new(
        @"USB#VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4})#(?<serial>[^#]*)#",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses vendor/product ids and serial from a USB device interface path.</summary>
    public static (string? VendorId, string? ProductId, string? Serial) Parse(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return (null, null, null);
        }

        var match = UsbPattern.Match(devicePath);
        if (!match.Success)
        {
            return (null, null, null);
        }

        return (match.Groups["vid"].Value.ToUpperInvariant(),
            match.Groups["pid"].Value.ToUpperInvariant(),
            match.Groups["serial"].Value);
    }

    /// <summary>Compact, non-localized device name: <c>USB 046D:C52B</c> (falls back to the last path token).</summary>
    public static string FormatFriendlyName(string? devicePath)
    {
        var (vendor, product, _) = Parse(devicePath);
        if (vendor is not null && product is not null)
        {
            return $"USB {vendor}:{product}";
        }

        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "USB device";
        }

        var token = devicePath.TrimStart('\\', '?', '.').Split('#')[0];
        return string.IsNullOrWhiteSpace(token) ? "USB device" : token;
    }

    /// <summary>Builds a device event from a raw interface path, or null when the path is unusable.</summary>
    public static DeviceEventEntry? TryCreateEntry(string? devicePath, bool connected, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return null;
        }

        return new DeviceEventEntry(devicePath, DeviceKind.Usb,
            connected ? DeviceChangeKind.Connected : DeviceChangeKind.Disconnected,
            FormatFriendlyName(devicePath), timestamp);
    }
}
