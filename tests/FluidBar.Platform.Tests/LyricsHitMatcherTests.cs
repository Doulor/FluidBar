using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// A lyric hit is only usable when it is the same recording. The online providers used to take the first search
/// result unconditionally, which served another song's lyric (report 2026-10-26: "作词 : 小柯" for a Jeff Satur track).
/// </summary>
public sealed class LyricsHitMatcherTests
{
    [Fact]
    public void The_same_title_and_artist_is_accepted()
    {
        Assert.True(LyricsHitMatcher.Accept("Fade (English Version)", "Jeff Satur", TimeSpan.Zero,
            "Fade (English Version)", "Jeff Satur", TimeSpan.Zero));
    }

    [Fact]
    public void A_different_artist_with_the_same_title_is_rejected()
    {
        Assert.False(LyricsHitMatcher.Accept("Fade (English Version)", "Jeff Satur", TimeSpan.FromSeconds(262),
            "Fade", "小柯", TimeSpan.FromSeconds(200)));
    }

    [Fact]
    public void A_decorated_title_still_passes_when_the_length_agrees()
    {
        Assert.True(LyricsHitMatcher.Accept("Fade (English Version)", "Jeff Satur", TimeSpan.FromSeconds(262),
            "Fade", "Jeff Satur", TimeSpan.FromSeconds(264)));
    }

    [Fact]
    public void A_decorated_title_with_a_different_length_is_rejected()
    {
        Assert.False(LyricsHitMatcher.Accept("Fade (English Version)", "Jeff Satur", TimeSpan.FromSeconds(262),
            "Fade", "Jeff Satur", TimeSpan.FromSeconds(180)));
    }

    [Fact]
    public void Duration_agreement_needs_both_sides_and_stays_within_ten_seconds()
    {
        Assert.True(LyricsHitMatcher.DurationAgrees(TimeSpan.FromSeconds(262), TimeSpan.FromSeconds(272)));
        Assert.False(LyricsHitMatcher.DurationAgrees(TimeSpan.FromSeconds(262), TimeSpan.FromSeconds(273)));
        Assert.False(LyricsHitMatcher.DurationAgrees(TimeSpan.Zero, TimeSpan.FromSeconds(262)));
    }

    [Fact]
    public void A_multi_artist_credit_matches_the_primary_artist()
    {
        Assert.True(LyricsHitMatcher.Accept("Yesterday", "Alan Walker、Ali Gatie", TimeSpan.Zero,
            "Yesterday", "Alan Walker", TimeSpan.Zero));
    }
}
