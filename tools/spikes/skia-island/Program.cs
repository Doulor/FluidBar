using System.Diagnostics;
using System.Runtime.InteropServices;
using FluidBar.SkiaIslandSpike;
using SkiaSharp;

const float Scale = 1.5f;
var repo = FindRepoRoot();
var output = Path.Combine(repo, "build", "reports", "skia-island");
Directory.CreateDirectory(output);
foreach (var stale in Directory.GetFiles(output, "*.png").Concat(Directory.GetFiles(output, "measurements.*")))
{
    File.Delete(stale);
}

var evidence = Path.Combine(repo, "tests", "FluidBar.Ui.Tests", "Evidence");

Console.WriteLine($"repo   : {repo}");
Console.WriteLine($"output : {output}");
Console.WriteLine($"scale  : {Scale:0.##}x  host {488 * Scale:0}x{332 * Scale:0}px");

using var text = new TextEngine();
using var shadows = new ShadowCache();
using var statics = new StaticLayerCache();
using var scenes = new IslandScenes(text, shadows, statics);

using var hostRenderer = new IslandRenderer(488, 332, Scale);
using var stripRenderer = new IslandRenderer(488, 384, Scale);

// ---------------------------------------------------------------- renders (EN + zh-Hans)
var idleEn = scenes.CompactIdle(Copy.En, Scale, useStatic: true);
var idleZh = scenes.CompactIdle(Copy.Zh, Scale, useStatic: true);
var mediaEn = scenes.CompactMedia(Copy.En, Scale, useStatic: true);
var mediaZh = scenes.CompactMedia(Copy.Zh, Scale, useStatic: true);
var homeEn = scenes.ExpandedHome(Copy.En, Scale, useStatic: true);
var homeZh = scenes.ExpandedHome(Copy.Zh, Scale, useStatic: true);
var pageMediaEn = scenes.ExpandedMedia(Copy.En, Scale, useStatic: true);
var pageMediaZh = scenes.ExpandedMedia(Copy.Zh, Scale, useStatic: true);
var stripEn = scenes.ExpandedWithStrip(Copy.En, Scale, useStatic: true);
var stripZh = scenes.ExpandedWithStrip(Copy.Zh, Scale, useStatic: true);
var coverage = scenes.GlyphCoverage(Copy.En, Scale, useStatic: true);

using var idleEnImage = RenderToImage(hostRenderer, idleEn);
using var idleZhImage = RenderToImage(hostRenderer, idleZh);
using var mediaEnImage = RenderToImage(hostRenderer, mediaEn);
using var mediaZhImage = RenderToImage(hostRenderer, mediaZh);
using var homeEnImage = RenderToImage(hostRenderer, homeEn);
using var homeZhImage = RenderToImage(hostRenderer, homeZh);
using var pageMediaEnImage = RenderToImage(hostRenderer, pageMediaEn);
using var pageMediaZhImage = RenderToImage(hostRenderer, pageMediaZh);
using var stripEnImage = RenderToImage(stripRenderer, stripEn);
using var stripZhImage = RenderToImage(stripRenderer, stripZh);
using var coverageImage = RenderToImage(hostRenderer, coverage);

SaveImage(homeEnImage, Path.Combine(output, "03-expanded-home-en.png"));
SaveImage(homeZhImage, Path.Combine(output, "03b-expanded-home-zh.png"));
SaveImage(pageMediaEnImage, Path.Combine(output, "04-expanded-media-en.png"));
SaveImage(pageMediaZhImage, Path.Combine(output, "04b-expanded-media-zh.png"));
SaveImage(stripEnImage, Path.Combine(output, "05-archipelago-en.png"));
SaveImage(stripZhImage, Path.Combine(output, "05b-archipelago-zh.png"));
SaveImage(coverageImage, Path.Combine(output, "06-glyph-coverage.png"));
using var compactIdleRenderer = new IslandRenderer(212, 116, Scale);
using var compactMediaRenderer = new IslandRenderer(306, 120, Scale);
using var idleCropEn = RenderToImage(compactIdleRenderer, scenes.CompactIdle(Copy.En, Scale, useStatic: true, standalone: true));
using var idleCropZh = RenderToImage(compactIdleRenderer, scenes.CompactIdle(Copy.Zh, Scale, useStatic: true, standalone: true));
using var mediaCropEn = RenderToImage(compactMediaRenderer, scenes.CompactMedia(Copy.En, Scale, useStatic: true, standalone: true));
using var mediaCropZh = RenderToImage(compactMediaRenderer, scenes.CompactMedia(Copy.Zh, Scale, useStatic: true, standalone: true));
SaveImage(idleCropEn, Path.Combine(output, "01a-compact-idle-en.png"));
SaveImage(idleCropZh, Path.Combine(output, "01b-compact-idle-zh.png"));
SaveImage(mediaCropEn, Path.Combine(output, "02a-compact-media-en.png"));
SaveImage(mediaCropZh, Path.Combine(output, "02b-compact-media-zh.png"));

