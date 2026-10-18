using System.Collections.Immutable;

namespace FluidBar.Core.Layout;

/// <summary>
/// One detached mini-island slot in the Page Archipelago strip (work-area DIP). Inactive slots are
/// 48x26; the active slot is 72x30 and shows the localized page name.
/// </summary>
/// <param name="Page">Zero-based pager page index.</param>
/// <param name="Rect">Slot rectangle in work-area DIP.</param>
/// <param name="IsActive">Whether this is the active page slot.</param>
public readonly record struct ArchipelagoSlot(int Page, VisualRect Rect, bool IsActive)
{
    /// <summary>Slot width in DIP (48 or 72).</summary>
    public double Width => Rect.Width;

    /// <summary>Slot height in DIP (26 or 30).</summary>
    public double Height => Rect.Height;
}

/// <summary>
/// Pure geometry for the detached Page Archipelago: six mini-islands below the expanded shell,
/// outside the 440x284 panel. The strip envelope is 352x30 (one active 72-wide slot plus five
/// 48-wide slots, 8 DIP gaps); the active indicator follows the fractional pager position so the
/// strip reads as a continuous filmstrip rather than a row of buttons.
/// <para>
/// The strip background is <b>transparent and hit-test-through</b>; only the six mini-island pill
/// rectangles are interactive. See <see cref="ArchipelagoLayoutResult.HitTest"/> and
/// <see cref="IslandLayoutResult.HitTest"/>.
/// </para>
/// </summary>
public static class ArchipelagoLayout
{
    /// <summary>Number of mini-islands (one per pager page).</summary>
    public const int SlotCount = LayoutTokens.ArchipelagoSlotCount;

    /// <summary>Inactive mini-island width in DIP (48).</summary>
    public const double MiniWidth = LayoutTokens.ArchipelagoMiniWidth;

    /// <summary>Inactive mini-island height in DIP (26).</summary>
    public const double MiniHeight = LayoutTokens.ArchipelagoMiniHeight;

    /// <summary>Active mini-island width in DIP (72).</summary>
    public const double ActiveWidth = LayoutTokens.ArchipelagoActiveWidth;

    /// <summary>Active mini-island height in DIP (30).</summary>
    public const double ActiveHeight = LayoutTokens.ArchipelagoActiveHeight;

    /// <summary>Gap between mini-islands in DIP (8).</summary>
    public const double SlotGap = LayoutTokens.ArchipelagoSlotGap;

    /// <summary>Distance between consecutive mini-island origins in DIP (56).</summary>
    public const double SlotPitch = LayoutTokens.ArchipelagoSlotPitch;

    /// <summary>Mini-island corner radius in DIP (13).</summary>
    public const double Radius = LayoutTokens.ArchipelagoRadius;

    /// <summary>Strip envelope width in DIP (352).</summary>
    public const double TrackWidth = LayoutTokens.ArchipelagoTrackWidth;

    /// <summary>Strip envelope height in DIP (30).</summary>
    public const double StripHeight = LayoutTokens.ArchipelagoStripHeight;

    /// <summary>Bottom hot-zone height that reveals the strip in DIP (12).</summary>
    public const double HotZoneHeight = LayoutTokens.ArchipelagoHotZoneHeight;

    /// <summary>Dwell before the hot zone reveals the strip (300 ms).</summary>
    public const int RevealDelayMs = LayoutTokens.ArchipelagoRevealDelayMs;

    /// <summary>Idle time after the last activity before the strip hides (1200 ms).</summary>
    public const int HideDelayMs = LayoutTokens.ArchipelagoHideDelayMs;

