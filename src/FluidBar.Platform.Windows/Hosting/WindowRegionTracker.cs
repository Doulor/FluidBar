using FluidBar.Platform.Windows.Interop;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// Keeps a top-level window's rounded region in sync with its real size. The region must be rebuilt
/// whenever the window is resized (Avalonia creates the window, then applies its own size, then may
/// resize again on DPI change), otherwise the clip covers the wrong rectangle and the window shows a
/// square plate outside the rounded content.
/// <para>
/// Each application is recorded in <see cref="Trace"/> (size, radius, <c>SetWindowRgn</c> result and the
/// resulting region box), which is the seam tests and diagnostics use to prove the region matches the
/// window.
/// </para>
/// </summary>
public sealed class WindowRegionTracker
{
    private readonly List<string> _trace = [];

    /// <summary>Return value of the last <c>SetWindowRgn</c> call (non-zero on success).</summary>
    public int LastSetWindowRgnResult { get; private set; }

    /// <summary>Whether a region is currently applied.</summary>
    public bool HasRegion { get; private set; }

    /// <summary>The size/radius that was last applied (physical pixels).</summary>
    public (int Width, int Height, int Radius)? Applied { get; private set; }

    /// <summary>Human-readable trace of every application attempt.</summary>
    public IReadOnlyList<string> Trace => _trace;

    /// <summary>Clears the trace (diagnostics only).</summary>
    public void ResetTrace() => _trace.Clear();

    /// <summary>
    /// Rebuilds the window region when the requested geometry differs from the applied one.
    /// </summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="widthPx">Window width in physical pixels.</param>
    /// <param name="heightPx">Window height in physical pixels.</param>
    /// <param name="radiusPx">Corner radius in physical pixels.</param>
    /// <param name="error">Failure description when the call returns false.</param>
    /// <returns>True when a region matching the requested geometry is in place.</returns>
    public bool TryUpdate(IntPtr hwnd, int widthPx, int heightPx, int radiusPx, out string? error)
    {
        error = null;
        if (hwnd == IntPtr.Zero)
        {
            error = "window handle is zero";
            Record(hwnd, widthPx, heightPx, radiusPx, "skipped: no handle");
            return false;
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            error = $"window size is empty ({widthPx}x{heightPx})";
            Record(hwnd, widthPx, heightPx, radiusPx, "skipped: empty size");
            return false;
        }

        var geometry = (widthPx, heightPx, radiusPx);
        if (HasRegion && Applied == geometry)
        {
            return true;
        }

        if (!WindowCornerRegion.TryApply(hwnd, widthPx, heightPx, radiusPx, out error))
        {
            HasRegion = false;
            Applied = null;
            Record(hwnd, widthPx, heightPx, radiusPx, "failed: " + error);
            return false;
        }

        HasRegion = true;
        Applied = geometry;
        var rgnType = NativeMethods.User32.GetWindowRgnBox(hwnd, out var box);
        Record(hwnd, widthPx, heightPx, radiusPx,
            $"ok rgnType={rgnType} box={box.Left},{box.Top},{box.Right},{box.Bottom}");
        return true;
    }

    /// <summary>Removes the region (maximized windows are their own rectangle).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    public void Clear(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            _ = WindowCornerRegion.Clear(hwnd, out _);
        }

        HasRegion = false;
        Applied = null;
        Record(hwnd, 0, 0, 0, "cleared");
    }

    /// <summary>Copies the region box of a window into the trace (diagnostics).</summary>
    /// <param name="hwnd">Top-level window handle.</param>
    /// <param name="label">Trace label.</param>
    public void RecordRegionState(IntPtr hwnd, string label)
    {
        var rgnType = hwnd == IntPtr.Zero ? 0 : NativeMethods.User32.GetWindowRgnBox(hwnd, out _);
        Record(hwnd, 0, 0, 0, $"{label} rgnType={rgnType}");
    }

    private void Record(IntPtr hwnd, int width, int height, int radius, string note)
    {
        var rectText = "n/a";
        if (hwnd != IntPtr.Zero && NativeMethods.User32.GetWindowRect(hwnd, out var rect))
        {
            rectText = $"{rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
        }

        _trace.Add($"hwnd=0x{hwnd.ToInt64():X} req={width}x{height} r={radius} win={rectText} {note}");
        while (_trace.Count > 40)
        {
            _trace.RemoveAt(0);
        }

        LastSetWindowRgnResult = note.StartsWith("ok", StringComparison.Ordinal) ? 1 : LastSetWindowRgnResult;
    }
}
