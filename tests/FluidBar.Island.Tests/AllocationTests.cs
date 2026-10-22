using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class AllocationTests
{
    [Fact]
    public void Warm_frames_do_not_allocate_after_warmup()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var expanded = IslandTestFixtures.Expanded(1, strip: true, pagePosition: 1.25);
        var compact = IslandTestFixtures.MediaCompact();
        var notifications = IslandTestFixtures.Expanded(2);
        var widgets = IslandTestFixtures.Expanded(5);

        using var bitmap = new SKBitmap(new SKImageInfo(882, 576, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);

        // Warm every text layout, shadow, path and icon for every surface (including pages 2 and 5).
        for (var i = 0; i < 12; i++)
        {
            renderer.Render(canvas, expanded, IslandTestFixtures.Scale);
            renderer.Render(canvas, compact, IslandTestFixtures.Scale);
            renderer.Render(canvas, notifications, IslandTestFixtures.Scale);
            renderer.Render(canvas, widgets, IslandTestFixtures.Scale);
        }

        const int frames = 40;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < frames; i++)
        {
            renderer.Render(canvas, expanded, IslandTestFixtures.Scale);
            renderer.Render(canvas, compact, IslandTestFixtures.Scale);
            renderer.Render(canvas, notifications, IslandTestFixtures.Scale);
            renderer.Render(canvas, widgets, IslandTestFixtures.Scale);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var perFrame = allocated / (double)(frames * 4);
        Directory.CreateDirectory(IslandTestFixtures.ReportDir);
        File.WriteAllText(
            Path.Combine(IslandTestFixtures.ReportDir, "allocations.md"),
            $"""
            # FluidBar island MVP — warm steady-state allocations

            Environment: {Environment.MachineName} · {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}
            Surfaces per frame: expanded media (page strip), compact media, notifications, widgets.

            | Metric | Value |
            |---|---:|
            | Warm-up frames per surface | 12 |
            | Measured frames per surface | {frames} |
            | Measured surfaces | 4 |
            | Total allocated | {allocated} B |
            | Allocated per surface-frame | {perFrame:0.0} B |
            | Gate | < 1024 B/frame |

            The measured value is the steady-state cost after every text layout, shadow image, squircle
            path and icon is cached; a value of 0 B means the warm frame path is allocation-free.
            """);
        Assert.True(perFrame < 1024, $"warm frames allocated {perFrame:0.0} B/frame ({allocated} B total)");
    }

    [Fact]
    public void Warm_compact_frames_do_not_allocate()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var state = IslandTestFixtures.Agent();

        using var bitmap = new SKBitmap(new SKImageInfo(882, 576, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        for (var i = 0; i < 8; i++)
        {
            renderer.Render(canvas, state, IslandTestFixtures.Scale);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
        {
            renderer.Render(canvas, state, IslandTestFixtures.Scale);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated / 50.0 < 1024, $"compact warm frame allocated {allocated / 50.0:0.0} B/frame");
    }
}
