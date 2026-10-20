using System.Collections.Immutable;

// The layout calculator assigns the obsolete Rail property exactly once per result to keep the
// legacy API populated (always empty). All consumers must use Content + the horizontal pager.
#pragma warning disable CS0618

namespace FluidBar.Core.Layout;

/// <summary>One visual layer of the compact stack (index 0 = main pill).</summary>
/// <param name="Index">Stack index.</param>
/// <param name="OffsetX">Horizontal offset from the shell origin, DIP.</param>
/// <param name="OffsetY">Vertical offset from the shell origin, DIP.</param>
/// <param name="Scale">Layer scale.</param>
/// <param name="Opacity">Layer opacity.</param>
/// <param name="IsGhost">Whether this is a background ghost pill.</param>
public readonly record struct StackSlotLayout(
    int Index,
    double OffsetX,
    double OffsetY,
    double Scale,
    double Opacity,
    bool IsGhost);

/// <summary>
/// Pure layout input: what to measure, on which monitor, in which mode/anchor.
/// The host builds it from <see cref="Activities.IslandState"/> via <see cref="FromState"/>.
/// </summary>
public sealed record IslandLayoutRequest
{
    /// <summary>Visual form to measure.</summary>
    public ShellForm Form { get; init; } = ShellForm.Hidden;

    /// <summary>Compact content kind; <see langword="null"/> means the idle pill.</summary>
    public Activities.ActivityKind? CompactKind { get; init; }

    /// <summary>Number of live ghost layers (already clamped by policy); ignored for latest-only.</summary>
    public int GhostCount { get; init; }

    /// <summary>Stacking strategy; latest-only renders no ghosts.</summary>
    public Activities.StackingStrategy Stacking { get; init; } = Activities.StackingStrategy.Stacked;

    /// <summary>Presentation mode.</summary>
    public IslandMode Mode { get; init; } = IslandMode.Float;

    /// <summary>Anchor position within the work area.</summary>
    public IslandAnchor Anchor { get; init; } = IslandAnchor.TopCenter;

    /// <summary>Horizontal offset from the anchored edge/horizontal centre, DIP.</summary>
    public double OffsetX { get; init; }

    /// <summary>Vertical offset from the anchored edge in DIP; NaN selects the mode default.</summary>
    public double OffsetY { get; init; } = double.NaN;

    /// <summary>Whether a hidden island keeps its 10 DIP hover strip.</summary>
    public bool HiddenHoverStrip { get; init; } = true;

    /// <summary>Optional width override (mid-animation measurement).</summary>
    public double? WidthOverride { get; init; }

    /// <summary>Optional height override (mid-animation measurement).</summary>
    public double? HeightOverride { get; init; }

    /// <summary>Whether the primary activity is pinned (diagnostics/visual state).</summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// Fractional pager page position (0..5) used to place the detached archipelago's active pill.
    /// Feed <see cref="Interaction.PagePagerPolicy"/>'s <c>PagePosition</c> (or the snap offset /
    /// pitch) here; it does not affect the shell rectangle.
    /// </summary>
    public double PagePosition { get; init; }

    /// <summary>Whether the detached archipelago is revealed; when false its pills are not hit-testable.</summary>
    public bool ArchipelagoVisible { get; init; } = true;

