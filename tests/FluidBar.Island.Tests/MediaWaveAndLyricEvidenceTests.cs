using FluidBar.Core.Activities;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// Task-41 media polish evidence: the compact wave follows prototype2's 4→13→6 px / 880 ms
/// alternate animation, and the expanded lyric card uses the same KaraokeText path at 11 DIP with a
/// visible per-word sweep.
/// </summary>
public sealed class MediaWaveAndLyricEvidenceTests
{
    private const float Scale = IslandTestFixtures.Scale;

    [Fact]
    public void Wave_bars_animate_visibly_between_phases()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var early = renderer.RenderToBitmap(MediaPill(0.0), Scale);
        using var late = renderer.RenderToBitmap(MediaPill(0.22), Scale);

        var shell = IslandMetrics.ShellRect(MediaPill(0.0));
        var wave = CompactMediaLayout.WaveRect((float)shell.Width, (float)shell.Height);
        var waveRect = new SKRect(
            (shell.Left + wave.Left) * Scale,
            (shell.Top + wave.Top) * Scale,
            (shell.Left + wave.Right) * Scale,
            (shell.Top + wave.Bottom) * Scale);

        var changed = DiffPixels(early, late, waveRect);
        Assert.True(changed > 20, $"wave should move between phases ({changed} pixels changed)");
        Save(early, "compact-wave-phase-0.png");
        Save(late, "compact-wave-phase-22.png");
    }

    [Fact]
    public void Expanded_lyric_is_karaoke_at_the_prototype_11_dip_size()
    {
        Assert.Equal(11f, IslandType.Lyric.Size, 3);

        var early = ExpandedLyric(0.18);
        var late = ExpandedLyric(0.82);
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        using var earlyBitmap = renderer.RenderToBitmap(early, Scale);
        using var lateBitmap = renderer.RenderToBitmap(late, Scale);

        var shell = IslandMetrics.ShellRect(early);
        var page = IslandMetrics.PageRect(early, 1);
        var lyric = PageMetricsLayout.MediaLyricCard(page, FontScaleMetrics.One);
        var lyricRect = new SKRect(
            lyric.Left * Scale,
            lyric.Top * Scale,
            lyric.Right * Scale,
            lyric.Bottom * Scale);

        Assert.True(DiffPixels(earlyBitmap, lateBitmap, lyricRect) > 40,
            "the expanded lyric sweep must change pixels inside the lyric card");
        Save(earlyBitmap, "expanded-lyric-sweep-018.png");
        Save(lateBitmap, "expanded-lyric-sweep-082.png");
    }

    private static IslandRenderState MediaPill(double phase) => IslandTestFixtures.Compact(
        new CompactActivity(ActivityKind.Media, new CompactModel
        {
            Title = "Midnight City",
            Subtitle = "M83",
            IsActive = true,
            ProgressPercent = 42,
        })) with { MotionPhase = phase };

    private static IslandRenderState ExpandedLyric(double highlight)
    {
        var state = IslandTestFixtures.Expanded(1);
        var media = state.Pages.Media with
        {
            LyricLine = "Waiting for a car to drive me home",
            NextLyricLine = "The city is my church",
            LyricHighlight = highlight,
        };
        return state with { Pages = state.Pages with { Media = media } };
    }

    private static int DiffPixels(SKBitmap left, SKBitmap right, SKRect area)
    {
        var count = 0;
        var leftPx = left.Pixels;
        var rightPx = right.Pixels;
        var x0 = Math.Max(0, (int)area.Left);
        var y0 = Math.Max(0, (int)area.Top);
        var x1 = Math.Min(left.Width, (int)Math.Ceiling(area.Right));
        var y1 = Math.Min(left.Height, (int)Math.Ceiling(area.Bottom));
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (leftPx[(y * left.Width) + x] != rightPx[(y * right.Width) + x])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void Save(SKBitmap bitmap, string name)
    {
        var dir = Path.Combine(RepoRoot(), "build", "reports", "ui-audit", "media-fix");
        Directory.CreateDirectory(dir);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(dir, name));
        data.SaveTo(stream);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FluidBar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
