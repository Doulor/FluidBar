using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// The catalogue length is the only exact duration KuGou exposes (its SMTC has none and its window text
/// truncates to whole seconds). These tests pin the parsing rules that keep unrelated songs out.
/// </summary>
public sealed class KugouDurationTests
{
    [Fact]
    public void Hash_is_read_from_a_lyric_file_name()
    {
        var path = @"D:\KuGou\Lyric\书兔 - Stjæler Mit Hjerte (偷走了我的心)-AF3A4A434B268BCF2E103B455D784324-1234-00000000.krc";
        Assert.Equal("af3a4a434b268bcf2e103b455d784324", KugouDurationCatalog.TryReadHash(path));
        Assert.Null(KugouDurationCatalog.TryReadHash("no-hash.krc"));
        Assert.Null(KugouDurationCatalog.TryReadHash(null));
    }

    [Fact]
    public void Lyric_lookup_returns_the_declared_duration()
    {
        const string json = """
            {"status":200,"candidates":[{"song":"Stjæler Mit Hjerte","singer":"书兔","duration":303000}]}
            """;
        Assert.Equal(TimeSpan.FromSeconds(303), KugouDurationCatalog.TryParseLyricDuration(json, "Stjæler Mit Hjerte"));
    }

    [Fact]
    public void Lyric_lookup_rejects_candidates_that_name_another_song()
    {
        // Measured 2026-10-22: a hash without a lyric answers with an unrelated song.
        const string json = """
            {"status":200,"candidates":[{"song":"The Seasons Op. 37b: June - Barcarole","singer":"arkady sevidov","duration":320317}]}
            """;
        Assert.Null(KugouDurationCatalog.TryParseLyricDuration(json, "Stjæler Mit Hjerte"));
    }

    [Fact]
    public void Lyric_lookup_accepts_a_version_suffix()
    {
        const string json = """
            {"status":200,"candidates":[{"song":"一人饮酒醉 (DJ版)","singer":"天佑","duration":79000}]}
            """;
        Assert.Equal(TimeSpan.FromSeconds(79), KugouDurationCatalog.TryParseLyricDuration(json, "一人饮酒醉"));
    }

    [Fact]
    public void Lyric_lookup_ignores_implausibly_short_candidates()
    {
        const string json = """
            {"status":200,"candidates":[{"song":"5","singer":"","duration":13296}]}
            """;
        Assert.Null(KugouDurationCatalog.TryParseLyricDuration(json, "5"));
    }

    [Fact]
    public void Catalogue_search_prefers_the_exact_artist()
    {
        const string json = """
            {"status":1,"data":{"info":[
              {"songname":"Stjæler Mit Hjerte","singername":"Basim","duration":248},
              {"songname":"Stjæler Mit Hjerte","singername":"书兔","duration":303}
            ]}}
            """;
        Assert.Equal(TimeSpan.FromSeconds(303), KugouDurationCatalog.TryParseSearchDuration(json, "Stjæler Mit Hjerte", "书兔"));
    }

    [Fact]
    public void Catalogue_search_rejects_other_songs()
    {
        const string json = """
            {"status":1,"data":{"info":[{"songname":"Die Fir You","singername":"书兔","duration":291}]}}
            """;
        Assert.Null(KugouDurationCatalog.TryParseSearchDuration(json, "Stjæler Mit Hjerte", "书兔"));
    }

    [Fact]
    public void Krc_declared_total_wins_over_a_malformed_last_line()
    {
        // Real file shape: the last span field repeats the song length, so start + span doubles the end.
        var parsed = KrcDecoder.Parse("[total:171023]\n[156328,171023]<0,3104,0>Silent");
        Assert.NotNull(parsed);
        Assert.Equal(TimeSpan.FromMilliseconds(171023), parsed!.DeclaredTotal);
        Assert.Equal(TimeSpan.FromMilliseconds(171023), parsed.Document.Duration);
    }

    [Fact]
    public void Krc_without_total_keeps_the_last_line_end()
    {
        var parsed = KrcDecoder.Parse("[total:0]\n[0,2000]a\n[2000,1500]b");
        Assert.NotNull(parsed);
        Assert.Equal(TimeSpan.Zero, parsed!.DeclaredTotal);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), parsed.Document.Duration);
    }
}