    /// <summary>
    /// Computes the strip geometry for a layout result. The placement already carries the strip
    /// rectangle and host surface, so this is the overload the host should use every frame.
    /// </summary>
    /// <param name="pagePosition">Fractional pager position (0..5); clamped.</param>
    /// <param name="placement">Placement produced by <see cref="IslandLayout"/>.</param>
    /// <param name="isVisible">Whether the strip is revealed (affects hit-testing only).</param>
    public static ArchipelagoLayoutResult Calculate(
        double pagePosition,
        in WindowPlacement placement,
        bool isVisible = true)
        => Calculate(pagePosition, placement.ArchipelagoStrip, placement.HostSurface, placement.Shell, isVisible);

    /// <summary>
    /// Computes the strip geometry from explicit rectangles. Prefer the placement overload when the
    /// layout result is available.
    /// </summary>
    /// <param name="pagePosition">Fractional pager position (0..5); clamped.</param>
    /// <param name="strip">Strip envelope in work-area DIP (352x30).</param>
    /// <param name="hostSurface">Host surface that owns the strip.</param>
    /// <param name="shell">Expanded shell in work-area DIP (defines the hot zone).</param>
    /// <param name="isVisible">Whether the strip is revealed (affects hit-testing only).</param>
    public static ArchipelagoLayoutResult Calculate(
        double pagePosition,
        in VisualRect strip,
        HostSurface hostSurface,
        in VisualRect shell,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(hostSurface);
        var position = double.IsFinite(pagePosition)
            ? Math.Clamp(pagePosition, 0, SlotCount - 1)
            : 0;
        var activePage = Math.Clamp(
            (int)Math.Round(position, MidpointRounding.AwayFromZero),
            0,
            SlotCount - 1);
        var hotZone = HotZone(shell);

        if (strip.IsEmpty)
        {
            return new ArchipelagoLayoutResult
            {
                PagePosition = position,
                ActivePage = activePage,
                Strip = VisualRect.Empty,
                Slots = [],
                ActiveIndicator = VisualRect.Empty,
                HotZone = hotZone,
                FollowOffset = 0,
                HostSurface = hostSurface,
                IsVisible = isVisible,
            };
        }

        var slots = ImmutableArray.CreateBuilder<ArchipelagoSlot>(SlotCount);
        var x = strip.X;
        for (var page = 0; page < SlotCount; page++)
        {
            var active = page == activePage;
            var width = active ? ActiveWidth : MiniWidth;
            var height = active ? ActiveHeight : MiniHeight;
            var y = strip.Y + ((strip.Height - height) / 2);
            slots.Add(new ArchipelagoSlot(page, new VisualRect(x, y, width, height), active));
            x += width + SlotGap;
        }

        // Continuous follow: the active pill travels with the pager at SlotPitch DIP per page and
        // is clamped to the strip, so a 2.5-page drag puts it exactly between slots 2 and 3.
        var indicatorX = Math.Clamp(
            strip.X + (position * SlotPitch),
            strip.X,
            strip.Right - ActiveWidth);
        var indicator = new VisualRect(indicatorX, strip.Y, ActiveWidth, ActiveHeight);
        var followOffset = indicator.X - slots[activePage].Rect.X;

        return new ArchipelagoLayoutResult
        {
            PagePosition = position,
            ActivePage = activePage,
            Strip = strip,
            Slots = slots.ToImmutable(),
            ActiveIndicator = indicator,
            HotZone = hotZone,
            FollowOffset = followOffset,
            HostSurface = hostSurface,
            IsVisible = isVisible,
        };
    }

