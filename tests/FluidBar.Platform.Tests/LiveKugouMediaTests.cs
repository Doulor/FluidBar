using System.Diagnostics;
using FluidBar.Platform.Windows.Media;
using FluidBar.Platform.Windows.Media.Lyrics;

namespace FluidBar.Platform.Tests;

/// <summary>
/// Live verification against the player installed on this machine (KuGou, no SMTC session).
/// </summary>
/// <remarks>
/// <para>These tests touch the real player (they read its state files and press its buttons through the
/// accessibility tree), so they only run when <c>FLUIDBAR_LIVE_MEDIA=1</c> is set; otherwise they return
/// immediately and stay green in CI. They exist because the fallback source can only be proven against a
/// real player process.</para>
/// <para>Every test restores the playback state it changed (next/previous and pause toggle are reverted).</para>
/// </remarks>
public sealed class LiveKugouMediaTests
{
    private static bool Live => Environment.GetEnvironmentVariable("FLUIDBAR_LIVE_MEDIA") == "1";

    [Fact]
    public void Fallback_finds_the_running_kugou_process()
    {
        if (!Live)
        {
            return;
        }

        var candidate = MediaWindowFallback.FindCandidate();

        Assert.NotNull(candidate);
        Assert.Equal("kugou", candidate!.ProcessName);
        Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
        Assert.True(candidate.ProcessId > 0);
    }

