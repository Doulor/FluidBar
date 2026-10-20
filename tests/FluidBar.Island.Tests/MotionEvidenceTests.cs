using FluidBar.Core.Activities;
using FluidBar.Core.Interaction;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Deterministic visual evidence for the animation pass. Normal CI skips it unless
/// FLUIDBAR_MOTION_EVIDENCE=1; the audit runner sets that variable and writes production PNGs.
/// </summary>
public sealed class MotionEvidenceTests
{
    [Fact]
    public void Emit_animation_before_after_frames()
    {
        if (Environment.GetEnvironmentVariable("FLUIDBAR_MOTION_EVIDENCE") != "1")
        {
            return;
        }

        var dir = Path.Combine(
            Environment.GetEnvironmentVariable("FLUIDBAR_EVIDENCE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "fluidbar-evidence", "motion-rendered"));
        Directory.CreateDirectory(dir);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);

        void Save(string name, IslandRenderState state)
        {
            using var bitmap = renderer.RenderToBitmap(state, IslandTestFixtures.Scale);
            IslandTestFixtures.SavePng(bitmap, Path.Combine(dir, name + ".png"));
        }

        var idle = IslandTestFixtures.Idle();
        Save("press-before", idle);
        Save("press-after", idle with { PressDepth = 1, PressScale = MotionTokens.PressScale });

        var expanded = IslandTestFixtures.Expanded(0);
        Save("expand-before", expanded with { Form = ShellForm.Compact, ShellWidth = 226, ShellHeight = 40 });
        Save("expand-mid", expanded with { ShellWidth = 332, ShellHeight = 162, CompactLayerOpacity = 0.3, ExpandedLayerOpacity = 0.3 });
        Save("expand-after", expanded);

        Save("page-before", IslandTestFixtures.Expanded(0, pagePosition: 0));
        Save("page-mid", IslandTestFixtures.Expanded(0, pagePosition: 0.45));
        Save("page-after", IslandTestFixtures.Expanded(0, pagePosition: 1));

        var media = IslandTestFixtures.MediaCompact();
        Save("breath-before", media with { MotionPhase = 0 });
        Save("breath-after", media with { MotionPhase = 1.2 });

        var split = IslandTestFixtures.MediaCompact() with
        {
            SplitProgress = 0.55,
            SplitIsletHeight = 40,
            SplitIslets =
            [
                new IslandSplitIslet(ActivityKind.Agent, IconKind.Agent, "Agent", "运行中", PageRef.Agent, 0, null, true),
                new IslandSplitIslet(ActivityKind.Media, IconKind.Media, "Media", "Midnight Drive", PageRef.Media, 0),
            ],
        };
        Save("split-morph-mid", split);
        Save("split-morph-settled", split with { SplitProgress = 1 });
    }
}
