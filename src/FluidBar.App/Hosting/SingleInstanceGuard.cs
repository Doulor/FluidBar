using System.Runtime.InteropServices;

namespace FluidBar.App.Hosting;

/// <summary>
/// One island per user session. Without this guard a second launch creates a second layered window,
/// a second WH_MOUSE_LL hook and a second tray icon, and both instances fight over settings.json.
/// The guard also nudges the running instance to open its settings window (the usual reason a user
/// launches the exe again).
/// </summary>
internal static class SingleInstanceGuard
{
    private const string MutexName = @"Local\FluidBar.SingleInstance";

    private static Mutex? _mutex;

    /// <summary>Acquires the per-session mutex. Returns false when another instance already owns it.</summary>
    public static bool TryAcquire()
    {
        // Development/acceptance runs often need a second instance while the user's build is open.
        // Production never sets this variable.
        if (string.Equals(Environment.GetEnvironmentVariable("FLUIDBAR_ALLOW_MULTI_INSTANCE"), "1",
                StringComparison.Ordinal))
        {
            return true;
        }

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>Asks the already-running instance to show its settings window.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            var hwnd = FindWindowW(FluidBar.Platform.Windows.Hosting.LayeredWindowHost.WindowClassName, null);
            if (hwnd != IntPtr.Zero)
            {
                PostMessageW(hwnd, FluidBar.Platform.Windows.Hosting.LayeredWindowHost.ActivateMessage,
                    IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception)
        {
            // Best effort: a duplicate launch must still exit cleanly if the running instance is the
            // Avalonia fallback (no layered window) or is shutting down.
        }
    }

    /// <summary>Releases the mutex (process exit also releases it).</summary>
    public static void Release()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owner (already released or never acquired); nothing to do.
        }
        finally
        {
            mutex.Dispose();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
