using FluidBar.Core.Activities;
using FluidBar.Core.Layout;

// Tests intentionally assert the legacy Rail compatibility surface (must stay empty) and the
// obsolete rail tokens (values retained for API compatibility) after the no-rail redesign.
#pragma warning disable CS0618

namespace FluidBar.Core.Tests;

public class LayoutTests
{
    private const double Eps = 0.001;

    public static TheoryData<IslandMode, IslandAnchor, uint, StackingStrategy> Matrix { get; } = BuildMatrix();

    public static TheoryData<IslandMode, IslandAnchor, uint> ArchipelagoMatrix { get; } = BuildArchipelagoMatrix();

    private static TheoryData<IslandMode, IslandAnchor, uint, StackingStrategy> BuildMatrix()
    {
        var data = new TheoryData<IslandMode, IslandAnchor, uint, StackingStrategy>();
        foreach (var mode in Enum.GetValues<IslandMode>())
        {
            foreach (var anchor in Enum.GetValues<IslandAnchor>())
            {
                foreach (var dpi in new uint[] { 96, 144, 192 })
                {
                    foreach (var stacking in Enum.GetValues<StackingStrategy>())
                    {
                        data.Add(mode, anchor, dpi, stacking);
                    }
                }
            }
        }

        return data;
    }

    private static TheoryData<IslandMode, IslandAnchor, uint> BuildArchipelagoMatrix()
    {
        var data = new TheoryData<IslandMode, IslandAnchor, uint>();
        foreach (var mode in Enum.GetValues<IslandMode>())
        {
            foreach (var anchor in Enum.GetValues<IslandAnchor>())
            {
                foreach (var dpi in new uint[] { 96, 144, 192 })
                {
                    data.Add(mode, anchor, dpi);
                }
            }
        }

        return data;
    }

    private static MonitorInfo Monitor(uint dpi) => dpi switch
    {
        96 => MonitorInfo.Create("m96", new PixelRect(0, 0, 1920, 1040), 96, isPrimary: true),
        144 => MonitorInfo.Create("m144", new PixelRect(1920, 0, 2560, 1400), 144),
        192 => MonitorInfo.Create("m192", new PixelRect(4480, 0, 3840, 2080), 192),
        _ => throw new ArgumentOutOfRangeException(nameof(dpi)),
    };

    private static IslandLayoutRequest Request(
        ShellForm form,
        IslandMode mode,
        IslandAnchor anchor,
        int ghostCount = 0,
        StackingStrategy stacking = StackingStrategy.Stacked,
        double offsetX = 0,
        double offsetY = double.NaN,
        ActivityKind? compactKind = ActivityKind.Notification,
        double pagePosition = 0,
        bool archipelagoVisible = true)
        => new()
        {
            Form = form,
            CompactKind = compactKind,
            GhostCount = ghostCount,
            Stacking = stacking,
            Mode = mode,
            Anchor = anchor,
            OffsetX = offsetX,
            OffsetY = offsetY,
            PagePosition = pagePosition,
            ArchipelagoVisible = archipelagoVisible,
        };

    private static bool IsBottom(IslandAnchor anchor)
        => anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;

    private static double ResolvedOffset(IslandMode mode, IslandAnchor anchor)
    {
        if (mode == IslandMode.Dock)
        {
            return LayoutTokens.DefaultDockOffsetY;
        }

        if (IsBottom(anchor))
        {
            return mode == IslandMode.Flush ? 0 : LayoutTokens.DefaultFloatOffsetY;
        }

        return mode == IslandMode.Flush ? 0 : LayoutTokens.DefaultFloatOffsetY;
    }

    private static VisualPoint ExpectedLocalOffset(
        IslandMode mode,
        IslandAnchor anchor,
        ShellForm form,
        double shellWidth,
        double shellHeight)
    {
        var host = HostSurface.ForMode(mode);
        var localX = shellWidth > host.Width - (2 * host.SidePadding)
            ? (host.Width - shellWidth) / 2
            : anchor switch
            {
                IslandAnchor.TopLeft or IslandAnchor.BottomLeft => host.SidePadding,
                IslandAnchor.TopRight or IslandAnchor.BottomRight => host.Width - shellWidth - host.SidePadding,
                _ => (host.Width - shellWidth) / 2,
            };

        if (mode == IslandMode.Dock)
        {
            return new VisualPoint(localX, form == ShellForm.Expanded ? 24 : 316);
        }

        return IsBottom(anchor)
            ? new VisualPoint(localX, host.Height - host.BottomAllowance - shellHeight)
            : new VisualPoint(localX, 24);
    }

    private static double ExpectedShellBottom(IslandMode mode, IslandAnchor anchor, ShellForm form, VisualRect work)
    {
        var host = HostSurface.ForMode(mode);
        if (mode == IslandMode.Dock)
        {
            var allowance = form == ShellForm.Expanded
                ? LayoutTokens.HostDockBottomAllowance
                : LayoutTokens.HostFloatBottomAllowance;
            return work.Height - LayoutTokens.DefaultDockOffsetY - allowance;
        }

        if (IsBottom(anchor))
        {
            return work.Height - ResolvedOffset(mode, anchor) - host.BottomAllowance;
        }

        return double.NaN;
    }

