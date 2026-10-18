using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluidBar.Core.Activities;
using FluidBar.Core.Events;
using FluidBar.Core.Presentation;

namespace FluidBar.Soak.Recording;

/// <summary>
/// Deterministic event-recording format (<c>fluidbar.event-recording.v1</c>) used by UI tests to
/// replay a realistic pipeline without Platform/UI dependencies.
/// <para>
/// A recording is a list of <see cref="RecordedEvent"/> with millisecond offsets plus
/// <see cref="ExpectedSnapshot"/> presentation assertions at specific event indices. The replay
/// helper feeds the events through the same <c>ActivityScheduler</c>/<c>IslandStateMachine</c>/
/// <c>ActivityPresenter</c> pipeline the app uses, so UI tests can snapshot the resulting view
/// models deterministically.
/// </para>
/// </summary>
public sealed record EventRecording
{
    /// <summary>Format identifier written into fixtures; validated on load.</summary>
    public const string ExpectedFormat = "fluidbar.event-recording.v1";

    /// <summary>Format id (always <see cref="ExpectedFormat"/>).</summary>
    public string Format { get; init; } = ExpectedFormat;

    /// <summary>Fixture name.</summary>
    public string Name { get; init; } = "unnamed";

    /// <summary>Human-readable description of the scenario.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Seed for any generator that produced the recording.</summary>
    public int Seed { get; init; }

    /// <summary>Recorded events in order.</summary>
    public ImmutableArray<RecordedEvent> Events { get; init; } = [];

    /// <summary>Expected presentation snapshots keyed by event index.</summary>
    public ImmutableArray<ExpectedSnapshot> Expected { get; init; } = [];

    /// <summary>Whether the recording uses the one-shot activity TTLs assumed by replay.</summary>
    public bool TickExpiryBeforeEachEvent { get; init; } = true;
}

/// <summary>One recorded producer event.</summary>
public sealed record RecordedEvent
{
    /// <summary>Offset from the recording start in milliseconds (simulated producer time).</summary>
    public int OffsetMs { get; init; }

    /// <summary>Event kind.</summary>
    public IslandEventKind Kind { get; init; } = IslandEventKind.New;

    /// <summary>Activity kind.</summary>
    public ActivityKind ActivityKind { get; init; }

    /// <summary>Stable producer identity.</summary>
    public string Source { get; init; } = "fixture";

    /// <summary>Monotonic per-key sequence.</summary>
    public long Sequence { get; init; }

    /// <summary>Content snapshot.</summary>
    public RecordedPayload Payload { get; init; } = new();

    /// <summary>Builds the Core event.</summary>
    public IslandEvent ToEvent()
        => new(
            new ActivityKey(ActivityKind, Source),
            Kind,
            Sequence,
            TimeSpan.FromMilliseconds(OffsetMs),
            Payload.ToPayload());
}

/// <summary>Serializable subset of <c>ActivityPayload</c> used by fixtures (nulls omitted).</summary>
public sealed record RecordedPayload
{
    /// <summary>Primary text.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Secondary text.</summary>
    public string? Subtitle { get; init; }

    /// <summary>Body/detail text.</summary>
    public string? Body { get; init; }

    /// <summary>Badge text.</summary>
    public string? Badge { get; init; }

    /// <summary>Source caption.</summary>
    public string? SourceName { get; init; }

    /// <summary>Progress 0..100.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>Indeterminate progress.</summary>
    public bool ProgressIndeterminate { get; init; }

    /// <summary>Playing/charging/active flag.</summary>
    public bool? IsActive { get; init; }

    /// <summary>Audio-wave flag.</summary>
    public bool? ShowsAudioWave { get; init; }

    /// <summary>Album artwork path.</summary>
    public string? AlbumArtPath { get; init; }

    /// <summary>Lyric line.</summary>
    public string? LyricLine { get; init; }

    /// <summary>Detected verification code.</summary>
    public string? VerificationCode { get; init; }

    /// <summary>HUD numeric level 0..100.</summary>
    public int? Level { get; init; }

    /// <summary>Explicit TTL override in milliseconds.</summary>
    public int? TtlMs { get; init; }

    /// <summary>Session pin.</summary>
    public bool IsPinned { get; init; }

    /// <summary>Preferred expanded page.</summary>
    public IslandPage? PreferredPage { get; init; }

    /// <summary>Playback position in ticks.</summary>
    public long PositionTicks { get; init; }