Console.WriteLine($"rendered PNGs: {Directory.GetFiles(output, "*.png").Length}");

// ---------------------------------------------------------------- comparisons with Avalonia evidence
SaveComparison(Path.Combine(evidence, "fluidbar-01-idle.png"), idleEnImage, "Avalonia 12 evidence", "SkiaSharp spike", Path.Combine(output, "12-compare-idle.png"), text);
SaveComparison(Path.Combine(evidence, "fluidbar-02-expanded-home.png"), homeEnImage, "Avalonia 12 evidence (rail, stale A1)", "SkiaSharp spike (no rail, A1)", Path.Combine(output, "10-compare-home.png"), text);
SaveComparison(Path.Combine(evidence, "fluidbar-03-media.png"), pageMediaEnImage, "Avalonia 12 evidence (rail, stale A1)", "SkiaSharp spike (no rail, A1)", Path.Combine(output, "11-compare-media.png"), text);

SaveContactSheet(
    Path.Combine(output, "00-contact-sheet.png"),
    idleCropEn,
    mediaCropEn,
    homeEnImage,
    pageMediaEnImage,
    stripEnImage,
    coverageImage,
    text);

// ---------------------------------------------------------------- measurements
var renders = new List<RenderBench>
{
    Benchmarks.MeasureRender("compact idle (host frame)", hostRenderer, idleEn, 600, 100),
    Benchmarks.MeasureRender("compact media (host frame)", hostRenderer, mediaEn, 600, 100),
    Benchmarks.MeasureRender("expanded home (full frame)", hostRenderer, homeEn, 600, 100),
    Benchmarks.MeasureRender("expanded media (full frame)", hostRenderer, pageMediaEn, 600, 100),
    Benchmarks.MeasureRender("expanded media (dirty rect 376×114 DIP)", hostRenderer, pageMediaEn, 800, 120,
        new SKRect(56, 132, 432, 246)),
    Benchmarks.MeasureRender("expanded home (live background, no static layer)", hostRenderer, homeEn, 400, 60,
        useStatic: false),
    Benchmarks.MeasureRender("expanded + archipelago (732×576)", stripRenderer, stripEn, 400, 60),
    Benchmarks.MeasureRender("compact media (dirty waveform 26×22 DIP)", hostRenderer, mediaEn, 800, 120,
        new SKRect(296, 30, 322, 52)),
};

using var morph = new MorphHarness(text, shadows, Scale);
renders.Add(Benchmarks.MeasureMorph(
    "morph 132×36→440×284 (scaled shadow cache)", hostRenderer, morph, MorphHarness.ShadowMode.ScaledCache));
renders.Add(Benchmarks.MeasureMorph(
    "morph 132×36→440×284 (live drop-shadow filter)", hostRenderer, morph, MorphHarness.ShadowMode.LiveFilter));
renders.Add(Benchmarks.MeasureMorph(
    "morph 132×36→440×284 (no shadow)", hostRenderer, morph, MorphHarness.ShadowMode.None));

// Text: unique strings force cache misses, then the same set measures the warm hit path.
var uiStrings = Enumerable.Range(0, 120)
    .Select(i => $"Notification {i}: background build finished in {i + 1}.{i % 10}s")
    .ToArray();
var zhStrings = Enumerable.Range(0, 120)
    .Select(i => $"第 {i} 条通知：后台构建已在 {i + 1} 秒内完成")
    .ToArray();
var longStrings = Enumerable.Range(0, 120)
    .Select(i => $"Waiting for a car to drive me home — extended lyric line number {i}")
    .ToArray();
var titles = Enumerable.Range(0, 120)
    .Select(i => $"{(i % 2 == 0 ? "Control center" : "Now playing")} — page {i}")
    .ToArray();

