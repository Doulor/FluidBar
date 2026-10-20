using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Frame-by-frame checks of the prototype2 shell motion. The prototype is a CSS transition
/// (width/height 380 ms cubic-bezier(.3,.8,.25,1), radius 260 ms ease-out, compact/expanded/chrome
/// fades 90/220/160 ms with their own delays), so these tests step <see cref="SkiaIslandMotion"/> and
/// compare the sampled values with the browser-measured ones (Playwright, Edge, prototype2).
/// </summary>
public sealed class ShellCssMotionTests
{
    private static readonly VisualSize Idle = LayoutTokens.CompactSize(null);

    [Fact]
    public void Expand_matches_the_prototype2_frame_samples()
    {
        var run = new MotionRun(ExpandedMotion());
        Assert.Equal(419.141, run.WidthAt(191.2), 0.75);
        Assert.Equal(267.172, run.HeightAt(191.1), 0.75);

        run = new MotionRun(ExpandedMotion());
        Assert.Equal(440, run.WidthAt(396.9), 0.001);
        Assert.Equal(284, run.HeightAt(396.9), 0.001);
    }

    [Fact]
    public void Expand_has_no_spring_overshoot()
    {
        var motion = ExpandedMotion();
        var run = new MotionRun(motion);
        var previous = 0.0;
        var samples = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            run.Advance(1.0 / 120.0);
            var width = motion.Width;
            Assert.True(width >= previous - 1e-9, $"shell width regressed at frame {i}: {width:0.###}");
            previous = width;
            samples.Add(width);
        }

