using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FluidBar;

public enum MediaAppCommand
{
    TogglePlayPause,
    NextTrack,
    PreviousTrack
}

public enum MediaControlRoute
{
    GsmFirst,
    AppCommandFirst
}

public enum MediaControlDispatchAttempt
{
    SameSourceGsm,
    AppCommand
}

public static class MediaControlDispatchPolicy
{
    public static MediaControlRoute RouteForSource(string? sourceId) =>
        MediaAppCommandFallbackPolicy.ShouldUseForSource(sourceId)
            ? MediaControlRoute.AppCommandFirst
            : MediaControlRoute.GsmFirst;

    public static IReadOnlyList<MediaControlDispatchAttempt> DispatchAttemptsForSource(string? sourceId) =>
        RouteForSource(sourceId) == MediaControlRoute.AppCommandFirst
            ? [MediaControlDispatchAttempt.AppCommand, MediaControlDispatchAttempt.SameSourceGsm]
            : [MediaControlDispatchAttempt.SameSourceGsm];

    public static bool CanUseGeneralGsmFallback(string? sourceId) =>
        MediaSnapshotSelectionPolicy.GetSourcePriority(sourceId) < 100;

    public static bool AllowsOptimisticPlaybackStateUpdate(string? sourceId) =>
        !MediaAppCommandFallbackPolicy.ShouldUseForSource(sourceId);

    public static string? ResolveControlSource(
        string? currentViewSource,
        string? activeHoverMediaSource)
    {
        return string.IsNullOrWhiteSpace(activeHoverMediaSource)
            ? currentViewSource
            : activeHoverMediaSource;
    }
}

public static class MediaAppCommandFallbackPolicy
{
    public static bool ShouldUseForSource(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;

        var lower = sourceId.ToLowerInvariant();
        return lower.Contains("kugou") ||
               lower.Contains("酷狗") ||
               lower.Contains("kgmusic");
    }
}

internal static class MediaAppCommandFallback
{
    private const uint WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    // SendInput media key codes
    private const ushort VK_MEDIA_NEXT_TRACK = 0xB0;
    private const ushort VK_MEDIA_PREV_TRACK = 0xB1;
    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static bool TrySend(string? sourceId, MediaAppCommand command)
    {
        if (!MediaAppCommandFallbackPolicy.ShouldUseForSource(sourceId))
            return false;

        // Method 1: WM_APPCOMMAND to target windows
        var targets = FindTargetWindows(sourceId);
        var appCmd = command switch
        {
            MediaAppCommand.NextTrack => APPCOMMAND_MEDIA_NEXTTRACK,
            MediaAppCommand.PreviousTrack => APPCOMMAND_MEDIA_PREVIOUSTRACK,
            _ => APPCOMMAND_MEDIA_PLAY_PAUSE
        };
        var lParam = (IntPtr)(appCmd << 16);
        foreach (var hWnd in targets)
            SendMessage(hWnd, WM_APPCOMMAND, hWnd, lParam);

        // Method 2: SendInput with system media key (works even when window is hidden)
        var vk = command switch
        {
            MediaAppCommand.NextTrack => VK_MEDIA_NEXT_TRACK,
            MediaAppCommand.PreviousTrack => VK_MEDIA_PREV_TRACK,
            _ => VK_MEDIA_PLAY_PAUSE
        };
        SendMediaKey(vk);

        return true;
    }

    private static void SendMediaKey(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].union.keyboard.wVk = vk;
        inputs[0].union.keyboard.dwFlags = 0;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].union.keyboard.wVk = vk;
        inputs[1].union.keyboard.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static IReadOnlyList<IntPtr> FindTargetWindows(string? sourceId)
    {
        var processNames = MediaSourceVisuals.ProcessNamesForSource(sourceId ?? "");
        if (processNames.Count == 0)
            return [];

        var processIds = new HashSet<uint>();
        foreach (var processName in processNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(processName))
                    processIds.Add((uint)process.Id);
            }
            catch { }
        }

        if (processIds.Count == 0)
            return [];

        var windows = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (!processIds.Contains(pid))
                return true;
            windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
