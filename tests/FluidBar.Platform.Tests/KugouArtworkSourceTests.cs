using System.Net;
using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>
/// KuGou catalogue source. It exists because the two incumbent catalogues are unreachable from mainland China
/// (itunes.apple.com does not resolve, api.deezer.com refuses the connection), so every KuGou track without a
/// player-cached cover showed the gradient placeholder (report 2026-10-26).
/// </summary>
public sealed class KugouArtworkSourceTests
{
    [Fact]
    public async Task Picks_the_confident_hit_and_resolves_the_size_placeholder()
    {
        var requested = new List<string>();
        using var http = new HttpClient(Handler(request =>
        {
            requested.Add(request.RequestUri!.AbsoluteUri);
            if (request.RequestUri!.AbsoluteUri.Contains("song_search_v2", StringComparison.Ordinal))
            {
                const string json = """
                    {"status":1,"data":{"lists":[
                      {"SongName":"Midnight City","SingerName":"Some Cover Band","AlbumName":"","Image":"http://imge.kugou.com/stdmusic/{size}/wrong.jpg"},
                      {"SongName":"Midnight City","SingerName":"M83","AlbumName":"Hurry Up, We're Dreaming","Image":"http://imge.kugou.com/stdmusic/{size}/right.jpg"}
                    ]}}
                    """;
                return Json(json);
            }

            return Png();
        }));
        var source = new KugouArtworkSource(http);

        var result = await source.FindAsync("M83", "Midnight City", null, CancellationToken.None);

        Assert.NotNull(result.Candidate);
        Assert.Equal(ArtworkFetchFailure.None, result.Failure);
        Assert.Equal("http://imge.kugou.com/stdmusic/400/right.jpg", result.Candidate!.SourceUrl);
        Assert.Equal("M83", result.Candidate.Artist);
        Assert.True(result.Candidate.Score >= ArtworkTitleMatcher.DefaultThreshold);
        Assert.Contains("keyword=M83%20Midnight%20City", requested[0]);
    }

    [Fact]
    public async Task Prefers_album_art_over_a_singer_portrait()
    {
        using var http = new HttpClient(Handler(request =>
            request.RequestUri!.AbsoluteUri.Contains("song_search_v2", StringComparison.Ordinal)
                ? Json("""
                    {"status":1,"data":{"lists":[
                      {"SongName":"Song","SingerName":"Artist","AlbumName":"","Image":"http://singerimg.kugou.com/uploadpic/softhead/{size}/portrait.jpg"},
                      {"SongName":"Song","SingerName":"Artist","AlbumName":"Album","Image":"http://imge.kugou.com/stdmusic/{size}/cover.jpg"}
                    ]}}
                    """)
                : Png()));
        var source = new KugouArtworkSource(http);

        var result = await source.FindAsync("Artist", "Song", null, CancellationToken.None);

        Assert.Equal("http://imge.kugou.com/stdmusic/400/cover.jpg", result.Candidate!.SourceUrl);
    }

    [Fact]
    public async Task Uses_the_portrait_when_the_catalogue_has_no_album_art()
    {
        using var http = new HttpClient(Handler(request =>
            request.RequestUri!.AbsoluteUri.Contains("song_search_v2", StringComparison.Ordinal)
                ? Json("""
                    {"status":1,"data":{"lists":[
                      {"SongName":"Song","SingerName":"Artist","AlbumName":"","Image":"http://singerimg.kugou.com/uploadpic/softhead/{size}/portrait.jpg"}
                    ]}}
                    """)
                : Png()));
        var source = new KugouArtworkSource(http);

        var result = await source.FindAsync("Artist", "Song", null, CancellationToken.None);

        Assert.NotNull(result.Candidate);
        Assert.Contains("softhead", result.Candidate!.SourceUrl);
    }

    [Fact]
    public async Task Reports_below_threshold_for_another_song()
    {
        using var http = new HttpClient(Handler(request =>
            request.RequestUri!.AbsoluteUri.Contains("song_search_v2", StringComparison.Ordinal)
                ? Json("""
                    {"status":1,"data":{"lists":[
                      {"SongName":"Something Else Entirely","SingerName":"Nobody Known","AlbumName":"X","Image":"http://imge.kugou.com/stdmusic/{size}/x.jpg"}
                    ]}}
                    """)
                : Png()));
        var source = new KugouArtworkSource(http);

        var result = await source.FindAsync("Alan Walker", "Yesterday", null, CancellationToken.None);

        Assert.Null(result.Candidate);
        Assert.Equal(ArtworkFetchFailure.BelowThreshold, result.Failure);
    }

    [Fact]
    public async Task Reports_network_error_when_the_catalogue_does_not_answer()
    {
        using var http = new HttpClient(Handler(_ => throw new HttpRequestException("blocked")));
        var source = new KugouArtworkSource(http);

        var result = await source.FindAsync("M83", "Midnight City", null, CancellationToken.None);

        Assert.Null(result.Candidate);
        Assert.Equal(ArtworkFetchFailure.NetworkError, result.Failure);
    }

    [Fact]
    public async Task Rejects_empty_payloads_and_blanks()
    {
        using var http = new HttpClient(Handler(_ => Json("""{"status":1,"data":{"lists":[]}}""")));
        var source = new KugouArtworkSource(http);

        var empty = await source.FindAsync("M83", "Midnight City", null, CancellationToken.None);
        Assert.Equal(ArtworkFetchFailure.NoResults, empty.Failure);

        var blank = await source.FindAsync("M83", "   ", null, CancellationToken.None);
        Assert.Equal(ArtworkFetchFailure.NoResults, blank.Failure);
    }

    [Theory]
    [InlineData("http://imge.kugou.com/stdmusic/{size}/a.jpg", "http://imge.kugou.com/stdmusic/400/a.jpg")]
    [InlineData("http://imge.kugou.com/stdmusic/240/a.jpg", "http://imge.kugou.com/stdmusic/240/a.jpg")]
    [InlineData("", null)]
    [InlineData("ftp://nope/a.jpg", null)]
    public void Cover_url_substitutes_the_size_placeholder(string image, string? expected)
    {
        Assert.Equal(expected, KugouArtworkSource.CoverUrl(image));
    }

    [Fact]
    public void Covers_from_kugou_are_recorded_as_the_kugou_kind()
    {
        Assert.Equal(ArtworkSourceKind.Kugou, ArtworkFetcher.KindFor("http://imge.kugou.com/stdmusic/400/a.jpg"));
        Assert.Equal(ArtworkSourceKind.ITunes, ArtworkFetcher.KindFor("https://is1-ssl.mzstatic.com/a.jpg"));
        Assert.Equal(ArtworkSourceKind.PlayerCache, ArtworkFetcher.KindFor("https://cdn-images.dzcdn.net/a.jpg"));
    }

    private static HttpMessageHandler Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => new Stub(respond);

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Png()
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==")) };

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