    /// <summary>Playback end in ticks.</summary>
    public long EndTicks { get; init; }

    /// <summary>Builds the immutable Core payload.</summary>
    public ActivityPayload ToPayload() => new()
    {
        Title = Title,
        Subtitle = Subtitle,
        Body = Body,
        Badge = Badge,
        SourceName = SourceName,
        ProgressPercent = ProgressPercent,
        ProgressIndeterminate = ProgressIndeterminate,
        IsActive = IsActive,
        ShowsAudioWave = ShowsAudioWave,
        AlbumArtPath = AlbumArtPath,
        LyricLine = LyricLine,
        VerificationCode = VerificationCode,
        Level = Level,
        Ttl = TtlMs is { } ttl ? TimeSpan.FromMilliseconds(ttl) : null,
        IsPinned = IsPinned,
        PreferredPage = PreferredPage,
        PositionTicks = PositionTicks,
        EndTicks = EndTicks,
    };
}

/// <summary>Expected presentation snapshot at one event index (UI tests compare against this).</summary>
public sealed record ExpectedSnapshot
{
    /// <summary>Index into <see cref="EventRecording.Events"/> (0-based).</summary>
    public int AtEventIndex { get; init; }

    /// <summary>Expected island state kind.</summary>
    public IslandStateKind StateKind { get; init; } = IslandStateKind.Compact;

    /// <summary>Expected selected page.</summary>
    public IslandPage Page { get; init; } = IslandPage.Home;

    /// <summary>Expected primary activity kind (<see langword="null"/> = idle surface).</summary>
    public ActivityKind? PrimaryKind { get; init; }

    /// <summary>Expected primary title.</summary>
    public string? Title { get; init; }

    /// <summary>Expected ghost layer count (0..2).</summary>
    public int GhostCount { get; init; }

    /// <summary>Expected <c>+N</c> badge (background layer count).</summary>
    public int BadgeCount { get; init; }

    /// <summary>Expected media progress percentage.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>Expected audio-wave flag.</summary>
    public bool ShowsWave { get; init; }

    /// <summary>Expected HUD label key for volume/brightness primaries.</summary>
    public string? HudLabelKey { get; init; }
}

/// <summary>One replay step: event, scheduler update, state transition and resulting view model.</summary>
/// <param name="Index">Event index.</param>
/// <param name="Event">The applied event.</param>
/// <param name="SchedulerState">Scheduler snapshot after the event.</param>
/// <param name="Update">Scheduler update outcome.</param>
/// <param name="Transition">State-machine transition caused by the update.</param>
/// <param name="View">Presentation view model after the event.</param>
public sealed record ReplayStep(
    int Index,
    IslandEvent Event,
    ActivitySchedulerState SchedulerState,
    ActivitySchedulerUpdate Update,
    IslandTransition Transition,
    IslandViewModel View);

/// <summary>
/// Replays a recording through the pure Core pipeline. Deterministic: the virtual clock is
/// derived from event offsets only, with no ambient time or I/O.
/// </summary>
public static class EventRecordingReplay
{
    /// <summary>Virtual recording origin used by every replay.</summary>
    public static DateTimeOffset Origin { get; } = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Replays the recording and returns one step per event.</summary>
    public static ImmutableArray<ReplayStep> Replay(EventRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var scheduler = new ActivityScheduler();
        using var controller = new IslandStateController(IslandBehaviorOptions.Default with
        {
            AutoHideMs = 0,
            AutoCollapseMs = 0,
            HoverReveal = false,
        });
        var state = ActivitySchedulerState.Empty;
        var steps = ImmutableArray.CreateBuilder<ReplayStep>(recording.Events.Length);

        for (var i = 0; i < recording.Events.Length; i++)
        {
            var recorded = recording.Events[i];
            var now = Origin.AddMilliseconds(recorded.OffsetMs);

            if (recording.TickExpiryBeforeEachEvent)
            {
                var tick = scheduler.Tick(state, now);
                if (tick.HasChange)
                {
                    state = tick.State;
                    controller.Apply(IslandInput.ForActivityUpdate(tick), now);
                }
            }

            var evt = recorded.ToEvent();
            var update = scheduler.Update(state, evt, now);
            state = update.State;
            var transition = controller.Apply(IslandInput.ForActivityUpdate(update), now);
            var view = ActivityPresenter.PresentIsland(state, controller.Current);
            steps.Add(new ReplayStep(i, evt, state, update, transition, view));
        }

        return steps.ToImmutable();
    }