using var textBenchEngine = new TextEngine(1024);
var textBenches = new List<TextBench>();
var (uiCold, uiWarm) = Benchmarks.MeasureTextPair(textBenchEngine, "UI 13.5 / 160 DIP (EN)", uiStrings, new TextStyle(FontRole.UiSemiBold, 13.5f), 160, 2);
textBenches.Add(uiCold);
textBenches.Add(uiWarm);
var (zhCold, zhWarm) = Benchmarks.MeasureTextPair(textBenchEngine, "UI 13.5 / 160 DIP (zh-Hans)", zhStrings, new TextStyle(FontRole.UiSemiBold, 13.5f), 160, 2);
textBenches.Add(zhCold);
textBenches.Add(zhWarm);
var (longCold, longWarm) = Benchmarks.MeasureTextPair(textBenchEngine, "Trim 13.5 / 120 DIP (EN long)", longStrings, new TextStyle(FontRole.UiSemiBold, 13.5f), 120, 2);
textBenches.Add(longCold);
textBenches.Add(longWarm);
var (titleCold, titleWarm) = Benchmarks.MeasureTextPair(textBenchEngine, "Page title 15 / 340 DIP", titles, new TextStyle(FontRole.DisplaySemiBold, 15f), 340, 2);
textBenches.Add(titleCold);
textBenches.Add(titleWarm);

// DPI scaling table (same scene, fresh renderer per scale).
var dpi = new List<DpiBench>();
foreach (var scale in new[] { 1.25f, 1.5f, 1.75f, 2.0f })
{
    using var renderer = new IslandRenderer(488, 332, scale);
    var scene = scenes.ExpandedHome(Copy.En, scale, useStatic: true);
    var bench = Benchmarks.MeasureRender($"dpi {scale:0.00}", renderer, scene, 250, 40);
    dpi.Add(new DpiBench(scale, bench.WidthPx, bench.HeightPx, bench.MeanMs, bench.P99Ms, 1000.0 / bench.P99Ms));
    Console.WriteLine($"DPI {scale:0.00}x {bench.WidthPx}x{bench.HeightPx}: mean {bench.MeanMs:0.000} ms p99 {bench.P99Ms:0.000} ms");
}

// Fidelity: opaque shell mask overlap + fill/alpha vs the Avalonia evidence.
var fidelity = new[]
{
    Fidelity.Compare(
        "compact idle",
        Path.Combine(evidence, "fluidbar-01-idle.png"),
        idleEnImage,
        Scale,
        "same layout; exact 132×36 bounds, alpha fill and squircle mask"),
    Fidelity.Compare(
        "expanded home",
        Path.Combine(evidence, "fluidbar-02-expanded-home.png"),
        homeEnImage,
        Scale,
        "evidence has the rejected 48 DIP rail; content positions differ, shell shape/colour comparable"),
    Fidelity.Compare(
        "expanded media",
        Path.Combine(evidence, "fluidbar-03-media.png"),
        pageMediaEnImage,
        Scale,
        "evidence has the rejected 48 DIP rail and a queue badge; shell shape/colour comparable"),
};

// Cold start: everything new, one expanded frame.
var coldWatch = Stopwatch.StartNew();
using (var coldText = new TextEngine())
using (var coldShadows = new ShadowCache())
using (var coldStatics = new StaticLayerCache())
using (var coldScenes = new IslandScenes(coldText, coldShadows, coldStatics))
using (var coldRenderer = new IslandRenderer(488, 332, Scale))
{
    var coldScene = coldScenes.ExpandedHome(Copy.En, Scale, useStatic: true);
    var buildMs = coldWatch.Elapsed.TotalMilliseconds;
    coldWatch.Restart();
    coldRenderer.Render(coldScene);
    coldWatch.Stop();
    Console.WriteLine($"cold build {buildMs:0.0} ms, first frame {coldWatch.Elapsed.TotalMilliseconds:0.0} ms");
    var coldRender = coldWatch.Elapsed.TotalMilliseconds;

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var memory = new MemoryBench(
        GC.GetTotalMemory(false),
        Process.GetCurrentProcess().WorkingSet64,
        text.ApproxCacheBytes + textBenchEngine.ApproxCacheBytes,
        shadows.PixelBytes + coldShadows.PixelBytes,
        statics.PixelBytes + coldStatics.PixelBytes,
        shadows.Count + coldShadows.Count,
        statics.Builds + coldStatics.Builds);

    var measurements = new Measurements(
        DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
        $"{Environment.MachineName} · {RuntimeInformation.OSDescription}",
        $"{RuntimeInformation.FrameworkDescription} · SkiaSharp {typeof(SKCanvas).Assembly.GetName().Version} · HarfBuzzSharp {typeof(HarfBuzzSharp.Face).Assembly.GetName().Version}",
        "150 % (1.5×) primary; DPI table 125/150/175/200 %",
        renders.ToArray(),
        textBenches.ToArray(),
        dpi.ToArray(),
        fidelity,
        memory,
        buildMs,
        coldRender);

    Benchmarks.Save(measurements, output);
    Console.WriteLine(Benchmarks.ToMarkdown(measurements));
    Console.WriteLine($"saved {Path.Combine(output, "measurements.md")}");
}

