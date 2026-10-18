using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace FluidBar.SkiaIslandSpike;

internal sealed record RenderBench(
    string Name,
    int WidthPx,
    int HeightPx,
    int Frames,
    double MeanMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double AllocBytesPerFrame,
    double DirtyAreaPx,
    bool StaticBackground,
    int NodesDrawn);

internal sealed record TextBench(
    string Name,
    int Ops,
    double MicrosPerOp,
    double AllocBytesPerOp,
    int Hits,
    int Misses,
    int Evictions,
    int MeasureCalls,
    int TrimSteps,
    long CacheBytes,
    int CacheCount);

internal sealed record DpiBench(float Scale, int WidthPx, int HeightPx, double MeanMs, double P99Ms, double FpsAtP99);

internal sealed record MemoryBench(
    long ManagedBytes,
    long WorkingSetBytes,
    long TextCacheBytes,
    long ShadowImageBytes,
    long StaticLayerBytes,
    int ShadowImages,
    int StaticLayers);

internal sealed record Measurements(
    string GeneratedAt,
    string Machine,
    string Runtime,
    string Dpi,
    RenderBench[] Renders,
    TextBench[] Text,
    DpiBench[] DpiScaling,
    FidelityResult[] Fidelity,
    MemoryBench Memory,
    double ColdSceneBuildMs,
    double ColdFirstFrameMs);