    /// <summary>
    /// Convenience overload for tests/tools: computes the expanded shell and host placement for a
    /// monitor, then the strip geometry. The host should use the placement overload instead.
    /// </summary>
    /// <param name="pagePosition">Fractional pager position (0..5); clamped.</param>
    /// <param name="hostSurface">Host surface for the mode.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="anchor">Anchor within the work area.</param>
    /// <param name="offsetY">Resolved vertical offset; NaN selects the mode default.</param>
    /// <param name="isVisible">Whether the strip is revealed (affects hit-testing only).</param>
    public static ArchipelagoLayoutResult Calculate(
        double pagePosition,
        HostSurface hostSurface,
        MonitorInfo monitor,
        IslandAnchor anchor = IslandAnchor.TopCenter,
        double offsetY = double.NaN,
        bool isVisible = true)
    {
        ArgumentNullException.ThrowIfNull(hostSurface);
        ArgumentNullException.ThrowIfNull(monitor);
        var resolvedOffset = double.IsNaN(offsetY)
            ? hostSurface.Mode == IslandMode.Dock
                ? LayoutTokens.DefaultDockOffsetY
                : LayoutTokens.DefaultFloatOffsetY
            : offsetY;
        if (hostSurface.Mode == IslandMode.Flush
            && anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight)
        {
            resolvedOffset = 0;
        }

        var request = new IslandLayoutRequest
        {
            Form = ShellForm.Expanded,
            Mode = hostSurface.Mode,
            Anchor = anchor,
            OffsetY = resolvedOffset,
        };
        var direction = hostSurface.Mode == IslandMode.Dock ? ExpandDirection.Up : ExpandDirection.Down;
        var shell = IslandLayout.MeasureExpanded(request, monitor.WorkAreaDip, direction);
        var placement = WindowPlacementConverter.PlaceWindow(
            shell,
            monitor,
            hostSurface,
            ShellForm.Expanded,
            anchor,
            resolvedOffset);
        return Calculate(pagePosition, placement, isVisible);
    }

    /// <summary>Bottom hot zone of the expanded shell (12 DIP tall), in work-area DIP.</summary>
    public static VisualRect HotZone(in VisualRect shell)
    {
        if (shell.IsEmpty)
        {
            return VisualRect.Empty;
        }

        var height = Math.Min(HotZoneHeight, shell.Height);
        return new VisualRect(shell.X, shell.Bottom - height, shell.Width, height);
    }

    /// <summary>Whether a work-area point is inside the bottom hot zone of the shell.</summary>
    public static bool IsInHotZone(in VisualRect shell, double x, double y)
        => HotZone(shell).Contains(x, y);
}

/// <summary>Result of <see cref="ArchipelagoLayout.Calculate(double, in WindowPlacement, bool)"/>.</summary>
public sealed record ArchipelagoLayoutResult
{
    /// <summary>Clamped fractional pager position (0..5).</summary>
    public required double PagePosition { get; init; }

    /// <summary>Rounded active page index (0..5).</summary>
    public required int ActivePage { get; init; }

    /// <summary>Strip envelope in work-area DIP (352x30; empty when the host has no strip).</summary>
    public required VisualRect Strip { get; init; }

    /// <summary>Six mini-island slots in page order.</summary>
    public required ImmutableArray<ArchipelagoSlot> Slots { get; init; }

    /// <summary>Continuous active-pill rectangle (72x30) that follows the pager position.</summary>
    public required VisualRect ActiveIndicator { get; init; }

    /// <summary>Bottom hot zone of the expanded shell (12 DIP).</summary>
    public required VisualRect HotZone { get; init; }

    /// <summary>Active indicator offset from its discrete slot in DIP (can be ±28 during a drag).</summary>
    public required double FollowOffset { get; init; }

    /// <summary>Host surface that owns this strip.</summary>
    public required HostSurface HostSurface { get; init; }

    /// <summary>Whether the strip is revealed; when false, <see cref="HitTest"/> returns -1.</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// Hit-tests the six mini-island pills (active pill first). The strip background and the gaps
    /// between pills return -1: the host must report <c>HTTRANSPARENT</c> there.
    /// </summary>
    /// <returns>Page index, or -1 for the transparent strip background.</returns>
    public int HitTest(double x, double y)
    {
        if (!IsVisible || Strip.IsEmpty)
        {
            return -1;
        }

        if (ActiveIndicator.Contains(x, y))
        {
            return ActivePage;
        }

        foreach (var slot in Slots)
        {
            if (slot.Rect.Contains(x, y))
            {
                return slot.Page;
            }
        }

        return -1;
    }

