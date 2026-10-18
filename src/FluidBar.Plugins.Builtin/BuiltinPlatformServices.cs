using FluidBar.Platform.Windows.Agent;
using FluidBar.Platform.Windows.Clipboard;
using FluidBar.Platform.Windows.Devices;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Plugins.Builtin;

/// <summary>
/// Platform services injected into the first-party plugins. Only the interfaces a plugin needs are
/// non-null; the catalog skips a plugin when its required dependency is missing (e.g. no Bluetooth radio
/// still yields a device plugin because USB is available).
/// </summary>
public sealed class BuiltinPlatformServices
{
    /// <summary>GSMTC media source (event-driven snapshots + transport commands).</summary>
    public IMediaSessionSource? Media { get; init; }

    /// <summary>Optional bounded lyrics lookup (lyrics remain owned/parsed by the platform layer).</summary>
    public ILyricsLookup? Lyrics { get; init; }

    /// <summary>Bounded artwork temp store used to expose an <c>AlbumArtPath</c> in the SDK payload.</summary>
    public ArtworkTempStore? ArtworkTempStore { get; init; }

    /// <summary>Clipboard listener source.</summary>
    public IClipboardSource? Clipboard { get; init; }

    /// <summary>Windows notification listener source.</summary>
    public INotificationSource? Notifications { get; init; }

    /// <summary>Agent inbox snapshots (hook-written JSON files).</summary>
    public IAgentInboxSource? AgentInbox { get; init; }

    /// <summary>Explicit, consent-gated hook installer (never called automatically).</summary>
    public IAgentHookInstaller? AgentHookInstaller { get; init; }

    /// <summary>USB device notifications.</summary>
    public IDeviceEventSource? UsbDevices { get; init; }

    /// <summary>Bluetooth device changes.</summary>
    public IDeviceEventSource? BluetoothDevices { get; init; }
}