    private static double ExpectedStripTop(IslandMode mode, VisualRect shell)
        => mode == IslandMode.Dock
            ? shell.Bottom + LayoutTokens.DockExpandedGap + LayoutTokens.DockStripHeight + LayoutTokens.ArchipelagoGap
            : shell.Bottom + LayoutTokens.ArchipelagoGap;

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Layout_Matrix_IsStableAcrossModesAnchorsDpisAndStrategies(
        IslandMode mode,
        IslandAnchor anchor,
        uint dpi,
        StackingStrategy stacking)
    {
        var monitor = Monitor(dpi);
        var work = monitor.WorkAreaDip;
        var scale = monitor.Scale;
        var host = HostSurface.ForMode(mode);
        var expectedHostHeight = mode == IslandMode.Dock
            ? LayoutTokens.HostSurfaceDockHeight
            : LayoutTokens.HostSurfaceFloatHeight;

        foreach (var form in new[] { ShellForm.Compact, ShellForm.Expanded })
        {
            var result = IslandLayout.Calculate(Request(form, mode, anchor, 2, stacking), monitor);
            var context = $"{form}/{mode}/{anchor}@{dpi}/{stacking}";

            Assert.True(result.IsVisible, context);
            Assert.True(result.Shell.X >= -Eps, context);
            Assert.True(result.Shell.Y >= -Eps, context);
            Assert.True(result.Shell.Right <= work.Width + Eps, context);
            Assert.True(result.Shell.Bottom <= work.Height + Eps, context);

            var expectedWidth = form == ShellForm.Expanded ? 440 : mode == IslandMode.Dock ? 440 : 400;
            var expectedHeight = form == ShellForm.Expanded ? 284 : mode == IslandMode.Dock ? 52 : 96;
            Assert.Equal(expectedWidth, result.Shell.Width, Eps);
            Assert.Equal(expectedHeight, result.Shell.Height, Eps);

            // The matrix already walks Enum.GetValues<StackingStrategy>(), so a new strategy needs no
            // data change. Split keeps the same shell/placement geometry as Stacked and LatestOnly: its
            // preview islets are painted by the Skia host beside the pill (inside the fixed 488 DIP host
            // surface), so the layout reports the primary slot only and no ghost layers.
            var expectedGhosts = IslandLayout.EffectiveGhostCount(2, stacking);
            Assert.Equal(1 + expectedGhosts, result.StackSlots.Length);
            Assert.Equal(expectedGhosts, result.GhostCount);
            Assert.Equal(1.0, result.StackSlots[0].Scale, Eps);
            if (expectedGhosts == 2)
            {
                Assert.Equal(-6, result.StackSlots[1].OffsetY, Eps);
                Assert.Equal(-12, result.StackSlots[2].OffsetY, Eps);
                Assert.Equal(0.55, result.StackSlots[1].Opacity, Eps);
                Assert.Equal(0.35, result.StackSlots[2].Opacity, Eps);
            }

            // Host surface is per-mode fixed; the shell remains 440x284.
            Assert.Equal(host, result.HostSurface);
            Assert.Equal(488, host.Width, Eps);
            Assert.Equal(expectedHostHeight, host.Height, Eps);
            Assert.Equal(expectedHostHeight, result.Placement.WindowSizeDip.Height, Eps);
            Assert.Equal(488, result.Placement.WindowSizeDip.Width, Eps);
            Assert.Equal(expectedHostHeight, result.Placement.WindowVisual.Height, Eps);
            Assert.Equal(488, result.Placement.WindowVisual.Width, Eps);

            // Exactly one conversion, padding applied on BOTH axes (v1 F-04 regression).
            Assert.True(
                WindowPlacementConverter.Ceil(488 * scale) == result.Placement.WindowPixel.Width,
                context + " window width");
            Assert.True(
                WindowPlacementConverter.Ceil(expectedHostHeight * scale) == result.Placement.WindowPixel.Height,
                context + " window height");
            Assert.True(
                WindowPlacementConverter.Round(monitor.WorkArea.X + (result.Placement.WindowVisual.X * scale))
                    == result.Placement.WindowPixel.X,
                context + " window X");
            Assert.True(
                WindowPlacementConverter.Round(monitor.WorkArea.Y + (result.Placement.WindowVisual.Y * scale))
                    == result.Placement.WindowPixel.Y,
                context + " window Y");

            // The shell lands at the host-local offset and the host is placed around it.
            var expectedLocal = ExpectedLocalOffset(mode, anchor, form, result.Shell.Width, result.Shell.Height);
            Assert.Equal(expectedLocal.X, result.Placement.ShellLocalOffset.X, Eps);
            Assert.Equal(expectedLocal.Y, result.Placement.ShellLocalOffset.Y, Eps);
            Assert.Equal(result.Shell.X - expectedLocal.X, result.Placement.WindowVisual.X, Eps);
            Assert.Equal(result.Shell.Y - expectedLocal.Y, result.Placement.WindowVisual.Y, Eps);
            Assert.Equal(result.Shell, result.Placement.Shell);

            // Horizontal anchor placement.
            switch (anchor)
            {
                case IslandAnchor.TopLeft or IslandAnchor.BottomLeft:
                    Assert.Equal(0, result.Shell.X, Eps);
                    Assert.Equal(-24, result.Placement.WindowVisual.X, Eps);
                    break;
                case IslandAnchor.TopRight or IslandAnchor.BottomRight:
                    Assert.Equal(work.Width, result.Shell.Right, Eps);
                    Assert.Equal(work.Width + 24, result.Placement.WindowVisual.Right, Eps);
                    break;
                default:
                    Assert.Equal(work.Width / 2, result.Shell.CenterX, Eps);
                    break;
            }

            // Vertical anchor placement per mode: bottom/dock anchors are host-clamped, so the
            // shell is lifted by the bottom allowance and the strip stays inside the work area.
            if (mode == IslandMode.Dock)
            {
                Assert.Equal(work.Height - LayoutTokens.DefaultDockOffsetY, result.Placement.WindowVisual.Bottom, Eps);
                Assert.Equal(ExpectedShellBottom(mode, anchor, form, work), result.Shell.Bottom, Eps);
                Assert.True(result.Placement.WindowVisual.Bottom <= work.Height + Eps, context + " dock clamp");
            }
            else if (IsBottom(anchor))
            {
                Assert.Equal(work.Height - ResolvedOffset(mode, anchor), result.Placement.WindowVisual.Bottom, Eps);
                Assert.Equal(ExpectedShellBottom(mode, anchor, form, work), result.Shell.Bottom, Eps);
                Assert.True(result.Placement.WindowVisual.Bottom <= work.Height + Eps, context + " bottom clamp");
            }
            else
            {
                Assert.Equal(mode == IslandMode.Flush ? 0 : LayoutTokens.DefaultFloatOffsetY, result.Shell.Y, Eps);
                Assert.Equal(result.Shell.Y - 24, result.Placement.WindowVisual.Y, Eps);
            }

            Assert.Equal(host.BottomAllowance, result.Placement.BottomAllowance, Eps);
            Assert.Equal(host.BottomAllowance, result.Placement.BottomBand.Height, Eps);
            Assert.Equal(result.Placement.WindowVisual.Bottom - host.BottomAllowance, result.Placement.BottomBand.Y, Eps);
            Assert.Equal(result.Placement.WindowVisual.Bottom, result.Placement.BottomBand.Bottom, Eps);

            // Radii policy.
            if (form == ShellForm.Expanded)
            {
                // No-rail expanded content: 400x252 at (20,16) inset; Rail is obsolete and empty.
                Assert.Equal(400, result.Content.Width, Eps);
                Assert.Equal(252, result.Content.Height, Eps);
                Assert.Equal(result.Shell.X + LayoutTokens.ContentHorizontalInset, result.Content.X, Eps);
                Assert.Equal(result.Shell.Y + LayoutTokens.ContentVerticalInset, result.Content.Y, Eps);
                Assert.False(result.HasRail);
                Assert.True(result.Rail.IsEmpty);

                switch (mode)
                {
                    case IslandMode.Dock:
                        Assert.Equal(16, result.Radii.TopLeft, Eps);
                        break;
                    case IslandMode.Flush:
                        Assert.Equal(0, result.Radii.TopLeft, Eps);
                        Assert.Equal(22, result.Radii.BottomLeft, Eps);
                        break;
                    default:
                        Assert.Equal(28, result.Radii.TopLeft, Eps);
                        break;
                }

                // Detached archipelago geometry (expanded only).
                var archipelago = result.Archipelago;
                Assert.NotNull(archipelago);
                Assert.Equal(result.Placement.ArchipelagoStrip, archipelago.Strip);
                Assert.Equal(352, archipelago.Strip.Width, Eps);
                Assert.Equal(32, archipelago.Strip.Height, Eps);
                Assert.Equal(result.Shell.X + 44, archipelago.Strip.X, Eps);
                Assert.Equal(ExpectedStripTop(mode, result.Shell), archipelago.Strip.Y, Eps);
                Assert.True(archipelago.Strip.Bottom <= work.Height + Eps, context + " strip inside work area");
                Assert.Equal(36, result.Placement.WindowVisual.Bottom - archipelago.Strip.Bottom, Eps);
                Assert.Equal(6, archipelago.Slots.Length);
                Assert.Equal(0, archipelago.ActivePage);
                Assert.Equal(72, archipelago.Slots[0].Width, Eps);
                Assert.Equal(30, archipelago.Slots[0].Height, Eps);
                Assert.Equal(48, archipelago.Slots[5].Width, Eps);
                Assert.Equal(26, archipelago.Slots[5].Height, Eps);
                Assert.Equal(archipelago.Strip.X, archipelago.Slots[0].Rect.X, Eps);
                Assert.Equal(archipelago.Strip.Right, archipelago.Slots[5].Rect.Right, Eps);
                Assert.Equal(result.Shell.Bottom, archipelago.HotZone.Bottom, Eps);
                Assert.Equal(12, archipelago.HotZone.Height, Eps);

                // Hit-test contract: shell + mini-island pills only; the strip background is
                // transparent and the host must return HTTRANSPARENT there.
                Assert.Equal(LayoutHitKind.Shell, result.HitTest(result.Shell.CenterX, result.Shell.CenterY).Kind);
                var lastSlot = archipelago.Slots[5];
                var slotHit = result.HitTest(lastSlot.Rect.CenterX, lastSlot.Rect.CenterY);
                Assert.Equal(LayoutHitKind.ArchipelagoSlot, slotHit.Kind);
                Assert.Equal(5, slotHit.SlotIndex);
                Assert.Equal(LayoutHitKind.None, result.HitTest(archipelago.Strip.CenterX, archipelago.Strip.Y - 2).Kind);
                Assert.Equal(LayoutHitKind.None, result.HitTest(archipelago.Strip.Right + 2, archipelago.Strip.CenterY).Kind);
            }
            else
            {
                Assert.Null(result.Archipelago);
                Assert.Equal(LayoutHitKind.None, result.HitTest(result.Shell.CenterX, result.Placement.BottomBand.CenterY).Kind);
                switch (mode)
                {
                    case IslandMode.Dock:
                        Assert.Equal(12, result.Radii.TopLeft, Eps);
                        break;
                    case IslandMode.Flush:
                        Assert.Equal(0, result.Radii.TopLeft, Eps);
                        Assert.Equal(22, result.Radii.BottomLeft, Eps);
                        break;
                    default:
                        Assert.Equal(44, result.Radii.TopLeft, Eps);
                        break;
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ArchipelagoMatrix))]
    public void Archipelago_Matrix_StaysInsideWorkAreaAndFillsTheStrip(IslandMode mode, IslandAnchor anchor, uint dpi)
    {
        var monitor = Monitor(dpi);
        var work = monitor.WorkAreaDip;
        var result = IslandLayout.Calculate(
            Request(ShellForm.Expanded, mode, anchor, pagePosition: 2.5),
            monitor);
        var context = $"{mode}/{anchor}@{dpi}";
        var archipelago = result.Archipelago;

        Assert.NotNull(archipelago);
        Assert.Equal(2.5, archipelago.PagePosition, Eps);
        Assert.Equal(3, archipelago.ActivePage);
        Assert.Equal(6, archipelago.Slots.Length);
        Assert.Equal(352, archipelago.Strip.Width, Eps);
        Assert.True(archipelago.Strip.X >= work.X - Eps, context + " strip left");
        Assert.True(archipelago.Strip.Right <= work.Right + Eps, context + " strip right");
        Assert.True(archipelago.Strip.Y >= work.Y - Eps, context + " strip top");
        Assert.True(archipelago.Strip.Bottom <= work.Bottom + Eps, context + " strip bottom");

        // Exactly one active 72x30 pill; all other slots are 48x26 and stay inside the strip.
        Assert.Single(archipelago.Slots.Where(slot => slot.IsActive));
        Assert.Equal(72, archipelago.Slots[3].Width, Eps);
        Assert.Equal(30, archipelago.Slots[3].Height, Eps);
        foreach (var slot in archipelago.Slots.Where(slot => !slot.IsActive))
        {
            Assert.Equal(48, slot.Width, Eps);
            Assert.Equal(26, slot.Height, Eps);
        }

        Assert.Equal(archipelago.Strip.X, archipelago.Slots[0].Rect.X, Eps);
        Assert.Equal(archipelago.Strip.Right, archipelago.Slots[5].Rect.Right, Eps);
        Assert.Equal(archipelago.Strip.X + (2.5 * 56), archipelago.ActiveIndicator.X, Eps);
        Assert.Equal(-28, archipelago.FollowOffset, Eps);

        var activeHit = result.HitTest(archipelago.Slots[3].Rect.CenterX, archipelago.Slots[3].Rect.CenterY);
        Assert.Equal(LayoutHitKind.ArchipelagoSlot, activeHit.Kind);
        Assert.Equal(3, activeHit.SlotIndex);
        Assert.Equal(LayoutHitKind.None, result.HitTest(archipelago.Strip.X - 1, archipelago.Strip.CenterY).Kind);
    }

    [Fact]
    public void Archipelago_SlotGeometryFollowsPagerPosition()
    {
        var monitor = Monitor(96);
        var host = HostSurface.Float;
        var atZero = ArchipelagoLayout.Calculate(0, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(0, atZero.ActivePage);
        Assert.Equal(atZero.Strip.X, atZero.ActiveIndicator.X, Eps);
        Assert.Equal(0, atZero.FollowOffset, Eps);

        var atTwo = ArchipelagoLayout.Calculate(2, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(2, atTwo.ActivePage);
        Assert.Equal(atTwo.Strip.X + 112, atTwo.ActiveIndicator.X, Eps);
        Assert.Equal(0, atTwo.FollowOffset, Eps);

        var mid = ArchipelagoLayout.Calculate(2.5, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(3, mid.ActivePage);
        Assert.Equal(mid.Strip.X + 140, mid.ActiveIndicator.X, Eps);
        Assert.Equal(-28, mid.FollowOffset, Eps);
        Assert.Equal(72, mid.Slots[3].Width, Eps);
        Assert.Equal(48, mid.Slots[2].Width, Eps);

        var atFive = ArchipelagoLayout.Calculate(5, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(5, atFive.ActivePage);
        Assert.Equal(atFive.Strip.Right - 72, atFive.ActiveIndicator.X, Eps);
        Assert.Equal(atFive.Strip.Right, atFive.Slots[5].Rect.Right, Eps);

        var clamped = ArchipelagoLayout.Calculate(99, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(5, clamped.ActivePage);
        var nan = ArchipelagoLayout.Calculate(double.NaN, host, monitor, IslandAnchor.TopCenter);
        Assert.Equal(0, nan.ActivePage);
        Assert.Equal(0, nan.PagePosition, Eps);
    }

    [Fact]
    public void Archipelago_HitTestExcludesTransparentStripBackground()
    {
        var monitor = Monitor(96);
        var result = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter, pagePosition: 4),
            monitor);
        var archipelago = result.Archipelago!;

        // Every pill is interactive.
        for (var page = 0; page < 6; page++)
        {
            var rect = archipelago.Slots[page].Rect;
            Assert.Equal(page, archipelago.HitTest(rect.CenterX, rect.CenterY));
        }

        // The strip background, the gaps outside the active indicator, and the shadow band are not.
        Assert.Equal(-1, archipelago.HitTest(archipelago.Strip.X + 4, archipelago.Strip.Y - 4));
        Assert.Equal(-1, archipelago.HitTest(archipelago.Strip.Right + 4, archipelago.Strip.CenterY));
        Assert.Equal(LayoutHitKind.None, result.HitTest(archipelago.Strip.X + 4, archipelago.Strip.Y - 4).Kind);

        // A hidden strip is not interactive even on a pill.
        var hidden = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter, pagePosition: 4, archipelagoVisible: false),
            monitor);
        Assert.NotNull(hidden.Archipelago);
        Assert.False(hidden.Archipelago!.IsVisible);
        Assert.Equal(-1, hidden.Archipelago.HitTest(hidden.Archipelago.Slots[4].Rect.CenterX, hidden.Archipelago.Slots[4].Rect.CenterY));
        Assert.Equal(LayoutHitKind.None, hidden.HitTest(hidden.Archipelago.Slots[4].Rect.CenterX, hidden.Archipelago.Slots[4].Rect.CenterY).Kind);
    }

    [Fact]
    public void Archipelago_RevealPolicy_RespectsThresholds()
    {
        var shell = new VisualRect(100, 12, 440, 284);
        Assert.True(ArchipelagoRevealPolicy.IsInHotZone(shell, 320, shell.Bottom - 1));
        Assert.False(ArchipelagoRevealPolicy.IsInHotZone(shell, 320, shell.Bottom - 13));
        Assert.Equal(shell.Bottom, ArchipelagoRevealPolicy.HotZone(shell).Bottom, Eps);
        Assert.Equal(12, ArchipelagoRevealPolicy.HotZone(shell).Height, Eps);

        Assert.Equal(
            ArchipelagoRevealAction.None,
            ArchipelagoRevealPolicy.Evaluate(true, true, TimeSpan.FromMilliseconds(299), false, false, TimeSpan.Zero));
        Assert.Equal(
            ArchipelagoRevealAction.Show,
            ArchipelagoRevealPolicy.Evaluate(true, true, TimeSpan.FromMilliseconds(300), false, false, TimeSpan.Zero));
        Assert.Equal(
            ArchipelagoRevealAction.None,
            ArchipelagoRevealPolicy.Evaluate(true, false, TimeSpan.FromSeconds(10), false, false, TimeSpan.Zero));
        Assert.Equal(
            ArchipelagoRevealAction.None,
            ArchipelagoRevealPolicy.Evaluate(true, false, TimeSpan.Zero, true, false, TimeSpan.FromMilliseconds(899)));
        Assert.Equal(
            ArchipelagoRevealAction.Hide,
            ArchipelagoRevealPolicy.Evaluate(true, false, TimeSpan.Zero, true, false, TimeSpan.FromMilliseconds(900)));
        Assert.Equal(
            ArchipelagoRevealAction.None,
            ArchipelagoRevealPolicy.Evaluate(true, false, TimeSpan.Zero, true, true, TimeSpan.FromSeconds(10)));
        Assert.Equal(
            ArchipelagoRevealAction.Hide,
            ArchipelagoRevealPolicy.Evaluate(false, false, TimeSpan.Zero, true, false, TimeSpan.Zero));
    }

    [Fact]
    public void HostSurface_TokensMatchPerModeContract()
    {
        Assert.Equal(488, LayoutTokens.HostSurfaceWidth, Eps);
        Assert.Equal(384, LayoutTokens.HostSurfaceFloatHeight, Eps);
        Assert.Equal(444, LayoutTokens.HostSurfaceDockHeight, Eps);
        Assert.Equal(24, LayoutTokens.HostTopPadding, Eps);
        Assert.Equal(36, LayoutTokens.HostBottomPadding, Eps);
        Assert.Equal(8, LayoutTokens.ArchipelagoGap, Eps);
        Assert.Equal(32, LayoutTokens.ArchipelagoStripHeight, Eps);
        Assert.Equal(76, LayoutTokens.HostFloatBottomAllowance, Eps);
        Assert.Equal(136, LayoutTokens.HostDockBottomAllowance, Eps);
        Assert.Equal(352, LayoutTokens.ArchipelagoTrackWidth, Eps);
        Assert.Equal(56, LayoutTokens.ArchipelagoSlotPitch, Eps);
        Assert.Equal(300, LayoutTokens.ArchipelagoRevealDelayMs);
        Assert.Equal(900, LayoutTokens.ArchipelagoHideDelayMs);

        var floatHost = HostSurface.ForMode(IslandMode.Float);
        var flushHost = HostSurface.ForMode(IslandMode.Flush);
        var dockHost = HostSurface.ForMode(IslandMode.Dock);
        Assert.Equal(384, floatHost.Height, Eps);
        Assert.Equal(384, flushHost.Height, Eps);
        Assert.Equal(444, dockHost.Height, Eps);
        Assert.Equal(76, floatHost.BottomAllowance, Eps);
        Assert.Equal(136, dockHost.BottomAllowance, Eps);
        Assert.Equal(new VisualRect(24, 24, 440, 284), floatHost.ShellEnvelopeLocal);
        Assert.Equal(new VisualRect(24, 24, 440, 284), dockHost.ShellEnvelopeLocal);
        Assert.Equal(new VisualRect(68, 316, 352, 32), floatHost.ArchipelagoStripLocal);
        Assert.Equal(new VisualRect(24, 316, 440, 52), dockHost.DockStripLocal);
        Assert.Equal(new VisualRect(68, 376, 352, 32), dockHost.ArchipelagoStripLocal);
        Assert.True(floatHost.BottomBandLocal.Height > floatHost.BottomPadding);
        Assert.True(dockHost.BottomBandLocal.Height > dockHost.BottomPadding);
    }

    [Fact]
    public void BottomAnchors_LiftShellByBottomAllowance()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var host = HostSurface.Float;

        var expanded = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.BottomCenter),
            monitor);
        Assert.Equal(work.Height - 12 - host.BottomAllowance, expanded.Shell.Bottom, Eps);
        Assert.Equal(work.Height - 12, expanded.Placement.WindowVisual.Bottom, Eps);
        Assert.Equal(work.Height - 12 - 36, expanded.Placement.ArchipelagoStrip.Bottom, Eps);
        Assert.True(expanded.Placement.ArchipelagoStrip.Bottom <= work.Height);

