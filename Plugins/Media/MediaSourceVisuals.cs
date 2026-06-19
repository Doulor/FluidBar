using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FluidBar;

public static class MediaSourceVisuals
{
    public static IReadOnlyList<string> ProcessNamesForSource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return Array.Empty<string>();

        var lower = sourceId.ToLowerInvariant();
        if (lower.Contains("kugou") || lower.Contains("酷狗")) return ["KuGou", "kugou", "KGMusic", "KuGouMusic"];
        if (lower.Contains("cloudmusic") || lower.Contains("netease") || lower.Contains("网易云")) return ["cloudmusic"];
        if (lower.Contains("qqmusic") || lower.Contains("qq音乐") || lower.Contains("qq 音乐")) return ["qqmusic"];
        if (lower.Contains("spotify")) return ["spotify"];
        if (lower.Contains("chrome")) return ["chrome"];
        if (lower.Contains("edge") || lower.Contains("msedge")) return ["msedge"];
        if (lower.Contains("firefox")) return ["firefox"];
        if (lower.Contains("kwmusic") || lower.Contains("酷我")) return ["kwmusic"];
        return Array.Empty<string>();
    }

    public static string? ExtractAppIconPath(string sourceId)
    {
        try
        {
            var procNames = ProcessNamesForSource(sourceId);
            if (procNames.Count == 0)
                return null;

            var allProcs = new List<Process>();
            foreach (var name in procNames)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(name))
                        allProcs.Add(process);
                }
                catch { }
            }

            if (allProcs.Count == 0)
                return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "FluidBar", "icons");
            Directory.CreateDirectory(tempDir);
            var outPath = Path.Combine(tempDir, procNames[0] + ".png");
            if (File.Exists(outPath))
                return outPath;

            var icon = TryExtractWindowIcon(allProcs) ?? TryExtractExeIcon(allProcs);
            if (icon is null)
                return null;

            using (icon)
            using (var bmp = icon.ToBitmap())
            {
                bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            return outPath;
        }
        catch
        {
            return null;
        }
    }

    private static Icon? TryExtractWindowIcon(IReadOnlyList<Process> processes)
    {
        var procIds = new HashSet<uint>(processes.Select(process => (uint)process.Id));
        var foundHwnd = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out var pid);
            if (!procIds.Contains(pid))
                return true;

            foundHwnd = hWnd;
            return false;
        }, IntPtr.Zero);

        if (foundHwnd == IntPtr.Zero)
            return null;

        var hIcon = SendMessage(foundHwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
            hIcon = SendMessage(foundHwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
        if (hIcon == IntPtr.Zero)
            hIcon = GetClassLong(foundHwnd, GCL_HICON);
        if (hIcon == IntPtr.Zero)
            hIcon = GetClassLong(foundHwnd, GCL_HICONSM);

        return hIcon == IntPtr.Zero ? null : Icon.FromHandle(hIcon);
    }

    private static Icon? TryExtractExeIcon(IReadOnlyList<Process> processes)
    {
        foreach (var process in processes)
        {
            try
            {
                var exePath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                    return Icon.ExtractAssociatedIcon(exePath);
            }
            catch { }
        }

        return null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClassLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint WM_GETICON = 0x007F;
    private const int ICON_BIG = 1;
    private const int ICON_SMALL = 0;
    private const int GCL_HICON = -14;
    private const int GCL_HICONSM = -34;
}