internal static class Benchmarks
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static RenderBench MeasureRender(
        string name,
        IslandRenderer renderer,
        IslandScene scene,
        int frames = 400,
        int warmup = 60,
        SKRect? dirty = null,
        bool useStatic = true)
    {
        for (var i = 0; i < warmup; i++)
        {
            renderer.Render(scene, dirty, useStatic);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var ticks = new double[frames];
        var allocStart = GC.GetAllocatedBytesForCurrentThread();
        var sw = new Stopwatch();
        for (var i = 0; i < frames; i++)
        {
            sw.Restart();
            renderer.Render(scene, dirty, useStatic);
            sw.Stop();
            ticks[i] = sw.Elapsed.TotalMilliseconds;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        var sorted = (double[])ticks.Clone();
        Array.Sort(sorted);
        var dirtyArea = dirty is { } rect
            ? (double)renderer.Scale * renderer.Scale * rect.Width * rect.Height
            : renderer.WidthPx * (double)renderer.HeightPx;

        return new RenderBench(
            name,
            renderer.WidthPx,
            renderer.HeightPx,
            frames,
            ticks.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            allocated / (double)frames,
            dirtyArea,
            useStatic,
            scene.LastContentNodesDrawn);
    }

    public static RenderBench MeasureMorph(
        string name,
        IslandRenderer renderer,
        MorphHarness harness,
        MorphHarness.ShadowMode mode,
        int frames = 240,
        int warmup = 60)
    {
        for (var i = 0; i < warmup; i++)
        {
            var t = (i % frames) / (double)(frames - 1);
            renderer.DrawProbe(canvas => harness.Draw(canvas, t, mode));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var ticks = new double[frames];
        var allocStart = GC.GetAllocatedBytesForCurrentThread();
        var sw = new Stopwatch();
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)(frames - 1);
            sw.Restart();
            renderer.DrawProbe(canvas => harness.Draw(canvas, t, mode));
            sw.Stop();
            ticks[i] = sw.Elapsed.TotalMilliseconds;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        var sorted = (double[])ticks.Clone();
        Array.Sort(sorted);
        return new RenderBench(
            name,
            renderer.WidthPx,
            renderer.HeightPx,
            frames,
            ticks.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            allocated / (double)frames,
            renderer.WidthPx * (double)renderer.HeightPx,
            false,
            1);
    }

    public static (TextBench Cold, TextBench Warm) MeasureTextPair(
        TextEngine engine,
        string name,
        IReadOnlyList<string> strings,
        TextStyle style,
        float maxWidth,
        int warmIterations = 3)
    {
        engine.ClearCache();
        var sw = Stopwatch.StartNew();
        var allocStart = GC.GetAllocatedBytesForCurrentThread();
        var ops = 0;
        for (var pass = 0; pass < 1; pass++)
        {
            foreach (var text in strings)
            {
                engine.GetLayout(text, style, maxWidth);
                ops++;
            }
        }

        sw.Stop();
        var alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        var cold = new TextBench(
            name + " (cold)",
            ops,
            (sw.Elapsed.TotalMilliseconds * 1000) / ops,
            alloc / (double)ops,
            engine.Hits,
            engine.Misses,
            engine.Evictions,
            engine.MeasureCalls,
            engine.TrimSteps,
            engine.ApproxCacheBytes,
            engine.CacheCount);

        engine.ResetStats();
        sw.Restart();
        allocStart = GC.GetAllocatedBytesForCurrentThread();
        ops = 0;
        for (var pass = 0; pass < warmIterations; pass++)
        {
            foreach (var text in strings)
            {
                engine.GetLayout(text, style, maxWidth);
                ops++;
            }
        }

        sw.Stop();
        alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart;
        var warm = new TextBench(
            name + " (warm)",
            ops,
            (sw.Elapsed.TotalMilliseconds * 1000) / ops,
            alloc / (double)ops,
            engine.Hits,
            engine.Misses,
            engine.Evictions,
            engine.MeasureCalls,
            engine.TrimSteps,
            engine.ApproxCacheBytes,
            engine.CacheCount);

        return (cold, warm);
    }

    public static string ToMarkdown(Measurements m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# FluidBar Skia island spike — measured results");
        sb.AppendLine();
        sb.AppendLine($"Generated: {m.GeneratedAt}  ");
        sb.AppendLine($"Machine: {m.Machine}  ");
        sb.AppendLine($"Runtime: {m.Runtime}  ");
        sb.AppendLine($"DPI: {m.Dpi}");
        sb.AppendLine();
        sb.AppendLine("## Render time (offscreen raster, 488×332 DIP host)");
        sb.AppendLine();
        sb.AppendLine("| Case | Surface px | Frames | mean ms | p50 | p95 | p99 | max | alloc B/frame | nodes |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in m.Renders)
        {
            sb.AppendLine($"| {r.Name} | {r.WidthPx}×{r.HeightPx} | {r.Frames} | {r.MeanMs:0.000} | {r.P50Ms:0.000} | {r.P95Ms:0.000} | {r.P99Ms:0.000} | {r.MaxMs:0.000} | {r.AllocBytesPerFrame:0.0} | {r.NodesDrawn} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Text layout / measure cache");
        sb.AppendLine();
        sb.AppendLine("| Case | ops | µs/op | alloc B/op | hits | misses | evictions | measure calls | trim steps | cache |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var t in m.Text)
        {
            sb.AppendLine($"| {t.Name} | {t.Ops} | {t.MicrosPerOp:0.000} | {t.AllocBytesPerOp:0.0} | {t.Hits} | {t.Misses} | {t.Evictions} | {t.MeasureCalls} | {t.TrimSteps} | {t.CacheCount} / {t.CacheBytes / 1024.0:0.0} KiB |");
        }

        sb.AppendLine();
        sb.AppendLine("## DPI scaling (expanded home, warm)");
        sb.AppendLine();
        sb.AppendLine("| Scale | Surface px | mean ms | p99 ms | fps at p99 |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        foreach (var d in m.DpiScaling)
        {
            sb.AppendLine($"| {d.Scale:0.00}× | {d.WidthPx}×{d.HeightPx} | {d.MeanMs:0.000} | {d.P99Ms:0.000} | {d.FpsAtP99:0.0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Fidelity vs Avalonia evidence (transparent PNGs)");
        sb.AppendLine();
        sb.AppendLine("| Case | Evidence opaque bounds | Spike opaque bounds | mask IoU | mean RGB Δ | evidence fill | spike fill | notes |");
        sb.AppendLine("|---|---|---|---:|---:|---|---|---|");
        foreach (var f in m.Fidelity)
        {
            sb.AppendLine($"| {f.Case} | {f.EvidenceBounds} | {f.OurBounds} | {f.MaskIou:0.0000} | {f.MeanRgbDelta:0.00} | {f.EvidenceFill} | {f.OurFill} | {f.Notes} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Cold start and memory");
        sb.AppendLine();
        sb.AppendLine($"- Cold scene build + first frame: **{m.ColdSceneBuildMs:0.0} ms** (first render {m.ColdFirstFrameMs:0.0} ms)");
        sb.AppendLine($"- Managed heap: {m.Memory.ManagedBytes / 1024.0 / 1024.0:0.0} MiB; working set: {m.Memory.WorkingSetBytes / 1024.0 / 1024.0:0.0} MiB");
        sb.AppendLine($"- Text layout cache: {m.Memory.TextCacheBytes / 1024.0:0.0} KiB; shadow images: {m.Memory.ShadowImageBytes / 1024.0:0.0} KiB ({m.Memory.ShadowImages}); static layers: {m.Memory.StaticLayerBytes / 1024.0:0.0} KiB ({m.Memory.StaticLayers})");
        sb.AppendLine();
        sb.AppendLine("> Raster CPU numbers; a layered Win32 host additionally pays one BGRA copy + `UpdateLayeredWindow`");
        sb.AppendLine("> (or a DirectComposition present). 60 fps budget = 16.67 ms, 120 fps = 8.33 ms.");
        return sb.ToString();
    }

    public static void Save(Measurements measurements, string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "measurements.json"),
            JsonSerializer.Serialize(measurements, JsonOptions));
        File.WriteAllText(Path.Combine(directory, "measurements.md"), ToMarkdown(measurements));
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
