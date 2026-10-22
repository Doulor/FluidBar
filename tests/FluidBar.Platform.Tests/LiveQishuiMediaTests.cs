using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Live verification against Soda Music installed on this machine (no SMTC session, like KuGou).
/// </summary>
/// <remarks>
/// <para>Read-only: the queue cache and the Luna lyric cache are the player's own files, and nothing is
/// written back. Tests only run when <c>FLUIDBAR_LIVE_MEDIA=1</c> so CI stays green without the player
/// installed. They exist because the fallback source can only be proven against a real player.</para>
/// </remarks>
public sealed class LiveQishuiMediaTests
{
    private static bool Live => Environment.GetEnvironmentVariable("FLUIDBAR_LIVE_MEDIA") == "1";

    [Fact]
    public void Queue_cache_reports_the_last_played_track()
    {
        if (!Live)
        {
            return;
        }

        var track = QishuiQueueReader.TryRead();

        Assert.NotNull(track);
        Assert.False(string.IsNullOrWhiteSpace(track!.Title));
        Assert.True(track.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Fallback_candidate_matches_the_queue_identity()
    {
        if (!Live)
        {
            return;
        }

        var candidate = MediaWindowFallback.FindCandidate();

        Assert.NotNull(candidate);
        Assert.Equal("sodamusic", candidate!.ProcessName);
        Assert.True(candidate.ProcessId > 0);
        var track = QishuiQueueReader.TryRead();
        Assert.NotNull(track);
        Assert.Equal(track!.Title, candidate.Title, ignoreCase: true);
    }

    [Fact]
    public async Task Local_lyrics_cache_decodes_the_last_played_track()
    {
        if (!Live)
        {
            return;
        }

        var track = QishuiQueueReader.TryRead();
        Assert.NotNull(track);

        var provider = new QishuiLocalLyricsProvider();
        var document = await provider.FetchAsync(
            new LyricsQuery(track!.Title, track.Artist, track.Duration, "live-qishui"), default);

        // Not every track carries lyrics (instrumentals / DJ mixes); a miss must stay a miss.
        if (document is null)
        {
            return;
        }

        Assert.NotEmpty(document.Lines);
        Assert.True(document.Lines.Length >= 3, "a real lyric has at least three lines");
    }
}
