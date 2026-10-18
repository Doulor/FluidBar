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
        ActivityKind? compactKind = ActivityKind.Notification)
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
        };

    private static bool IsBottom(IslandAnchor anchor)
        => anchor is IslandAnchor.BottomLeft or IslandAnchor.BottomCenter or IslandAnchor.BottomRight;

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

        foreach (var form in new[] { ShellForm.Compact, ShellForm.Expanded })
        {
            var result = IslandLayout.Calculate(Request(form, mode, anchor, 2, stacking), monitor);
            var context = $"{form}/{mode}/{anchor}@{dpi}/{stacking}";

            Assert.True(result.IsVisible, context);
            Assert.True(result.Shell.X >= -Eps, context);
            Assert.True(result.Shell.Y >= -Eps, context);
            Assert.True(result.Shell.Right <= work.Width + Eps, context);
            Assert.True(result.Shell.Bottom <= work.Height + Eps, context);

            var expectedWidth = form == ShellForm.Expanded ? 440 : mode == IslandMode.Dock ? 440 : 248;
            var expectedHeight = form == ShellForm.Expanded ? 284 : mode == IslandMode.Dock ? 52 : 36;
            Assert.Equal(expectedWidth, result.Shell.Width, Eps);
            Assert.Equal(expectedHeight, result.Shell.Height, Eps);

            var expectedGhosts = stacking == StackingStrategy.LatestOnly ? 0 : 2;
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

            // Fixed host window: always 488x332 DIP, whatever the shell form is.
            Assert.True(WindowPlacementConverter.Ceil(488 * scale) == result.Placement.WindowPixel.Width, context);
            Assert.True(WindowPlacementConverter.Ceil(332 * scale) == result.Placement.WindowPixel.Height, context);
            Assert.Equal(24, result.Placement.ShellLocalOffset.X, Eps);
            Assert.Equal(24, result.Placement.ShellLocalOffset.Y, Eps);

            // Exactly one conversion, padding applied on BOTH axes (v1 F-04 regression).
            var expectedWindowX = WindowPlacementConverter.Round(monitor.WorkArea.X + ((result.Shell.X - 24) * scale));
            var expectedWindowY = WindowPlacementConverter.Round(monitor.WorkArea.Y + ((result.Shell.Y - 24) * scale));
            Assert.True(expectedWindowX == result.Placement.WindowPixel.X, context + " window X");
            Assert.True(expectedWindowY == result.Placement.WindowPixel.Y, context + " window Y");

            // Horizontal anchor placement.
            switch (anchor)
            {
                case IslandAnchor.TopLeft or IslandAnchor.BottomLeft:
                    Assert.Equal(0, result.Shell.X, Eps);
                    break;
                case IslandAnchor.TopRight or IslandAnchor.BottomRight:
                    Assert.Equal(work.Width, result.Shell.Right, Eps);
                    break;
                default:
                    Assert.Equal(work.Width / 2, result.Shell.CenterX, Eps);
                    break;
            }

            // Vertical anchor placement per mode.
            if (mode == IslandMode.Dock)
            {
                if (form == ShellForm.Expanded)
                {
                    Assert.Equal(work.Height - 52 - 4 - 8, result.Shell.Bottom, Eps);
                }
                else
                {
                    Assert.Equal(work.Height - 4, result.Shell.Bottom, Eps);
                }
            }
            else if (mode == IslandMode.Flush)
            {
                if (IsBottom(anchor))
                {
                    Assert.Equal(work.Height, result.Shell.Bottom, Eps);
                }
                else
                {
                    Assert.Equal(0, result.Shell.Y, Eps);
                }
            }
            else if (IsBottom(anchor))
            {
                Assert.Equal(work.Height - 12, result.Shell.Bottom, Eps);
            }
            else
            {
                Assert.Equal(12, result.Shell.Y, Eps);
            }

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
            }
            else
            {
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
                        Assert.Equal(18, result.Radii.TopLeft, Eps);
                        break;
                }
            }
        }
    }

    [Fact]
    public void Expanded_IsUniformly440x284()
    {
        var monitor = Monitor(96);
        foreach (var page in Enum.GetValues<IslandPage>())
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
    [InlineData(ActivityKind.Notification, 248, 36)]
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
        var compact = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.TopCenter), monitor);
        Assert.Equal(440, compact.Shell.Width, Eps);
        Assert.Equal(52, compact.Shell.Height, Eps);
        Assert.Equal(ExpandDirection.Up, compact.ExpandDirection);

        var expanded = IslandLayout.Calculate(Request(ShellForm.Expanded, IslandMode.Dock, IslandAnchor.TopCenter), monitor);
        Assert.Equal(ExpandDirection.Up, expanded.ExpandDirection);
        Assert.Equal(compact.Shell.Y - 8 - 284, expanded.Shell.Y, Eps);
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
        Assert.Equal(work.Height - 12, result.Shell.Bottom, Eps);
        // The transparent host window may overhang, but the shell must be fully inside.
        Assert.True(result.Shell.Y >= 0);
        Assert.Equal(
            WindowPlacementConverter.Round(monitor.WorkArea.Y + ((result.Shell.Y - 24) * monitor.Scale)),
            result.Placement.WindowPixel.Y);
    }

    [Fact]
    public void DefaultOffsets_AreModeSpecific()
    {
        var monitor = Monitor(96);
        var floating = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.Equal(12, floating.Shell.Y, Eps);

        var docked = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Dock, IslandAnchor.BottomCenter), monitor);
        Assert.Equal(monitor.WorkAreaHeightDip - 52 - 4, docked.Shell.Y, Eps);

        var flush = IslandLayout.Calculate(Request(ShellForm.Compact, IslandMode.Flush, IslandAnchor.TopCenter), monitor);
        Assert.Equal(0, flush.Shell.Y, Eps);
    }

    [Fact]
    public void Hidden_ProducesHoverStripAndNoShell()
    {
        var monitor = Monitor(96);
        var top = IslandLayout.Calculate(Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.TopCenter), monitor);
        Assert.False(top.IsVisible);
        Assert.True(top.Shell.IsEmpty);
        var strip = top.HoverStrip;
        Assert.NotNull(strip);
        Assert.Equal(132, strip.Value.Width, Eps);
        Assert.Equal(10, strip.Value.Height, Eps);
        Assert.Equal(0, strip.Value.Y, Eps);

        var bottom = IslandLayout.Calculate(Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.BottomLeft), monitor);
        Assert.NotNull(bottom.HoverStrip);
        Assert.Equal(0, bottom.HoverStrip!.Value.X, Eps);
        Assert.Equal(monitor.WorkAreaHeightDip - 10, bottom.HoverStrip.Value.Y, Eps);

        var noStrip = IslandLayout.Calculate(
            Request(ShellForm.Hidden, IslandMode.Float, IslandAnchor.TopCenter) with { HiddenHoverStrip = false },
            monitor);
        Assert.Null(noStrip.HoverStrip);
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
        Assert.Equal(work.Height - result.Shell.Height, result.Shell.Y, Eps);

        var negative = IslandLayout.Calculate(
            Request(ShellForm.Compact, IslandMode.Float, IslandAnchor.TopLeft, offsetX: -500, offsetY: -500),
            monitor);
        Assert.Equal(0, negative.Shell.X, Eps);
        Assert.Equal(0, negative.Shell.Y, Eps);
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
    public void WindowPlacement_UsesFixedHostSizeOnEveryForm()
    {
        var monitor = Monitor(192);
        foreach (var form in Enum.GetValues<ShellForm>())
        {
            var result = IslandLayout.Calculate(
                Request(form, IslandMode.Float, IslandAnchor.TopCenter, compactKind: ActivityKind.Media),
                monitor);
            Assert.Equal(976, result.Placement.WindowPixel.Width);
            Assert.Equal(664, result.Placement.WindowPixel.Height);
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

        var expanded = state with { Kind = IslandStateKind.Expanded };
        var expandedRequest = IslandLayoutRequest.FromState(expanded);
        Assert.Equal(ShellForm.Expanded, expandedRequest.Form);
        Assert.Null(expandedRequest.CompactKind);
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

    [Fact]
    public void Overrides_MeasureMidAnimationShell()
    {
        var monitor = Monitor(96);
        var request = Request(ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter) with
        {
            WidthOverride = 300,
            HeightOverride = 150,
        };
        var result = IslandLayout.Calculate(request, monitor);
        Assert.Equal(300, result.Shell.Width, Eps);
        Assert.Equal(150, result.Shell.Height, Eps);
    }
}
