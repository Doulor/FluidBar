using SkiaSharp;

namespace FluidBar.Island;

/// <summary>
/// Geometry for the in-card left page rail. The 472 DIP shell reserves 10 + 40 + 10 DIP at the
/// left; the 400 DIP page content starts at x=60. Six 40×40 tabs with 4 DIP gaps fill the
/// 260 DIP content height exactly. Paint, pointer hit-testing and hover use this one layout.
/// </summary>
public static class SideRailLayout
{
    public const float TabWidth = 40;
    public const float TabHeight = 40;
    public const float TabGap = 4;
    public const float TabPitch = TabHeight + TabGap;
    public const float LeftOffset = 50;

    /// <summary>Tab rectangle in host-local DIP for a page index.</summary>
    public static SKRect TabRect(SKRect content, int index) => new(
        content.Left - LeftOffset,
        content.Top + (index * TabPitch),
        content.Left - LeftOffset + TabWidth,
        content.Top + (index * TabPitch) + TabHeight);

    /// <summary>Full rail envelope (40×260), useful for diagnostics/hit regions.</summary>
    public static SKRect RailRect(SKRect content, int pageCount) => new(
        content.Left - LeftOffset,
        content.Top,
        content.Left - LeftOffset + TabWidth,
        content.Top + (Math.Max(1, pageCount) * TabPitch) - TabGap);

    /// <summary>Page index under a point, or -1.</summary>
    public static int HitTest(SKRect content, int pageCount, float x, float y)
    {
        for (var i = 0; i < Math.Max(1, pageCount); i++)
        {
            if (TabRect(content, i).Contains(x, y))
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
