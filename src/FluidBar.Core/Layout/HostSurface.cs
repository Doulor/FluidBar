namespace FluidBar.Core.Layout;

/// <summary>
/// Fixed per-mode host surface (the layered-window rectangle) and the geometry that lives inside
/// it. The shell is still the uniform 440x284 expanded panel; the host surface is larger because it
/// also owns the transparent shadow bleed and the detached Page Archipelago strip below the shell.
/// <para>
/// Float/flush: <b>488x384 DIP</b> (332 shell envelope + 52 archipelago band). Dock:
/// <b>488x444 DIP</b> (the dock strip + its 8 DIP gap add 60 DIP). The bottom padding is
/// asymmetric on purpose: 24 DIP on the top/sides and 36 DIP below the archipelago strip.
/// </para>
/// <para>
/// All rectangles are host-local DIP unless a method says otherwise. The host window is created
/// once at <see cref="Size"/> and never resized during animation; <see cref="Place"/> only shifts
/// it on anchor/mode/display changes.
/// </para>
/// </summary>
public sealed record HostSurface
{
    /// <summary>Presentation mode this surface belongs to.</summary>
    public required IslandMode Mode { get; init; }

    /// <summary>Host surface width in DIP (488).</summary>
    public required double Width { get; init; }

    /// <summary>Host surface height in DIP (384 float/flush, 444 dock).</summary>
    public required double Height { get; init; }

    /// <summary>Transparent left/right padding in DIP (24).</summary>
    public required double SidePadding { get; init; }

    /// <summary>Transparent top padding in DIP (24).</summary>
    public required double TopPadding { get; init; }

    /// <summary>Bottom padding below the archipelago strip in DIP (36).</summary>
    public required double BottomPadding { get; init; }

    /// <summary>Gap between the expanded shell bottom and the archipelago strip in DIP (10).</summary>
    public required double ArchipelagoGap { get; init; }

    /// <summary>Archipelago strip height in DIP (30).</summary>
    public required double ArchipelagoStripHeight { get; init; }

    /// <summary>Dock strip height in DIP (52; zero for float/flush).</summary>
    public required double DockStripHeight { get; init; }

    /// <summary>Gap between the dock strip and the expanded panel above it in DIP (8; dock only).</summary>
    public required double DockExpandedGap { get; init; }

    /// <summary>Float host surface (488x384).</summary>
    public static HostSurface Float { get; } = Create(IslandMode.Float);

    /// <summary>Flush host surface (488x384; same geometry as float, different mode/radii).</summary>
    public static HostSurface Flush { get; } = Create(IslandMode.Flush);

    /// <summary>Dock host surface (488x444; includes the 52 DIP dock strip + 8 DIP gap).</summary>
    public static HostSurface Dock { get; } = Create(IslandMode.Dock);

    /// <summary>Returns the fixed host surface for a presentation mode.</summary>
    public static HostSurface ForMode(IslandMode mode) => mode switch
    {
        IslandMode.Float => Float,
        IslandMode.Flush => Flush,
        IslandMode.Dock => Dock,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown island mode."),
    };

    /// <summary>Host surface size in DIP.</summary>
    public VisualSize Size => new(Width, Height);

    /// <summary>Whether the host reserves the dock strip band between the panel and the archipelago.</summary>
    public bool HasDockStrip => Mode == IslandMode.Dock;

    /// <summary>Expanded shell top-left inside the host surface (24, 24).</summary>
    public VisualPoint ShellLocalOffset => new(SidePadding, TopPadding);

    /// <summary>Expanded shell envelope (440x284) inside the host surface.</summary>
    public VisualRect ShellEnvelopeLocal => new(SidePadding, TopPadding, LayoutTokens.ExpandedWidth, LayoutTokens.ExpandedHeight);

    /// <summary>Y of the expanded shell bottom inside the host surface (24 + 284 = 308).</summary>
    public double ShellBottomLocal => TopPadding + LayoutTokens.ExpandedHeight;

    /// <summary>Dock strip top inside the host surface (24 + 284 + 8 = 316).</summary>
    public double DockStripTopLocal => ShellBottomLocal + DockExpandedGap;

    /// <summary>Dock strip rectangle inside the host surface; empty for float/flush.</summary>
    public VisualRect DockStripLocal => HasDockStrip
        ? new VisualRect(SidePadding, DockStripTopLocal, LayoutTokens.ExpandedWidth, DockStripHeight)
        : VisualRect.Empty;

    /// <summary>Archipelago strip top inside the host surface (318 float/flush, 378 dock).</summary>
    public double ArchipelagoTopLocal => HasDockStrip
        ? DockStripTopLocal + DockStripHeight + ArchipelagoGap
        : ShellBottomLocal + ArchipelagoGap;

