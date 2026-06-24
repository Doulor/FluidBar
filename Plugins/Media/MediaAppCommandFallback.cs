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
        !(sourceId?.Contains("kugou", StringComparison.OrdinalIgnoreCase) == true ||
          sourceId?.Contains("酷狗", StringComparison.Ordinal) == true);

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
    // keybd_event (system media key) for music apps that may not respond to GSMTC.
    // TrySend returns true to prevent double-toggle from GSMTC fallback.
    public static bool ShouldUseForSource(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;

        var lower = sourceId.ToLowerInvariant();
        return lower.Contains("kugou") || lower.Contains("酷狗") || lower.Contains("kgmusic") ||
               lower.Contains("cloudmusic") || lower.Contains("netease") || lower.Contains("网易云");
    }
}

internal static class MediaAppCommandFallback
{
    private const uint WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;

    // keybd_event media key codes
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

    public static bool TrySend(string? sourceId, MediaAppCommand command)
    {
        if (!MediaAppCommandFallbackPolicy.ShouldUseForSource(sourceId))
            return false;

        // Only use keybd_event for Kugou (it doesn't respond to GSMTC).
        // All other music apps (NetEase, QQ, etc.) respond to GSMTC directly.
        // Using keybd_event for them causes double-toggle (keybd_event + GSMTC both fire).
        var isKugou = sourceId?.Contains("kugou", StringComparison.OrdinalIgnoreCase) == true ||
                       sourceId?.Contains("酷狗", StringComparison.Ordinal) == true;
        if (!isKugou)
            return false;

        var vk = command switch
        {
            MediaAppCommand.NextTrack => VK_MEDIA_NEXT_TRACK,
            MediaAppCommand.PreviousTrack => VK_MEDIA_PREV_TRACK,
            _ => VK_MEDIA_PLAY_PAUSE
        };

        keybd_event(vk, 0, 0, IntPtr.Zero);
        keybd_event(vk, 0, 2, IntPtr.Zero);

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

        return true;
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
