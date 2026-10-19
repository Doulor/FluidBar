using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Monitors;

namespace FluidBar.Platform.Windows.Tests;

/// <summary>Window-title parsing for players without SMTC (KuGou and friends).</summary>
public sealed class MediaWindowFallbackTests
{
    [Fact]
    public void Kugou_title_parses_artist_first()
    {
        var candidate = MediaWindowFallback.Parse("KuGou", "Avicii、Joe Janiak - Bad Reputation - 酷狗音乐");

        Assert.NotNull(candidate);
        Assert.Equal("Bad Reputation", candidate.Title);
        Assert.Equal("Avicii、Joe Janiak", candidate.Artist);
        Assert.Equal("kugou", candidate.ProcessName);
        Assert.Contains("Bad Reputation", candidate.TrackKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Kugou_playlist_first_title_keeps_the_track_as_the_title()
    {
        var candidate = MediaWindowFallback.Parse("kugou", "耳畔轻音间 - Horizon Glow (顶级纯音乐) - 酷狗音乐");

        Assert.NotNull(candidate);
        Assert.Equal("Horizon Glow (顶级纯音乐)", candidate.Title);
        Assert.Equal("耳畔轻音间", candidate.Artist);
    }

    [Fact]
    public void Standard_player_title_uses_song_then_artist()
    {
        var candidate = MediaWindowFallback.Parse("QQMusic", "Midnight City - M83 - QQ音乐");

        Assert.NotNull(candidate);
        Assert.Equal("Midnight City", candidate.Title);
        Assert.Equal("M83", candidate.Artist);
    }

    [Fact]
    public void Title_without_separators_becomes_the_track_title()
    {
        var candidate = MediaWindowFallback.Parse("spotify", "Some Track");

        Assert.NotNull(candidate);
        Assert.Equal("Some Track", candidate.Title);
        Assert.Equal(string.Empty, candidate.Artist);
    }

    [Fact]
    public void App_name_only_title_is_rejected()
    {
        Assert.Null(MediaWindowFallback.Parse("kugou", "酷狗音乐"));
        Assert.Null(MediaWindowFallback.Parse("kugou", ""));
        Assert.Null(MediaWindowFallback.Parse("", "Song - Artist"));
    }

    [Fact]
    public void Brightness_minimum_never_goes_to_zero()
    {
        Assert.True(BrightnessMath.MinimumPercent >= 1);
        Assert.Equal(BrightnessMath.MinimumPercent, Math.Clamp(0, BrightnessMath.MinimumPercent, 100));
    }
}