    /// <summary>Creates the actual snapshot for a replay step (same shape as the expected record).</summary>
    public static ExpectedSnapshot Snapshot(ReplayStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var view = step.View;
        return new ExpectedSnapshot
        {
            AtEventIndex = step.Index,
            StateKind = view.StateKind,
            Page = view.Page,
            PrimaryKind = view.Primary?.Kind,
            Title = view.Primary?.Title,
            GhostCount = view.Ghosts.Length,
            BadgeCount = view.OverflowCount,
            ProgressPercent = view.Primary?.ProgressPercent,
            ShowsWave = view.Primary?.ShowsWave ?? false,
            HudLabelKey = view.Hud?.LabelKey,
        };
    }

    /// <summary>
    /// Replays and verifies every expected snapshot. Returns an empty list when the recording is
    /// fully deterministic and matches, otherwise one mismatch string per failure.
    /// </summary>
    public static ImmutableArray<string> Validate(EventRecording recording)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var steps = Replay(recording);
        foreach (var expected in recording.Expected)
        {
            if ((uint)expected.AtEventIndex >= (uint)steps.Length)
            {
                failures.Add($"{recording.Name}: expected snapshot index {expected.AtEventIndex} is out of range.");
                continue;
            }

            var actual = Snapshot(steps[expected.AtEventIndex]);
            Compare(recording.Name, expected, actual, failures);
        }

        return failures.ToImmutable();
    }

    /// <summary>Compares one expected snapshot with the actual snapshot.</summary>
    public static void Compare(string recordingName, ExpectedSnapshot expected, ExpectedSnapshot actual, ImmutableArray<string>.Builder failures)
    {
        void Check<T>(string field, T expectedValue, T actualValue)
        {
            if (!EqualityComparer<T>.Default.Equals(expectedValue, actualValue))
            {
                failures.Add(
                    $"{recordingName}[{expected.AtEventIndex}]: {field} expected {Format(expectedValue)} but was {Format(actualValue)}");
            }
        }

        Check("stateKind", expected.StateKind, actual.StateKind);
        Check("page", expected.Page, actual.Page);
        Check("primaryKind", expected.PrimaryKind, actual.PrimaryKind);
        Check("title", expected.Title, actual.Title);
        Check("ghostCount", expected.GhostCount, actual.GhostCount);
        Check("badgeCount", expected.BadgeCount, actual.BadgeCount);
        Check("progressPercent", expected.ProgressPercent, actual.ProgressPercent);
        Check("showsWave", expected.ShowsWave, actual.ShowsWave);
        Check("hudLabelKey", expected.HudLabelKey, actual.HudLabelKey);
    }

    private static string Format<T>(T value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        _ => value.ToString() ?? "null",
    };
}

/// <summary>Loads the bundled deterministic fixtures from embedded resources / the output folder.</summary>
public static class EventRecordingCatalog
{
    private static readonly string[] FixtureNames = ["media-session", "notification-burst", "clipboard-agent-mix"];

    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Available fixture names.</summary>
    public static IReadOnlyList<string> Names => FixtureNames;

    /// <summary>Loads a fixture by name (without <c>.json</c>).</summary>
    public static EventRecording Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var assembly = typeof(EventRecordingCatalog).Assembly;
        var suffix = name + ".json";
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            // Fallback for consumers that copy fixtures next to the assembly instead of embedding.
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", suffix);
            if (File.Exists(path))
            {
                return Deserialize(File.ReadAllText(path), name);
            }

            throw new FileNotFoundException($"Recording fixture '{name}' was not found.", suffix);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded fixture '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return Deserialize(reader.ReadToEnd(), name);
    }

    /// <summary>Loads every bundled fixture.</summary>
    public static IReadOnlyList<EventRecording> LoadAll() => [.. FixtureNames.Select(Load)];

    private static EventRecording Deserialize(string json, string name)
    {
        var recording = JsonSerializer.Deserialize<EventRecording>(json, Options)
            ?? throw new InvalidOperationException($"Recording fixture '{name}' deserialized to null.");
        if (!string.Equals(recording.Format, EventRecording.ExpectedFormat, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Recording fixture '{name}' has format '{recording.Format}', expected '{EventRecording.ExpectedFormat}'.");
        }

        if (recording.Events.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException($"Recording fixture '{name}' contains no events.");
        }

        return recording;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