    /// <summary>Builds a layout request from an island state.</summary>
    /// <param name="state">Island state snapshot.</param>
    /// <param name="mode">Presentation mode from settings.</param>
    /// <param name="anchor">Anchor from per-monitor placement.</param>
    /// <param name="offsetX">Horizontal offset from placement.</param>
    /// <param name="offsetY">Vertical offset from placement; NaN selects the mode default.</param>
    /// <param name="stacking">Stacking strategy from settings.</param>
    /// <param name="hiddenHoverStrip">Whether the hidden hover strip is enabled.</param>
    /// <param name="pagePosition">Fractional pager page position for the archipelago (0..5).</param>
    /// <param name="archipelagoVisible">Whether the detached archipelago is revealed.</param>
    public static IslandLayoutRequest FromState(
        Activities.IslandState state,
        IslandMode mode = IslandMode.Float,
        IslandAnchor anchor = IslandAnchor.TopCenter,
        double offsetX = 0,
        double offsetY = double.NaN,
        Activities.StackingStrategy stacking = Activities.StackingStrategy.Stacked,
        bool hiddenHoverStrip = true,
        double pagePosition = 0,
        bool archipelagoVisible = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        var form = state.Kind switch
        {
            Activities.IslandStateKind.Hidden => ShellForm.Hidden,
            Activities.IslandStateKind.Compact => ShellForm.Compact,
            Activities.IslandStateKind.Peek => ShellForm.Peek,
            Activities.IslandStateKind.Expanded => ShellForm.Expanded,
            Activities.IslandStateKind.Closing => ShellForm.Closing,
            _ => ShellForm.Hidden,
        };
        var ghostCount = Math.Max(0, Math.Min(LayoutTokens.MaxGhosts, state.ActivityCount - state.BadgeCount - 1));
        return new IslandLayoutRequest
        {
            Form = form,
            CompactKind = form == ShellForm.Expanded ? null : state.ContentKind,
            GhostCount = ghostCount,
            Stacking = stacking,
            Mode = mode,
            Anchor = anchor,
            OffsetX = offsetX,
            OffsetY = offsetY,
            HiddenHoverStrip = hiddenHoverStrip,
            IsPinned = state.IsPinned,
            PagePosition = pagePosition,
            ArchipelagoVisible = archipelagoVisible,
        };
    }
}

/// <summary>
/// The single layout result for a state/monitor combination (spec 5.5). All rectangles are in
/// work-area-relative DIP; <see cref="Placement"/> is the only physical-pixel product.
/// </summary>
public sealed record IslandLayoutResult
{
    /// <summary>Measured visual form.</summary>
    public required ShellForm Form { get; init; }

    /// <summary>Presentation mode.</summary>
    public required IslandMode Mode { get; init; }

    /// <summary>Anchor position.</summary>
    public required IslandAnchor Anchor { get; init; }

    /// <summary>Whether the shell is visible at all.</summary>
    public required bool IsVisible { get; init; }

    /// <summary>Shell rectangle (pill or expanded panel) in work-area DIP.</summary>
    public required VisualRect Shell { get; init; }

    /// <summary>Usable content rectangle (expanded: 400x252 at inset (20,16); compact: the shell).</summary>
    public required VisualRect Content { get; init; }

    /// <summary>
    /// Former left-rail rectangle. <b>Obsolete</b>: the left vertical rail was rejected
    /// (2026-10-18) in favour of the horizontal pager (<see cref="Interaction.PagePagerPolicy"/>).
    /// Always <see cref="VisualRect.Empty"/>; kept for API compatibility.
    /// </summary>
    [Obsolete("The left rail was rejected (2026-10-18). This property is always empty; use Content and the horizontal pager (PagePagerPolicy).")]
    public VisualRect Rail { get; init; } = VisualRect.Empty;

    /// <summary>Per-corner radii of the shell.</summary>
    public required CornerRadii Radii { get; init; }

    /// <summary>Shared squircle geometry for drawing, clipping and hit-testing.</summary>
    public required ShellGeometry Geometry { get; init; }

    /// <summary>The single visual-&gt;window placement (host surface + shell local offset).</summary>
    public required WindowPlacement Placement { get; init; }

    /// <summary>Fixed per-mode host surface used for this result (488x384 float/flush, 488x444 dock).</summary>
    public required HostSurface HostSurface { get; init; }

    /// <summary>
    /// Detached Page Archipelago geometry for the expanded form; <see langword="null"/> for compact,
    /// peek, closing and hidden forms. The strip lives outside the shell, in the host's transparent
    /// bottom band.
    /// </summary>
    public ArchipelagoLayoutResult? Archipelago { get; init; }

    /// <summary>
    /// Transparent host band below the expanded shell envelope (holds the archipelago;
    /// hit-test-through). Constant per mode even when a compact form is showing.
    /// </summary>
    public VisualRect BottomBand => Placement.BottomBand;

