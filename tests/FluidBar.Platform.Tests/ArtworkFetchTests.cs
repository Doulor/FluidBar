using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluidBar.Platform.Windows.Media;

namespace FluidBar.Platform.Tests;

/// <summary>Active cover lookup: match confidence, disk cache, source order and failure fallback.</summary>
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
        // Bracketed decorations are dropped entirely: the player and the catalogues disagree on them.
        Assert.Equal("untamed", ArtworkTitleMatcher.Normalize("Untamed（失 控）"));
        Assert.Equal("untamed", ArtworkTitleMatcher.Normalize("Untamed (失控)"));
        Assert.Equal("miss you a little", ArtworkTitleMatcher.Normalize("Miss You a Little (feat. lovelytheband)"));
        Assert.Equal("dragonhearted", ArtworkTitleMatcher.Normalize("Dragonhearted"));
        Assert.Equal(string.Empty, ArtworkTitleMatcher.Normalize(null));
    }

    [Fact]
    public void Confidence_accepts_the_same_song_and_rejects_other_versions()
    {
        // Same song, same artist: accepted even when the catalogue adds a version suffix.
        Assert.True(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "TryHardNinja", "Dragonhearted (Remix)"));
        // Same title, different artist (piano/karaoke covers): rejected.
        Assert.False(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "Piano Tribute Players", "Dragonhearted"));
        // Different song by the same artist: rejected.
        Assert.False(ArtworkTitleMatcher.IsConfident("TryHardNinja", "Dragonhearted",
            "TryHardNinja", "Take Back the Night"));
        // Chinese titles survive folding (full-width brackets, spaces).
        Assert.True(ArtworkTitleMatcher.IsConfident("王菲", "红豆（Live）", "王菲", "红豆"));
    }

    [Fact]
    public void Disk_cache_round_trips_bytes_and_records_the_source()
    {
        var cache = Cache();
        var key = ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", "Dragonhearted - Single");
        var metadata = new ArtworkCacheMetadata(key, ArtworkSourceKind.ITunes,
            "https://is1-ssl.mzstatic.com/x/600x600bb.jpg", "TryHardNinja", "Dragonhearted",
            "Dragonhearted - Single", 0.94, DateTimeOffset.UtcNow);

        var written = cache.Write(key, TinyPng(), metadata);
        Assert.NotNull(written);

        var read = cache.TryRead(key);
        Assert.NotNull(read);
        Assert.Equal(TinyPng(), read!.Bytes);
        Assert.Equal("https://is1-ssl.mzstatic.com/x/600x600bb.jpg", read.Metadata.SourceUrl);
        Assert.Equal(ArtworkSourceKind.ITunes, read.Metadata.SourceKind);
        Assert.Equal(0.94, read.Metadata.Score, 3);
    }

    [Fact]
    public void Disk_cache_key_is_stable_across_spellings()
    {
        // Case, decorations and empty album spellings must all land on the same cache entry.
        var a = ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", "");
        var b = ArtworkDiskCache.KeyFor("tryhardninja", "Dragonhearted (feat. X)", " ");
        Assert.Equal(a, b);

        // A bracketed version marker must not create a second cache entry for the same song.
        var c = ArtworkDiskCache.KeyFor("王菲", "红豆（Live）", null);
        var d = ArtworkDiskCache.KeyFor("王菲", "红豆", "");
        Assert.Equal(c, d);
    }

    [Fact]
    public async Task Fetcher_serves_the_disk_cache_without_asking_any_source()
    {
        var source = new CountingSource(_ => null);
        using var fetcher = new ArtworkFetcher(Cache(), [source]);
        var key = ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", string.Empty);
        fetcher.Cache.Write(key, TinyPng(), new ArtworkCacheMetadata(key, ArtworkSourceKind.ITunes,
            "https://example.test/a.jpg", "TryHardNinja", "Dragonhearted", string.Empty, 0.9, DateTimeOffset.UtcNow));

        var entry = await fetcher.GetAsync("TryHardNinja", "Dragonhearted", null);

        Assert.NotNull(entry);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Fetcher_stops_at_the_first_confident_source()
    {
        var first = new CountingSource(_ => Task.FromResult<ArtworkCandidate?>(null));
        var second = new CountingSource(_ => Task.FromResult<ArtworkCandidate?>(new ArtworkCandidate(
            TinyPng(), "https://example.test/b.jpg", 0.91, "TryHardNinja", "Dragonhearted", "Single")));
        var third = new CountingSource(_ => Task.FromResult<ArtworkCandidate?>(new ArtworkCandidate(
            TinyPng(), "https://example.test/c.jpg", 0.99, "TryHardNinja", "Dragonhearted", "Single")));
        using var fetcher = new ArtworkFetcher(Cache(), [first, second, third]);

        var entry = await fetcher.GetAsync("TryHardNinja", "Dragonhearted", null);

        Assert.NotNull(entry);
        Assert.Equal("https://example.test/b.jpg", entry!.Metadata.SourceUrl);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(0, third.Calls);
    }

    [Fact]
    public async Task Fetcher_falls_back_to_null_and_survives_a_throwing_source()
    {
        var throwing = new CountingSource(_ => throw new HttpRequestException("catalogue down"));
        var missing = new CountingSource(_ => Task.FromResult<ArtworkCandidate?>(null));
        using var fetcher = new ArtworkFetcher(Cache(), [throwing, missing]);

        var entry = await fetcher.GetAsync("TryHardNinja", "Dragonhearted", null);

        Assert.Null(entry);
        Assert.Equal(1, throwing.Calls);
        Assert.Equal(1, missing.Calls);
        // Nothing cached, so a later lookup goes back to the sources instead of remembering a failure.
        Assert.Null(fetcher.Cache.TryRead(ArtworkDiskCache.KeyFor("TryHardNinja", "Dragonhearted", null)));
    }

    [Fact]
    public async Task Fetcher_only_fetches_a_track_once_while_a_lookup_is_in_flight()
    {
        var gate = new TaskCompletionSource();
        var source = new CountingSource(async _ =>
        {
            await gate.Task;
            return new ArtworkCandidate(TinyPng(), "https://example.test/d.jpg", 0.9, "A", "T", "B");
        });
        using var fetcher = new ArtworkFetcher(Cache(), [source]);

        var first = fetcher.GetAsync("A", "T", "B");
        var second = fetcher.GetAsync("A", "T", "B");
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, entry => Assert.NotNull(entry));
        Assert.Equal(1, source.Calls);
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

        var candidate = await source.FindAsync("TryHardNinja", "Dragonhearted", null, CancellationToken.None);

        Assert.NotNull(candidate);
        Assert.Contains("right/600x600bb.jpg", candidate!.SourceUrl, StringComparison.Ordinal);
        Assert.Equal("TryHardNinja", candidate.Artist);
        Assert.True(candidate.Score >= ArtworkTitleMatcher.DefaultThreshold);
    }

    [Fact]
    public async Task Itunes_source_returns_null_when_nothing_matches_confidently()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"resultCount\":1,\"results\":[{\"trackName\":\"Something Else\",\"artistName\":\"Nobody\",\"artworkUrl100\":\"https://x/100x100bb.jpg\"}]}",
                Encoding.UTF8, "application/json"),
        });
        using var http = new HttpClient(handler);
        var source = new ItunesArtworkSource(http);

        Assert.Null(await source.FindAsync("TryHardNinja", "Dragonhearted", null, CancellationToken.None));
    }


    [Fact]
    public void Multi_artist_credits_match_across_the_cjk_and_latin_spellings()
    {
        // KuGou writes "Alan Walker、Ali Gatie"; iTunes returns "Alan Walker & Ali Gatie". This failed
        // before the separator fold and rejected every cover of every featured-artist track.
        Assert.True(ArtworkTitleMatcher.IsConfident("Alan Walker、Ali Gatie", "Yesterday",
            "Alan Walker & Ali Gatie", "Yesterday"));
        Assert.Equal("Alan Walker", ArtworkTitleMatcher.PrimaryArtist("Alan Walker、Ali Gatie"));
        Assert.Equal("24KGoldn", ArtworkTitleMatcher.PrimaryArtist("24KGoldn、iann dior"));
        Assert.Equal("Solo", ArtworkTitleMatcher.PrimaryArtist("Solo"));
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

    private sealed class CountingSource(Func<object, Task<ArtworkCandidate?>> behavior) : IArtworkSource
    {
        public string Name => "counting";

        public int Calls { get; private set; }

        public Task<ArtworkCandidate?> FindAsync(string? artist, string? title, string? album,
            CancellationToken cancellationToken)
        {
            Calls++;
            return behavior(new object());
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