    /// <summary>Archipelago track envelope inside the host surface (352x30, horizontally centred).</summary>
    public VisualRect ArchipelagoStripLocal => new(
        (Width - LayoutTokens.ArchipelagoTrackWidth) / 2,
        ArchipelagoTopLocal,
        LayoutTokens.ArchipelagoTrackWidth,
        ArchipelagoStripHeight);

    /// <summary>
    /// Host height below the expanded shell (76 float/flush, 136 dock). This is the transparent
    /// band that holds the archipelago strip (and, for dock, the dock strip).
    /// </summary>
    public double BottomAllowance => Height - ShellBottomLocal;

    /// <summary>Transparent band below the shell, host-local (from y=308 to the host bottom).</summary>
    public VisualRect BottomBandLocal => new(0, ShellBottomLocal, Width, BottomAllowance);

    /// <summary>
    /// Places the host surface for a measured shell. The returned shell may differ from
    /// <paramref name="shell"/> for bottom/dock anchors: the host is kept inside the work area so
    /// the detached archipelago never falls off-screen, and the shell is shifted with it.
    /// </summary>
    /// <param name="shell">Measured shell in work-area DIP (pre-host placement).</param>
    /// <param name="work">Work area in work-area-relative DIP.</param>
    /// <param name="form">Visual form (selects the dock compact vs expanded shell local offset).</param>
    /// <param name="anchor">Anchor within the work area.</param>
    /// <param name="offsetY">Resolved vertical offset in DIP; NaN selects the mode default.</param>
    public HostPlacement Place(
        in VisualRect shell,
        in VisualRect work,
        ShellForm form,
        IslandAnchor anchor,
        double offsetY)
    {
        var bottomAnchored = IsBottomAnchor(anchor);
        if (double.IsNaN(offsetY))
        {
            offsetY = Mode == IslandMode.Dock ? LayoutTokens.DefaultDockOffsetY : LayoutTokens.DefaultFloatOffsetY;
        }

        var localX = ResolveLocalX(shell.Width, anchor);
        var localY = ResolveLocalY(form, shell.Height, bottomAnchored);

        var windowX = shell.X - localX;
        if (shell.Width <= Width)
        {
            // Keep the whole shell inside the host surface. Normally this is a no-op; it matters
            // for the 480 DIP lookup pill, where 24 DIP side padding does not fit.
            windowX = Math.Clamp(windowX, shell.Right - Width, shell.X);
        }

        localX = shell.X - windowX;

        double windowY;
        var clamped = false;
        if (Mode == IslandMode.Dock || bottomAnchored)
        {
            // Anchor the host bottom, not the shell bottom: the archipelago (and, for dock, the
            // strip) must stay inside the work area.
            windowY = work.Bottom - offsetY - Height;
            if (windowY < work.Y)
            {
                windowY = work.Y;
                clamped = true;
            }
        }
        else
        {
            // Top anchors keep the shell at its configured offset; the transparent 24 DIP bleed may
            // overhang the work area (by design). Clamp only if the host would fall off the bottom.
            windowY = shell.Y - localY;
            if (windowY + Height > work.Bottom)
            {
                windowY = work.Bottom - Height;
                clamped = true;
            }

            if (windowY < work.Y - TopPadding)
            {
                windowY = work.Y - TopPadding;
                clamped = true;
            }
        }

        var adjustedShell = new VisualRect(
            windowX + localX,
            windowY + localY,
            shell.Width,
            shell.Height);
        var windowVisual = new VisualRect(windowX, windowY, Width, Height);
        var archipelago = ArchipelagoStripLocal.Offset(windowX, windowY);
        var dockStrip = HasDockStrip ? DockStripLocal.Offset(windowX, windowY) : VisualRect.Empty;
        var bottomBand = BottomBandLocal.Offset(windowX, windowY);
        return new HostPlacement(
            Shell: adjustedShell,
            WindowVisual: windowVisual,
            ShellLocalOffset: new VisualPoint(localX, localY),
            ArchipelagoStrip: archipelago,
            DockStrip: dockStrip,
            BottomBand: bottomBand,
            IsClamped: clamped || adjustedShell != shell);
    }

