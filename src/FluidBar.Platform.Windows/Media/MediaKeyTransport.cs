using System.Runtime.InteropServices;

namespace FluidBar.Platform.Windows.Media;

/// <summary>System-wide media keys used to drive players that expose no SMTC session.</summary>
public enum MediaKeyCommand
{
    /// <summary>Play/pause toggle (VK_MEDIA_PLAY_PAUSE).</summary>
    TogglePlayPause,

    /// <summary>Next track (VK_MEDIA_NEXT_TRACK).</summary>
    NextTrack,

    /// <summary>Previous track (VK_MEDIA_PREV_TRACK).</summary>
    PreviousTrack,
}

/// <summary>Injects one media key press into the system input queue.</summary>
public interface IMediaKeySender
{
    /// <summary>Presses the media key. Returns false when the input queue rejected it.</summary>
    bool TrySend(MediaKeyCommand command);

    /// <summary>Human-readable reason the last send failed, or null on success.</summary>
    string? LastFailureReason { get; }
}

/// <summary>
/// Sends VK_MEDIA_* through <c>SendInput</c>.
/// </summary>
/// <remarks>
/// <para>Media keys are the last transport channel a non-SMTC player might still answer: Windows
/// delivers them through the registered-hotkey path, so a player can receive them while it is not the
/// foreground window.</para>
/// <para>They are not the primary fallback channel because another process may own the registration
/// (on the reference machine VK_MEDIA_* is registered by a different app, and KuGou ignored the key
/// entirely) and because <c>SendInput</c> is unavailable on a locked workstation. The accessibility
/// transport is tried first; this sender stays as the generic last resort and reports failure honestly
/// instead of faking a success.</para>
/// </remarks>
public sealed class SystemMediaKeySender : IMediaKeySender
{
    private const ushort VkMediaNextTrack = 0xB0;
    private const ushort VkMediaPrevTrack = 0xB1;
    private const ushort VkMediaPlayPause = 0xB3;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 2;

    /// <inheritdoc />
    public string? LastFailureReason { get; private set; }

    /// <inheritdoc />
    public bool TrySend(MediaKeyCommand command)
    {
        var virtualKey = command switch
        {
            MediaKeyCommand.NextTrack => VkMediaNextTrack,
            MediaKeyCommand.PreviousTrack => VkMediaPrevTrack,
            _ => VkMediaPlayPause,
        };

        var inputs = new INPUT[2];
        inputs[0].Type = InputKeyboard;
        inputs[0].Data.Keyboard.VirtualKey = virtualKey;
        inputs[1].Type = InputKeyboard;
        inputs[1].Data.Keyboard.VirtualKey = virtualKey;
        inputs[1].Data.Keyboard.Flags = KeyEventKeyUp;

        var size = Marshal.SizeOf<INPUT>();
        var sent = SendInput((uint)inputs.Length, inputs, size);
        if (sent == inputs.Length)
        {
            LastFailureReason = null;
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        LastFailureReason = sent == 0
            ? $"SendInput rejected the media key (win32 error {error}); an interactive, unlocked session is required."
            : $"SendInput injected {sent}/{inputs.Length} media-key events (win32 error {error}).";
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParamL;
        public ushort ParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion Data;
    }
}

/// <summary>Where a transport command should go.</summary>
public enum TransportRoute
{
    /// <summary>The active GSMTC session owns the command.</summary>
    Session,

    /// <summary>No session: press the player's own button through its accessibility tree.</summary>
    Accessibility,

    /// <summary>No session and no accessibility tree: press the system media key.</summary>
    MediaKey,

    /// <summary>Nothing can handle the command right now.</summary>
    Unavailable,
}

/// <summary>Routing decision for one transport command.</summary>
/// <param name="Route">Chosen channel.</param>
/// <param name="Reason">Why the channel was chosen / why the command is unavailable.</param>
public readonly record struct TransportRouteDecision(TransportRoute Route, string Reason)
{
    /// <summary>True when the command can actually be executed.</summary>
    public bool IsExecutable => Route != TransportRoute.Unavailable;
}

/// <summary>
/// Pure routing policy for transport commands: SMTC first, then the player's accessibility tree, then
/// the generic media keys. Kept free of Win32/COM so every branch is unit-tested.
/// </summary>
public static class FallbackTransportPolicy
{
    /// <summary>Chooses the transport channel for the current media state.</summary>
    /// <param name="hasSession">A GSMTC session is attached.</param>
    /// <param name="fallbackActive">The window-title fallback currently publishes the snapshot.</param>
    /// <param name="accessibilityAvailable">
    /// The fallback player exposes an accessibility tree whose transport buttons can be invoked. Preferred
    /// over media keys: it targets one player directly (no global key another app may own) and works
    /// without an interactive input desktop.
    /// </param>
    /// <param name="senderAvailable">A media-key sender is wired up.</param>
    public static TransportRouteDecision Decide(bool hasSession, bool fallbackActive, bool accessibilityAvailable,
        bool senderAvailable)
    {
        if (hasSession)
        {
            return new TransportRouteDecision(TransportRoute.Session,
                "The active GSMTC session handles transport commands.");
        }

        if (fallbackActive && accessibilityAvailable)
        {
            return new TransportRouteDecision(TransportRoute.Accessibility,
                "No SMTC session; the command is pressed through the player's accessibility tree.");
        }

        if (fallbackActive && senderAvailable)
        {
            return new TransportRouteDecision(TransportRoute.MediaKey,
                "No SMTC session and no accessibility tree; the system media key is used.");
        }

        if (fallbackActive)
        {
            return new TransportRouteDecision(TransportRoute.Unavailable,
                "No SMTC session, no accessibility tree and no media-key sender.");
        }

        return new TransportRouteDecision(TransportRoute.Unavailable,
            "No media session is active; transport commands are ignored.");
    }

    /// <summary>Overload for callers without an accessibility transport.</summary>
    public static TransportRouteDecision Decide(bool hasSession, bool fallbackActive, bool senderAvailable)
        => Decide(hasSession, fallbackActive, accessibilityAvailable: false, senderAvailable);
}