    /// <summary>Visual layers of the compact stack (main first).</summary>
    public required ImmutableArray<StackSlotLayout> StackSlots { get; init; }

    /// <summary>Hidden-state hover strip, when the island is hidden and the strip is enabled.</summary>
    public VisualRect? HoverStrip { get; init; }

    /// <summary>Direction the expanded panel grows from the anchor.</summary>
    public required ExpandDirection ExpandDirection { get; init; }

    /// <summary>Anchor point used for drag/snap physics.</summary>
    public required VisualPoint AnchorPoint { get; init; }

    /// <summary>
    /// Whether an expanded rail is present. <b>Always false</b> since the rail was rejected
    /// (2026-10-18); kept for API compatibility.
    /// </summary>
#pragma warning disable CA1822 // kept as an instance property for UI-source compatibility
    public bool HasRail => false;
#pragma warning restore CA1822

    /// <summary>Effective ghost layer count in this result.</summary>
    public int GhostCount => Math.Max(0, StackSlots.Length - 1);

    /// <summary>
    /// Hit-tests a work-area point against the interactive host surfaces. The shell squircle and
    /// the six mini-island pills are client areas; the rest of the transparent bottom band (strip
    /// background, gaps between pills, shadow bleed) is hit-test-through.
    /// </summary>
    /// <param name="x">Work-area X in DIP.</param>
    /// <param name="y">Work-area Y in DIP.</param>
    /// <returns>Shell, archipelago slot (with page index), or none.</returns>
    public LayoutHitTarget HitTest(double x, double y)
    {
        if (!IsVisible)
        {
            return LayoutHitTarget.None;
        }

        if (Geometry.Contains(x, y))
        {
            return LayoutHitTarget.ForShell();
        }

        if (Archipelago is { } archipelago)
        {
            var slot = archipelago.HitTest(x, y);
            if (slot >= 0)
            {
                return LayoutHitTarget.ForSlot(slot);
            }
        }

        return LayoutHitTarget.None;
    }
}

/// <summary>What a work-area point hits in the host surface.</summary>
public enum LayoutHitKind
{
    /// <summary>Transparent area: the host returns <c>HTTRANSPARENT</c>.</summary>
    None,

    /// <summary>The shell squircle (expanded panel or compact pill): <c>HTCLIENT</c>.</summary>
    Shell,

    /// <summary>A detached archipelago mini-island pill: <c>HTCLIENT</c>.</summary>
    ArchipelagoSlot,
}

/// <summary>Hit-test result for <see cref="IslandLayoutResult.HitTest"/>.</summary>
/// <param name="Kind">Kind of hit surface.</param>
/// <param name="SlotIndex">Archipelago page index when <paramref name="Kind"/> is <see cref="LayoutHitKind.ArchipelagoSlot"/>; otherwise -1.</param>
public readonly record struct LayoutHitTarget(LayoutHitKind Kind, int SlotIndex)
{
    /// <summary>Transparent area (no client hit).</summary>
    public static LayoutHitTarget None => new(LayoutHitKind.None, -1);

    /// <summary>Creates a shell hit.</summary>
    public static LayoutHitTarget ForShell() => new(LayoutHitKind.Shell, -1);

    /// <summary>Creates an archipelago slot hit.</summary>
    /// <param name="page">Zero-based pager page index.</param>
    public static LayoutHitTarget ForSlot(int page) => new(LayoutHitKind.ArchipelagoSlot, page);

    /// <summary>Whether the host should return <c>HTCLIENT</c> for this hit.</summary>
    public bool IsClient => Kind != LayoutHitKind.None;
}

