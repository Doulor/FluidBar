using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FluidBar.Platform.Windows.Agent;

/// <summary>
/// Best-effort activation of the terminal window that owns an agent session. Agent providers are
/// read-only and do not expose an HWND, so the row is matched by its session title (preferred) or
/// project leaf name. If the match is not unique the action refuses rather than focusing the wrong
/// window; the caller falls back to its deferred-action path.
/// </summary>
public static class AgentWindowFocus
{
    private const int SwRestore = 9;

    private static readonly HashSet<string> EligibleProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "wt", "OpenConsole", "conhost", "powershell", "pwsh", "cmd",
        "Code", "Code - Insiders", "devenv", "idea64", "pycharm64", "Cursor", "Windsurf", "Alacritty",
        "wezterm-gui", "Tabby", "Hyper", "mintty", "MobaXterm", "zcode", "Qoder",
        "WorkBuddy", "claude", "codex",
    };

    /// <summary>Attempts to focus a single visible top-level window matching the row.</summary>
    /// <param name="sessionTitle">Provider session title (may contain the project/AI task name).</param>
    /// <param name="project">Project/workspace leaf name.</param>
    /// <returns>True when exactly one matching window was restored and activated.</returns>
    public static bool TryActivate(string? sessionTitle, string? project)
    {
        var hints = new[] { sessionTitle?.Trim(), project?.Trim() }
            .Where(hint => !string.IsNullOrWhiteSpace(hint))
            .Select(hint => hint!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (hints.Length == 0)
        {
            return false;
        }

        var matches = new List<IntPtr>(2);
        var currentPid = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) <= 0)
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == currentPid || !IsEligibleProcess(pid))
            {
                return true;
            }

            var title = new StringBuilder(512);
            if (GetWindowText(hwnd, title, title.Capacity) <= 0)
            {
                return true;
            }

            if (hints.Any(hint => title.ToString().Contains(hint, StringComparison.OrdinalIgnoreCase)))
            {
                matches.Add(hwnd);
                if (matches.Count > 1)
                {
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        if (matches.Count != 1)
        {
            return false;
        }

        ShowWindow(matches[0], SwRestore);
        return SetForegroundWindow(matches[0]);
    }

    /// <summary>True when the process is a terminal/editor that can own an agent session.</summary>
    public static bool IsEligibleProcessName(string? processName)
        => !string.IsNullOrWhiteSpace(processName) && EligibleProcesses.Contains(processName);

    private static bool IsEligibleProcess(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return IsEligibleProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Pure title matcher exposed for tests.</summary>
    public static bool Matches(string title, string? sessionTitle, string? project)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return (!string.IsNullOrWhiteSpace(sessionTitle)
                && title.Contains(sessionTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(project)
                && title.Contains(project.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
