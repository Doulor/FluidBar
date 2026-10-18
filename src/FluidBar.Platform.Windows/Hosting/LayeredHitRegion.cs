using FluidBar.Core.Layout;

namespace FluidBar.Platform.Windows.Hosting;

/// <summary>
/// The host's click-through policy: a squircle shell plus an explicit set of interactive rectangles
/// (detached mini-islands, buttons drawn outside the panel). Everything else — the shadow bleed, the
/// corners, the detached-island lane between the pills — is <c>HTTRANSPARENT</c>.
/// </summary>
/// <remarks>
/// <para>This is the same rule as <c>IslandWindowInterop.RegisterHitTest</c> +
/// <c>HitTestPolicy</c> (spec §5.6), but expressed in the layered host's client coordinate space and
/// extended with per-rectangle interactivity for the detached islands. The layered host owns its
/// window procedure, so it must not also be subclassed with
/// <c>IslandWindowInterop.RegisterHitTest</c>; the App keeps using that API for non-layered windows.</para>
/// <para>Coordinates are window-client DIP (the DIB is <c>clientDIP * scale</c> pixels). The region is
/// mutated only on state changes (mode/layout settle), never per frame.</para>
/// </remarks>
public sealed class LayeredHitRegion
{
    /// <summary>Maximum number of detached interactive rectangles.</summary>
    public const int MaxInteractiveRects = 32;

    private readonly VisualRect[] _rects = new VisualRect[MaxInteractiveRects];
    private int _rectCount;
    private FluidBar.Core.Layout.ShellGeometry? _shell;

    /// <summary>
    /// False when the island is hidden/collapsed: the whole window is click-through even if geometry
    /// is still set (mirrors <c>HitTestPolicy.ShouldWindowBeClickThrough</c>).
    /// </summary>
    public bool HasInteractiveContent { get; set; } = true;

    /// <summary>The shell squircle in client DIP, if any.</summary>
    public FluidBar.Core.Layout.ShellGeometry? Shell => _shell;

    /// <summary>Number of detached interactive rectangles currently set.</summary>
    public int InteractiveRectCount => _rectCount;

    /// <summary>True when no shell and no rectangles are set (everything is click-through).</summary>
    public bool IsEmpty => _shell is null && _rectCount == 0;

    /// <summary>Sets the shell squircle from a client-DIP rectangle and its corner radii.</summary>
    /// <param name="shellDip">Shell rectangle in client DIP.</param>
    /// <param name="radii">Per-corner radii in DIP.</param>
    public void SetShell(in VisualRect shellDip, CornerRadii radii)
    {
        if (shellDip.IsEmpty)
        {
            _shell = null;
            return;
        }

        _shell = FluidBar.Core.Layout.ShellGeometry.Create(shellDip, radii);
    }

    /// <summary>Sets the shell from an already-built client-DIP geometry (e.g. a sampled outline owner).</summary>
    /// <param name="geometry">Client-DIP shell geometry, or <see langword="null"/> to clear.</param>
    public void SetShell(FluidBar.Core.Layout.ShellGeometry? geometry) => _shell = geometry;

    /// <summary>Clears the shell and every detached rectangle.</summary>
    public void Clear()
    {
        _shell = null;
        _rectCount = 0;
    }

    /// <summary>
    /// Replaces the detached interactive rectangles (client DIP). Copies into a fixed buffer, so the
    /// per-settle path allocates nothing. Rectangles with no area are ignored.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">More than <see cref="MaxInteractiveRects"/> rectangles.</exception>
    public void SetInteractiveRects(ReadOnlySpan<VisualRect> rectsDip)
    {
        if (rectsDip.Length > MaxInteractiveRects)
        {
            throw new ArgumentOutOfRangeException(nameof(rectsDip), rectsDip.Length,
                $"At most {MaxInteractiveRects} detached interactive rectangles are supported.");
        }

        _rectCount = 0;
        foreach (var rect in rectsDip)
        {
            if (!rect.IsEmpty)
            {
                _rects[_rectCount++] = rect;
            }
        }
    }

    /// <summary>True when a client-DIP point is interactive (inside the squircle or a detached rectangle).</summary>
    public bool IsInteractive(double clientXDip, double clientYDip)
    {
        if (!HasInteractiveContent)
        {
            return false;
        }

        if (_shell is not null && _shell.Contains(clientXDip, clientYDip))
        {
            return true;
        }

        for (var i = 0; i < _rectCount; i++)
        {
            if (_rects[i].Contains(clientXDip, clientYDip))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a client-pixel point is interactive.</summary>
    public bool IsInteractivePixels(int clientXPx, int clientYPx, double scale)
    {
        if (scale <= 0)
        {
            return false;
        }

        return IsInteractive(clientXPx / scale, clientYPx / scale);
    }

    /// <summary>Returns the detached rectangles as a read-only view (diagnostics/tests).</summary>
    public ReadOnlySpan<VisualRect> InteractiveRects => _rects.AsSpan(0, _rectCount);
}
