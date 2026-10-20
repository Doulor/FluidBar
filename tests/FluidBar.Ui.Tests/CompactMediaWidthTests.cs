using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Compact media pill after the changes: no play/pause control anywhere on the pill, and the lyric form
/// widens the shell smoothly instead of ellipsising the line.
/// </summary>
public sealed class CompactMediaWidthTests
{
    private const float TokenWidth = 226f;

    private static IslandRenderState MediaPill(double width) => new()
    {
        Mode = IslandMode.Float,
        Form = ShellForm.Compact,
        ShellWidth = width,
        ShellHeight = 40,
        Compact = new CompactActivity(ActivityKind.Media, new CompactModel
        {
            Title = "Miss You a Little",
            Subtitle = "Bryce Vine",
            IsActive = true,
            ShowLyrics = true,
            LyricLine = "I miss you a little all the time",
        }),
    };

    [Fact]
    public void Compact_media_pill_has_no_play_control_at_the_old_button_position()
    {
        var state = MediaPill(TokenWidth);
        var shell = IslandMetrics.ShellRect(state);

        // The right-hand 26 DIP circle used to be the play/pause control; it must be plain pill now.
        var oldButton = SkiaIslandHitTest.Hit(state, shell.Right - 20, shell.MidY);
        Assert.Equal(IslandHitKind.Compact, oldButton.Kind);
        Assert.Equal(IslandPageControl.None, oldButton.Control);

        // Nothing else in the pill is a control either.
        for (var x = shell.Left + 4; x < shell.Right - 2; x += 10)
        {
            Assert.Equal(IslandPageControl.None, SkiaIslandHitTest.Hit(state, x, shell.MidY).Control);
        }
    }

    [Fact]
    public void Compact_media_hit_area_follows_the_widened_shell()
    {
        var wide = MediaPill(360);
        var shell = IslandMetrics.ShellRect(wide);

        // The widened pill is a valid expand target all the way to its new right edge…
        Assert.Equal(IslandHitKind.Compact, SkiaIslandHitTest.Hit(wide, shell.Right - 6, shell.MidY).Kind);
        // …and nothing exists beyond it.
        Assert.NotEqual(IslandHitKind.Compact, SkiaIslandHitTest.Hit(wide, shell.Right + 4, shell.MidY).Kind);
    }

    [Fact]
    public void Adaptive_pill_width_grows_then_shrinks_monotonically()
    {
        using var motion = new SkiaIslandMotion();

        motion.SetTarget(Target(TokenWidth));
        Assert.Equal(TokenWidth, motion.Width, 0.5);

        // A long lyric line must widen the pill over several frames, never in one jump.
        var wide = 320.0;
        motion.SetTarget(Target(wide));
        var growth = Sample(motion);
        Assert.True(growth.Count > 2, "the width change must be animated over multiple frames");
        Assert.True(growth[0] < wide - 1, "the width must not jump to the new target in one frame");
        AssertMonotonic(growth, increasing: true);
        Assert.Equal(wide, growth[^1], 1.5);

        // The next (shorter) line shrinks it back the same way.
        motion.SetTarget(Target(240));
        var shrink = Sample(motion);
        Assert.True(shrink.Count > 2);
        AssertMonotonic(shrink, increasing: false);
        Assert.Equal(240, shrink[^1], 1.5);
    }

    private static void AssertMonotonic(List<double> samples, bool increasing)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var delta = samples[index] - samples[index - 1];
            Assert.True(
                increasing ? delta >= -0.51 : delta <= 0.51,
                $"width must change monotonically ({samples[index - 1]:0.##} → {samples[index]:0.##} at frame {index})");
        }
    }

    private static List<double> Sample(SkiaIslandMotion motion)
    {
        var samples = new List<double>();
        var frames = 0;
        while (!motion.IsAtRest && frames < 240)
        {
            motion.Step(1.0 / 60.0);
            samples.Add(motion.Width);
            frames++;
        }

        Assert.True(frames < 240, "the width transition must settle");
        return samples;
    }

    private static SkiaMotionTarget Target(double compactWidth) => new(
        ShellForm.Compact,
        IslandMode.Float,
        IslandAnchor.TopCenter,
        new VisualSize(compactWidth, 40),
        0,
        0,
        false);
}