/// <summary>
/// Pure layout function (spec 5.5): turns a form/mode/anchor/monitor into the one DIP rectangle
/// set plus the single window placement. Unit tests cover 3 modes x 6 anchors x 3 DPIs x
/// 2 stacking strategies.
/// </summary>
public static class IslandLayout
{
    /// <summary>Computes the layout for <paramref name="request"/> on <paramref name="monitor"/>.</summary>
    public static IslandLayoutResult Calculate(IslandLayoutRequest request, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(monitor);

        var work = monitor.WorkAreaDip;
        var hostSurface = HostSurface.ForMode(request.Mode);
        var expandDirection = request.Mode == IslandMode.Dock ? ExpandDirection.Up : ExpandDirection.Down;
        var stackSlots = BuildStackSlots(request);
        var offsetY = ResolvePlacementOffset(request);

        if (request.Form == ShellForm.Hidden)
        {
            var hoverStrip = request.HiddenHoverStrip ? BuildHoverStrip(request, work) : null;
            var hiddenPlacement = hoverStrip is { } strip
                ? WindowPlacementConverter.PlaceHoverStrip(strip, monitor, hostSurface, request.Anchor)
                : WindowPlacementConverter.PlaceWindow(
                    new VisualRect(AnchorX(request, work, 0), TopEdgeY(request, work, 0), 0, 0),
                    monitor,
                    hostSurface,
                    ShellForm.Hidden,
                    request.Anchor,
                    offsetY);
            var emptyShell = VisualRect.Empty;
            return new IslandLayoutResult
            {
                Form = ShellForm.Hidden,
                Mode = request.Mode,
                Anchor = request.Anchor,
                IsVisible = false,
                Shell = emptyShell,
                Content = emptyShell,
                Rail = emptyShell,
                Radii = CornerRadii.Zero,
                Geometry = ShellGeometry.ForShell(emptyShell, CornerRadii.Zero),
                Placement = hiddenPlacement,
                HostSurface = hostSurface,
                Archipelago = null,
                StackSlots = stackSlots,
                HoverStrip = hoverStrip,
                ExpandDirection = expandDirection,
                AnchorPoint = new VisualPoint(hiddenPlacement.Shell.X, hiddenPlacement.Shell.Y),
            };
        }

        var measured = request.Form == ShellForm.Expanded
            ? MeasureExpanded(request, work, expandDirection)
            : MeasureCompact(request, work);
        measured = measured.ClampInside(work);

        // The host surface owns the window rect. Bottom/dock anchors are host-clamped so the
        // detached strip stays inside the work area; placement.Shell is the authoritative shell.
        var placement = WindowPlacementConverter.PlaceWindow(
            measured,
            monitor,
            hostSurface,
            request.Form,
            request.Anchor,
            offsetY);

        // Animation overrides change the visual shell only: the window rect stays put (D7) and the
        // anchored edge stays fixed (top anchors grow down, bottom/dock anchors grow up).
        var shell = ApplyOverrides(placement.Shell, request).ClampInside(work);
        placement = placement with
        {
            Shell = shell,
            ShellLocalOffset = new VisualPoint(
                shell.X - placement.WindowVisual.X,
                shell.Y - placement.WindowVisual.Y),
        };

        var radii = LayoutTokens.RadiiFor(request.Form, request.Mode, shell.Height);
        var (content, rail) = MeasureContent(request.Form, shell);
        var geometry = ShellGeometry.ForShell(shell, radii);
        var archipelago = request.Form == ShellForm.Expanded
            ? ArchipelagoLayout.Calculate(request.PagePosition, placement, request.ArchipelagoVisible)
            : null;

        return new IslandLayoutResult
        {
            Form = request.Form,
            Mode = request.Mode,
            Anchor = request.Anchor,
            IsVisible = true,
            Shell = shell,
            Content = content,
            Rail = rail,
            Radii = radii,
            Geometry = geometry,
            Placement = placement,
            HostSurface = hostSurface,
            Archipelago = archipelago,
            StackSlots = stackSlots,
            HoverStrip = null,
            ExpandDirection = expandDirection,
            AnchorPoint = AnchorPointOf(request.Anchor, shell),
        };
    }

    /// <summary>Effective ghost count for a strategy.</summary>
    public static int EffectiveGhostCount(int requestedGhosts, Activities.StackingStrategy stacking)
        => stacking is Activities.StackingStrategy.LatestOnly or Activities.StackingStrategy.Split
            ? 0
            : Math.Clamp(requestedGhosts, 0, LayoutTokens.MaxGhosts);

