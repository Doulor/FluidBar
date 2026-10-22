using FluidBar.App.Hosting.Skia;
using FluidBar.App.Settings;
using FluidBar.Core.Activities;
using FluidBar.Core.Configuration;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>Deterministic scheduling gate for the Performance/Saver parameter table.</summary>
public sealed class PowerModeProbe
{
    [Fact]
    public async Task PowerModeTable_And_SplitRingCadence()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "FluidBar.sln")))
        {
            var parent = Directory.GetParent(root);
            if (parent is null) break;
            root = parent.FullName;
        }

        var output = Path.Combine(root, "build", "reports", "perf");
        Directory.CreateDirectory(output);
        var rows = new List<string> { "mode,case,frameIntervalMs,ring,pointerInside" };

        await AvaloniaTest.Run(() =>
        {
            rows.Add(Row("performance", "no-ring", Run(AppSettings.Default with { PowerMode = IslandPowerMode.Performance }), false, false));
            rows.Add(Row("saver", "no-ring", Run(AppSettings.Default with { PowerMode = IslandPowerMode.Saver }), false, false));
            rows.Add(Row("performance", "split-ring", RunWithRing(IslandPowerMode.Performance, pointerInside: false), true, false));
            rows.Add(Row("saver", "split-ring-idle", RunWithRing(IslandPowerMode.Saver, pointerInside: false), true, false));
            rows.Add(Row("saver", "split-ring-hover", RunWithRing(IslandPowerMode.Saver, pointerInside: true), true, true));
        });

        File.WriteAllText(Path.Combine(output, "power-mode-frame-intervals.csv"), string.Join(Environment.NewLine, rows));
        var angles = new List<string> { "mode,frame,seconds,degrees" };
        for (var frame = 1; frame <= 30; frame++)
        {
            angles.Add($"saver,{frame},{frame / 30.0:0.000000},{IslandMetrics.SplitRingDegrees(frame / 30.0):0.###}");
            angles.Add($"performance,{frame},{frame / 60.0:0.000000},{IslandMetrics.SplitRingDegrees(frame / 60.0):0.###}");
        }

        File.WriteAllText(Path.Combine(output, "power-split-ring-angles.csv"), string.Join(Environment.NewLine, angles));
        // The ring shares one spinner primitive with the big island: 0.9 s per turn = 400 deg/s
        // (IslandMetrics.SplitRingPeriodSeconds -> AgentSpinner.PeriodSeconds).
        Assert.Equal(400.0 / 30.0, IslandMetrics.SplitRingDegrees(1.0 / 30.0), 1);
        Assert.Equal(400.0 / 60.0, IslandMetrics.SplitRingDegrees(1.0 / 60.0), 1);
    }

    private static double Run(AppSettings settings)
    {
        using var fixture = ControllerFixture.Create(settings with { AutoCollapseMs = 0, AutoHideMs = 0 });
        fixture.Settle();
        return fixture.Controller.PreferredFrameInterval.TotalMilliseconds;
    }

    private static double RunWithRing(IslandPowerMode mode, bool pointerInside)
    {
        using var fixture = ControllerFixture.Create(AppSettings.Default with
        {
            PowerMode = mode, AutoCollapseMs = 0, AutoHideMs = 0, Stacking = StackingStrategySetting.Split,
        });
        fixture.Settle();
        fixture.Controller.PublishActivity(ActivityKind.Media, new ActivityPayload
        {
            Title = "Song", IsActive = true, ShowsAudioWave = true,
        }, 1, "power-probe");
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        fixture.Controller.PumpTimers();
        fixture.Settle();
        fixture.Controller.PublishActivity(ActivityKind.Agent, new ActivityPayload
        {
            Title = "Agent", Badge = "running", IsActive = true,
        }, 2, "power-probe");
        fixture.Time.Advance(TimeSpan.FromMilliseconds(120));
        fixture.Controller.PumpTimers();
        fixture.Settle();
        Assert.True(fixture.Controller.HasSpinningSplitRing, "probe needs a split agent ring");
        if (pointerInside)
        {
            var shell = IslandMetrics.ShellRect(fixture.Controller.RenderState);
            fixture.Controller.HandlePointerMoved(shell.MidX, shell.MidY);
        }

        return fixture.Controller.PreferredFrameInterval.TotalMilliseconds;
    }

    private static string Row(string mode, string @case, double interval, bool ring, bool pointerInside)
        => string.Join(',', mode, @case, interval.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            ring ? "1" : "0", pointerInside ? "1" : "0");
}
