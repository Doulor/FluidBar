using System.Runtime.InteropServices;
using FluidBar.Platform.Windows.Notifications;

namespace FluidBar.Platform.Tests;

public sealed class NotificationCandidateReaderTests
{
    [Fact]
    public void A_broken_notification_is_retried_with_backoff_without_spamming_the_log_or_blocking_good_items()
    {
        var time = new ManualTimeProvider();
        var logger = new CapturingLogger();
        var reader = new NotificationCandidateReader(logger, time, 8);
        var reads = 0;
        NotificationCandidate Broken() { reads++; throw new COMException("Element not found.", unchecked((int)0x80070490)); }
        Assert.Null(reader.Read(1, Broken));
        for (var poll = 0; poll < 100; poll++) Assert.Null(reader.Read(1, Broken));
        Assert.Equal(1, reads);
        var error = Assert.Single(logger.Entries);
        Assert.Contains("COMException", error.Exception);
        Assert.Equal(1u, error.Data!["notificationId"]);
        Assert.NotNull(reader.Read(2, () => Candidate(2)));
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(reader.Read(1, Broken));
        Assert.Equal(2, reads);
        Assert.Single(logger.Entries);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.NotNull(reader.Read(1, () => Candidate(1)));
        Assert.Null(reader.Read(1, Broken));
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void Removed_notifications_and_service_restart_clear_retry_state_and_the_cache_has_a_budget()
    {
        var reader = new NotificationCandidateReader(new CapturingLogger(), new ManualTimeProvider(), 8);
        for (uint id = 1; id < 30; id++) reader.Read(id, () => throw new InvalidOperationException("bad projection"));
        Assert.InRange(reader.FailureCount, 1, 8);
        reader.Prune(new HashSet<uint> { 29 });
        Assert.Equal(1, reader.FailureCount);
        reader.Reset();
        Assert.Equal(0, reader.FailureCount);
        Assert.NotNull(reader.Read(29, () => Candidate(29)));
    }

    [Fact]
    public void Missing_app_metadata_does_not_discard_readable_notification_text()
    {
        var candidate = NotificationCandidateReader.Parse(7,
            () => throw new COMException("App metadata disappeared.", unchecked((int)0x80070490)),
            () => ["Readable title", "Readable body"], () => DateTimeOffset.UnixEpoch, 512);
        Assert.Equal("Windows", candidate.AppName);
        Assert.Equal("Readable title", candidate.Title);
        Assert.Equal("Readable body", candidate.Body);
    }

    [Fact]
    public void Truncating_notification_text_does_not_split_an_emoji_surrogate_pair()
    {
        var candidate = NotificationCandidateReader.Parse(7, () => "App", () => ["123😀tail"],
            () => DateTimeOffset.UnixEpoch, 4);
        Assert.Equal("123", candidate.Title);
    }

    private static NotificationCandidate Candidate(uint id) => new(id, "App", "Title", "Body", DateTimeOffset.UnixEpoch);
}
