using FluidBar.Island;
using SkiaSharp;

namespace FluidBar.Island.Tests;

/// <summary>
/// A source without seek capability (KuGou: no SMTC session) must paint its position row read-only:
/// the same geometry, but dimmed and with a smaller knob, so nobody tries to drag a dead slider.
/// </summary>
public sealed class MediaSeekReadOnlyTests
{
    private static IslandRenderState With(bool canSeek)
    {
        var fixture = IslandTestFixtures.Expanded(1);
        return fixture with { Pages = fixture.Pages with { Media = fixture.Pages.Media with { CanSeek = canSeek } } };
    }

    private static long InkInSeekRow(SKBitmap bitmap, in IslandRenderState state)
    {
        var page = IslandMetrics.PageRect(state, 1);
        var metrics = FontScaleMetrics.From(state.FontScale);
        var row = PageMetricsLayout.MediaSeekTrack(page, metrics);
        var scale = IslandTestFixtures.Scale;
        var total = 0L;
        for (var y = (int)(row.Top * scale); y < (int)Math.Ceiling(row.Bottom * scale); y++)
        {
            for (var x = (int)(row.Left * scale); x < (int)Math.Ceiling(row.Right * scale); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                total += (pixel.Red + pixel.Green + pixel.Blue) * pixel.Alpha / 255;
            }
        }

        return total;
    }

    [Fact]
    public void Read_only_seek_row_paints_dimmer_than_a_seekable_one()
    {
        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var seekable = With(true);
        var readOnly = With(false);

        using var seekableBitmap = renderer.RenderToBitmap(seekable, IslandTestFixtures.Scale);
        using var readOnlyBitmap = renderer.RenderToBitmap(readOnly, IslandTestFixtures.Scale);

        var live = InkInSeekRow(seekableBitmap, seekable);
        var dead = InkInSeekRow(readOnlyBitmap, readOnly);
        Assert.True(live > 0, "the seekable row painted nothing");
        Assert.True(dead < live, $"the read-only row must be dimmer (live={live}, dead={dead})");
    }
}
