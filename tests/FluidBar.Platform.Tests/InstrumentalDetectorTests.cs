using System.Collections.Immutable;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Instrumentals must never inherit another song's lyric. These pin the two signals the island uses.
/// </summary>
public sealed class InstrumentalDetectorTests
{
    [Theory]
    [InlineData("平静的海面如此安宁 (纯音乐)")]
    [InlineData("Song (Instrumental)")]
    [InlineData("晴天 (伴奏)")]
    [InlineData("Track - Off Vocal")]
    [InlineData("カラオケ版")]
    public void Instrumental_titles_are_detected(string title)
        => Assert.True(InstrumentalDetector.LooksInstrumental(title));

    [Theory]
    [InlineData("晴天")]
    [InlineData("Stjæler Mit Hjerte (偷走了我的心)")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_titles_are_not(string? title)
        => Assert.False(InstrumentalDetector.LooksInstrumental(title));

    [Fact]
    public void Placeholder_document_is_recognised()
    {
        var document = new LyricsDocument("x", "y", "kugou-krc",
            ImmutableArray.Create(new LyricsLine(TimeSpan.FromMilliseconds(500), "纯音乐，请欣赏")));
        Assert.True(InstrumentalDetector.IsPlaceholder(document));
    }

    [Fact]
    public void Real_lyrics_are_not_a_placeholder()
    {
        var document = new LyricsDocument("x", "y", "kugou-krc",
            ImmutableArray.Create(
                new LyricsLine(TimeSpan.FromMilliseconds(500), "第一句"),
                new LyricsLine(TimeSpan.FromMilliseconds(4000), "第二句"),
                new LyricsLine(TimeSpan.FromMilliseconds(8000), "第三句")));
        Assert.False(InstrumentalDetector.IsPlaceholder(document));
    }

    [Fact]
    public void Single_unrelated_line_is_not_a_placeholder()
    {
        var document = new LyricsDocument("x", "y", "kugou-krc",
            ImmutableArray.Create(new LyricsLine(TimeSpan.FromMilliseconds(500), "Hello, world")));
        Assert.False(InstrumentalDetector.IsPlaceholder(document));
    }

    [Fact]
    public void Credit_only_document_is_treated_as_instrumental()
    {
        var document = new LyricsDocument("x", "y", "kugou-krc",
            ImmutableArray.Create(
                new LyricsLine(TimeSpan.FromMilliseconds(500), "作词：张三"),
                new LyricsLine(TimeSpan.FromMilliseconds(2000), "作曲：李四"),
                new LyricsLine(TimeSpan.FromMilliseconds(4000), "编曲：王五")));
        Assert.True(InstrumentalDetector.IsPlaceholder(document));
    }

    [Fact]
    public void Empty_document_is_not_a_placeholder()
        => Assert.False(InstrumentalDetector.IsPlaceholder(LyricsDocument.Empty));
}
