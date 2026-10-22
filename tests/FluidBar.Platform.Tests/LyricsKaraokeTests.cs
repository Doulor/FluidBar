using System.Collections.Immutable;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>KRC word timeline, karaoke sweep and the faster fallback play-state poll.</summary>
public sealed class LyricsKaraokeTests
{
    [Fact]
    public void ParseWords_KeepsTheKrcPerWordTimeline()
    {
        var (text, words) = KrcDecoder.ParseWords("<0,2,0>Today <2000,5,0>is fine");

        Assert.Equal("Today is fine", text);
        Assert.Equal(2, words.Length);
        Assert.Equal(0, words[0].OffsetMs);
        Assert.Equal(2, words[0].Length);
        Assert.Equal(2000, words[1].OffsetMs);
        Assert.Equal(5, words[1].Length);
    }

    [Fact]
    public void ParseWords_LinesWithoutTagsCarryNoWordTiming()
    {
        var (text, words) = KrcDecoder.ParseWords("plain line");

        Assert.Equal("plain line", text);
        Assert.True(words.IsDefaultOrEmpty);
    }

    [Fact]
    public void ParseWords_ClampsNegativeOffsetsAndSortsOutOfOrderSpans()
    {
        var (_, words) = KrcDecoder.ParseWords("<3000,3,0>cd<-50,1,0>a<1000,1,0>b");

        Assert.Equal(3, words.Length);
        Assert.All(words, word => Assert.True(word.OffsetMs >= 0));
        for (var index = 1; index < words.Length; index++)
        {
            Assert.True(words[index].OffsetMs >= words[index - 1].OffsetMs);
        }
    }

    [Fact]
    public void Parse_AttachesWordTimingToTheParsedLines()
    {
        var parsed = KrcDecoder.Parse("[0,2000]<0,2,0>He<1000,3,0>llo");
        Assert.NotNull(parsed);

        var line = Assert.Single(parsed!.Document.Lines);
        Assert.Equal("Hello", line.Text);
        Assert.True(line.HasWordTiming);
        Assert.Equal(2, line.Words.Length);
    }

    [Fact]
    public void Progress_InterpolatesInsideAWordAndReachesOneAtTheEnd()
    {
        var line = new LyricsLine(TimeSpan.Zero, "Hello", null,
            ImmutableArray.Create(new LyricWord(0, 2, "He"), new LyricWord(1000, 3, "llo")));

        var start = LyricKaraoke.Progress(line, TimeSpan.Zero);
        var midFirst = LyricKaraoke.Progress(line, TimeSpan.FromMilliseconds(500));
        var afterFirst = LyricKaraoke.Progress(line, TimeSpan.FromMilliseconds(1000));
        var end = LyricKaraoke.Progress(line, TimeSpan.FromSeconds(5));

        Assert.Equal(0, start, 3);
        Assert.InRange(midFirst, 0.15, 0.35);
        Assert.InRange(afterFirst, 0.35, 0.45);
        Assert.Equal(1, end, 3);
    }

    [Fact]
    public void Progress_WithoutWordTimingFallsBackToTheWholeLine()
    {
        var line = new LyricsLine(TimeSpan.Zero, "Hello");

        Assert.Equal(LyricKaraoke.NoWordTiming, LyricKaraoke.Progress(line, TimeSpan.FromSeconds(1)));
        Assert.Equal(LyricKaraoke.NoWordTiming, LyricKaraoke.Progress(null, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void SelectDetailed_ReturnsTheLineNextLineAndSweep()
    {
        var document = new LyricsDocument("t", "a", "kugou-krc",
            ImmutableArray.Create(
                new LyricsLine(TimeSpan.Zero, "Hello", null,
                    ImmutableArray.Create(new LyricWord(0, 2, "He"), new LyricWord(1000, 3, "llo"))),
                new LyricsLine(TimeSpan.FromSeconds(3), "World")));

        // 1000 ms still sits inside the line's word timeline (it ends ~1400 ms); past that the line hands
        // over to the next one immediately (see LyricLineSelectionTests).
        var selection = LyricsTimelineSelector.SelectDetailed(document, TimeSpan.FromMilliseconds(1000), null, null);

        Assert.Equal("Hello", selection.Current);
        Assert.Equal("World", selection.Next);
        Assert.InRange(selection.Highlight, 0.4, 1.0);
    }

    [Fact]
    public void FallbackPollInterval_TracksTapsWithinAHumanFrameBudget()
    {
        Assert.True(GsmtcMediaSessionService.FallbackPollInterval <= TimeSpan.FromMilliseconds(300),
            "the fallback play-state poll must stay within 200-300 ms");
        Assert.True(GsmtcMediaSessionService.FallbackPollInterval >= TimeSpan.FromMilliseconds(150));
    }
}
