using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Regression gate for the expand animation: controller Advance + Skia Render are measured on a
/// reused surface (no PNG encoding / bitmap allocation in the timing). The host warms the expanded
/// and compact shadow-bucket layouts at startup; this test reproduces that warm-up and verifies the
/// first animated frame and the 60-frame tail stay inside the 60 fps budget.
/// </summary>
public sealed class MotionRenderBench
{
    private const double UiBudgetMs = 16.7;
    private const double RenderBudgetMs = 8.0;

    [Fact]
    public Task Bench_expand_render_reused_surface()
    {
        return AvaloniaTest.Run(() =>
        {
            var cold = RunRepeated(warmup: false, repeats: 3);
            var warm = RunRepeated(warmup: true, repeats: 3);

            var root = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(root, "FluidBar.sln")))
            {
                var parent = Directory.GetParent(root);
                if (parent is null) { break; }
                root = parent.FullName;
            }

            var outputDir = Path.Combine(root, "build", "reports", "perf");
            Directory.CreateDirectory(outputDir);
            var lines = new List<string>
            {
                "mode,run,advanceP99,renderP99,uiP99,uiFirst,over16,maxStreak33,frame3Render",
            };
            AddRows(lines, "cold", cold);
            AddRows(lines, "warm", warm);
            File.WriteAllText(Path.Combine(outputDir, "motion-render-bench.csv"), string.Join(Environment.NewLine, lines));
            var coldMedianSummary = cold.OrderBy(static sample => sample.RenderP99).ElementAt(cold.Count / 2);
            var warmMedianSummary = warm.OrderBy(static sample => sample.RenderP99).ElementAt(warm.Count / 2);
            var report = "cold=" + coldMedianSummary.Summary() + " warm=" + warmMedianSummary.Summary();
            File.WriteAllText(Path.Combine(outputDir, "motion-render-bench.txt"), report);
            Console.WriteLine(report);

            var warmMedian = warm.OrderBy(static sample => sample.RenderP99).ElementAt(warm.Count / 2);
            var coldWorstRender = cold.Max(static sample => sample.RenderP99);
            Assert.True(warmMedian.UiP99 < UiBudgetMs,
                $"warm expand ui p99 {warmMedian.UiP99:0.000} ms exceeds {UiBudgetMs} ms");
            Assert.True(warmMedian.RenderP99 < coldWorstRender * 0.75,
                $"warm render p99 {warmMedian.RenderP99:0.000} ms did not beat cold {coldWorstRender:0.000} ms by 25%");
            Assert.True(warmMedian.UiFirst < UiBudgetMs,
                $"warm expand first frame {warmMedian.UiFirst:0.000} ms exceeds {UiBudgetMs} ms");
            Assert.Equal(0, warmMedian.Over16);
            Assert.True(warmMedian.MaxStreak33 < 2,
                $"warm expand had {warmMedian.MaxStreak33} consecutive frames over 33 ms");
        });
    }

    private static List<BenchSample> RunRepeated(bool warmup, int repeats)
    {
        var samples = new List<BenchSample>(repeats);
        for (var run = 0; run < repeats; run++)
        {
            samples.Add(Run(warmup));
        }

        return samples;
    }

    private static BenchSample Run(bool warmup)
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Controller.HandleShowHotkey();
        fixture.Settle();
        using var renderer = new IslandRenderer(IslandTheme.Dark);
        if (warmup)
        {
            foreach (var state in fixture.Controller.CreateMotionWarmupStates())
            {
                using var bitmap = renderer.RenderToBitmap(state, 1.5f);
            }
        }

        using var surface = renderer.CreateSurface(fixture.Controller.RenderState, 1.5f);
        var step = TimeSpan.FromMilliseconds(1000.0 / 60.0);
        var advances = new List<double>(60);
        var renders = new List<double>(60);
        var ui = new List<double>(60);
        for (var i = 0; i < 3; i++)
        {
            fixture.Time.Advance(step);
            fixture.Controller.Advance(step);
            renderer.Render(surface.Canvas, fixture.Controller.RenderState, 1.5f);
        }

        fixture.Controller.HandleToggleHotkey();
        for (var i = 0; i < 60; i++)
        {
            fixture.Time.Advance(step);
            var advanceWatch = System.Diagnostics.Stopwatch.StartNew();
            fixture.Controller.Advance(step);
            advanceWatch.Stop();
            advances.Add(advanceWatch.Elapsed.TotalMilliseconds);
            var renderWatch = System.Diagnostics.Stopwatch.StartNew();
            renderer.Render(surface.Canvas, fixture.Controller.RenderState, 1.5f);
            surface.Canvas.Flush();
            renderWatch.Stop();
            renders.Add(renderWatch.Elapsed.TotalMilliseconds);
            ui.Add(advanceWatch.Elapsed.TotalMilliseconds + renderWatch.Elapsed.TotalMilliseconds);
        }

        var maxStreak33 = 0;
        var streak33 = 0;
        foreach (var value in ui)
        {
            if (value > 33.0)
            {
                streak33++;
                maxStreak33 = Math.Max(maxStreak33, streak33);
            }
            else
            {
                streak33 = 0;
            }
        }

        return new BenchSample(
            Percentile(advances, 99),
            Percentile(renders, 99),
            Percentile(ui, 99),
            ui[0],
            ui.Count(static value => value > UiBudgetMs),
            maxStreak33,
            renders.Count > 3 ? renders[3] : 0);
    }

    private static void AddRows(List<string> lines, string mode, IReadOnlyList<BenchSample> samples)
    {
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            lines.Add(mode + "," + (i + 1).ToString() + "," + sample.AdvanceP99.ToString("0.000") + "," + sample.RenderP99.ToString("0.000") + "," + sample.UiP99.ToString("0.000") + "," + sample.UiFirst.ToString("0.000") + "," + sample.Over16.ToString() + "," + sample.MaxStreak33.ToString() + "," + sample.Frame3Render.ToString("0.000"));
        }
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var sorted = values.OrderBy(static value => value).ToArray();
        return sorted[(int)Math.Clamp(Math.Ceiling((percentile / 100.0) * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private sealed record BenchSample(
        double AdvanceP99,
        double RenderP99,
        double UiP99,
        double UiFirst,
        int Over16,
        int MaxStreak33,
        double Frame3Render)
    {
        public string Summary()
            => $"advanceP99={AdvanceP99:0.000} renderP99={RenderP99:0.000} uiP99={UiP99:0.000} first={UiFirst:0.000} over16={Over16}";
    }
}

