using FluidBar.Core.Layout;
using FluidBar.Island;

namespace FluidBar.App.Hosting.Skia;

/// <summary>
/// Hover band that reveals the preview rail when the pointer approaches it: the transparent strip
/// between the expanded shell's bottom edge and the rail's bottom, widened by a horizontal margin and
/// clamped to the host surface. It exists only while the island is expanded (the prototype has no
/// approach-reveal at all, and a collapsed pill has no rail to show), and it is deliberately derived
/// from the same geometry the rail paints, so the hook's watch rectangle and the controller's hit test
/// can never disagree.
/// </summary>
public static class ArchipelagoHoverBand
{
    /// <summary>Horizontal margin around the expanded shell in DIP (fills the host surface: 440 + 2×24 = 488).</summary>
    public const double HorizontalMarginDip = 24;

    /// <summary>Margin below the rail's bottom edge in DIP, so a fast pointer cannot skip the band.</summary>
    public const double BottomMarginDip = 8;

    /// <summary>
    /// Band rectangle in host-local DIP, or <see cref="VisualRect.Empty"/> when the island is not
    /// expanded/visible (compact, hidden, or the rail host option is disabled).
    /// </summary>
    /// <param name="state">Current render state.</param>
    public static VisualRect For(in IslandRenderState state)
    {
        if (!state.IsVisible || state.Form != ShellForm.Expanded)
        {
            return VisualRect.Empty;
        }

        var host = HostSurface.ForMode(state.Mode);
        var shell = IslandMetrics.ShellRect(state);
        var stripTop = host.HasDockStrip
            ? host.ArchipelagoTopLocal
            : shell.Bottom + IslandMetrics.StripGap;
        var stripBottom = stripTop + IslandMetrics.StripHeight;

        var left = Math.Max(0, shell.Left - HorizontalMarginDip);
        var right = Math.Min(host.Width, shell.Right + HorizontalMarginDip);
        var top = shell.Bottom;
        var bottom = Math.Min(host.Height, stripBottom + BottomMarginDip);
        return right > left && bottom > top
            ? new VisualRect(left, top, right - left, bottom - top)
            : VisualRect.Empty;
    }
}
