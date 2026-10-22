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

    /// <summary>Returns the parsed artist and title, or null when no suitable window is found.</summary>
    public static (string Artist, string Title)? TryRead(int processId)
    {
        var handle = FindMainWindow(processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

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

        return parts.Length switch
        {
            >= 2 => (string.Join(" - ", parts[..^1]), parts[^1]),
            1 => (string.Empty, parts[0]),
            _ => null,
        };
    }

    private static IntPtr FindMainWindow(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return process.MainWindowHandle;
            }
        }
        catch (ArgumentException)
        {
            return IntPtr.Zero;
        }

        // Fall back to the first visible top-level window owned by the process.
        var found = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == processId && IsWindowVisible(handle) && GetWindowTextLength(handle) > 0)
            {
                found = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
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