        // The same host position is used for compact and expanded: only the shell morphs.
        var compact = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.BottomCenter),
            monitor);
        Assert.Equal(expanded.Placement.WindowPixel, compact.Placement.WindowPixel);
        Assert.Equal(work.Height - 12 - host.BottomAllowance, compact.Shell.Bottom, Eps);

        // Flush bottom anchors pin the host to the work-area bottom (no 12 DIP offset).
        var flush = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Flush, IslandAnchor.BottomCenter),
            monitor);
        Assert.Equal(work.Height, flush.Placement.WindowVisual.Bottom, Eps);
        Assert.Equal(work.Height - host.BottomAllowance, flush.Shell.Bottom, Eps);
    }

    [Fact]
    public void DockClamp_KeepsStripAndPanelInsideWorkArea()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var expanded = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Dock, IslandAnchor.TopCenter),
            monitor);
        var compact = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.TopCenter),
            monitor);

        Assert.Equal(work.Height - 4, expanded.Placement.WindowVisual.Bottom, Eps);
        Assert.Equal(work.Height - 4 - 136, expanded.Shell.Bottom, Eps);
        Assert.Equal(work.Height - 4 - 76, compact.Shell.Bottom, Eps);
        Assert.Equal(compact.Shell.Y - 8 - 284, expanded.Shell.Y, Eps);
        Assert.Equal(work.Height - 4 - 76, expanded.Placement.DockStrip.Bottom, Eps);
        Assert.Equal(work.Height - 4 - 36, expanded.Placement.ArchipelagoStrip.Bottom, Eps);
        Assert.Equal(expanded.Placement.DockStrip.Bottom + LayoutTokens.ArchipelagoGap, expanded.Placement.ArchipelagoStrip.Y, Eps);
        Assert.True(expanded.Placement.DockStrip.Bottom <= work.Height);
        Assert.True(expanded.Placement.ArchipelagoStrip.Bottom <= work.Height);
        Assert.True(expanded.Placement.IsClamped);
        Assert.True(compact.Placement.IsClamped);
    }

    [Fact]
    public void AnchorMath_HostOverhangsTransparentPaddingButShellStaysAnchored()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var left = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopLeft),
            monitor);
        Assert.Equal(0, left.Shell.X, Eps);
        Assert.Equal(-24, left.Placement.WindowVisual.X, Eps);

        var right = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopRight),
            monitor);
        Assert.Equal(work.Width, right.Shell.Right, Eps);
        Assert.Equal(work.Width + 24, right.Placement.WindowVisual.Right, Eps);

        var center = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter),
            monitor);
        Assert.Equal(work.Width / 2, center.Shell.CenterX, Eps);
        Assert.Equal(12, center.Shell.Y, Eps);
        Assert.Equal(-12, center.Placement.WindowVisual.Y, Eps);

        var flush = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Flush, IslandAnchor.TopCenter),
            monitor);
        Assert.Equal(0, flush.Shell.Y, Eps);
        Assert.Equal(-24, flush.Placement.WindowVisual.Y, Eps);
    }

    [Fact]
    public void LookupWideShell_FitsInsideHostSurface()
    {
        var monitor = Monitor(96);
        var result = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopLeft, compactKind: ActivityKind.Lookup),
            monitor);

        Assert.Equal(480, result.Shell.Width, Eps);
        Assert.True(result.Shell.X >= result.Placement.WindowVisual.X - Eps);
        Assert.True(result.Shell.Right <= result.Placement.WindowVisual.Right + Eps);
        Assert.Equal(4, result.Placement.ShellLocalOffset.X, Eps);
        Assert.Equal(-4, result.Placement.WindowVisual.X, Eps);
    }

    [Fact]
    public void Overrides_MeasureMidAnimationShellWithoutMovingTheHost()
    {
        var monitor = Monitor(96);
        var baseline = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter),
            monitor);
        var overridden = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter) with
            {
                WidthOverride = 300,
                HeightOverride = 150,
            },
            monitor);

        Assert.Equal(300, overridden.Shell.Width, Eps);
        Assert.Equal(150, overridden.Shell.Height, Eps);
        Assert.Equal(baseline.Placement.WindowPixel, overridden.Placement.WindowPixel);
        Assert.Equal(baseline.Placement.WindowVisual, overridden.Placement.WindowVisual);

        // Bottom anchors also keep the host fixed: the shell grows upward from the same bottom.
        var bottomBaseline = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.BottomCenter),
            monitor);
        var bottomOverride = IslandLayout.Calculate(
            Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.BottomCenter) with { HeightOverride = 150 },
            monitor);
        Assert.Equal(bottomBaseline.Placement.WindowPixel, bottomOverride.Placement.WindowPixel);
        Assert.Equal(bottomBaseline.Shell.Bottom, bottomOverride.Shell.Bottom, Eps);
        Assert.Equal(150, bottomOverride.Shell.Height, Eps);
    }

    [Fact]
    public void Expanded_IsUniformly440x284()
    {
        var monitor = Monitor(96);
        foreach (var page in PageRef.BuiltinPages)
        {
            _ = page;
            var result = IslandLayout.Calculate(Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter), monitor);
            Assert.Equal(440, result.Shell.Width, Eps);
            Assert.Equal(284, result.Shell.Height, Eps);
        }

        Assert.Equal(new VisualSize(440, 284), LayoutTokens.ExpandedSize());
        var expandedRelative = LayoutTokens.RadiiFor(ShellForm.Expanded, IslandMode.Float, 284);
        Assert.Equal(28, expandedRelative.TopLeft, Eps);
    }

    [Theory]
    [InlineData(ActivityKind.Idle, 132, 36)]
    [InlineData(ActivityKind.Media, 226, 40)]
    [InlineData(ActivityKind.Volume, 208, 40)]
    [InlineData(ActivityKind.Brightness, 208, 40)]
    [InlineData(ActivityKind.Notification, 400, 96)]
    [InlineData(ActivityKind.Clipboard, 264, 36)]
    [InlineData(ActivityKind.Agent, 236, 36)]
    [InlineData(ActivityKind.Timer, 190, 36)]
    [InlineData(ActivityKind.BatteryLow, 208, 36)]
    [InlineData(ActivityKind.Lookup, 480, 44)]
    [InlineData(ActivityKind.LockKeys, 180, 36)]
    [InlineData(ActivityKind.Device, 240, 44)]
    [InlineData(ActivityKind.Bluetooth, 280, 44)]
    [InlineData(ActivityKind.Focus, 220, 36)]
    [InlineData(ActivityKind.Progress, 236, 36)]
    public void CompactSize_MatchesTokenTable(ActivityKind? kind, double width, double height)
    {
        var size = LayoutTokens.CompactSize(kind);
        Assert.Equal(width, size.Width, Eps);
        Assert.Equal(height, size.Height, Eps);
    }

    [Fact]
    public void Dock_OverridesCompactSizeAndGrowsUpward()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var compact = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.TopCenter), monitor);
        Assert.Equal(440, compact.Shell.Width, Eps);
        Assert.Equal(52, compact.Shell.Height, Eps);
        Assert.Equal(ExpandDirection.Up, compact.ExpandDirection);
        Assert.Equal(work.Height - 4 - 76, compact.Shell.Bottom, Eps);

        var expanded = IslandLayout.Calculate(Request(ShellForm.Expanded, IslandMode.Dock, IslandAnchor.TopCenter), monitor);
        Assert.Equal(ExpandDirection.Up, expanded.ExpandDirection);
        Assert.Equal(compact.Shell.Y - 8 - 284, expanded.Shell.Y, Eps);
        Assert.Equal(work.Height - 4 - 136, expanded.Shell.Bottom, Eps);
    }

    [Fact]
    public void Peek_GrowsOutwardAndDockDoesNotGrow()
    {
        var monitor = Monitor(96);
        var top = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        var topPeek = IslandLayout.Calculate(Request(ShellForm.Peek, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.Equal(top.Shell.Width + 10, topPeek.Shell.Width, Eps);
        Assert.Equal(top.Shell.Height + 6, topPeek.Shell.Height, Eps);
        Assert.Equal(top.Shell.Y, topPeek.Shell.Y, Eps);
        Assert.Equal(top.Shell.CenterX, topPeek.Shell.CenterX, Eps);

        var bottom = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.BottomCenter), monitor);
        var bottomPeek = IslandLayout.Calculate(Request(ShellForm.Peek, IslandMode.Float, IslandAnchor.BottomCenter), monitor);
        Assert.Equal(bottom.Shell.Y - 6, bottomPeek.Shell.Y, Eps);
        Assert.Equal(bottom.Shell.Bottom, bottomPeek.Shell.Bottom, Eps);

        var dock = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.BottomCenter), monitor);
        var dockPeek = IslandLayout.Calculate(Request(ShellForm.Peek, IslandMode.Dock, IslandAnchor.BottomCenter), monitor);
        Assert.Equal(dock.Shell.Width, dockPeek.Shell.Width, Eps);
        Assert.Equal(dock.Shell.Height, dockPeek.Shell.Height, Eps);
    }

    [Fact]
    public void BottomAnchor_StaysInsideWorkArea_RegressionF04()
    {
        var monitor = Monitor(144);
        var work = monitor.WorkAreaDip;
        var result = IslandLayout.Calculate(Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.BottomCenter), monitor);

        Assert.True(result.Shell.Bottom <= work.Height + Eps);
        Assert.Equal(work.Height - 12 - 76, result.Shell.Bottom, Eps);
        Assert.Equal(work.Height - 12, result.Placement.WindowVisual.Bottom, Eps);
        Assert.Equal(
            WindowPlacementConverter.Round(monitor.WorkArea.Y + (result.Placement.WindowVisual.Y * monitor.Scale)),
            result.Placement.WindowPixel.Y);
        Assert.True(result.Placement.ArchipelagoStrip.Bottom <= work.Height + Eps);
    }

    [Fact]
    public void DefaultOffsets_AreModeSpecific()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var floating = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.Equal(12, floating.Shell.Y, Eps);

        var docked = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.BottomCenter), monitor);
        Assert.Equal(work.Height - 4 - 76, docked.Shell.Bottom, Eps);

        var flush = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Flush, IslandAnchor.TopCenter), monitor);
        Assert.Equal(0, flush.Shell.Y, Eps);
    }

    [Fact]
    public void Hidden_ProducesHoverStripAndNoShell()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var top = IslandLayout.Calculate(Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.False(top.IsVisible);
        Assert.True(top.Shell.IsEmpty);
        Assert.Null(top.Archipelago);
        var strip = top.HoverStrip;
        Assert.NotNull(strip);
        Assert.Equal(132, strip.Value.Width, Eps);
        Assert.Equal(10, strip.Value.Height, Eps);
        Assert.Equal(0, strip.Value.Y, Eps);
        Assert.Equal(0, top.Placement.WindowVisual.Y, Eps);
        Assert.Equal(384, top.Placement.WindowSizeDip.Height, Eps);

        var bottom = IslandLayout.Calculate(Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.BottomLeft), monitor);
        Assert.NotNull(bottom.HoverStrip);
        Assert.Equal(0, bottom.HoverStrip!.Value.X, Eps);
        Assert.Equal(work.Height - 10, bottom.HoverStrip.Value.Y, Eps);
        Assert.Equal(work.Height, bottom.Placement.WindowVisual.Bottom, Eps);
        Assert.Equal(384 - 10, bottom.Placement.ShellLocalOffset.Y, Eps);

        var noStrip = IslandLayout.Calculate(
            Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.TopCenter) with { HiddenHoverStrip = false },
            monitor);
        Assert.Null(noStrip.HoverStrip);
        Assert.Equal(384, noStrip.Placement.WindowSizeDip.Height, Eps);
    }

    [Fact]
    public void Expanded_NoRailContentIs400x252At20Inset()
    {
        var monitor = Monitor(96);
        var result = IslandLayout.Calculate(Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter), monitor);

        Assert.False(result.HasRail);
        Assert.True(result.Rail.IsEmpty);
        Assert.Equal(440, result.Shell.Width, Eps);
        Assert.Equal(284, result.Shell.Height, Eps);
        Assert.Equal(400, result.Content.Width, Eps);
        Assert.Equal(252, result.Content.Height, Eps);
        Assert.Equal(result.Shell.X + 20, result.Content.X, Eps);
        Assert.Equal(result.Shell.Y + 16, result.Content.Y, Eps);
        Assert.Equal(result.Shell.Right - 20, result.Content.Right, Eps);
        Assert.Equal(result.Shell.Bottom - 16, result.Content.Bottom, Eps);
    }

    [Fact]
    public void NoRailTokens_AreCurrentAndRailTokensStayObsoleteCompatible()
    {
        Assert.Equal(440, LayoutTokens.ExpandedWidth, Eps);
        Assert.Equal(284, LayoutTokens.ExpandedHeight, Eps);
        Assert.Equal(20, LayoutTokens.ContentHorizontalInset, Eps);
        Assert.Equal(16, LayoutTokens.ContentVerticalInset, Eps);
        Assert.Equal(400, LayoutTokens.ContentWidth, Eps);
        Assert.Equal(400, LayoutTokens.ContentUsableWidth, Eps);
        Assert.Equal(252, LayoutTokens.ContentUsableHeight, Eps);
        Assert.Equal(LayoutTokens.ContentVerticalInset, LayoutTokens.ContentInset, Eps);

        // Legacy rail tokens keep their old values for source compatibility but are unused.
        Assert.Equal(48, LayoutTokens.RailWidth, Eps);
    }

    [Fact]
    public void Compact_HasNoRailAndContentEqualsShell()
    {
        var monitor = Monitor(96);
        var result = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.False(result.HasRail);
        Assert.True(result.Rail.IsEmpty);
        Assert.Equal(result.Shell, result.Content);
    }

    [Fact]
    public void Offsets_AreClampedInsideWorkArea()
    {
        var monitor = Monitor(96);
        var work = monitor.WorkAreaDip;
        var result = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopLeft, offsetX: 10_000, offsetY: 10_000),
            monitor);
        Assert.Equal(work.Width - result.Shell.Width, result.Shell.X, Eps);
        Assert.True(result.Shell.Bottom <= work.Height + Eps);
        Assert.Equal(work.Height, result.Placement.WindowVisual.Bottom, Eps);

        var negative = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopLeft, offsetX: -500, offsetY: -500),
            monitor);
        Assert.Equal(0, negative.Shell.X, Eps);
        Assert.Equal(0, negative.Shell.Y, Eps);
        Assert.Equal(-24, negative.Placement.WindowVisual.X, Eps);
        Assert.Equal(-24, negative.Placement.WindowVisual.Y, Eps);
    }

    [Fact]
    public void ToPhysical_AppliesMonitorOriginAndScale()
    {
        var monitor = Monitor(144);
        var physical = WindowPlacementConverter.ToPhysical(new VisualRect(10, 20, 440, 284), monitor);

        Assert.Equal(1920 + 15, physical.X);
        Assert.Equal(30, physical.Y);
        Assert.Equal(660, physical.Width);
        Assert.Equal(426, physical.Height);
    }

    [Fact]
    public void LegacyPlaceWindow_UsesFloatHostSurfaceForSourceCompatibility()
    {
        var monitor = Monitor(96);
        var placement = WindowPlacementConverter.PlaceWindow(new VisualRect(100, 100, 440, 284), monitor);

        Assert.Equal(488, placement.WindowSizeDip.Width, Eps);
        Assert.Equal(384, placement.WindowSizeDip.Height, Eps);
        Assert.Equal(new VisualRect(76, 76, 488, 384), placement.WindowVisual);
        Assert.Equal(new VisualPoint(24, 24), placement.ShellLocalOffset);
    }

    [Fact]
    public void WindowPlacement_UsesPerModeHostSizeOnEveryForm()
    {
        var monitor = Monitor(192);
        foreach (var mode in Enum.GetValues<IslandMode>())
        {
            foreach (var form in Enum.GetValues<ShellForm>())
            {
                var result = IslandLayout.Calculate(
                    Request(form, mode, IslandAnchor.TopCenter, compactKind: ActivityKind.Media),
                    monitor);
                var expectedHeight = mode == IslandMode.Dock ? 444 : 384;
                Assert.Equal(976, result.Placement.WindowPixel.Width);
                Assert.Equal(WindowPlacementConverter.Ceil(expectedHeight * monitor.Scale), result.Placement.WindowPixel.Height);
                Assert.Equal(488, result.Placement.WindowSizeDip.Width, Eps);
                Assert.Equal(expectedHeight, result.Placement.WindowSizeDip.Height, Eps);
            }
        }
    }

    [Fact]
    public void Squircle_UsesSuperellipseExponent()
    {
        var pill = ShellGeometry.ForShell(new VisualRect(0, 0, 132, 36), CornerRadii.Uniform(18));
        Assert.Equal(2.0, pill.ExponentK, 1e-9);

        var shell = new VisualRect(0, 0, 440, 284);
        var radii = CornerRadii.Uniform(28);
        var squircle = ShellGeometry.ForShell(shell, radii);
        var halfMin = 142.0;
        var expectedK = 2 + ((1 - (28 / halfMin)) * 1.22);
        Assert.Equal(expectedK, squircle.ExponentK, 1e-9);

        Assert.True(squircle.Contains(shell.CenterX, shell.CenterY));
        Assert.True(squircle.Contains(shell.X + 220, shell.Y + 1));
        Assert.False(squircle.Contains(shell.X + 1, shell.Y + 1));
        Assert.False(squircle.Contains(-5, -5));

        var outline = squircle.SampleOutline(15);
        Assert.Equal(60, outline.Length);
    }

    [Fact]
    public void Squircle_OutlineIsClockwiseClosedAndDirectlyConnectable()
    {
        var shell = new VisualRect(24, 24, 440, 284);
        var radii = CornerRadii.Uniform(28);
        var geometry = ShellGeometry.Create(shell, radii);
        const int steps = 15;
        var outline = geometry.SampleOutline(steps);

        Assert.Equal(steps * 4, outline.Length);

        // Corner traversal order: TL (left->top), TR (top->right), BR (right->bottom),
        // BL (bottom->left). The previous implementation emitted TL top->left and TR right->top,
        // so joining consecutive samples cut across the top of the shell.
        Assert.Equal(shell.X, outline[0].X, Eps);
        Assert.InRange(outline[steps - 1].Y, shell.Y, shell.Y + 0.5);
        Assert.Equal(shell.Y, outline[steps].Y, Eps);
        Assert.InRange(outline[(2 * steps) - 1].X, shell.Right - 0.5, shell.Right + Eps);
        Assert.Equal(shell.Right, outline[2 * steps].X, Eps);
        Assert.InRange(outline[(3 * steps) - 1].Y, shell.Bottom - 0.5, shell.Bottom + Eps);
        Assert.Equal(shell.Bottom, outline[3 * steps].Y, Eps);
        Assert.InRange(outline[(4 * steps) - 1].X, shell.X - Eps, shell.X + 0.5);

        // A clockwise traversal has a strictly increasing unwrapped polar angle around the centre,
        // with no step larger than a half turn (a reversed/crossed corner produces a >pi jump).
        var centre = new VisualPoint(shell.CenterX, shell.CenterY);
        var previous = Angle(outline[0], centre);
        var swept = 0.0;
        for (var i = 1; i < outline.Length; i++)
        {
            var angle = Angle(outline[i], centre);
            var delta = PositiveDelta(previous, angle);
            Assert.InRange(delta, 0, Math.PI - Eps);
            swept += delta;
            previous = angle;
        }

        var closing = PositiveDelta(previous, Angle(outline[0], centre));
        Assert.InRange(closing, 0, Math.PI - Eps);
        swept += closing;
        Assert.Equal(2 * Math.PI, swept, 2);

        // Every chord (including the closing segment) stays inside the convex shell; a crossed
        // outline would have at least one chord outside the shape.
        for (var i = 0; i < outline.Length; i++)
        {
            var next = outline[(i + 1) % outline.Length];
            var midX = (outline[i].X + next.X) / 2;
            var midY = (outline[i].Y + next.Y) / 2;
            // Pull the boundary-edge midpoints one DIP inward (Contains is half-open on edges).
            var x = midX + ((centre.X - midX) * 0.02);
            var y = midY + ((centre.Y - midY) * 0.02);
            Assert.True(geometry.Contains(x, y), $"chord {i}->{(i + 1) % outline.Length} leaves the shell");
        }
    }

    private static double Angle(VisualPoint point, VisualPoint centre)
        => Math.Atan2(point.Y - centre.Y, point.X - centre.X);

    private static double PositiveDelta(double from, double to)
    {
        var delta = to - from;
        while (delta < 0)
        {
            delta += 2 * Math.PI;
        }

        while (delta >= 2 * Math.PI)
        {
            delta -= 2 * Math.PI;
        }

        return delta;
    }

    [Fact]
    public void FlushTopCorners_AreSquareForHitTesting()
    {
        var monitor = Monitor(96);
        var result = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Flush, IslandAnchor.TopCenter), monitor);
        Assert.True(result.Geometry.Contains(result.Shell.X + 1, result.Shell.Y + 1));
        Assert.True(result.Geometry.Contains(result.Shell.Right - 1, result.Shell.Y + 1));
    }

    [Fact]
    public void RequestFromState_MapsGhostsFromActivityCount()
    {
        var state = IslandState.Hidden with { Kind = IslandStateKind.Compact, ActivityCount = 3, BadgeCount = 1, ContentKind = ActivityKind.Notification };
        var request = IslandLayoutRequest.FromState(state, IslandMode.Float, IslandAnchor.TopCenter, stacking: StackingStrategy.Stacked);

        Assert.Equal(ShellForm.Compact, request.Form);
        Assert.Equal(1, request.GhostCount); // 3 live - 1 hidden - 1 primary = 1 ghost
        Assert.Equal(ActivityKind.Notification, request.CompactKind);
        Assert.Equal(0, request.PagePosition, Eps);
        Assert.True(request.ArchipelagoVisible);

        var expanded = state with { Kind = IslandStateKind.Expanded };
        var expandedRequest = IslandLayoutRequest.FromState(expanded, pagePosition: 2.5, archipelagoVisible: false);
        Assert.Equal(ShellForm.Expanded, expandedRequest.Form);
        Assert.Null(expandedRequest.CompactKind);
        Assert.Equal(2.5, expandedRequest.PagePosition, Eps);
        Assert.False(expandedRequest.ArchipelagoVisible);
    }

    [Fact]
    public void Radii_AreDerivedPerFormAndMode()
    {
        Assert.Equal(CornerRadii.Zero, LayoutTokens.RadiiFor(ShellForm.Hidden, IslandMode.Float, 36));
        Assert.Equal(12, LayoutTokens.RadiiFor(ShellForm.Compact, IslandMode.Dock, 52).TopLeft, Eps);
        Assert.Equal(16, LayoutTokens.RadiiFor(ShellForm.Expanded, IslandMode.Dock, 284).TopLeft, Eps);
        Assert.Equal(44, LayoutTokens.RadiiFor(ShellForm.Compact, IslandMode.Float, 120).TopLeft, Eps);
        Assert.Equal(14, LayoutTokens.ConcentricRadius(28, 14), Eps);
        Assert.Equal(0, LayoutTokens.ConcentricRadius(12, 16), Eps);
    }
}

