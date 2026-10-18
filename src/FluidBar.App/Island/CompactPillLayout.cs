using Avalonia;

namespace FluidBar.App.Island;

/// <summary>
/// Shared slot geometry for compact pills with a leading icon, a single-line text column and an
/// optional trailing chip. The icon owns a fixed square slot, the gap is fixed, and the text column is
/// allowed to shrink to zero (<c>min-width: 0</c>) so a long string can never push the icon or chip
/// outside the pill. Content is clipped to the pill bounds by the surfaces that use this layout.
/// </summary>
public static class CompactPillLayout
{
    /// <summary>Horizontal inset from the pill edge to the icon/chip (DIP).</summary>
    public const double Inset = 12;

    /// <summary>Fixed leading icon slot (DIP).</summary>
    public const double IconSlot = 20;

    /// <summary>Fixed gap between the icon slot and the text column (DIP).</summary>
    public const double IconGap = 8;

    /// <summary>Fixed gap between the text column and a trailing chip (DIP).</summary>
    public const double ChipGap = 8;

    /// <summary>Trailing chip height (DIP).</summary>
    public const double ChipHeight = 20;

    /// <summary>Fixed leading icon slot for a pill of <paramref name="height"/> DIP.</summary>
    public static Rect IconRect(double height)
        => new(Inset, (height - IconSlot) / 2, IconSlot, IconSlot);

    /// <summary>
    /// Text column between the icon slot and the trailing chip (or right inset). Width is clamped to
    /// zero so the column can shrink without moving the icon or the chip.
    /// </summary>
    public static Rect TextColumn(double width, double height, double chipWidth = 0)
    {
        var left = Inset + IconSlot + IconGap;
        var right = width - Inset - (chipWidth > 0 ? chipWidth + ChipGap : 0);
        return new Rect(left, 0, Math.Max(0, right - left), height);
    }

    /// <summary>Trailing chip rectangle for a pill of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static Rect ChipRect(double width, double height, double chipWidth)
        => new(width - Inset - chipWidth, (height - ChipHeight) / 2, chipWidth, ChipHeight);

    /// <summary>Full pill bounds (the clip rectangle for surface content).</summary>
    public static Rect Bounds(double width, double height) => new(0, 0, width, height);
}
