using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>Result of preparing a top-level window for its first composited frame.</summary>
/// <param name="Steps">Ordered steps that ran successfully (class background first, then the window region).</param>
/// <param name="Error">Failure description of the first step that went wrong, or null.</param>
public sealed record FirstFramePreparation(IReadOnlyList<string> Steps, string? Error)
{
    /// <summary>True when every step ran.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>Platform operations used to prepare the first frame (replaceable in tests).</summary>
public interface IWindowFirstFrameOps
{
    /// <summary>Sets the window class background brush (<c>hbrBackground</c>) to a solid colour.</summary>
    bool SetClassBackground(IntPtr hwnd, uint argb, out string? failure);

    /// <summary>Clips the window to a rounded region of its current rectangle.</summary>
    bool SetWindowRegion(IntPtr hwnd, int radiusPx, out string? failure);
}

/// <summary>
/// Prepares a window before the compositor sees it for the first time.
/// <para>
/// The window is created by Avalonia (class registration included) and shown immediately, so the only
/// interception point before the first composite is <c>WM_SHOWWINDOW</c>, raised inside
/// <c>ShowWindow</c> while the window is still hidden. Two things happen there, in order:
/// </para>
/// <list type="number">
/// <item>the window class background brush (<c>GCLP_HBRBACKGROUND</c>) is replaced with the panel colour,
/// so the system erase that precedes the first paint no longer fills the rectangle with the default grey
/// plate;</item>
/// <item>the window region is set to the rounded rectangle, so the very first frame is already clipped to
/// the painted radius.</item>
/// </list>
/// </summary>
public static class WindowFirstFrame
{
    /// <summary>Step name for the class-background change.</summary>
    public const string StepClassBackground = "class-background";

    /// <summary>Step name for the rounded window region.</summary>
    public const string StepWindowRegion = "window-region";

    /// <summary>Prepares <paramref name="hwnd"/> using the real Win32 calls.</summary>
    /// <param name="hwnd">Top-level window handle (may be hidden).</param>
    /// <param name="radiusPx">Corner radius in physical pixels.</param>
    /// <param name="argb">Panel colour as 0xAARRGGBB.</param>
    /// <returns>The ordered steps that ran.</returns>
    public static FirstFramePreparation Prepare(IntPtr hwnd, int radiusPx, uint argb)
        => Prepare(hwnd, radiusPx, argb, Win32WindowFirstFrameOps.Instance);

    /// <summary>Prepare with explicit operations (used by tests to assert ordering).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="radiusPx">Corner radius in physical pixels.</param>
    /// <param name="argb">Panel colour as 0xAARRGGBB.</param>
    /// <param name="ops">Platform operations.</param>
    /// <returns>The ordered steps that ran.</returns>
    public static FirstFramePreparation Prepare(IntPtr hwnd, int radiusPx, uint argb, IWindowFirstFrameOps ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        var steps = new List<string>(2);
        if (hwnd == IntPtr.Zero)
        {
            return new FirstFramePreparation(steps, "window handle is zero");
        }

        // 1) Class background first: it is what the default erase paints before any Avalonia frame.
        if (ops.SetClassBackground(hwnd, argb, out var classError))
        {
            steps.Add(StepClassBackground);
        }
        else
        {
            return new FirstFramePreparation(steps, classError ?? "class background failed");
        }

        // 2) Then the region, so the first frame is already the rounded shape.
        if (ops.SetWindowRegion(hwnd, radiusPx, out var regionError))
        {
            steps.Add(StepWindowRegion);
            return new FirstFramePreparation(steps, null);
        }

        return new FirstFramePreparation(steps, regionError ?? "window region failed");
    }

    private sealed class Win32WindowFirstFrameOps : IWindowFirstFrameOps
    {
        public static readonly Win32WindowFirstFrameOps Instance = new();

        // Brushes created for the class background must stay alive for as long as the class uses them.
        private static readonly List<IntPtr> Brushes = new();

        public bool SetClassBackground(IntPtr hwnd, uint argb, out string? failure)
        {
            failure = null;
            var brush = NativeMethods.Gdi32.CreateSolidBrush(argb);
            if (brush == IntPtr.Zero)
            {
                failure = "CreateSolidBrush failed. " + Win32Error.Format(Win32Error.LastError);
                return false;
            }

            var previous = NativeMethods.User32.SetClassLongPtr(hwnd, NativeMethods.User32.GCLP_HBRBACKGROUND, brush);
            if (previous == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            {
                NativeMethods.Gdi32.DeleteObject(brush);
                failure = "SetClassLongPtr(GCLP_HBRBACKGROUND) failed. " + Win32Error.Format(Win32Error.LastError);
                return false;
            }

            lock (Brushes)
            {
                Brushes.Add(brush);
                // A window class is shared; keep at most the last few brushes alive.
                while (Brushes.Count > 8)
                {
                    NativeMethods.Gdi32.DeleteObject(Brushes[0]);
                    Brushes.RemoveAt(0);
                }
            }

            return true;
        }

        public bool SetWindowRegion(IntPtr hwnd, int radiusPx, out string? failure)
        {
            if (!NativeMethods.User32.GetWindowRect(hwnd, out var rect))
            {
                failure = "GetWindowRect failed. " + Win32Error.Format(Win32Error.LastError);
                return false;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                failure = $"window rect is empty ({width}x{height})";
                return false;
            }

            return WindowCornerRegion.TryApply(hwnd, width, height, radiusPx, out failure);
        }
    }
}
