using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FluidBar.Platform.Windows.Media;

/// <summary>
/// Reads the player's window caption, which changes the instant a new track starts.
/// </summary>
/// <remarks>
/// The accessibility clock text is repainted only every ~30 s, so a track change was detected up to seven
/// seconds late and the new song then started from zero at the detection moment - i.e. permanently behind
/// (user report 2026-10-22: "按下一首要五六秒才跳，跳完还慢五六秒"). The caption of the KuGou window
/// changes immediately and reads "artist - song - 酷狗音乐", so it is both the fastest change signal and a
/// usable artist/title pair.
/// </remarks>
internal static class KugouWindowTitle
{
    private const string PlayerSuffix = "酷狗音乐";

    /// <summary>
    /// Captions that are player UI verbs, not track names. KuGou's window text briefly reads "下一首"
    /// while a switch is in flight; publishing that as the title put the literal words "下一首" in the
    /// island for several seconds (user report 2026-10-22).
    /// </summary>
    private static readonly HashSet<string> BlockedCaptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "下一首", "上一首", "播放", "暂停", "停止", "继续", "快进", "快退", "静音", "取消静音",
        "音量", "随机播放", "顺序播放", "单曲循环", "列表循环", "酷狗音乐", "酷狗",
    };

    /// <summary>True when the caption is a player command rather than a track name.</summary>
    public static bool IsPlayerCommand(string? title)
        => !string.IsNullOrWhiteSpace(title) && BlockedCaptions.Contains(title.Trim());

    /// <summary>
    /// Returns the parsed artist and title, or null when no suitable window is found.
    /// </summary>
    /// <remarks>
    /// Every top-level window of the process is inspected, not just the main one: during a switch KuGou's
    /// main caption reads "下一首" for up to eleven seconds (measured 2026-10-22) while the mini bar already
    /// carries the new track name, so taking the first window that parses as a real track is what makes the
    /// island switch instantly without ever showing the command verb.
    /// </remarks>
    public static (string Artist, string Title)? TryRead(int processId)
    {
        (string Artist, string Title)? firstTrack = null;
        foreach (var handle in EnumerateWindows(processId))
        {
            var parsed = TryReadFrom(handle);
            if (parsed is not { Title.Length: > 0 } candidate)
            {
                continue;
            }

            if (candidate.Artist.Length > 0)
            {
                return candidate;
            }

            firstTrack ??= candidate;
        }

        return firstTrack;
    }

    /// <summary>Parses one window's caption, or null when it is not a track.</summary>
    private static (string Artist, string Title)? TryReadFrom(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new StringBuilder(length + 2);
        if (GetWindowText(handle, buffer, buffer.Capacity) <= 0)
        {
            return null;
        }

        var caption = buffer.ToString().Trim();
        if (caption.Length == 0)
        {
            return null;
        }

        // Strip the trailing " - 酷狗音乐" (and any similar product suffix) so only "artist - song" remains.
        var parts = caption.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 && parts[^1].Contains(PlayerSuffix, StringComparison.Ordinal))
        {
            parts = parts[..^1];
        }

        // The caption briefly degrades to just the product name while the next song loads; that is not a
        // track, and treating it as one made the island switch to a fake entry (2026-10-22).
        if (parts.Length == 0 || (parts.Length == 1 && (parts[0].Contains(PlayerSuffix, StringComparison.Ordinal) || parts[0].Length < 2)))
        {
            return null;
        }

        var parsedTitle = parts.Length == 1 ? parts[0] : parts[^1];
        if (IsPlayerCommand(parsedTitle))
        {
            return null;
        }

        return parts.Length == 1
            ? (string.Empty, parsedTitle)
            : (string.Join(" - ", parts[..^1]), parsedTitle);
    }

    /// <summary>Top-level windows of the process that carry a caption, main window first.</summary>
    private static List<IntPtr> EnumerateWindows(int processId)
    {
        var windows = new List<IntPtr>(8);
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                windows.Add(process.MainWindowHandle);
            }
        }
        catch (ArgumentException)
        {
            return windows;
        }

        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == processId && !windows.Contains(handle) && GetWindowTextLength(handle) > 0)
            {
                windows.Add(handle);
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);
}