// ---------------------------------------------------------------- helpers

static SKImage RenderToImage(IslandRenderer renderer, IslandScene scene)
{
    renderer.Render(scene);
    return renderer.Snapshot();
}

static void SaveImage(SKImage image, string path)
{
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);
}

static void SaveComparison(string evidencePath, SKImage ours, string leftLabel, string rightLabel, string path, TextEngine text)
{
    const int panelW = 732;
    const int panelH = 498;
    const int header = 42;
    using var evidenceImage = SKImage.FromEncodedData(evidencePath);
    using var surface = SKSurface.Create(new SKImageInfo(panelW * 2, panelH + header, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.White);
    canvas.DrawImage(evidenceImage, new SKRect(0, header, panelW, header + panelH));
    canvas.DrawImage(ours, new SKRect(panelW, header, panelW * 2, header + panelH));
    text.Draw(canvas, leftLabel, new TextStyle(FontRole.UiSemiBold, 15f), new SKRect(16, 8, 700, 36), SKColors.Black);
    text.Draw(canvas, rightLabel, new TextStyle(FontRole.UiSemiBold, 15f), new SKRect(panelW + 16, 8, panelW + 700, 36), SKColors.Black);
    using var snapshot = surface.Snapshot();
    SaveImage(snapshot, path);
}

static void SaveContactSheet(
    string path,
    SKImage idle,
    SKImage compactMedia,
    SKImage home,
    SKImage pageMedia,
    SKImage strip,
    SKImage coverage,
    TextEngine text)
{
    const int width = 1540;
    const int height = 1500;
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    canvas.Clear(SKColor.Parse("#E9EBEF"));

    var title = new TextStyle(FontRole.DisplaySemiBold, 20f);
    var label = new TextStyle(FontRole.UiSemiBold, 12f);
    text.Draw(canvas, "FluidBar v2 · SkiaSharp island spike · 150 % DPI (transparent PNGs on #E9EBEF)", title,
        new SKRect(24, 18, width - 24, 48), SKColor.Parse("#14161A"));

    canvas.DrawImage(idle, new SKRect(40, 90, 40 + 318, 90 + 174));
    text.Draw(canvas, "compact idle 132×36 (with shadow bleed)", label, new SKRect(40, 66, 520, 86), SKColor.Parse("#14161A"));
    canvas.DrawImage(compactMedia, new SKRect(400, 84, 400 + 459, 84 + 180));
    text.Draw(canvas, "compact media 226×40 (with shadow bleed)", label, new SKRect(400, 60, 1320, 80), SKColor.Parse("#14161A"));

    text.Draw(canvas, "expanded home 440×284", label, new SKRect(24, 270, 700, 290), SKColor.Parse("#14161A"));
    canvas.DrawImage(home, new SKRect(24, 294, 24 + 732, 294 + 498));
    text.Draw(canvas, "expanded media 440×284", label, new SKRect(780, 270, 1500, 290), SKColor.Parse("#14161A"));
    canvas.DrawImage(pageMedia, new SKRect(780, 294, 780 + 732, 294 + 498));

    text.Draw(canvas, "archipelago 488×384 host (shell + detached strip)", label, new SKRect(24, 820, 740, 840), SKColor.Parse("#14161A"));
    canvas.DrawImage(strip, new SKRect(24, 844, 24 + 732, 844 + 576));
    text.Draw(canvas, "glyph coverage (EN / zh-Hans / emoji / kana / Hangul)", label, new SKRect(780, 820, 1500, 840), SKColor.Parse("#14161A"));
    canvas.DrawImage(coverage, new SKRect(780, 844, 780 + 732, 844 + 498));

    using var snapshot = surface.Snapshot();
    SaveImage(snapshot, path);
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "FluidBar.sln")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
}