    /// <summary>Builds the deterministic visual layers of the compact stack (spec 2.5).</summary>
    public static ImmutableArray<StackSlotLayout> BuildStackSlots(IslandLayoutRequest request)
    {
        var ghosts = EffectiveGhostCount(request.GhostCount, request.Stacking);
        var builder = ImmutableArray.CreateBuilder<StackSlotLayout>(1 + ghosts);
        builder.Add(new StackSlotLayout(0, 0, 0, 1.0, 1.0, false));
        if (ghosts >= 1)
        {
            builder.Add(new StackSlotLayout(1, 0, LayoutTokens.GhostOffsetY1, LayoutTokens.GhostScale1, LayoutTokens.GhostOpacity1, true));
        }

        if (ghosts >= 2)
        {
            builder.Add(new StackSlotLayout(2, 0, LayoutTokens.GhostOffsetY2, LayoutTokens.GhostScale2, LayoutTokens.GhostOpacity2, true));
        }

        return builder.ToImmutable();
    }

    /// <summary>Computes the expanded shell: uniform 440x284, growing down or up from the anchor.</summary>
    public static VisualRect MeasureExpanded(IslandLayoutRequest request, VisualRect work, ExpandDirection direction)
    {
        var size = LayoutTokens.ExpandedSize();
        var width = size.Width;
        var height = size.Height;
        var x = AnchorX(request, work, width);
        double y;
        if (request.Mode == IslandMode.Dock)
        {
            var offsetY = ResolveOffsetY(request);
            var stripTop = work.Height - LayoutTokens.DockStripHeight - offsetY;
            y = stripTop - LayoutTokens.DockExpandedGap - height;
        }
        else if (IsBottomAnchor(request.Anchor))
        {
            var bottomOffset = request.Mode == IslandMode.Flush ? 0 : ResolveOffsetY(request);
            y = work.Height - height - bottomOffset;
        }
        else
        {
            // Flush mode pins the top edge to y=0 (spec 2.2); float uses the configured offset.
            y = request.Mode == IslandMode.Flush ? 0 : ResolveOffsetY(request);
        }

        return new VisualRect(x, y, width, height).ClampInside(work);
    }

    /// <summary>Computes the compact/peek/closing shell rectangle.</summary>
    public static VisualRect MeasureCompact(IslandLayoutRequest request, VisualRect work)
    {
        var isDock = request.Mode == IslandMode.Dock;
        var compact = LayoutTokens.CompactSize(request.CompactKind, request.Mode);
        var baseWidth = compact.Width;
        var baseHeight = compact.Height;
        var baseRect = new VisualRect(
            AnchorX(request, work, baseWidth),
            TopEdgeY(request, work, baseHeight),
            baseWidth,
            baseHeight);

        if (request.Form == ShellForm.Peek && !isDock)
        {
            var deltaX = LayoutTokens.PeekDeltaWidth;
            var deltaY = LayoutTokens.PeekDeltaHeight;
            var growth = IsBottomAnchor(request.Anchor) && request.Mode != IslandMode.Flush;
            var width = baseWidth + deltaX;
            var height = baseHeight + deltaY;
            var x = request.Anchor switch
            {
                IslandAnchor.TopLeft or IslandAnchor.BottomLeft => baseRect.X,
                IslandAnchor.TopRight or IslandAnchor.BottomRight => baseRect.X - deltaX,
                _ => baseRect.X - (deltaX / 2),
            };
            var y = growth ? baseRect.Y - deltaY : baseRect.Y;
            return new VisualRect(x, y, width, height).ClampInside(work);
        }

        return baseRect.ClampInside(work);
    }

    private static (VisualRect Content, VisualRect Rail) MeasureContent(ShellForm form, VisualRect shell)
    {
        if (form != ShellForm.Expanded)
        {
            return (shell, VisualRect.Empty);
        }

        // No-rail expanded panel (user feedback 2026-10-18): content is 400x252, inset (20,16).
        var contentX = shell.X + LayoutTokens.ContentHorizontalInset;
        var contentY = shell.Y + LayoutTokens.ContentVerticalInset;
        var contentWidth = Math.Max(0, shell.Width - (2 * LayoutTokens.ContentHorizontalInset));
        var contentHeight = Math.Max(0, shell.Height - (2 * LayoutTokens.ContentVerticalInset));
        return (new VisualRect(contentX, contentY, contentWidth, contentHeight), VisualRect.Empty);
    }