        Assert.Equal(440, samples[^1], 0.5);
        Assert.True(samples.Distinct().Count() > 40, "the shell should change every frame, not snap");
    }

    [Fact]
    public void Expand_layer_fades_match_the_prototype2_timeline()
    {
        var motion = ExpandedMotion();
        var run = new MotionRun(motion);

        // t=0: compact visible, panel delayed.
        Assert.Equal(1, motion.CompactContentOpacity, 3);
        Assert.Equal(0, motion.ExpandedContentOpacity, 3);
        Assert.Equal(0, motion.ChromeOpacity, 3);

        run.AdvanceTo(0.050);
        Assert.Equal(0, motion.ExpandedContentOpacity, 3); // 60 ms content stagger
        Assert.Equal(0, motion.ChromeOpacity, 3);          // 120 ms chrome stagger

        run.AdvanceTo(0.080);
        Assert.True(motion.ExpandedContentOpacity > 0, "content must follow the container");
        Assert.Equal(0, motion.ChromeOpacity, 3);

        run.AdvanceTo(0.150);
        Assert.True(motion.ExpandedContentOpacity > 0.5);
        Assert.True(motion.ChromeOpacity > 0);

        run.AdvanceTo(0.400);
        Assert.Equal(1, motion.ExpandedContentOpacity, 0.01);
        Assert.Equal(1, motion.ChromeOpacity, 0.01);
        Assert.Equal(0, motion.CompactContentOpacity, 3);
    }

    [Fact]
    public void Collapse_layer_fades_match_the_prototype2_timeline()
    {
        var motion = ExpandedMotion();
        new MotionRun(motion).AdvanceTo(0.45); // settle the expansion
        motion.SetTarget(CompactTarget());
        var run = new MotionRun(motion);

        Assert.Equal(0, motion.CompactContentOpacity, 3);
        Assert.Equal(1, motion.ExpandedContentOpacity, 3);

        run.AdvanceTo(0.030);
        Assert.Equal(0, motion.CompactContentOpacity, 3); // 40 ms delay

        run.AdvanceTo(0.0548);
        Assert.Equal(0.387, motion.CompactContentOpacity, 0.005);
        Assert.Equal(1 - ShellTransition.EaseOut.Evaluate(0.0548 / 0.140),
            motion.ExpandedContentOpacity, 0.005); // 140 ms ease-out

        run.AdvanceTo(0.200);
        Assert.Equal(0, motion.ExpandedContentOpacity, 3);
        Assert.Equal(0, motion.ChromeOpacity, 3);

        run.AdvanceTo(0.380);
        Assert.Equal(132, motion.Width, 0.75);
        Assert.Equal(36, motion.Height, 0.75);
    }

    [Fact]
    public void Radius_morphs_over_260_ms_from_stadium_to_panel_radius()
    {
        var motion = ExpandedMotion();
        Assert.Equal(18, motion.ShellRadii.TopLeft, 0.01);

        new MotionRun(motion).AdvanceTo(0.260);
        Assert.Equal(30, motion.ShellRadii.TopLeft, 0.01);
        Assert.Equal(30, motion.ShellRadii.BottomRight, 0.01);
    }

    [Fact]
    public void Reduced_motion_snaps_the_shell()
    {
        using var motion = new SkiaIslandMotion(new MotionProfile { ReducedMotion = true });
        motion.SetTarget(CompactTarget());
        motion.SetTarget(ExpandedTarget());

        Assert.Equal(440, motion.Width, 0.001);
        Assert.Equal(284, motion.Height, 0.001);
        Assert.Equal(1, motion.ExpandedContentOpacity, 3);
        Assert.Equal(0, motion.CompactContentOpacity, 3);
        Assert.Equal(30, motion.ShellRadii.TopLeft, 0.01);
        Assert.True(motion.IsAtRest);
    }

    [Fact]
    public void Settles_without_further_frame_work()
    {
        var motion = ExpandedMotion();
        var run = new MotionRun(motion);
        var frames = 0;
        while (!motion.IsAtRest && frames < 600)
        {
            run.Advance(1.0 / 120.0);
            frames++;
        }

        Assert.True(motion.IsAtRest);
        Assert.True(frames <= 50, $"380 ms transition should settle in <= 50 frames of 1/120 s, took {frames}");
    }

    [Fact]
    public void Writes_the_shell_motion_frame_report()
    {
        var motion = ExpandedMotion();
        var run = new MotionRun(motion);
        var rows = new List<string> { "phase,milliseconds,widthDip,heightDip,radiusTopLeftDip,compactLayerOpacity,expandedLayerOpacity,chromeOpacity" };

        for (var frame = 0; frame <= 27; frame++)
        {
            if (frame > 0)
            {
                run.AdvanceTo(frame * (1000.0 / 60.0) / 1000.0);
            }

            rows.Add(Row("expand", frame * (1000.0 / 60.0), motion));
        }

        motion.SetTarget(CompactTarget());
        var collapse = new MotionRun(motion);
        for (var frame = 0; frame <= 27; frame++)
        {
            if (frame > 0)
            {
                collapse.AdvanceTo(frame * (1000.0 / 60.0) / 1000.0);
            }

            rows.Add(Row("collapse", frame * (1000.0 / 60.0), motion));
        }

        var root = FindRepoRoot();
        var reportDir = Path.Combine(root, "build", "reports", "island-body");
        Directory.CreateDirectory(reportDir);
        File.WriteAllLines(Path.Combine(reportDir, "shell-motion-frames.csv"), rows);
        Assert.True(rows.Count > 50);
        Assert.StartsWith("expand,383.3", rows[1 + 23]);
    }

    private static string Row(string phase, double milliseconds, SkiaIslandMotion motion)
        => string.Join(',',
            phase,
            milliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            motion.Width.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            motion.Height.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            motion.ShellRadii.TopLeft.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            motion.CompactContentOpacity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
            motion.ExpandedContentOpacity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
            motion.ChromeOpacity.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluidBar.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static SkiaIslandMotion ExpandedMotion()
    {
        var motion = new SkiaIslandMotion();
        motion.SetTarget(CompactTarget());
        motion.SetTarget(ExpandedTarget());
        return motion;
    }

    private static SkiaMotionTarget CompactTarget() => new(
        ShellForm.Compact, IslandMode.Float, IslandAnchor.TopCenter, Idle, 0, 0, false);

    private static SkiaMotionTarget ExpandedTarget() => new(
        ShellForm.Expanded, IslandMode.Float, IslandAnchor.TopCenter, Idle, 0, 0, false);

    /// <summary>Drives a motion instance on a monotonic clock so tests can sample absolute times.</summary>
    private sealed class MotionRun
    {
        private readonly SkiaIslandMotion _motion;
        private double _now;

        public MotionRun(SkiaIslandMotion motion) => _motion = motion;

        public void Advance(double seconds)
        {
            var remaining = seconds;
            while (remaining > 1e-9)
            {
                var delta = Math.Min(1.0 / 120.0, remaining);
                _motion.Step(delta);
                remaining -= delta;
                _now += delta;
            }
        }

        public void AdvanceTo(double seconds)
        {
            var target = Math.Max(seconds, _now);
            Advance(target - _now);
        }

        public double WidthAt(double milliseconds)
        {
            AdvanceTo(milliseconds / 1000.0);
            return _motion.Width;
        }

        public double HeightAt(double milliseconds)
        {
            AdvanceTo(milliseconds / 1000.0);
            return _motion.Height;
        }
    }
}
