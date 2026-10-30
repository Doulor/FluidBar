using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Geometry for the in-card page rail. The 472 DIP shell reserves 10 + 40 + 10 DIP on the
/// configured side; the 400 DIP page content starts at x=60 (left rail) or x=12 (right rail).
/// Six 40×40 tabs with 4 DIP gaps fill the 260 DIP content height exactly; extra pages use a
/// follow offset only when the selected tab would leave the card. Paint, hit-testing, hover and
/// follow use this one layout.
/// </summary>
public static class SideRailLayout
{
    public const float TabWidth = 40;
    public const float TabGap = 4;
    public const float LeftOffset = 50;
    public const float TabHeight = 40;
    public const float TabPitch = TabHeight + TabGap;

    /// <summary>Follow offset in DIP applied to every tab when the selected tab would leave the card.</summary>
    public static float ClampScroll(double offset, int pageCount, SKRect content)
        => (float)Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, MaxScroll(pageCount, content));

    /// <summary>How far the tab strip exceeds the card body.</summary>
    public static float MaxScroll(int pageCount, SKRect content)
        => Math.Max(0, (Math.Max(1, pageCount) * TabPitch) - TabGap - content.Height);

    /// <summary>X coordinate of the rail for a side.</summary>
    public static float RailLeft(SKRect content, PageRailSide side)
        => side == PageRailSide.Right
            ? content.Right + LeftOffset - TabWidth
            : content.Left - LeftOffset;

    /// <summary>Tab rectangle in host-local DIP for a page index.</summary>
    public static SKRect TabRect(SKRect content, int index, PageRailSide side = PageRailSide.Left)
    {
        var left = RailLeft(content, side);
        return new SKRect(left, content.Top + (index * TabPitch),
            left + TabWidth, content.Top + (index * TabPitch) + TabHeight);
    }

    /// <summary>Tab rectangle with the rail's current scroll offset applied.</summary>
    public static SKRect TabRect(SKRect content, int index, double scrollOffset, int pageCount, PageRailSide side = PageRailSide.Left)
    {
        var rect = TabRect(content, index, side);
        rect.Offset(0, -ClampScroll(scrollOffset, pageCount, content));
        return rect;
    }

    /// <summary>Full rail envelope (40×260), useful for diagnostics/hit regions.</summary>
    public static SKRect RailRect(SKRect content, int pageCount, PageRailSide side = PageRailSide.Left)
    {
        var left = RailLeft(content, side);
        return new SKRect(left, content.Top, left + TabWidth,
            content.Top + (Math.Max(1, pageCount) * TabPitch) - TabGap);
    }

    /// <summary>
    /// Visible rail band: enough X room for the active marker/tooltip, clipped to the card body's Y.
    /// </summary>
    public static SKRect VisibleRect(SKRect content, int pageCount, PageRailSide side = PageRailSide.Left)
    {
        var rail = RailRect(content, pageCount, side);
        return side == PageRailSide.Right
            ? new SKRect(content.Left, content.Top, rail.Right + 6, content.Bottom)
            : new SKRect(rail.Left - 6, content.Top, content.Right, content.Bottom);
    }

    /// <summary>Page index under a point, or -1.</summary>
    public static int HitTest(SKRect content, int pageCount, float x, float y, double scrollOffset = 0,
        PageRailSide side = PageRailSide.Left)
    {
        // Tabs scrolled outside the card body are not clickable. The rail lives left of the
        // content rect, so only the vertical card bounds apply here.
        if (y < content.Top || y > content.Bottom)
        {
            return -1;
        }

        for (var i = 0; i < Math.Max(1, pageCount); i++)
        {
            if (TabRect(content, i, scrollOffset, pageCount, side).Contains(x, y))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Stable icon identity for a page slot.</summary>
    public static IconKind IconFor(PageSlotKind kind) => kind switch
    {
        PageSlotKind.Home => IconKind.Home,
        PageSlotKind.Media => IconKind.Media,
        PageSlotKind.Notifications => IconKind.Bell,
        PageSlotKind.Clipboard => IconKind.Clipboard,
        PageSlotKind.Agent => IconKind.Agent,
        _ => IconKind.Grid,
    };
}
