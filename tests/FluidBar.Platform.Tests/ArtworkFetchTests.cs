using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>Active cover lookup: match confidence, disk cache, source order, failure attribution.</summary>
public sealed class ArtworkFetchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "fluidbar-artwork-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Smallest valid PNG (1×1) so the header check accepts the bytes.</summary>
    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private ArtworkDiskCache Cache() => new(new ArtworkDiskCacheOptions { DirectoryPath = _directory });

    [Fact]
    public void Normalize_folds_case_decorations_and_punctuation()
    {
        Assert.Equal("untamed", ArtworkTitleMatcher.Normalize("Untamed（失 控）"));
        Assert.Equal("untamed", ArtworkTitleMatcher.Normalize("Untamed (失控)"));
        Assert.Equal("miss you a little", ArtworkTitleMatcher.Normalize("Miss You a Little (feat. lovelytheband)"));
        Assert.Equal(string.Empty, ArtworkTitleMatcher.Normalize(null));
    }

    [Fact]
    public void Confidence_accepts_the_same_song_and_rejects_other_versions()
    {
        Assert.True(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "TryHardNinja", "Dragonhearted (Remix)"));
        Assert.False(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "Piano Tribute Players", "Dragonhearted"));
        Assert.False(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "TryHardNinja", "Take Back the Night"));
        Assert.True(ArtworkTitleMatcher.IsConfident("王菲", "红豆（Live）", "王菲", "红豆"));
    }

    [Fact]
    public void Threshold_is_065_so_catalogue_versions_pass_but_artist_guards_stay()
    {
        Assert.Equal(0.65, ArtworkTitleMatcher.DefaultThreshold, 3);
        var remix = ArtworkTitleMatcher.Score("M83", "Midnight City", "M83", "Midnight City (Remix)");
        Assert.True(remix >= ArtworkTitleMatcher.DefaultThreshold, $"remix scored {remix:0.000}");
        Assert.Equal(0, ArtworkTitleMatcher.Score("M83", "Midnight City", "Cover Band", "Midnight City"), 3);
    }

    [Fact]
    public void Multi_artist_credits_match_across_the_cjk_and_latin_spellings()
    {
        Assert.True(ArtworkTitleMatcher.IsConfident("Alan Walker、Ali Gatie", "Yesterday",
            "Alan Walker & Ali Gatie", "Yesterday"));
        Assert.Equal("Alan Walker", ArtworkTitleMatcher.PrimaryArtist("Alan Walker、Ali Gatie"));
    }

    [Fact]
    public void Disk_cache_round_trips_bytes_and_records_the_source()
    {
        var cache = Cache();
        var key = ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", "Dragonhearted - Single");
        var metadata = new ArtworkCacheMetadata(key, ArtworkSourceKind.ITunes,
            "https://is1-ssl.mzstatic.com/x/600x600bb.jpg", "TryHardNinja", "Dragonhearted",
            "Dragonhearted - Single", 0.94, DateTimeOffset.UtcNow);

        Assert.NotNull(cache.Write(key, TinyPng(), metadata));
        var read = cache.TryRead(key);

        Assert.NotNull(read);
        Assert.Equal(TinyPng(), read!.Bytes);
        Assert.Equal("https://is1-ssl.mzstatic.com/x/600x600bb.jpg", read.Metadata.SourceUrl);
        Assert.Equal(0.94, read.Metadata.Score, 3);
    }

    [Fact]
    public async Task Fetcher_serves_the_disk_cache_without_asking_any_source()
    {
        var source = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.NoResults)));
        using var fetcher = new ArtworkFetcher(Cache(), [source]);
        var key = ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", string.Empty);
        fetcher.Cache.Write(key, TinyPng(), new ArtworkCacheMetadata(key, ArtworkSourceKind.ITunes,
            "https://example.test/a.jpg", "TryHardNinja", "Dragonhearted", string.Empty, 0.9, DateTimeOffset.UtcNow));

        var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);

        Assert.True(outcome.Succeeded);
        Assert.Equal("cache", outcome.Source);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Fetcher_stops_at_the_first_confident_source()
    {
        var first = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.NoResults)));
        var second = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(
            new ArtworkCandidate(TinyPng(), "https://example.test/b.jpg", 0.91, "TryHardNinja", "Dragonhearted",
                "Single"), ArtworkFetchFailure.None, 0.91)));
        var third = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(
            new ArtworkCandidate(TinyPng(), "https://example.test/c.jpg", 0.99, "A", "B", "C"),
            ArtworkFetchFailure.None, 0.99)));
        using var fetcher = new ArtworkFetcher(Cache(), [first, second, third]);

        var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);

        Assert.True(outcome.Succeeded);
        Assert.Equal("https://example.test/b.jpg", outcome.Entry!.Metadata.SourceUrl);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(0, third.Calls);
    }

    [Fact]
    public async Task Fetcher_attributes_the_failure_class()
    {
        var none = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.NoResults)));
        using (var fetcher = new ArtworkFetcher(Cache(), [none]))
        {
            var outcome = await fetcher.FetchAsync("Nobody", "Nothing", null);
            Assert.False(outcome.Succeeded);
            Assert.Equal(ArtworkFetchFailure.NoResults, outcome.Failure);
        }

        var below = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.BelowThreshold, 0.41)));
        using (var fetcher = new ArtworkFetcher(Cache(), [below]))
        {
            var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);
            Assert.Equal(ArtworkFetchFailure.BelowThreshold, outcome.Failure);
            Assert.Equal(0.41, outcome.Score, 3);
        }

        var throwing = new CountingSource(_ => throw new HttpRequestException("catalogue down"));
        using (var fetcher = new ArtworkFetcher(Cache(), [throwing]))
        {
            var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);
            Assert.Equal(ArtworkFetchFailure.NetworkError, outcome.Failure);
        }

        var hanging = new CountingSource(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new ArtworkSourceResult(null, ArtworkFetchFailure.NoResults);
        });
        using (var fetcher = new ArtworkFetcher(Cache(), [hanging],
                   new ArtworkFetcherOptions { Timeout = TimeSpan.FromMilliseconds(120) }))
        {
            var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);
            Assert.Equal(ArtworkFetchFailure.Timeout, outcome.Failure);
        }
    }

    [Fact]
    public async Task Fetcher_keeps_the_most_specific_failure_of_all_sources()
    {
        var noResults = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.NoResults)));
        var network = new CountingSource(_ => Task.FromResult(new ArtworkSourceResult(null,
            ArtworkFetchFailure.NetworkError)));
        using var fetcher = new ArtworkFetcher(Cache(), [noResults, network]);

        var outcome = await fetcher.FetchAsync("TryHardNinja", "Dragonhearted", null);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ArtworkFetchFailure.NetworkError, outcome.Failure);
    }

    [Fact]
    public async Task Itunes_source_upgrades_the_artwork_url_and_rejects_a_wrong_artist()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/search", StringComparison.Ordinal))
            {
                const string json = """
                    {"resultCount":2,"results":[
                      {"trackName":"Dragonhearted","artistName":"Piano Tribute Players","collectionName":"Covers","artworkUrl100":"https://is1-ssl.mzstatic.com/wrong/100x100bb.jpg"},
                      {"trackName":"Dragonhearted","artistName":"TryHardNinja","collectionName":"Dragonhearted - Single","artworkUrl100":"https://is1-ssl.mzstatic.com/right/100x100bb.jpg"}
                    ]}
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(TinyPng())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("image/png") },
                },
            };
        });
        using var http = new HttpClient(handler);
        var source = new ItunesArtworkSource(http);

        var result = await source.FindAsync("TryHardNinja", "Dragonhearted", null, CancellationToken.None);

        Assert.NotNull(result.Candidate);
        Assert.Contains("right/600x600bb.jpg", result.Candidate!.SourceUrl, StringComparison.Ordinal);
        Assert.Equal(ArtworkFetchFailure.None, result.Failure);
    }

    [Fact]
    public async Task Deezer_source_validates_the_hit_and_reports_below_threshold()
    {
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("api.deezer.com", StringComparison.Ordinal))
            {
                const string json = """
                    {"data":[
                      {"title":"Yesterday","artist":{"name":"Someone Else"},"album":{"title":"X","cover_big":"https://cdn/x.jpg"}},
                      {"title":"Yesterday","artist":{"name":"Alan Walker"},"album":{"title":"Y","cover_big":"https://cdn/right.jpg"}}
                    ]}
                    """;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng()) };
        });
        using var http = new HttpClient(handler);
        var source = new DeezerArtworkSource(http);

        var hit = await source.FindAsync("Alan Walker", "Yesterday", null, CancellationToken.None);
        Assert.NotNull(hit.Candidate);
        Assert.Equal("https://cdn/right.jpg", hit.Candidate!.SourceUrl);

        var miss = await source.FindAsync("Totally Different", "Nothing Alike", null, CancellationToken.None);
        Assert.Null(miss.Candidate);
        Assert.Equal(ArtworkFetchFailure.BelowThreshold, miss.Failure);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class CountingSource(Func<CancellationToken, Task<ArtworkSourceResult>> behavior)
        : IArtworkSource
    {
        public string Name => "counting";

        public int Calls { get; private set; }

        public Task<ArtworkSourceResult> FindAsync(string? artist, string? title, string? album,
            CancellationToken cancellationToken)
        {
            Calls++;
            return behavior(cancellationToken);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
