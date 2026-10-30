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
        Assert.Equal(448.984, run.WidthAt(191.2), 0.75);
        Assert.Equal(267.172, run.HeightAt(191.1), 0.75);

        run = new MotionRun(ExpandedMotion());
        Assert.Equal(472, run.WidthAt(396.9), 0.001);
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

        Assert.Equal(472, samples[^1], 0.5);
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

        run.AdvanceTo(0.055);
        Assert.Equal(0, motion.ExpandedContentOpacity, 3); // 60 ms delay

        run.AdvanceTo(0.0900);
        Assert.Equal(0, motion.CompactContentOpacity, 3); // 90 ms ease-in complete
        // 30 ms into the 220 ms content fade (ease-out, cubic-bezier(.2,.8,.3,1)): sampled 0.478.
        Assert.Equal(0.478, motion.ExpandedContentOpacity, 0.02);
        Assert.Equal(0, motion.ChromeOpacity, 3);         // chrome waits until 120 ms
        run.AdvanceTo(0.1300);
        Assert.InRange(motion.ChromeOpacity, 0.15, 0.35); // started 10 ms ago at 120 ms, sampled 0.24

        run.AdvanceTo(0.310);
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

        Assert.Equal(472, motion.Width, 0.001);
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

    [Fact]
    public void Writes_the_expand_choreography_report()
    {
        // Same-frame comparison of the three phases: container geometry vs page-content opacity vs
        // chrome opacity. Sampled at the render clock (1/120 s) so the file is the exact frame data.
        var motion = ExpandedMotion();
        var run = new MotionRun(motion);
        var rows = new List<string>
        {
            "frame,tMs,shellWidthDip,shellHeightDip,containerProgress,contentOpacity,chromeOpacity,containerMoving,contentMoving,chromeMoving",
        };
        var startWidth = motion.Width;
        var startHeight = motion.Height;
        var endWidth = 472.0;
        var endHeight = 284.0;
        var contentStart = -1.0;
        var chromeStart = -1.0;
        var containerStart = -1.0;
        var previousContent = motion.ExpandedContentOpacity;
        var previousChrome = motion.ChromeOpacity;

        for (var frame = 0; frame <= 60; frame++)
        {
            var ms = frame * (1000.0 / 120.0);
            if (frame > 0)
            {
                run.AdvanceTo(ms / 1000.0);
            }

            var content = motion.ExpandedContentOpacity;
            var chrome = motion.ChromeOpacity;
            var widthProgress = Math.Clamp(
                (motion.Width - startWidth) / Math.Max(1e-6, endWidth - startWidth), 0, 1);
            if (containerStart < 0 && motion.Width > startWidth + 1e-6)
            {
                containerStart = ms;
            }

            if (contentStart < 0 && content > previousContent + 1e-6)
            {
                contentStart = ms;
            }

            if (chromeStart < 0 && chrome > previousChrome + 1e-6)
            {
                chromeStart = ms;
            }

            rows.Add(string.Join(',',
                frame.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ms.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                motion.Width.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                motion.Height.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
                widthProgress.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                content.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                chrome.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture),
                (containerStart >= 0 ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (contentStart >= 0 ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (chromeStart >= 0 ? 1 : 0).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            previousContent = content;
            previousChrome = chrome;
        }

        rows.Add(string.Empty);
        rows.Add("# measured starts (first frame the value moved):");
        rows.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"# container {containerStart:0.0} ms, content {contentStart:0.0} ms, chrome {chromeStart:0.0} ms"));
        rows.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"# stagger: content - container = {contentStart - containerStart:0.0} ms, chrome - content = {chromeStart - contentStart:0.0} ms"));

        var root = FindRepoRoot();
        var reportDir = Path.Combine(root, "build", "reports", "island-motion");
        Directory.CreateDirectory(reportDir);
        File.WriteAllLines(Path.Combine(reportDir, "expand-choreography.csv"), rows);

        // The container must lead, then the content, then the chrome - never all at once.
        Assert.True(containerStart < contentStart, "container must move before the content fades in");
        Assert.True(contentStart < chromeStart, "chrome must start after the page content");
        Assert.InRange(contentStart - containerStart, 55, 75);
        Assert.InRange(chromeStart - containerStart, 115, 135);
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
