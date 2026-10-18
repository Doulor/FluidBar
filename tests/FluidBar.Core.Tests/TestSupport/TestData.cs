using FluidBar.Core.Activities;
using FluidBar.Core.Events;

namespace FluidBar.Core.Tests.TestSupport;

/// <summary>Canonical factories so tests do not repeat literal boilerplate.</summary>
public static class TestData
{
    /// <summary>Canonical deterministic instant.</summary>
    public static DateTimeOffset Now { get; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Creates an activity key.</summary>
    public static ActivityKey Key(ActivityKind kind = ActivityKind.Notification, string source = "test")
        => new(kind, source);

    /// <summary>Creates a payload with a title and optional tweaks.</summary>
    public static ActivityPayload Payload(string title = "Title", string? subtitle = null, ActivityPayload? extend = null)
        => (extend ?? new ActivityPayload()) with { Title = title, Subtitle = subtitle ?? extend?.Subtitle };

    /// <summary>Creates a New event.</summary>
    public static IslandEvent NewEvent(ActivityKind kind, string source = "test", string title = "Title", long sequence = 1, ActivityPayload? payload = null)
        => IslandEvent.New(Key(kind, source), payload ?? Payload(title), sequence);

    /// <summary>Creates a compact notification event (rank 9, 5 s TTL).</summary>
    public static IslandEvent Notification(string source = "notif:A", string title = "Notification", long sequence = 1)
        => IslandEvent.New(Key(ActivityKind.Notification, source), Payload(title), sequence);

    /// <summary>Creates a media New event (rank 14, persistent).</summary>
    public static IslandEvent Media(string source = "media:gsmtc", string title = "Track", long sequence = 1)
        => IslandEvent.New(Key(ActivityKind.Media, source), Payload(title) with { IsActive = true, ShowsAudioWave = true }, sequence);

    /// <summary>Creates a payload with a custom TTL.</summary>
    public static ActivityPayload WithTtl(this ActivityPayload payload, TimeSpan ttl) => payload with { Ttl = ttl };

    /// <summary>Creates a payload with the session pin.</summary>
    public static ActivityPayload Pinned(this ActivityPayload payload) => payload with { IsPinned = true };

    /// <summary>Runs a scheduler update against a fresh/current state.</summary>
    public static ActivitySchedulerUpdate Apply(this ActivityScheduler scheduler, ActivitySchedulerState state, IslandEvent evt, DateTimeOffset now)
        => scheduler.Update(state, evt, now);

    /// <summary>Builds a scheduler state from events applied in order.</summary>
    public static ActivitySchedulerState BuildState(ActivityScheduler scheduler, params IslandEvent[] events)
    {
        var state = ActivitySchedulerState.Empty;
        foreach (var evt in events)
        {
            state = scheduler.Update(state, evt, Now).State;
        }

        return state;
    }
}
