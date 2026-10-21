using System.Collections.Immutable;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// The current lyric line switches on the next line's <c>StartMs</c>, not on the previous line's end
/// (a gap between the two must never hold the old line on screen).
/// </summary>
public sealed class LyricLineSelectionTests
{
    private static LyricsDocument Document() => new(
        "t", "a", "test",
        ImmutableArray.Create(
            new LyricsLine(TimeSpan.FromMilliseconds(1000), "Line A"),
            new LyricsLine(TimeSpan.FromMilliseconds(2000), "Line B"),
            new LyricsLine(TimeSpan.FromMilliseconds(3000), "Line C")));

    [Fact]
    public void Line_switches_exactly_on_the_next_start()
    {
        var document = Document();

        Assert.Equal("Line A", LyricsTimelineSelector.SelectDetailed(document,
            TimeSpan.FromMilliseconds(1999), null, null).Current);
        Assert.Equal("Line B", LyricsTimelineSelector.SelectDetailed(document,
            TimeSpan.FromMilliseconds(2000), null, null).Current);
        Assert.Equal("Line B", LyricsTimelineSelector.SelectDetailed(document,
            TimeSpan.FromMilliseconds(2999), null, null).Current);
        Assert.Equal("Line C", LyricsTimelineSelector.SelectDetailed(document,
            TimeSpan.FromMilliseconds(3000), null, null).Current);
    }

    [Fact]
    public void Gap_between_lines_does_not_delay_the_switch()
    {
        // Line A "ends" long before 2000 (no end times exist), yet the switch still happens at 2000 sharp.
        var document = Document();
        for (var ms = 1990; ms <= 2010; ms += 2)
        {
            var selection = LyricsTimelineSelector.SelectDetailed(document,
                TimeSpan.FromMilliseconds(ms), null, null);
            Assert.Equal(ms >= 2000 ? "Line B" : "Line A", selection.Current);
        }
    }
}