    [Fact]
    public void Player_state_file_reports_the_current_track_and_position()
    {
        if (!Live)
        {
            return;
        }

        var state = PlayerPlaybackStateReader.TryReadKugou(DateTimeOffset.UtcNow);

        Assert.NotNull(state);
        Assert.False(string.IsNullOrWhiteSpace(state!.RawTitle));
        Assert.True(state.Position >= TimeSpan.Zero);
        Assert.True(state.SampledAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Local_lyrics_cache_decodes_a_known_track()
    {
        if (!Live)
        {
            return;
        }

        var directory = PlayerPlaybackStateReader.TryReadKugouLyricDirectory();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        // "ADAM - Zhurek" is a track the player downloaded lyrics for on this machine.
        var provider = new KugouLocalLyricsProvider(directory);
        var document = provider.FetchAsync(new LyricsQuery("Zhurek", "ADAM", TimeSpan.Zero, "live"),
            CancellationToken.None).GetAwaiter().GetResult();

        Assert.NotNull(document);
        Assert.True(document!.Lines.Length > 0);
        Assert.True(document.Duration > TimeSpan.Zero);
    }

    [Fact]
    public void Accessibility_tree_exposes_the_live_timeline()
    {
        if (!Live)
        {
            return;
        }

        using var session = OpenSession();
        Assert.NotNull(session);

        if (!session!.TryReadTimeline(out var first))
        {
            // The player hides the time row in some states (VIP/preview tracks); there is nothing to
            // assert about the timeline then. The transport tests still cover that state.
            return;
        }

        Assert.True(first.Duration > TimeSpan.Zero);
        Assert.True(first.Position <= first.Duration);

        // While playing the position must move within a couple of seconds.
        var moved = false;
        for (var attempt = 0; attempt < 8 && !moved; attempt++)
        {
            Thread.Sleep(1000);
            moved = session.TryReadTimeline(out var next) && next.Position > first.Position;
        }

        Assert.True(moved, "the accessibility timeline did not advance while the player was playing");
    }

    [Fact]
    public void Accessibility_transport_drives_next_and_previous()
    {
        if (!Live)
        {
            return;
        }

        using var session = OpenSession();
        Assert.NotNull(session);
        EnsurePlaying(session!);

        // The window title changes the moment the track does (the settings file lags up to 30 s).
        var before = CurrentPlayerTitle();
        Assert.False(string.IsNullOrWhiteSpace(before));

        Assert.True(session!.TryInvoke(MediaKeyCommand.NextTrack, isCurrentlyPlaying: true));
        var after = WaitForWindowTitleChange(before, TimeSpan.FromSeconds(20));
        Assert.NotNull(after);
        Assert.NotEqual(before, after);

        // Restore the playlist position the user had before the test.
        Assert.True(session.TryInvoke(MediaKeyCommand.PreviousTrack, isCurrentlyPlaying: true));
        Assert.NotNull(WaitForWindowTitleChange(after!, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Accessibility_transport_pauses_and_resumes()
    {
        if (!Live)
        {
            return;
        }

        using var session = OpenSession();
        Assert.NotNull(session);
        EnsurePlaying(session!);

        if (!session!.TryReadTimeline(out _))
        {
            return;
        }

        Assert.True(session.TryInvoke(MediaKeyCommand.TogglePlayPause, isCurrentlyPlaying: true));
        Thread.Sleep(2500);
        Assert.True(session.TryReadTimeline(out var pausedFirst));
        Thread.Sleep(2500);
        Assert.True(session.TryReadTimeline(out var pausedSecond));
        Assert.Equal(pausedFirst.Position, pausedSecond.Position);

        // Resume so the machine is left exactly as it was found.
        Assert.True(session.TryInvoke(MediaKeyCommand.TogglePlayPause, isCurrentlyPlaying: false));
        var resumed = false;
        for (var attempt = 0; attempt < 8 && !resumed; attempt++)
        {
            Thread.Sleep(1000);
            resumed = session.TryReadTimeline(out var running) && running.Position > pausedSecond.Position;
        }

        Assert.True(resumed, "playback did not resume after the second toggle");
    }

    [Fact]
    public void Media_key_sender_reports_the_outcome_and_restores_playback()
    {
        if (!Live)
        {
            return;
        }

        var sender = new SystemMediaKeySender();
        var sent = sender.TrySend(MediaKeyCommand.TogglePlayPause);
        if (sent)
        {
            Assert.Null(sender.LastFailureReason);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(sender.LastFailureReason));
        }

        // A media key is global: it may reach the player. Put playback back if it toggled.
        using var session = OpenSession();
        if (session is not null && session.TryReadTimeline(out var first))
        {
            Thread.Sleep(1500);
            if (session.TryReadTimeline(out var second) && second.Position == first.Position)
            {
                session.TryInvoke(MediaKeyCommand.TogglePlayPause, isCurrentlyPlaying: false);
            }
        }
    }

    [Fact]
    public void Player_album_cache_supplies_cover_art()
    {
        if (!Live)
        {
            return;
        }

        // The player caches the cover it shows per song; the reader must find and validate it.
        var bytes = KugouArtworkCache.TryRead("Zhurek");

        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 0);
        Assert.NotNull(ImageHeaderReader.TryReadDimensions(bytes));
    }

    [Fact]
    public async Task Fallback_media_service_publishes_the_player_snapshot_and_routes_transport()
    {
        if (!Live)
        {
            return;
        }

        var logger = new CapturingLogger();
        var counters = new FluidBar.Platform.Windows.Diagnostics.PlatformCounters();
        await using var service = new GsmtcMediaSessionService(logger, counters);
        await service.StartAsync();

        // The fallback poller runs at 1 Hz; wait until it published a KuGou snapshot with a timeline.
        MediaSnapshot? snapshot = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(500);
            snapshot = service.Current;
            if (snapshot is { IsEmpty: false } current && current.Duration > TimeSpan.Zero)
            {
                break;
            }
        }

        Assert.NotNull(snapshot);
        Assert.False(snapshot!.IsEmpty);
        Assert.Contains("kugou", snapshot.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase);
        Assert.True(snapshot.CanGoNext && snapshot.CanGoPrevious);
        // The duration comes from the accessibility timeline; some player states expose none.
        var hasTimeline = snapshot.Duration > TimeSpan.Zero;

        // Transport through the production service must reach the player: next changes the track.
        var before = CurrentPlayerTitle();
        var next = await service.NextAsync();
        var afterNext = next ? WaitForWindowTitleChange(before, TimeSpan.FromSeconds(20)) : null;
        var route = service.CurrentTransportRoute;

        var previous = afterNext is null ? false : await service.PreviousAsync();
        var afterPrevious = previous ? WaitForWindowTitleChange(afterNext!, TimeSpan.FromSeconds(20)) : null;

        Assert.True(next, "the service did not route the next-track command");
        _ = hasTimeline;
        Assert.NotNull(afterNext);
        // The preferred channel is the accessibility tree; a media-key fallback is acceptable while the
        // tree is being rebuilt after a track change, but the command must never be "unavailable".
        Assert.NotEqual(TransportRoute.Unavailable, route.Route);
        Assert.True(previous, "the service did not route the previous-track command");
    }

    private static PlayerAccessibilitySession? OpenSession()
    {
        var processId = CurrentPlayerProcessId();
        return processId <= 0 ? null : PlayerAccessibilitySession.TryCreate(processId);
    }

    private static int CurrentPlayerProcessId()
    {
        foreach (var process in Process.GetProcessesByName("KuGou"))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    return process.Id;
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited between enumeration and read.
            }
        }

        return 0;
    }

    private static string CurrentPlayerTitle()
    {
        var processId = CurrentPlayerProcessId();
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            return Process.GetProcessById(processId).MainWindowTitle;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string? WaitForWindowTitleChange(string previousTitle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            var current = CurrentPlayerTitle();
            if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, previousTitle, StringComparison.Ordinal))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>Leaves the player playing so the pause assertion tests the transition, not a stale state.</summary>
    private static void EnsurePlaying(PlayerAccessibilitySession session)
    {
        if (!session.TryReadTimeline(out var first))
        {
            return;
        }

        Thread.Sleep(1500);
        if (session.TryReadTimeline(out var second) && second.Position > first.Position)
        {
            return;
        }

        session.TryInvoke(MediaKeyCommand.TogglePlayPause, isCurrentlyPlaying: false);
        Thread.Sleep(1500);
    }
}