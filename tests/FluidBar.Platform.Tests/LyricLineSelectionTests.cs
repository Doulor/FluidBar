using System.Collections.Immutable;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// A lyric line must be on screen BEFORE its first word is sung: the island's clock comes from the
/// player's whole-second text and runs a fraction behind the audio, so the selection carries a small
/// lead. A finished line also hands over immediately, however far away the next timestamp is.
/// </summary>
public sealed class LyricLineSelectionTests
{
    private static LyricsDocument Document() => new(
        "t", "a", "test",
        ImmutableArray.Create(
            new LyricsLine(TimeSpan.FromMilliseconds(1000), "Line A"),
            new LyricsLine(TimeSpan.FromMilliseconds(2000), "Line B"),
            new LyricsLine(TimeSpan.FromMilliseconds(3000), "Line C")));

    private static LyricsDocument WordTimed(params LyricsLine[] lines)
        => new("t", "a", "test", ImmutableArray.Create(lines));

    private static string? At(LyricsDocument document, int ms)
        => LyricsTimelineSelector.SelectDetailed(document, TimeSpan.FromMilliseconds(ms), null, null).Current;

    [Fact]
    public void Line_appears_before_its_first_word()
    {
        var document = Document();

        // The lead (250 ms) makes Line B current before its 2000 ms timestamp.
        Assert.Equal("Line A", At(document, 1700));
        Assert.Equal("Line B", At(document, 1780));
        Assert.Equal("Line B", At(document, 2000));
        Assert.Equal("Line C", At(document, 2800));
    }

    [Fact]
    public void Finished_line_hands_over_immediately_across_a_long_gap()
    {
        // Line A's words end ~1600 ms; Line B starts at 6000 ms. A used to stay on screen for the whole
        // gap and B only appeared once the singing had already started.
        var document = WordTimed(
            new LyricsLine(TimeSpan.FromMilliseconds(1000), "Line A", null,
                ImmutableArray.Create(new LyricWord(0, 2, "Li"), new LyricWord(200, 4, "ne A"))),
            new LyricsLine(TimeSpan.FromMilliseconds(6000), "Line B"));

        Assert.Equal("Line A", At(document, 1200));
        Assert.Equal("Line B", At(document, 1500));
    }

    [Fact]
    public void Sweep_starts_at_zero_when_the_line_is_shown_early()
    {
        var document = WordTimed(
            new LyricsLine(TimeSpan.FromMilliseconds(1000), "Line A", null,
                ImmutableArray.Create(new LyricWord(0, 2, "Li"), new LyricWord(200, 4, "ne A"))),
            new LyricsLine(TimeSpan.FromMilliseconds(6000), "Line B", null,
                ImmutableArray.Create(new LyricWord(0, 2, "Li"), new LyricWord(200, 4, "ne B"))));

        // Line B is already displayed at 5700 ms (lead) but nothing of it is highlighted yet.
        var selection = LyricsTimelineSelector.SelectDetailed(document,
            TimeSpan.FromMilliseconds(5700), null, null);
        Assert.Equal("Line B", selection.Current);
        Assert.Equal(0, selection.Highlight);
    }
}