    /// <summary>
    /// Places the host surface for the hidden-state hover strip. Hidden uses the host edge (no
    /// 24 DIP shadow inset) so the 10 DIP hot strip remains at the work-area edge.
    /// </summary>
    /// <param name="hoverStrip">Hover strip in work-area DIP.</param>
    /// <param name="work">Work area in work-area-relative DIP.</param>
    /// <param name="anchor">Anchor within the work area.</param>
    public HostPlacement PlaceHoverStrip(in VisualRect hoverStrip, in VisualRect work, IslandAnchor anchor)
    {
        var bottom = IsBottomAnchor(anchor) || Mode == IslandMode.Dock;
        var windowY = bottom ? work.Bottom - Height : work.Y;
        var localY = bottom ? Height - hoverStrip.Height : 0;
        var localX = ResolveLocalX(hoverStrip.Width, anchor);
        var windowX = hoverStrip.X - localX;
        if (hoverStrip.Width <= Width)
        {
            windowX = Math.Clamp(windowX, hoverStrip.Right - Width, hoverStrip.X);
        }

        localX = hoverStrip.X - windowX;
        var adjusted = new VisualRect(windowX + localX, windowY + localY, hoverStrip.Width, hoverStrip.Height);
        return new HostPlacement(
            Shell: adjusted,
            WindowVisual: new VisualRect(windowX, windowY, Width, Height),
            ShellLocalOffset: new VisualPoint(localX, localY),
            ArchipelagoStrip: VisualRect.Empty,
            DockStrip: VisualRect.Empty,
            BottomBand: BottomBandLocal.Offset(windowX, windowY),
            IsClamped: false);
    }

    /// <summary>
    /// Shell local X for a given shell width and anchor. Left anchors inset by 24, right anchors
    /// leave 24 on the right, centre anchors centre the shell in the host. Shells wider than the
    /// padded envelope (the 480 DIP lookup pill) are centred instead of overflowing.
    /// </summary>
    private double ResolveLocalX(double shellWidth, IslandAnchor anchor)
    {
        if (shellWidth > Width - (2 * SidePadding))
        {
            return Math.Clamp((Width - shellWidth) / 2, 0, Math.Max(0, Width - shellWidth));
        }

        var localX = anchor switch
        {
            IslandAnchor.TopLeft or IslandAnchor.BottomLeft => SidePadding,
            IslandAnchor.TopRight or IslandAnchor.BottomRight => Width - shellWidth - SidePadding,
            _ => (Width - shellWidth) / 2,
        };
        return Math.Clamp(localX, 0, Math.Max(0, Width - shellWidth));
    }

    /// <summary>Shell local Y: top-aligned for top anchors, bottom-aligned for bottom/dock forms.</summary>
    private double ResolveLocalY(ShellForm form, double shellHeight, bool bottomAnchored)
    {
        if (Mode == IslandMode.Dock)
        {
            // The expanded panel sits above the dock strip; the compact strip itself is the shell.
            return form == ShellForm.Expanded ? TopPadding : DockStripTopLocal;
        }

        return bottomAnchored
            ? Height - BottomAllowance - shellHeight
            : TopPadding;
    }

    private static bool IsBottomAnchor(IslandAnchor anchor)
        => anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;

    private static HostSurface Create(IslandMode mode) => new()
    {
        Mode = mode,
        Width = LayoutTokens.HostSurfaceWidth,
        Height = mode == IslandMode.Dock ? LayoutTokens.HostSurfaceDockHeight : LayoutTokens.HostSurfaceFloatHeight,
        SidePadding = LayoutTokens.HostTopPadding,
        TopPadding = LayoutTokens.HostTopPadding,
        BottomPadding = LayoutTokens.HostBottomPadding,
        ArchipelagoGap = LayoutTokens.ArchipelagoGap,
        ArchipelagoStripHeight = LayoutTokens.ArchipelagoStripHeight,
        DockStripHeight = LayoutTokens.DockStripHeight,
        DockExpandedGap = LayoutTokens.DockExpandedGap,
    };
}

/// <summary>
/// DIP-only result of <see cref="HostSurface.Place"/>: where the host window goes, where the shell
/// sits inside it, and where the detached strip lands in work-area coordinates.
/// </summary>
/// <param name="Shell">Shell rectangle after host clamping/shifting (work-area DIP).</param>
/// <param name="WindowVisual">Host window rectangle (work-area DIP).</param>
/// <param name="ShellLocalOffset">Shell top-left inside the host window (DIP).</param>
/// <param name="ArchipelagoStrip">Archipelago track envelope (work-area DIP; empty when hidden).</param>
/// <param name="DockStrip">Dock strip rectangle (work-area DIP; empty for float/flush).</param>
/// <param name="BottomBand">Transparent band below the expanded shell envelope (work-area DIP).</param>
/// <param name="IsClamped">Whether the shell was host-adjusted (bottom/dock) or clamped inside the work area.</param>
public readonly record struct HostPlacement(
    VisualRect Shell,
    VisualRect WindowVisual,
    VisualPoint ShellLocalOffset,
    VisualRect ArchipelagoStrip,
    VisualRect DockStrip,
    VisualRect BottomBand,
    bool IsClamped)
{
    /// <summary>Host window size in DIP.</summary>
    public VisualSize WindowSize => new(WindowVisual.Width, WindowVisual.Height);
}