    private static VisualRect? BuildHoverStrip(IslandLayoutRequest request, VisualRect work)
    {
        var width = LayoutTokens.CompactSize(Activities.ActivityKind.Idle).Width;
        var height = LayoutTokens.HiddenHoverStripHeight;
        var x = AnchorX(request, work, width);
        var y = IsBottomAnchor(request.Anchor) ? work.Height - height : 0;
        return new VisualRect(x, y, width, height).ClampInside(work);
    }

    private static double ResolveOffsetY(IslandLayoutRequest request)
        => double.IsNaN(request.OffsetY)
            ? request.Mode == IslandMode.Dock ? LayoutTokens.DefaultDockOffsetY : LayoutTokens.DefaultFloatOffsetY
            : request.OffsetY;

    /// <summary>Host placement offset: flush bottom anchors pin the host to the work-area bottom.</summary>
    private static double ResolvePlacementOffset(IslandLayoutRequest request)
        => request.Mode == IslandMode.Flush && IsBottomAnchor(request.Anchor) ? 0 : ResolveOffsetY(request);

    /// <summary>
    /// Applies mid-animation width/height overrides around the anchored edge: top anchors keep the
    /// top edge fixed, bottom/dock anchors keep the bottom edge fixed, and horizontal anchors keep
    /// left/centre/right fixed.
    /// </summary>
    private static VisualRect ApplyOverrides(in VisualRect shell, IslandLayoutRequest request)
    {
        if (request.WidthOverride is null && request.HeightOverride is null)
        {
            return shell;
        }

        var width = Math.Max(0, request.WidthOverride ?? shell.Width);
        var height = Math.Max(0, request.HeightOverride ?? shell.Height);
        var x = request.Anchor switch
        {
            IslandAnchor.TopLeft or IslandAnchor.BottomLeft => shell.X,
            IslandAnchor.TopRight or IslandAnchor.BottomRight => shell.Right - width,
            _ => shell.CenterX - (width / 2),
        };
        var bottomAnchored = request.Mode == IslandMode.Dock || IsBottomAnchor(request.Anchor);
        var y = bottomAnchored ? shell.Bottom - height : shell.Y;
        return new VisualRect(x, y, width, height);
    }

    private static double TopEdgeY(IslandLayoutRequest request, VisualRect work, double height)
    {
        if (request.Mode == IslandMode.Dock)
        {
            return work.Height - LayoutTokens.DockStripHeight - ResolveOffsetY(request);
        }

        if (IsBottomAnchor(request.Anchor))
        {
            // Flush mode pins the edges: top anchors at 0, bottom anchors at the work-area bottom.
            var bottomOffset = request.Mode == IslandMode.Flush ? 0 : ResolveOffsetY(request);
            return work.Height - height - bottomOffset;
        }

        return request.Mode == IslandMode.Flush ? 0 : ResolveOffsetY(request);
    }

    private static double AnchorX(IslandLayoutRequest request, VisualRect work, double width) => request.Anchor switch
    {
        IslandAnchor.TopLeft or IslandAnchor.BottomLeft => request.OffsetX,
        IslandAnchor.TopCenter or IslandAnchor.BottomCenter => ((work.Width - width) / 2) + request.OffsetX,
        _ => work.Width - width - request.OffsetX,
    };

    private static bool IsBottomAnchor(IslandAnchor anchor)
        => anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;

    private static VisualPoint AnchorPointOf(IslandAnchor anchor, VisualRect shell)
    {
        var x = anchor switch
        {
            IslandAnchor.TopLeft or IslandAnchor.BottomLeft => shell.X,
            IslandAnchor.TopRight or IslandAnchor.BottomRight => shell.Right,
            _ => shell.CenterX,
        };
        var y = IsBottomAnchor(anchor) ? shell.Bottom : shell.Y;
        return new VisualPoint(x, y);
    }
}