    /// <summary>Whether the point lands on a mini-island pill (not the transparent background).</summary>
    public bool ContainsSlot(double x, double y) => HitTest(x, y) >= 0;

    /// <summary>Slot rectangle for a page, or <see cref="VisualRect.Empty"/> when out of range.</summary>
    public VisualRect SlotRect(int page)
        => page >= 0 && page < Slots.Length ? Slots[page].Rect : VisualRect.Empty;
}

/// <summary>What the host should do with the archipelago visibility on this input.</summary>
public enum ArchipelagoRevealAction
{
    /// <summary>No change.</summary>
    None,

    /// <summary>Reveal the strip (hot-zone dwell reached).</summary>
    Show,

    /// <summary>Hide the strip (collapsed or idle threshold reached).</summary>
    Hide,
}

/// <summary>
/// Pure reveal/hide thresholds for the detached archipelago. The host owns the timers; this policy
/// only maps (dwell, idle, dragging, expanded) to an action so the rules stay deterministic.
/// </summary>
public static class ArchipelagoRevealPolicy
{
    /// <summary>Dwell in the bottom hot zone before reveal (300 ms).</summary>
    public const int RevealDelayMs = LayoutTokens.ArchipelagoRevealDelayMs;

    /// <summary>Idle time after the last drag/wheel activity before hide (1200 ms).</summary>
    public const int HideDelayMs = LayoutTokens.ArchipelagoHideDelayMs;

    /// <summary>Bottom hot-zone height (12 DIP).</summary>
    public const double HotZoneHeight = LayoutTokens.ArchipelagoHotZoneHeight;

    /// <summary>Reveal delay as a <see cref="TimeSpan"/>.</summary>
    public static TimeSpan RevealDelay => TimeSpan.FromMilliseconds(RevealDelayMs);

    /// <summary>Hide delay as a <see cref="TimeSpan"/>.</summary>
    public static TimeSpan HideDelay => TimeSpan.FromMilliseconds(HideDelayMs);

    /// <summary>Bottom hot zone of the expanded shell (12 DIP).</summary>
    public static VisualRect HotZone(in VisualRect shell) => ArchipelagoLayout.HotZone(shell);

    /// <summary>Whether a point is inside the bottom hot zone of the expanded shell.</summary>
    public static bool IsInHotZone(in VisualRect shell, double x, double y)
        => ArchipelagoLayout.IsInHotZone(shell, x, y);

    /// <summary>
    /// Evaluates the reveal/hide thresholds. Precedence: collapse hides immediately; otherwise a
    /// hot-zone dwell reveals, and an idle non-dragging strip hides after 1200 ms.
    /// </summary>
    /// <param name="expanded">Whether the island is expanded (the strip only exists then).</param>
    /// <param name="pointerInHotZone">Whether the pointer is in the bottom 12 DIP hot zone.</param>
    /// <param name="hotZoneDwell">How long the pointer has dwelt in the hot zone.</param>
    /// <param name="isVisible">Whether the strip is currently visible.</param>
    /// <param name="isDragging">Whether a strip/pager drag is in progress (blocks auto-hide).</param>
    /// <param name="sinceLastActivity">Time since the last drag/wheel/pointer activity on the strip.</param>
    public static ArchipelagoRevealAction Evaluate(
        bool expanded,
        bool pointerInHotZone,
        TimeSpan hotZoneDwell,
        bool isVisible,
        bool isDragging,
        TimeSpan sinceLastActivity)
    {
        if (!expanded)
        {
            return isVisible ? ArchipelagoRevealAction.Hide : ArchipelagoRevealAction.None;
        }

        if (!isVisible && pointerInHotZone && hotZoneDwell >= RevealDelay)
        {
            return ArchipelagoRevealAction.Show;
        }

        if (isVisible && !isDragging && sinceLastActivity >= HideDelay)
        {
            return ArchipelagoRevealAction.Hide;
        }

        return ArchipelagoRevealAction.None;
    }
}
