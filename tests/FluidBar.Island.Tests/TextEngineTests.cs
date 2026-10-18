using FluidBar.Island;

namespace FluidBar.Island.Tests;

public sealed class TextEngineTests
{
    [Fact]
    public void Layout_cache_is_bounded_and_evicts()
    {
        using var text = new TextEngine(cacheCapacity: 16);
        var style = new TextStyle(FontRole.Ui, 13.5f);

        for (var i = 0; i < 200; i++)
        {
            text.GetLayout($"unique string {i}", style, 180);
        }

        Assert.True(text.CacheCount <= 16, $"cache grew to {text.CacheCount}");
        Assert.True(text.Evictions > 0);
        Assert.True(text.Misses >= 200);
        Assert.True(text.ApproxCacheBytes > 0);
    }

    [Fact]
    public void Warm_hits_do_not_grow_the_cache()
    {
        using var text = new TextEngine(cacheCapacity: 32);
        var style = new TextStyle(FontRole.Ui, 13.5f);
        text.GetLayout("Midnight City", style, 180);
        var count = text.CacheCount;
        var hits = text.Hits;

        for (var i = 0; i < 100; i++)
        {
            text.GetLayout("Midnight City", style, 180);
        }

        Assert.Equal(count, text.CacheCount);
        Assert.Equal(hits + 100, text.Hits);
    }

    [Fact]
    public void Shapes_cjk_and_emoji_into_multiple_runs()
    {
        using var text = new TextEngine();
        var layout = text.GetLayout("正在播放 🎵", new TextStyle(FontRole.UiSemiBold, 13.5f), 400);

        Assert.True(layout.Width > 0);
        Assert.True(layout.GlyphCount > 0);
        Assert.True(layout.RunCount >= 2, $"expected a CJK + emoji run split, got {layout.RunCount}");
        Assert.False(layout.Trimmed);
        Assert.Equal("正在播放 🎵", layout.Text);
    }

    [Fact]
    public void Emoji_zwj_sequence_is_shaped_as_one_cluster()
    {
        using var text = new TextEngine();
        var layout = text.GetLayout("👩‍💻", new TextStyle(FontRole.Ui, 20f), 200);
        Assert.True(layout.Width > 0);
        Assert.True(layout.GlyphCount >= 1);
    }

    [Fact]
    public void Long_text_is_trimmed_to_the_available_width()
    {
        using var text = new TextEngine();
        var style = new TextStyle(FontRole.Ui, 13.5f);
        var layout = text.GetLayout(
            "This is a deliberately long notification title that cannot fit inside the pill",
            style,
            120);

        Assert.True(layout.Trimmed);
        Assert.True(layout.Width <= 120.5f, $"trimmed width {layout.Width} > 120");
        Assert.EndsWith("…", layout.Text);
    }

    [Fact]
    public void Fractional_sizes_are_supported_and_cached()
    {
        using var text = new TextEngine();
        var small = new TextStyle(FontRole.Ui, 10.5f);
        var large = new TextStyle(FontRole.Ui, 10.75f);
        var a = text.Measure("FluidBar", small);
        var b = text.Measure("FluidBar", large);
        Assert.True(a > 0);
        Assert.True(b > 0);
        Assert.True(text.CacheCount >= 2);
    }

    [Fact]
    public void Clear_cache_drops_layouts_but_keeps_working()
    {
        using var text = new TextEngine();
        text.GetLayout("hello", new TextStyle(FontRole.Ui, 12f), 100);
        Assert.True(text.CacheCount > 0);

        text.ClearCache();
        Assert.Equal(0, text.CacheCount);
        Assert.Equal(0, text.Hits);

        var layout = text.GetLayout("hello", new TextStyle(FontRole.Ui, 12f), 100);
        Assert.True(layout.Width > 0);
    }

    [Fact]
    public void Missing_fonts_fall_back_without_throwing()
    {
        using var text = new TextEngine();
        var layout = text.GetLayout("こんにちは · 안녕하세요 · Café — ½", new TextStyle(FontRole.Ui, 13f), 400);
        Assert.True(layout.Width > 0);
        Assert.True(layout.GlyphCount > 0);
    }
}
