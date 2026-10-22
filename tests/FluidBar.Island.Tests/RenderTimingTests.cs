using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluidBar.Core.Layout;
using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class RenderTimingTests
{
    [Fact]
    public void Writes_the_full_and_dirty_render_timing_report()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var expanded = IslandTestFixtures.Expanded(1);
        var compact = IslandTestFixtures.MediaCompact();
        var dock = IslandTestFixtures.Expanded(1, mode: IslandMode.Dock);
        var notifications = IslandTestFixtures.Expanded(2);
        var widgets = IslandTestFixtures.Expanded(5);

        using var bitmap = new SKBitmap(new SKImageInfo(976, 888, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);

        // Warm caches (text, shadow, paths, icons) before measuring.
        for (var i = 0; i < 20; i++)
        {
            renderer.Render(canvas, expanded, IslandTestFixtures.Scale);
            renderer.Render(canvas, compact, IslandTestFixtures.Scale);
            renderer.Render(canvas, notifications, IslandTestFixtures.Scale);
            renderer.Render(canvas, widgets, IslandTestFixtures.Scale);
        }

        var full = Measure(renderer, canvas, expanded, null, 200);
        var dirty = Measure(renderer, canvas, expanded, new SKRect(60, 150, 428, 210), 200);
        var compactFull = Measure(renderer, canvas, compact, null, 200);
        var dockFull = Measure(renderer, canvas, dock, null, 100);
        var notificationsFull = Measure(renderer, canvas, notifications, null, 200);
        var notificationsDirty = Measure(renderer, canvas, notifications, new SKRect(60, 150, 428, 210), 200);
        var widgetsFull = Measure(renderer, canvas, widgets, null, 200);

        var report = BuildReport(full, dirty, compactFull, dockFull, notificationsFull, notificationsDirty, widgetsFull);
        Directory.CreateDirectory(IslandTestFixtures.ReportDir);
        File.WriteAllText(Path.Combine(IslandTestFixtures.ReportDir, "timings.md"), report);
        File.WriteAllText(
            Path.Combine(IslandTestFixtures.ReportDir, "timings.json"),
            JsonSerializer.Serialize(
                new
                {
                    environment = new
                    {
                        machine = Environment.MachineName,
                        os = RuntimeInformation.OSDescription,
                        framework = RuntimeInformation.FrameworkDescription,
                        dpiScale = IslandTestFixtures.Scale,
                        surface = "488x384 DIP (882x576 px)",
                    },
                    expandedFull = full,
                    expandedDirty = dirty,
                    compactFull = compactFull,
                    dockFull = dockFull,
                    notificationsFull,
                    notificationsDirty,
                    widgetsFull,
                    text = new
                    {
                        renderer.Text.CacheCount,
                        renderer.Text.CacheCapacity,
                        renderer.Text.Hits,
                        renderer.Text.Misses,
                        renderer.Text.Evictions,
                    },
                    caches = new { paths = renderer.PathCacheCount, shadows = renderer.ShadowCacheCount },
                },
                new JsonSerializerOptions { WriteIndented = true }));

        // Generous gates: the MVP must stay inside a 60 fps frame even on a loaded CI machine.
        Assert.True(full.Mean < 16.67, $"expanded full mean {full.Mean:0.000} ms exceeds 16.67 ms");
        Assert.True(dirty.Mean <= (full.Mean * 1.5) + 0.1, $"dirty {dirty.Mean:0.000} ms vs full {full.Mean:0.000} ms");
        Assert.True(compactFull.Mean < 8.33, $"compact mean {compactFull.Mean:0.000} ms exceeds 8.33 ms");
        Assert.True(dockFull.Mean < 16.67, $"dock mean {dockFull.Mean:0.000} ms exceeds 16.67 ms");
        Assert.True(notificationsFull.Mean < 16.67, $"notifications mean {notificationsFull.Mean:0.000} ms exceeds 16.67 ms");
        Assert.True(
            notificationsDirty.Mean <= (notificationsFull.Mean * 1.5) + 0.1,
            $"notifications dirty {notificationsDirty.Mean:0.000} ms vs full {notificationsFull.Mean:0.000} ms");
        Assert.True(widgetsFull.Mean < 16.67, $"widgets mean {widgetsFull.Mean:0.000} ms exceeds 16.67 ms");
    }

    private static Timing Measure(
        IslandRenderer renderer,
        SKCanvas canvas,
        IslandRenderState state,
        SKRect? damage,
        int frames)
    {
        var samples = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            var start = Stopwatch.GetTimestamp();
            if (damage is { } rect)
            {
                renderer.Render(canvas, state, IslandTestFixtures.Scale, rect);
            }
            else
            {
                renderer.Render(canvas, state, IslandTestFixtures.Scale);
            }

            samples[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        }

        Array.Sort(samples);
        var mean = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            mean += samples[i];
        }

        mean /= samples.Length;
        var p99 = samples[(int)Math.Min(samples.Length - 1, Math.Ceiling(samples.Length * 0.99) - 1)];
        return new Timing(mean, p99, samples[^1], frames);
    }

    private static string BuildReport(
        Timing full,
        Timing dirty,
        Timing compact,
        Timing dock,
        Timing notifications,
        Timing notificationsDirty,
        Timing widgets) => $"""
        # FluidBar island MVP — render timings

        Environment: {Environment.MachineName} · {RuntimeInformation.OSDescription} · {RuntimeInformation.FrameworkDescription}
        DPI: 150 % (1.5×) · host surface 488×384 DIP → 882×576 px (dock 882×666 px)
        Canvas: CPU raster, premultiplied BGRA, warm caches, unpaced.

        | Frame | mean ms | p99 ms | max ms | frames |
        |---|---:|---:|---:|---:|
        | Expanded media full | {full.Mean:0.000} | {full.P99:0.000} | {full.Max:0.000} | {full.Frames} |
        | Expanded media dirty (368×60 DIP) | {dirty.Mean:0.000} | {dirty.P99:0.000} | {dirty.Max:0.000} | {dirty.Frames} |
        | Compact media full | {compact.Mean:0.000} | {compact.P99:0.000} | {compact.Max:0.000} | {compact.Frames} |
        | Expanded dock media full | {dock.Mean:0.000} | {dock.P99:0.000} | {dock.Max:0.000} | {dock.Frames} |
        | Expanded notifications full | {notifications.Mean:0.000} | {notifications.P99:0.000} | {notifications.Max:0.000} | {notifications.Frames} |
        | Expanded notifications dirty (368×60 DIP) | {notificationsDirty.Mean:0.000} | {notificationsDirty.P99:0.000} | {notificationsDirty.Max:0.000} | {notificationsDirty.Frames} |
        | Expanded widgets full | {widgets.Mean:0.000} | {widgets.P99:0.000} | {widgets.Max:0.000} | {widgets.Frames} |

        120 fps budget = 8.33 ms; 60 fps budget = 16.67 ms. The dirty frame touches the page body band;
        the host uploads only that rectangle. Warm frames allocate 0 B (AllocationTests).
        """;

    private readonly record struct Timing(double Mean, double P99, double Max, int Frames);
}
